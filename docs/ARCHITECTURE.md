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

## Use cases

Grouped by folder under `Application/`. `Social/` is the core:

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
| `TitleSearchRules`, `TitleSearchPlanner` | How the sources search a title: when a name is an everyday word and which terms (cast, characters, creators, `Search:Titles` extras) must appear with it. |
| `PullGate` | One live pull per title at a time. |

The rest:

| Folder | Classes | Responsibility |
|---|---|---|
| `Guardrails/` | `LivePullBudget`, `UsageMeter` (+ `UsageMeterWorker`), `SourceBreakers`, `GuardrailLog`, `GuardrailStatusService` | Bound the bill and report on it: the one door for paid pulls (kill switch, daily spend limit, daily and hourly caps), today's usage, a circuit breaker per outside service, the log line the alerts search for, the admin page's view. See OPERATIONS.md. |
| `Home/` | `HomeFeedService` (+ `HomeWarmupWorker`), `HomeLayout`, `HomeSignals`, `FeaturedPick`, `SearchSuggestions` | The home page and the search suggestions, rebuilt in the background every 4 minutes so no visitor waits for a build; pure rules for rows, the hero, crowd-vs-critics and movers. |
| `Tracking/` | `TitleTracker`, `WatchWindows` (+ `WatchWindowWorker`), `SeasonalTheme` | Which titles may be pulled; scheduled reads during an event. |
| `Ratings/` | `CriticsBaseline`, `CriticsScores` (+ `CriticsRefreshWorker`) | The critics number (Rotten Tomatoes + Metacritic only) stored on snapshots. |
| `Moderation/` | `ModerationService`, `PostModeration` | Hide and restore posts, logged in `ModerationActions`. |
| `Events/` | `OutboundClicks` | Anonymous daily counts of clicks that leave the site. |
| `Common/` | `Text`, `OperationInProgressException` | Small shared helpers. |

Rule of thumb: a service that grows past ~200 lines or starts to need a second verb in its name is two services.

## Patterns to copy

These came out of real bugs and reviews; new code should follow them.

1. **Paid work goes through one door.** Anything that spends money per call is started by `LivePullBudget` and counted
   by `UsageMeter`; its client throws `HttpRequestException` with the status code, and its caller reports success or
   failure to `SourceBreakers`. A new paid source that skips these is a bill nobody can see.
2. **Never share a `DbContext` across parallel work.** A request's context runs one query at a time. Parallelise
   outside calls (TMDB, Steam, Claude batches), never database reads: `HomeFeedService` keeps the two in separate,
   labelled places, and `UsageMeter` counts in memory for the same reason. The 2026-10-23 home-page 500s were this.
   Before shipping an endpoint change, fire about 20 uncached requests at once locally (vary `region` on /api/home).
3. **Rules are small pure classes with tests.** `PullPolicy`, `PostModeration`, `FeaturedPick`, `HomeLayout`,
   `HomeSignals`, `CriticsBaseline`, `TrustPolicy`: no I/O, a few public static methods, an xUnit file each. Services
   gather data and call them.
4. **Side writes are best effort.** Recording something next to the main work (refresh log, critics score, click
   counts, usage) catches its own errors and logs a warning; it must never turn the reader's request into an error.
5. **Say the true state.** Never invent a timestamp or a "nothing found": a title never read says so, a paused read
   says until when (from `NextRefreshAllowedAt`), a source that failed says "Temporarily unavailable" publicly and the
   real error goes to the log.
6. **Configuration over code for things that change.** Home rows (`Home:Rows`), guardrail limits (`Guardrails:*`),
   watch windows (`Watch:Windows`) are App Service settings. Defaults for lists live in code, not `appsettings.json`,
   because the configuration binder appends to arrays instead of replacing them (`HomeOptions.DefaultRows`).
7. **Background workers** get their own `*Worker.cs` file, read their interval from options, wait a little after
   start-up, create a scope per run, and `LogError` a failed run (which reaches the error alert).
