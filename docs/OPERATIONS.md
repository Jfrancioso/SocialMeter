# Running Chatterbub: alerts, guardrails and what to do

This is the page to open when an alert email arrives, or when you want to know whether the site is healthy and what it is
spending. DEPLOY.md covers how the pieces are deployed; this covers keeping them running.

## The short version

- **Something watches the site all the time and emails you** when it is down, throwing errors, or when a guardrail stops
  something. The emails come from Azure Monitor (azure-noreply@microsoft.com) to the address in the `chatterbub-alerts`
  action group.
- **Guardrails cap what the site can spend**, whatever the traffic: a daily spend limit, a daily and an hourly pull cap,
  a kill switch, and a circuit breaker per outside service. When one trips, readers keep seeing the last reading with a
  plain note, and you get an email.
- **The admin page (/admin) is the command center**: today's estimated spend and pulls against the limits, each
  service's health, and any watch windows.
- **Stack traces and logs live in Application Insights** (`socialmeter-jf-ai` in the Azure portal).

## What watches the site

| Watcher | What it checks | Fires when | Rule |
|---|---|---|---|
| Uptime test | `GET https://api.chatterbub.com/api/health` from two US locations every 15 min, through Cloudflare | both locations fail | `chatterbub-api-down` (severity 0) |
| App Service health check | the same path from inside Azure, every minute | it fails for more than half of 10 minutes | `chatterbub-api-unhealthy` (severity 1) |
| Server errors | HTTP 5xx answers and errors logged by the app | more than 5 in 15 minutes | `chatterbub-server-errors` (severity 1) |
| Guardrails | the log line `Guardrail tripped: {Guardrail}` | any trip except the hourly cap; one alert per guardrail | `chatterbub-guardrails` (severity 2) |

The error and guardrail rules stay quiet for an hour after they fire, so a bad evening is a few emails, not a flood.
A deploy restarts the app for about 30 seconds; the thresholds above are set so that alone doesn't email you.

## When an email arrives

### "Chatterbub API down" or "API unhealthy"

1. Open https://chatterbub.com and https://api.chatterbub.com/api/health. If both work, it was a blip; the alert
   resolves itself.
2. If the API is down: Azure portal > `socialmeter-jf-api` > **Diagnose and solve problems**, and **Log stream**.
   A failed deploy shows in GitHub Actions (Deploy API).
3. If only the uptime test fails but the health check is fine, the problem is in front of the app: Cloudflare
   (status page, DNS, the origin certificate) or App Service's access restrictions.
4. Roll back by re-running the previous green Deploy API run (DEPLOY.md, "Rollback").

### "Chatterbub: server errors"

The email's **View results** link shows which requests failed. For the stack trace:
Azure portal > `socialmeter-jf-ai` > **Failures** > **Exceptions**, pick the exception, then **Suggested** or
**All** to see the full trace with the line numbers. Or run the "Recent errors" query below in **Logs**.

### "Chatterbub: guardrail tripped"

The alert names the guardrail. The results link shows the detail line, which carries the numbers.

| Guardrail | What happened | Readers see | What to do |
|---|---|---|---|
| `spend-limit` | Today's estimated Claude + X spend reached `Guardrails:DailySpendLimitUsd` | the last reading, "Live reads are paused until 8:00 PM" (midnight UTC) | Check /admin for where it went. If it was real traffic, raise the limit; if it looks wrong, look at RefreshLogs for one title being pulled again and again. It resets itself at midnight UTC. |
| `daily-cap` | `Guardrails:MaxLivePullsPerDay` pulls today | the same | Same as above. |
| `kill-switch` | Live pulls are switched off | "Live reads are paused for now" | Only you turn this on. Turn it off when done (below). |
| `breaker:x` | X answered "out of credit" (402), "slow down" (429), rejected the key, or failed 3 times in a row | readings without new X posts | 402: add credit in the X developer console. 429: wait; it retries after an hour. Key rejected: check `X__BearerToken`. |
| `breaker:claude` | Claude is out of credit, rate limited, rejected the key, or failing | posts are read by the word list instead, and re-read by Claude once it is back | Out of credit: Anthropic console > Billing. Key: `Anthropic__ApiKey`. |
| `breaker:bluesky`, `breaker:steam` | that service keeps failing | readings without that source | Usually theirs, not ours; it retries every 15 minutes. Check the service's status page if it lasts. |

The hourly cap (`hourly-cap`) is logged but never emailed: it means many readers at once, which is good news. The admin
page and the queries below show how often it happens.

## The guardrails

All are App Service settings under `Guardrails__` (double underscore). Changing one restarts the app (about 30 seconds);
no deploy is needed. Defaults are in `appsettings.json` and `Options/GuardrailOptions.cs`.

| Setting | Default | What it does |
|---|---|---|
| `LivePullsEnabled` | `true` | The kill switch. `false` stops every live pull; readers get cached readings. |
| `DailySpendLimitUsd` | `10` | Estimated spend per UTC day at which live pulls stop until midnight UTC ("safe mode"). Normal days were $2–4 of Claude. |
| `MaxLivePullsPerDay` | `200` | A plain count per UTC day, which holds even if the price estimate is off. |
| `MaxLivePullsPerHour` | `12` | Token bucket; hands one pull back every 5 minutes. |
| `MaxForcedRefreshesPerHourPerCaller` | `6` | Refresh presses per visitor per hour. |
| `BreakerFailures` / `BreakerMinutes` | `3` / `15` | Failures in a row that pause a service, and for how long. |
| `BreakerHardStopMinutes` | `60` | Pause after "out of credit", "slow down" or a rejected key. |
| `ClaudeInputUsdPerMillion` etc. | Opus 5.5 and X list prices | Only for the estimate. Update them if the model or prices change. |

