# Chatterbub API

The back end for [Chatterbub](https://chatterbub.com) ([front end](https://github.com/Jfrancioso/SocialMeter.Ui)): an audience
sentiment score for movies, TV shows and games, scored from public conversation on X, Bluesky and Steam and shown beside
TMDB, IMDb, Rotten Tomatoes, Metacritic and Steam.

SocialMeter is the project's code name: namespaces, repositories and Azure resources keep it; everything a visitor sees
says Chatterbub.

.NET 8 · EF Core 8 · SQL Server (Azure SQL in production). Front end: Vue 3 (separate repo).

Where to read next: [docs/LEARNING-GUIDE.md](docs/LEARNING-GUIDE.md) (how it works, in order),
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (the map and the patterns to copy), [docs/DEPLOY.md](docs/DEPLOY.md)
(Azure, Cloudflare, settings) and [docs/OPERATIONS.md](docs/OPERATIONS.md) (alerts, guardrails, what to do when
something breaks).

## What it does with social data

- **Read-only.** The app never posts, comments, votes or messages on any platform.
- **Only tracked titles, at most every six hours.** A live pull (one search per source, authors included) happens when
  a tracked title's cached reading is older than `RefreshAfterHours` or a reader presses Refresh, and only inside the
  guardrails: hourly and daily pull caps, a daily spend limit, a kill switch and a circuit breaker per source
  (`Application/Guardrails`, docs/OPERATIONS.md). Readings are cached in `TitleSnapshots`.
- **Sentiment in context.** With `Anthropic:ApiKey` set, Claude reads each post against the title and answers two
  questions — is this post actually about *this* title, and how does the author feel about *it* (not about a bot,
  another film, or the weather)? Off-topic posts are excluded. Without a key, a word lexicon scores tone. Nothing is
  trained on platform data; posts are sent for inference only.
- **Trust-weighted.** Account age and reputation discount throwaway accounts; one counted post per account per title;
  duplicate text collapses to one (`TrustPolicy`). Thresholds are server-side only.
- **Deletion sync.** `DeletionSyncService` re-checks stored posts against the platform (X tweet lookup, Bluesky)
  and purges anything deleted or removed there, prunes posts older than `RetentionDays`, and recomputes affected
  scores. Runs every `DeletionSyncHours` via `DeletionSyncWorker`; `POST /api/operator/sync-deletions` runs it now.
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
dotnet user-secrets set "X:BearerToken"      "<X API v2 bearer token>"     # optional; X bills every post read
dotnet user-secrets set "Bluesky:Identifier" "<handle.bsky.social>"        # optional, free
dotnet user-secrets set "Bluesky:AppPassword" "<Bluesky app password>"     # optional
dotnet user-secrets set "Omdb:ApiKey"        "<omdbapi.com key>"           # optional, unlocks IMDb/RT/Metacritic
dotnet user-secrets set "Anthropic:ApiKey"   "<console.anthropic.com key>" # optional, context-aware sentiment
dotnet user-secrets set "Admin:ApiKey"       "<long random string>"       # enables the operator endpoints (sent as X-Admin-Key)
dotnet run --launch-profile https
```

Prefer `Tmdb:ReadAccessToken` (the v4 token) over `Tmdb:ApiKey`: it travels in a header, never in a URL.

Operator endpoints (`POST /api/operator/rescore`, `sync-deletions`, `tracked/refresh`) require the `X-Admin-Key` header and are disabled until
`Admin:ApiKey` is set. The admin endpoints (`/api/admin/*`, used by the site's /admin page) accept the same key or, in
production, a Cloudflare Access sign-in for an email in `Admin:Emails`. Every client is limited to 300 requests a minute
(180 for the bulk read endpoints); forced refreshes are limited per client; live pulls have hourly and daily caps and a
daily spend limit (`Guardrails:*`), beyond which cached readings are served with a note saying until when.

Migrations apply on start-up (connection string in `appsettings.json`). Without social credentials there are no
readings, only title details and ratings. Swagger is at `/swagger` in Development; `SocialMeter.http` has example
requests. Application Insights switches on only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.

## Tests and evals

`SocialMeter.Tests` holds two kinds of checks:

```bash
dotnet test --filter "Category!=Eval"   # unit tests: lexicon, trust policy, cross-post rules, verdict scoring, meter maths — fast, free
dotnet test --filter "Category=Eval"    # the assessor eval: 50 hand-labelled real posts through Claude, ~5 cents a run
```

The eval (`SocialMeter.Tests/Evals/cases.json`) is the contract for the prompt: each case says whether a post is about
the title and, if so, the acceptable sentiment and substance ranges. It asserts ≥ 90 % relevance accuracy and ≥ 85 % overall
pass rate and writes `eval-report.md` beside the test assembly with every case's verdict and Claude's one-line reason.
Change the prompt, run the eval, read the report — that is the loop. Add a case whenever a real post is misread.

## Endpoints

| Route | Purpose |
|---|---|
| `GET /api/health` | Liveness, used by the uptime test and App Service's health check |
| `GET /api/home` | Featured title and rows for the home page (rows from `Home:Rows`) |
| `GET /api/home/suggestions` | What the search box offers before anything is typed |
| `GET /api/titles/search?query=` | Movies and shows together (`&type=movie\|tv` to narrow) |
| `GET /api/titles/{movie\|tv}/{id}` | Title details |
| `GET /api/titles/{type}/{id}/ratings` | Other sites' ratings, normalized to 0–100 |
| `GET /api/socialmeter/{type}/{id}` | The SocialMeter reading (`?refresh=true` re-pulls from sources) |
| `GET /api/socialmeter/{type}/{id}/posts` | Posts behind the score, filterable by sentiment/platform |
| `GET /api/socialmeter/{type}/{id}/explain` | The numbers behind the reading: effective posts, 95 % margin, weight by sentiment and substance, top drivers |
| `GET /api/socialmeter/{type}/{id}/crowd` | "The crowd": every counted post as a dot (value, weight, platform; no text), the crowd's shape, the mood over the last 48 hours (`timeline`) and whether a live event is on (`live`, `liveLabel`), for listed titles, live events and the most talked about (`CrowdShape` settings). `enabled: false` elsewhere |
| `GET /api/socialmeter/{type}/{id}/posts/{postId}` | One counted post of the title, for a clicked dot (404 for hidden, filtered or other titles' posts) |
| `GET /api/socialmeter/{type}/{id}/highlights` | "In their words": the featured post on each side (rules in `HighlightRules`), with each side's post count and weight share |
| `GET /api/socialmeter/{type}/{id}/history?days=30` | One reading per day plus the Mann-Kendall trend verdict |
| `GET /api/socialmeter/snapshots?keys=movie:1,tv:2` | Cached readings for many titles |
| `POST /api/events/outbound?kind=&host=` | Count one click on a link that leaves the site (a daily total, nothing about the visitor) |
| `GET /api/admin/me`, `GET /api/admin/guardrails`, `GET /api/admin/moderation` | Admin page: who is signed in, health and spend, the moderation log |
| `POST /api/admin/posts/{id}/hide`, `.../restore` | Remove a post from the feed and the score, or put it back |
| `POST /api/operator/sync-deletions`, `rescore`, `tracked/refresh` | Operator actions (admin key) |
