# Deploying SocialMeter

Two environments: **Development** on your machine, **Production** on Azure. CI runs on every push and pull request;
a push to `main` that passes deploys. That is the whole pipeline for a solo project; staging slots can be added later.

## What runs where

| Piece | Azure resource | Tier | Why |
|---|---|---|---|
| API | App Service (Linux, .NET 8) | B1 | The free tier cannot keep Always On, and the background workers (tracked set, deletion sync) need the process alive. |
| Database | Azure SQL, free offer | Serverless, auto-pause | Same engine as local SQL Server Express, so the migrations apply unchanged. First request after a pause is slow. |
| Front end | Static Web Apps | Free | Vite's `dist/` is static; the free tier includes HTTPS and a hostname. |

Rough running cost is the B1 plan; the other two are free. Set a budget alert on the resource group anyway.

## One-time setup

Prerequisites: Azure CLI (`az login`), GitHub CLI (`gh auth login`), both repos on GitHub, the API's user secrets set
locally (TMDB, X, OMDb, Anthropic, Admin keys; the script copies them to Azure).

```powershell
cd C:\Users\JoeFr\source\repos\SocialMeter
.\infra\azure-provision.ps1 -Prefix socialmeter-jf
```

Pick a prefix that is globally unique (it becomes part of hostnames). The script prompts once for a SQL admin
password and never prints it or any key. It then stores the App Service publish profile and the Static Web Apps token as
GitHub secrets and the names as repository variables, which the deploy workflows read.

## Every deploy after that

Push to `main`. The API workflow runs the tests, publishes, deploys, and checks `/api/health`. The UI workflow builds
with `VITE_API_BASE` pointing at the API and deploys `dist/`. Both are visible under the Actions tab, and a failed test
stops the deploy.

Manual deploy of the current `main`: Actions → Deploy → Run workflow.

## Configuration in production

Everything is an App Service application setting, named with `__` for `:` (`Tmdb__ReadAccessToken`, `Admin__ApiKey`,
`Cors__AllowedOrigins__0`). `ASPNETCORE_ENVIRONMENT=Production` turns off Swagger and the dev-only archive source and
turns on HSTS. Change a setting in the portal or with `az webapp config appsettings set`; the app restarts.

Keep the tracked set small at first: `SocialMeter__TrackedListPages=1` halves the number of titles that can trigger
pulls, and `SocialMeter__MaxLivePullsPerHour` is the ceiling on spend.

## Operator actions in production

```bash
curl -X POST https://<api-host>/api/operator/tracked/refresh -H "X-Admin-Key: <key>"
```

The key is the `Admin__ApiKey` application setting (copied from your local user secrets).

## Rollback

App Service keeps the previous deployment: Deployment Center → select the earlier run → Redeploy. Or revert the commit
on `main` and let the workflow deploy it.

## Later, if traffic justifies it

- A staging deployment slot on the App Service (needs the Standard tier) with swap-on-success in the workflow.
- Migrations run from the workflow instead of at startup, with a least-privilege SQL login for the app.
- Application Insights for request and dependency telemetry (one line in `AddWeb`).

## Domain and Cloudflare (live since 2026-10-22)

The public site is **https://chatterbub.com**; the API is **https://api.chatterbub.com**. Both are proxied by
Cloudflare (zone chatterbub.com, registered through Cloudflare Registrar).

| Host | Cloudflare | Origin | Certificate at the origin |
|---|---|---|---|
| chatterbub.com | proxied | Static Web App `socialmeter-jf-ui` | Azure-managed, validated by the apex TXT record (keep it) |
| www.chatterbub.com | proxied, 301 to chatterbub.com at the edge (path and query kept) | never reached | n/a |
| api.chatterbub.com | proxied | App Service `socialmeter-jf-api` | Cloudflare Origin CA, valid to 2041, stored in Key Vault as `chatterbub-origin` |

Why the origin certificate: Azure's free App Service certificate renews by checking that the hostname points straight
at Azure, which stops being true behind Cloudflare's proxy. The Origin CA certificate is trusted only by Cloudflare,
which is the only thing that should ever connect to the API by that name.

Zone settings: SSL **Full (strict)**, Always Use HTTPS, minimum TLS 1.2, TLS 1.3, automatic HTTPS rewrites, HSTS
six months (no subdomains, no preload), browser integrity check, security level medium. Cloudflare Web Analytics is
injected and allowed by the site's CSP.

**Bot Fight Mode is deliberately off.** On the free plan it forces JavaScript detections, which inject an inline
script whose content changes per request, so it can only run if the site's CSP allows `'unsafe-inline'` scripts. The
strict CSP is the stronger protection, and with JS detections blocked Cloudflare would score real visitors as bots.
API scraping is handled by the API's own per-visitor limits and, once the token has Zone WAF permission, a Cloudflare
rate-limiting rule on api.chatterbub.com.

The API reads the visitor's address from `CF-Connecting-IP` only when the connection comes from Cloudflare's published
ranges (`Web/ClientAddress.cs`); otherwise per-visitor limits would be shared by everyone at one Cloudflare location.
Update that list if Cloudflare changes https://www.cloudflare.com/ips-v4 or ips-v6.

The Cloudflare API token lives in Key Vault (`cloudflare-api-token`), scoped to this zone. A rate-limiting rule needs
the token's **Zone WAF: Edit** permission, not yet granted; the API's own per-visitor limits apply meanwhile.

Still direct-reachable: `socialmeter-jf-api.azurewebsites.net` bypasses Cloudflare (app limits still apply). Locking the
App Service to Cloudflare's ranges closes that, once the deploy smoke test no longer depends on the Azure hostname.
