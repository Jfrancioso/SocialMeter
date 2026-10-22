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
