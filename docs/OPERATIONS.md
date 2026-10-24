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
| Access granted | Azure's activity log, whole subscription | anyone is given a role anywhere | `chatterbub-access-changed` |
| Settings changed | Azure's activity log, socialmeter-jf-rg | app settings, deploy credentials (password deploys are off; turning them on shows here), deploy identities and their GitHub trust, custom domains, database firewall, Key Vault, the alert email | `chatterbub-app-changed` |
| Something deleted | Azure's activity log, socialmeter-jf-rg | the app, plan, database, Key Vault, monitoring or any alert rule | `chatterbub-resource-deleted` |

The error and guardrail rules stay quiet for an hour after they fire, so a bad evening is a few emails, not a flood.
The three change alerts send one email per change: they match only "Succeeded" events (Azure also logs "Started"), and
they do not watch `publishxml` (reading the publish profile), which every OIDC deploy does and which is harmless now
that password deploys are off. A routine deploy sends no change email.
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

### "chatterbub-access-changed", "chatterbub-app-changed" or "chatterbub-resource-deleted"

Someone changed the Azure setup. If it was you (or Claude, on a day you asked for changes), ignore it. If not, treat it as
a possible stolen login: sign in to the Azure portal, open **Activity log** to see who did what, remove any access you
don't recognise (**Access control (IAM)**), change your Microsoft account password, and check that two-factor is on.
Changing app settings restarts the app, so an unexpected restart in the uptime log can be the same event.

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
on its page, leads the home page (the hero, with the label and a pulsing dot), and is read every `EveryMinutes` (at
least 15) through the same guardrails. Times are UTC. The hero switches within one home warm-up (4 minutes) of the
window opening and goes back to the usual pick within 4 minutes of it closing; with two windows open, the one that
started first leads. The title needs a TMDB backdrop to lead; without one the usual hero stays.

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings \
  Watch__Windows__0__MediaType=movie Watch__Windows__0__TmdbId=1514026 "Watch__Windows__0__Label=Watch party tonight" \
  Watch__Windows__0__StartsAt=2026-10-24T00:45:00Z Watch__Windows__0__EndsAt=2026-10-24T04:00:00Z Watch__Windows__0__EveryMinutes=30
```

Each read is roughly the cost of one Refresh press (about $0.10–0.50, depending on how many new posts there are). Delete
the settings once the event is over; a closed window does nothing, but the admin page lists it for a day.

## When a title's posts are all filtered out

Symptom: the title page says "No posts measured yet: we found N posts, but none passed our filters", and the log shows
"Claude assessed 20 posts about X: 20 off-topic". Usually the name is an everyday word and the search found sentences that
only contain it. Names like that are handled automatically (`TitleSearch`), but a title can be helped with hand-picked
terms: a place, a nickname, a hashtag people really use.

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings Search__Titles__tv_124364__Terms__0=Fromville
```

The key is the type and id from the title's URL (`/title/tv/124364` gives `tv_124364`). `Search__Titles__<key>__Ambiguous`
set to `true` or `false` forces the treatment either way. Defaults live in `appsettings.json` under `Search:Titles`.
Before adding a term, try it free on Bluesky's own search (bsky.app, search box) and read the first page of results.

## Correcting how a post was read (relabel)

With the admin bar's "Remove buttons" switch on, every post card has **Relabel** beside Remove: pick positive, neutral or
negative and say why ("sarcasm, it's praise", "quotes a clip about another movie"). The title is rescored at once, the
card shows the new label and a public "corrected" tag, and the label outlasts later re-reads by Claude
(`PostModeration.WriteAssessedLabel` is the only way an assessed label reaches a post). Undo is on the card and in the
command center's log.