How it fits together in code (`Application/Guardrails/`):

- **`LivePullBudget`** is the one door every live pull goes through. It checks, cheapest first: the kill switch, today's
  estimated spend, today's pull count, then the hourly bucket.
- **`UsageMeter`** counts today's Claude tokens (`ClaudePostAssessor`) and X posts and profiles read (`XSource`,
  including the deletion sync's lookups). It is in memory because Claude batches run in parallel, and a DbContext must
  never be shared across parallel work. `UsageMeterWorker` saves it to the `UsageDays` table every minute and loads it
  back at startup, so a restart doesn't hand out a fresh budget.
- **`SourceBreakers`** keeps one circuit breaker per outside service. `PostIngestionService`, `DeletionSyncService` and
  `ClaudePostAssessor` ask it before calling a service and tell it how the call went.
- **`GuardrailLog`** writes every trip in the one shape the alert rule searches for, at most once an hour per guardrail.

The real ceilings sit outside the app and are worth setting too: a **monthly spend limit in the Anthropic console**, X's
**prepaid credits with auto-recharge off** (or capped), and the **$30/month Azure budget alert**.

### Turning live reads off and on (kill switch)

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings Guardrails__LivePullsEnabled=false
```

Back on: the same with `true`, or delete the setting. In the portal: `socialmeter-jf-api` > **Environment variables**.

## Watch windows (reading a title during an event)

For a watch party, premiere or finale: while a window is open, the title counts as tracked, shows "Tracking · {Label}"
on its page, and is read every `EveryMinutes` (at least 15) through the same guardrails. Times are UTC.

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings \
  Watch__Windows__0__MediaType=movie Watch__Windows__0__TmdbId=1514026 "Watch__Windows__0__Label=Watch party tonight" \
  Watch__Windows__0__StartsAt=2026-10-24T00:45:00Z Watch__Windows__0__EndsAt=2026-10-24T04:00:00Z Watch__Windows__0__EveryMinutes=30
```

Each read is roughly the cost of one Refresh press (about $0.10–0.50, depending on how many new posts there are). Delete
the settings once the event is over; a closed window does nothing, but the admin page lists it for a day.

## Useful queries

Azure portal > `socialmeter-jf-logs` (or `socialmeter-jf-ai`) > **Logs**.

```kusto
// Recent errors with their stack traces
AppExceptions
| where TimeGenerated > ago(1d) and SeverityLevel >= 3
| project TimeGenerated, ExceptionType, OuterMessage, Details
| order by TimeGenerated desc

// Every guardrail trip and recovery, including the hourly cap
AppTraces
| where Message has "Guardrail"
| project TimeGenerated, Message
| order by TimeGenerated desc

// Slowest endpoints today
AppRequests
| where TimeGenerated > ago(1d)
| summarize Requests = count(), P95ms = percentile(DurationMs, 95), Failed = countif(Success == false) by Name
| order by P95ms desc

// Which outside calls are failing (X, Bluesky, TMDB...)
AppDependencies
| where TimeGenerated > ago(1d) and Success == false
| summarize count() by Target, ResultCode
```

Spend by day, from the app's own database (SSMS or `sqlcmd`):

```sql
SELECT Day, LivePulls, ClaudeInputTokens, ClaudeOutputTokens, XPostsRead, XUsersRead, EstimatedUsd
FROM UsageDays ORDER BY Day DESC;
```

## What monitoring costs

About $5–6 a month: the uptime test (2 locations every 15 minutes, about $3.20), two log alert rules ($0.50–1.50 each),
two metric alerts ($0.10 each). Log storage is free up to 5 GB a month, and the workspace has a **0.2 GB daily cap**, so
logging itself can never run up a bill (worst case about $3 a month over the free amount).

## Known gaps

- **Credentials in URLs.** TMDB (with `Tmdb__ApiKey`), OMDb and Steam take their key in the query string. Telemetry
  rewrites every query value to "Redacted" (`Web/Telemetry.cs`, since 2026-10-23; the first hour of data before that
  held the TMDB and OMDb keys). Prefer `Tmdb__ReadAccessToken`, which travels in a header.

- **Browser errors aren't recorded.** Telemetry is server-side only (privacy page). A broken page that still gets
  200s from the API won't alert.
- **The website itself (Static Web Apps) has no uptime test**, only the API. A second test on https://chatterbub.com costs
  about $3.20 a month more.
- **The deletion sync's X lookups grow with stored posts.** Every stored X post is re-checked once a day, and X bills each
  post a lookup returns (about $0.005). The cost rises as posts accumulate (up to 4,000 a day, about $20, at today's
  batch settings). It counts toward the spend estimate, and the X breaker stops it when credit runs out. Re-checking
  older posts less often is a policy decision about X's deletion rules, not yet made.
- **One instance.** The meter, the breakers and the hourly bucket live in that one process. With two instances they would
  move to the database.
