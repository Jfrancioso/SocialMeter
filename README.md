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

## Running it

```bash
cd SocialMeter
dotnet user-secrets set "Tmdb:ApiKey"        "<themoviedb.org API key>"
dotnet user-secrets set "Reddit:ClientId"    "<script app client id>"      # optional
dotnet user-secrets set "Reddit:ClientSecret" "<script app secret>"        # optional
dotnet user-secrets set "X:BearerToken"      "<X API v2 bearer token>"     # optional
dotnet user-secrets set "Omdb:ApiKey"        "<omdbapi.com key>"           # optional, unlocks IMDb/RT/Metacritic
dotnet user-secrets set "Anthropic:ApiKey"   "<console.anthropic.com key>" # optional, context-aware sentiment
dotnet user-secrets set "Steam:ApiKey"       "<steamcommunity.com/dev/apikey>" # optional, proper library art for new games
dotnet run --launch-profile https
```

Migrations apply on start-up (connection string in `appsettings.json`). Without social credentials the meter only
shows the seeded demo posts for *Dune: Part Two* and *Gladiator II*. Swagger is at `/swagger`; `SocialMeter.http`
has example requests.

## Endpoints

| Route | Purpose |
|---|---|
| `GET /api/home` | Featured title and rows for the home page |
| `GET /api/titles/search?query=` | Movies and shows together (`&type=movie\|tv` to narrow) |
| `GET /api/titles/{movie\|tv}/{id}` | Title details |
| `GET /api/titles/{type}/{id}/ratings` | Other sites' ratings, normalized to 0–100 |
| `GET /api/socialmeter/{type}/{id}` | The SocialMeter reading (`?refresh=true` re-pulls from sources) |
| `GET /api/socialmeter/{type}/{id}/posts` | Posts behind the score, filterable by sentiment/platform |
| `GET /api/socialmeter/snapshots?keys=movie:1,tv:2` | Cached readings for many titles |
| `POST /api/socialmeter/sync-deletions` | Run the deletion sync now |
