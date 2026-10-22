# Architecture

The product is Chatterbub; SocialMeter is the code name used in namespaces, class names and infrastructure.

SocialMeter follows **Clean Architecture** in the shape the standard ASP.NET Core templates use (the layout popularised by
Jason Taylor's template and Microsoft's own guidance): four layers with dependencies pointing inward, interfaces as the
seam between the application and the outside world, and one class per use case. It is the same family as Onion and
Hexagonal ("ports and adapters"): the interfaces in `Application/Abstractions` are the ports, the classes in
`Infrastructure` are the adapters.

Why this one: the project is mostly integrations that change often (sources, ratings providers, the assessor) built by
one person. The pattern makes each integration a single class behind an interface, keeps the scoring pure and testable,
and is one a reviewer will recognise on sight. It is folders in one project today; splitting into four projects is a
mechanical move when the team grows and nothing else changes.

```
Web/            Presentation. Controllers, DTOs (Contracts/), filters, the request pipeline, CORS, rate limiting.
                Thin: an action validates input, calls one application service, returns a DTO. No business rules.
Application/    Use cases and policies. One service per use case; pure calculators; the interfaces everything else
                implements. Knows the domain and the DbContext; knows nothing about HTTP or external APIs.
Domain/         Entities and value types (Title, SocialPost, TitleSnapshot, ScoreHistory, SocialPostCandidate).
                No dependencies on anything.
Infrastructure/ Everything that talks to the outside world: EF Core DbContext, TMDB/Steam/OMDb clients, the social
                sources, the Anthropic assessor. Implements Application's interfaces.
Options/        Typed configuration, one class per concern, bound from appsettings + user secrets.
```

Dependency direction: `Web → Application → Domain`, and `Infrastructure → Application` (it implements the interfaces).
Nothing in Application references a concrete client. The one pragmatic exception is that Application services take the
`DbContext` directly rather than a repository layer; at this size a repository would only forward LINQ.

## Composition root

`Program.cs` does three things: `AddInfrastructure`, `AddApplication`, `AddWeb`, then `UseWeb`. Each layer registers its
own services in its `DependencyInjection.cs`. To add something, register it in the layer it belongs to.

## Use cases (Application/Social)

| Class | Responsibility |
|---|---|
| `SocialMeterService` | The reading for one title: decide whether a pull is due (tracked, stale or forced, inside budget), run it under the per-title gate, compute, persist snapshot and history. |
| `PostIngestionService` | Fetch one source for one title, upsert, assess what the current assessor has not read, apply trust. Knows nothing about scores. |
| `ScoreExplainer` | The numbers behind a cached reading: weight shares, substance mix, drivers, filter reasons, notes. Read-only. |
| `PostQueryService` | Posts page, cached snapshots for many titles, daily history + trend. Read-only. |
| `RescoreService` | Operator: re-read stored posts with the current assessor, title by title. One run at a time. |
| `DeletionSyncService` | Operator and worker: purge what the platforms removed, prune by retention, recompute. One run at a time. |
| `ScoreHistoryService` | One row per title per day; backfill from post dates; Mann-Kendall trend. |
| `TrustPolicy`, `VerdictAssessor`, `SocialScoreCalculator`, `ScoreStatistics`, `SubstanceHeuristic` | Pure policies and maths. No I/O, fully unit-tested. |
| `PullGate`, `LivePullBudget` | Process-wide guards: one pull per title at a time; hourly ceilings on live pulls and forced refreshes. |

Rule of thumb: a service that grows past ~200 lines or starts to need a second verb in its name is two services.

## Adding things

- **A social platform**: implement `ISocialSource` in `Infrastructure/<Platform>/`, register it in `Infrastructure/DependencyInjection.cs`. Trust, assessment, scoring, history and the UI need no change.
- **An assessor**: implement `IPostAssessor`; see `ClaudePostAssessor` and the lexicon fallback.
- **A media type**: extend `MediaType` and `MediaTypeExtensions`, add a title provider in Infrastructure, add the UI route segment. The tracker, scoring and pages are type-agnostic.
- **An endpoint**: a thin action in the right controller calling an existing or new use-case service. Operator actions go in `OperatorController`, which carries the admin-key filter.
- **A tunable**: a property on the relevant options class with a doc comment, and a line in `appsettings.json` when it is not secret.

## Security posture

Operator endpoints (`/api/operator/*`) require `X-Admin-Key` (constant-time compare against `Admin:ApiKey`) and are
disabled when the key is unset. Every client has a sliding-window request limit; forced refreshes have a per-client
allowance and live pulls a process-wide hourly ceiling. Secrets live only in user secrets or environment; HttpClient
request logging is at Warning so query-string credentials never reach logs. The dev-only archive source is gated on
`IsDevelopment()` at registration. See `docs/code-review-2026-10-21.md` for the full review and the remaining medium
and low items.

## Scaling notes

The API is stateless except for the memory cache, the pull gate and the two hosted workers. One instance serves
thousands of readers; the cost driver is live pulls, which the tracked set and the budget bound. For two or more
instances: move cache and gate to Redis, run workers in a single worker instance, run migrations from CI instead of at
startup, add response caching on GETs.

## Front end (socialmeter-ui)

Vue 3 with the conventional split: `pages/` are routes and layout, `components/` are presentational and grouped by area
(`Meter/`, `Social/`, `Title/`, `Ratings/`), `composables/` hold state and data loading (`useTitlePage`, `useSnapshots`,
`useDevicePref`), `api.js` is the only place that knows URLs, `utils/` are pure helpers. Each component owns its CSS
module. A page should be layout plus a composable call; when a page's script grows past a screen, the logic moves to a
composable and the markup splits into section components under `components/<Page>/`.

## Tests

`SocialMeter.Tests`: unit tests for the pure layer (lexicon, trust policy, cross-post rules, verdict scoring, statistics,
meter maths) run on every build and are free. `Category=Eval` runs the assessor over 50 hand-labelled real posts through
Claude; it costs a few cents and gates prompt changes. Run `dotnet test --filter "Category!=Eval"` for the free set.