Every relabel keeps what Claude said (label and tone), what it became, and the reason, in `ModerationActions` (no copy
of the post's text, so "deleted there, deleted here" still holds). That is the record of Claude's mistakes: the source of
worked examples for its instructions and of cases for its accuracy test (`ClaudeAssessorEval`). Claude is not retrained
on it. To pull the corrections with their posts (SSMS, after opening the database as in DEPLOY.md):

```sql
SELECT a.At, p.Platform, p.Content, a.FromLabel, a.FromScore, a.ToLabel, a.Note, p.AnalyzerNote
FROM ModerationActions a JOIN SocialPosts p ON p.Id = a.PostId
WHERE a.Action = 'relabel' AND p.LabelFixedAt IS NOT NULL
ORDER BY a.At DESC;
```

## When a bad post is featured ("In their words")

Each title page features the strongest post on each side (`HighlightRules` in `Application/Social/TitleHighlights.cs`
has the rules; How it works explains them to readers). The rules filter out crude wording, spoiler warnings, media,
shouting and discounted accounts, but they can't judge meaning: an off-topic or nasty post can still get through.

Fix: open the title page with the admin bar's "Remove buttons" switch on, press **Remove** on the featured card and pick a
reason. The post leaves the score and the feed at once; the card shows "Post removed" with Undo, and the next page load
features the next post. If a word keeps getting through, add it to `BlockedWords` (or `BlockedRoots` for a word that hides
inside longer words) with a test case in `HighlightTests.cs`.

## "The crowd" (Crowd Shape)

Since 2026-10-24 some title pages show "The crowd": every counted post as a dot in one of three zones, Hated it |
Mixed | Loved it, each as wide as its share of the score's weight (stronger opinions further out inside a zone), sized
by its weight in the score and marked with its platform, with the score as a line across. Clicking a dot opens that
post: under the strip on a wide screen, in a sheet from the bottom on a phone. The first time the strip scrolls into
view the dots drop into place (once; skipped for visitors who turn on reduced motion). The layout's rules are tested in
the UI repo (`npm test`, `src/utils/crowdLayout.test.js`, also run by CI).

Which titles get it (`CrowdShapeService`, settings under `CrowdShape`):

| Rule | Setting |
|---|---|
| Listed titles, always | `Titles` (Other Mommy, `movie_1400837`, the first pilot) |
| A title with an open watch window (a live event), always | `Watch:Windows` |
| The top titles of the home page's "Most talked about" ranking, once they have enough counted posts | `TopTalkedAbout` = 10, `MinPosts` = 20 |

Every other title page is unchanged. Code: `Application/Social/CrowdShape.cs` (rules + service),
`Application/Home/MostTalkedAbout.cs` (the ranking, shared with the home page), `CrowdShapeOptions`, UI
`components/Title/CrowdShape.vue`, `utils/crowdLayout.js` and `composables/useInView.js`.

Guardrails: the title checks need no database; one snapshot row then says whether the reading changed, and the posts are
read again only when it did (cached per reading for 30 minutes), so a busy title costs one small query per view. At most
`MaxDots` = 200 posts are drawn (each side's heaviest, as many as its share; phones draw 100); the shares and the shape
always use every counted post.

### The live line

Above the dots, once a title has at least 10 counted posts from the last 48 hours: a line of the mood of the latest 12
posts (0 hated it, 100 loved it), one step per post in the order they were posted, like a trading tick chart, with the
score as a dashed line and a tick per post underneath (`CrowdShapeRules.Timeline`, the newest 300 posts at most; it
comes with the crowd, so no extra endpoint or cache). During a live event (an open watch window) it says LIVE with the
window's label, pings on the latest post, and the page asks for the crowd again every 2 minutes while the tab is in view
(`useTitlePage`). That check never pulls from the sources: an unchanged reading costs the one snapshot query, and only a
new reading redraws the page and fetches the score again. New posts arrive as often as the sources are read: every
`Watch:Windows` interval (30 minutes for Buddy), or when someone presses Refresh. UI: `components/Title/PulseLine.vue`,
`utils/pulseLine.js` (tested), `composables/useElementWidth.js`.

Turn it off everywhere (no deploy; the setting change restarts the app, about 30 seconds):

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings CrowdShape__Enabled=false
```

Add a title by hand (`__1`, `__2`, ...), or only listed titles and live events (no ranking):

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings CrowdShape__Titles__1=tv_124364
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings CrowdShape__TopTalkedAbout=0
```

Is anyone using it? Each dot click is one request to the single-post endpoint, which nothing else calls:

```kusto
AppRequests
| where TimeGenerated > ago(7d) and Name == "GET api/socialmeter/{type}/{id:int}/posts/{postId:int}"
| summarize clicks = sum(ItemCount) by bin(TimeGenerated, 1d)
```

Remove it for good: delete the "The crowd" section from `TitlePage.vue` (the component then goes unused) and the
`crowd` endpoint; `MostTalkedAbout` stays, since the home page uses it.

## The Outbox (the brand's Bluesky posts)

The site drafts posts for the brand's Bluesky account; an admin posts or skips each one on the admin page. **Nothing is
ever posted without that tap** (two taps, so a stray one on a phone can't publish). X is by hand: "Copy for X".

What a post is: facts only, then the link (with the title page's preview card), and two replies quoting the strongest
Bluesky post on each side, "The case for:" and "The case against:" (picked by the "In their words" rules: established
author, no spoilers, no crude wording, words only; only Bluesky posts can be quoted on Bluesky). A quote whose author
has turned off quoting is left out. Quoted authors get a notification, so read both quotes before posting: the sides
come from Claude's labels, which can miss sarcasm. If a quote is misread, Skip, and relabel that post on its title page
so it isn't picked again.

When: one draft a day at `Posting:DraftHourUtc` (14 = 10 am Eastern): movies and shows only (games' Steam reviews
would always win, as for the home hero), preferring a title with a quotable post on both sides, then a split crowd,
never one drafted in the last `RepeatAfterDays` (7, skipped ones included). Plus one per live event (an open watch
window). "Draft one now" on the admin page makes today's draft straight away. Drafts expire after `ExpireAfterHours`
(36). At most `MaxPerDay` (2) posts a day. Drafting reads stored data only; it never pulls from the sources. The numbers
are filled in when a draft is shown and again when it's posted, so they're current. Code: `Application/Outbox`,
`Infrastructure/Bluesky/BlueskyPoster.cs` (the brand account's own session), `Controllers/OutboxController.cs`, UI
`components/Admin/OutboxPanel.vue`.

Setting up the brand account (once): in Bluesky, Settings → Privacy and security → App passwords → Add. Then in the API's
App Service settings (never in the repo):

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings Posting__Bluesky__Identifier=<handle> Posting__Bluesky__AppPassword=<app password>
```

Turn it all off (no drafts, Post refused): `Posting__Enabled=false`. Revoke the app password in Bluesky to cut the site
off from the account entirely.

## The reading shelf (/shelf, private)

A private page of ten hand-picked spicy romance and romantasy series (`Application/Shelf/ReadingShelf.cs`): each
series' books in order and its first book's cover. Not linked anywhere, not in the sitemap, `Disallow`ed in robots.txt.
Not scored yet: scoring books is a separate, later piece.

Who can open it: admins, plus the emails on `Shelf:Readers` (App Service settings only; the repo is public). The shelf's
endpoints live under `/api/admin/shelf`, so the Cloudflare Access application that guards the admin endpoints guards
them too: one sign-in, nothing extra to set up for admins. A reader signs in through that same application, but the API
lets readers into the shelf's read-only endpoints only (`[RequireAdmin(ReadersToo = true)]` on `ShelfController`);
every other admin endpoint still answers 403 for them. The site's nav shows a Shelf link to admins and to a browser
that has opened the shelf (a link only; the API decides who sees it).

Setting up a reader (both steps are needed):

1. Cloudflare Zero Trust → Access → Applications → the application protecting `api.chatterbub.com/api/admin`: add the
   reader's email to its policy, so Access sends them the one-time code.
2. The API's App Service setting `Shelf__Readers__0` = the reader's email (`__1`, `__2` for more):

```bash
az webapp config appsettings set -g socialmeter-jf-rg -n socialmeter-jf-api --settings Shelf__Readers__0=<email>
```

Covers come from Open Library through the API (`GET api/admin/shelf/covers/{id}`, only the shelf's own ten ids, kept a week in
memory), so the site's image policy allows only its own API rather than archive.org, where Open Library redirects.

Remove a reader: delete the setting and the email from the Access policy. Remove the shelf: delete `ShelfController`,
`Application/Shelf`, `ShelfOptions`, the UI's `ShelfPage.vue` and its route.

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

## The database is private

Azure SQL has public network access turned off; the API reaches it through a private endpoint (DEPLOY.md, "How traffic
flows"). A login from anywhere else is refused before any password is checked ("Deny Public Network Access is set to
Yes"). If the API ever logs SQL connection errors right after a networking change, check in this order: the API is still
joined to `socialmeter-jf-vnet` (App Service → Networking), the private endpoint's connection is Approved, and
`socialmeter-jf-sql.privatelink.database.windows.net` still has its A record (10.20.2.4) in the private DNS zone.

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
