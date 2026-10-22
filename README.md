# SocialMeter API

The back end for [SocialMeter](https://github.com/Jfrancioso/SocialMeter.Ui): an audience sentiment meter for movies and
TV shows, scored from public conversation on Reddit and X and shown beside TMDB, IMDb, Rotten Tomatoes and Metacritic.

.NET 8 · EF Core 8 · SQL Server. Front end: Vue 3 (separate repo).

## What it does with social data

- **Read-only.** The app never posts, comments, votes or messages on any platform.
- **Two calls per title per six hours.** A title search plus one batched author lookup, cached in `TitleSnapshots`.
- **Sentiment in context.** With `Anthropic:ApiKey` set, Claude reads each post against the title and answers two
  questions — is this post actually about *this* title, and how does the author feel about *it* (not about a bot,
  another film, or the weather)? Off-topic posts are excluded. Without a key, a word lexicon scores tone. Nothing is
  trained on platform data; posts are sent for inference only.
- **Trust-weighted.** Account age and reputation discount throwaway accounts; one counted post per account per title;
  duplicate text collapses to one (`TrustPolicy`). Thresholds are server-side only.
- **Deletion sync.** `DeletionSyncService` re-checks stored posts against the platform (Reddit `/api/info`, X tweet lookup)
  and purges anything deleted or removed there, prunes posts older than `RetentionDays`, and recomputes affected
  scores. Runs every `DeletionSyncHours` via `DeletionSyncWorker`; `POST /api/socialmeter/sync-deletions` runs it now.
- **No profiling.** Nothing is inferred about users beyond sentiment toward a title; data is never sold or shared.

## How the number is made

`SocialScoreCalculator` is a weighted mean (engagement × recency × trust × substance) pulled toward neutral by a
Bayesian average, with a Student-t 95 % interval; `ScoreStatistics` holds the maths (Kish effective sample size,
weighted standard error, Mann-Kendall trend test with Sen's slope). `ScoreHistoryService` keeps one reading per title
per day, reconstructs the past from post dates for titles with no history yet, and runs the trend test. The
UI's "how it works" page publishes the method with a worked example; only the trust cut-offs stay private.

## Running it

```bash
cd SocialMeter
dotnet user-secrets set "Tmdb:ApiKey"        "<themoviedb.org API key>"
dotnet user-secrets set "Reddit:ClientId"    "<script app client id>"      # optional
dotnet user-secrets set "Reddit:ClientSecret" "<script app secret>"        # optional
dotnet user-secrets set "X:BearerToken"      "<X API v2 bearer token>"     # optional
dotnet user-secrets set "Omdb:ApiKey"        "<omdbapi.com key>"           # optional, unlocks IMDb/RT/Metacritic
dotnet user-secrets set "Anthropic:ApiKey"   "<console.anthropic.com key>" # optional, context-aware sentiment
dotnet user-secrets set "Admin:ApiKey"       "<long random string>"       # enables the operator endpoints (sent as X-Admin-Key)
dotnet run --launch-profile https
```

Prefer `Tmdb:ReadAccessToken` (the v4 token) over `Tmdb:ApiKey`: it travels in a header, never in a URL.

Operator endpoints (`rescore`, `sync-deletions`, `tracked/refresh`) require the `X-Admin-Key` header and are disabled until
`Admin:ApiKey` is set. Every client is limited to 120 requests a minute; forced refreshes are limited per client and the
process has an hourly ceiling on live pulls (`SocialMeter:MaxLivePullsPerHour`), beyond which cached readings are served.

Migrations apply on start-up (connection string in `appsettings.json`). Without social credentials the meter only
shows the seeded demo posts for *Dune: Part Two* and *Gladiator II*. Swagger is at `/swagger`; `SocialMeter.http`
has example requests.

## Tests and evals

`SocialMeter.Tests` holds two kinds of checks:

```bash
dotnet test --filter "Category!=Eval"   # unit tests: lexicon, trust policy, cross-post rules, verdict scoring, meter maths — fast, free
dotnet test --filter "Category=Eval"    # the assessor eval: 48 hand-labelled real posts through Claude, ~5 cents a run
```

The eval (`SocialMeter.Tests/Evals/cases.json`) is the contract for the prompt: each case says whether a post is about
the title and, if so, the acceptable sentiment and substance ranges. It asserts ≥ 90 % relevance accuracy and ≥ 85 % overall
pass rate and writes `eval-report.md` beside the test assembly with every case's verdict and Claude's one-line reason.
Change the prompt, run the eval, read the report — that is the loop. Add a case whenever a real post is misread.

## Endpoints

| Route | Purpose |
|---|---|
| `GET /api/home` | Featured title and rows for the home page |
| `GET /api/titles/search?query=` | Movies and shows together (`&type=movie\|tv` to narrow) |
| `GET /api/titles/{movie\|tv}/{id}` | Title details |
| `GET /api/titles/{type}/{id}/ratings` | Other sites' ratings, normalized to 0–100 |
| `GET /api/socialmeter/{type}/{id}` | The SocialMeter reading (`?refresh=true` re-pulls from sources) |
| `GET /api/socialmeter/{type}/{id}/posts` | Posts behind the score, filterable by sentiment/platform |
| `GET /api/socialmeter/{type}/{id}/explain` | The numbers behind the reading: effective posts, 95 % margin, weight by sentiment and substance, top drivers |
| `GET /api/socialmeter/{type}/{id}/history?days=30` | One reading per day plus the Mann-Kendall trend verdict |
| `GET /api/socialmeter/snapshots?keys=movie:1,tv:2` | Cached readings for many titles |
| `POST /api/socialmeter/sync-deletions` | Run the deletion sync now |