8. **Log lines that alerts depend on have one shape** and one writer (`GuardrailLog`). Change the wording and update
   the alert rule's query in the same change.
9. **One door for outbound links in the UI** (`OutboundLink.vue` + `utils/outbound.js`), so partner codes and click
   counts can be added in one place later. Shared lists (platform names, moderation reasons) live once in `utils/`.

## Adding things

- **A social platform**: implement `ISocialSource` in `Infrastructure/<Platform>/`, register it in `Infrastructure/DependencyInjection.cs`. Throw `HttpRequestException` with the status on a failed call, and if it is paid, report reads to `UsageMeter` (pattern 1). Trust, assessment, scoring, history, breakers and the UI need no change.
- **A home row**: one line in `HomeFeedService` in the right place (outside services, or our database), a key in `HomeRowKeys`, and add the key to `Home:Rows` to show it.
- **An assessor**: implement `IPostAssessor`; see `ClaudePostAssessor` and the lexicon fallback.
- **A media type**: extend `MediaType` and `MediaTypeExtensions`, add a title provider in Infrastructure, add the UI route segment. The tracker, scoring and pages are type-agnostic.
- **An endpoint**: a thin action in the right controller calling an existing or new use-case service. Operator actions go in `OperatorController`, which carries the admin-key filter.
- **A tunable**: a property on the relevant options class with a doc comment, and a line in `appsettings.json` when it is not secret.

## Security posture

Operator endpoints (`/api/operator/*`) require `X-Admin-Key` (constant-time compare against `Admin:ApiKey`) and are
disabled when the key is unset; Cloudflare also blocks them except from the owner's addresses. The admin endpoints
(`/api/admin/*`) sit behind Cloudflare Access and accept either the key or Access's signed token for an allowed email,
checked again in `RequireAdminAttribute`. Every client has a sliding-window request limit (and Cloudflare a stricter
edge limit); forced refreshes have a per-client allowance; live pulls have hourly and daily caps, a daily spend limit
and a kill switch. Secrets live only in user secrets or App Service settings; HttpClient request logging is at Warning
and telemetry redacts query strings, so credentials in URLs never reach logs. The dev-only archive source is gated on
`IsDevelopment()` at registration. Weekly ZAP scans, CodeQL and dependency gates are listed in DEPLOY.md.
`docs/code-review-2026-10-21.md` is a dated review kept for history.

## Scaling notes

The API is stateless except for the memory cache, the pull gate, the guardrails' in-memory counters and the hosted
workers (tracked set, deletion sync, critics refresh, usage saver, watch windows). One instance serves
thousands of readers; the cost driver is live pulls, which the tracked set and the budget bound. For two or more
instances: move cache, gate, usage counts and breakers to Redis or the database, run workers in a single worker instance, run migrations from CI instead of at
startup, add response caching on GETs.

## Front end (socialmeter-ui)

Vue 3 with the conventional split (and, in front of it, a small Cloudflare Worker in `edge/seo-worker/` that gives
title pages real titles, link previews and structured data before any JavaScript runs; DEPLOY.md, "Search engines").
`pages/` are routes and layout, `components/` are presentational and grouped by area
(`Meter/`, `Social/`, `Title/`, `Ratings/`, `Admin/`), `composables/` hold state and data loading (`useTitlePage`,
`useSnapshots`, `useDevicePref`, `useAdmin`), `api.js` is the only place that knows URLs, `utils/` are pure helpers
(`format`, `platforms`, `outbound`, `moderation`, `shareCard`). Each component owns its CSS
module. A page should be layout plus a composable call; when a page's script grows past a screen, the logic moves to a
composable and the markup splits into section components under `components/<Page>/`.

## Tests

`SocialMeter.Tests`: unit tests for the pure layer (lexicon, trust policy, cross-post rules, verdict scoring, statistics,
meter maths) run on every build and are free. `Category=Eval` runs the assessor over 50 hand-labelled real posts through
Claude; it costs a few cents and gates prompt changes. Run `dotnet test --filter "Category!=Eval"` for the free set.
