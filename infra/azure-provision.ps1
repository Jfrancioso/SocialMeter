<#
.SYNOPSIS
  Creates the Azure resources SocialMeter runs on and wires GitHub Actions to them. Run once, from this repo's root,
  after `az login` and `gh auth login`. Idempotent: re-running updates settings without recreating resources.

.DESCRIPTION
  Resources (one resource group):
    - App Service plan (Linux, B1) + Web App (.NET 8)   the API
    - Azure SQL server + database on the free offer      the database (auto-pauses when idle)
    - Static Web App (Free)                              the Vue front end

  Secrets are read from the API project's local user secrets (dotnet user-secrets) and pushed to the Web App's
  application settings; nothing is printed. The GitHub repositories get the publish profile / deployment token as
  secrets and the app names as variables, which is what .github/workflows/deploy.yml in each repo expects.

.PARAMETER Prefix
  Globally unique name prefix for the resources (lowercase letters and digits), e.g. "socialmeter-jf".
#>
param(
  [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]{3,30}$')] [string] $Prefix,
  [string] $Location = "eastus2",
  [string] $ApiRepo = "Jfrancioso/SocialMeter",
  [string] $UiRepo = "Jfrancioso/SocialMeter.Ui",
  [string] $SqlAdmin = "socialmeter_admin"
)

$ErrorActionPreference = "Stop"
$rg      = "$Prefix-rg"
$plan    = "$Prefix-plan"
$webapp  = "$Prefix-api"
$sqlSrv  = "$Prefix-sql"
$sqlDb   = "SocialMeterDb"
$swa     = "$Prefix-ui"

Write-Host "==> Resource group $rg in $Location"
az group create --name $rg --location $Location --output none

# ---- Database: Azure SQL free offer (auto-pauses; the first request after a pause is slow, which is fine here) ----
$sqlPassword = Read-Host -Prompt "SQL admin password for $SqlAdmin (16+ chars, mixed case, digit, symbol)" -AsSecureString
$sqlPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sqlPassword))

Write-Host "==> SQL server $sqlSrv"
if (-not (az sql server show --name $sqlSrv --resource-group $rg --output none 2>$null)) {
  az sql server create --name $sqlSrv --resource-group $rg --location $Location `
    --admin-user $SqlAdmin --admin-password $sqlPasswordPlain --output none
}
az sql server firewall-rule create --resource-group $rg --server $sqlSrv --name AllowAzureServices `
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none

Write-Host "==> SQL database $sqlDb (free offer)"
if (-not (az sql db show --name $sqlDb --server $sqlSrv --resource-group $rg --output none 2>$null)) {
  az sql db create --name $sqlDb --server $sqlSrv --resource-group $rg `
    --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 `
    --use-free-limit --free-limit-exhaustion-behavior AutoPause --output none
}
$connectionString = "Server=tcp:$sqlSrv.database.windows.net,1433;Initial Catalog=$sqlDb;User ID=$SqlAdmin;Password=$sqlPasswordPlain;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

# ---- API: App Service (B1 keeps the background workers alive; the free tier does not allow Always On) ----
Write-Host "==> App Service plan $plan and web app $webapp"
az appservice plan create --name $plan --resource-group $rg --is-linux --sku B1 --output none
if (-not (az webapp show --name $webapp --resource-group $rg --output none 2>$null)) {
  az webapp create --name $webapp --resource-group $rg --plan $plan --runtime "DOTNETCORE:8.0" --output none
}
az webapp config set --name $webapp --resource-group $rg --always-on true --https-only true --output none
az webapp config connection-string set --name $webapp --resource-group $rg --connection-string-type SQLAzure `
  --settings DefaultConnection="$connectionString" --output none

# ---- Front end: Static Web App (Free) ----
Write-Host "==> Static Web App $swa"
if (-not (az staticwebapp show --name $swa --resource-group $rg --output none 2>$null)) {
  az staticwebapp create --name $swa --resource-group $rg --location $Location --sku Free --output none
}
$uiHost = az staticwebapp show --name $swa --resource-group $rg --query defaultHostname --output tsv

# ---- Application settings: environment, CORS, and every key from local user secrets (ASP.NET uses __ for :) ----
Write-Host "==> Application settings (secrets copied from dotnet user-secrets, not printed)"
$settings = @(
  "ASPNETCORE_ENVIRONMENT=Production",
  "Cors__AllowedOrigins__0=https://$uiHost"
)
$secretLines = dotnet user-secrets list --project SocialMeter 2>$null
foreach ($line in $secretLines) {
  if ($line -match '^\s*([^=]+?)\s*=\s*(.*)$') {
    $key = $Matches[1].Trim() -replace ':', '__'
    $settings += "$key=$($Matches[2])"
  }
}
az webapp config appsettings set --name $webapp --resource-group $rg --settings @settings --output none

# ---- GitHub: the deploy workflows read these ----
Write-Host "==> GitHub secrets and variables"
$profile = az webapp deployment list-publishing-profiles --name $webapp --resource-group $rg --xml
$profile | gh secret set AZURE_WEBAPP_PUBLISH_PROFILE --repo $ApiRepo
gh variable set AZURE_WEBAPP_NAME --repo $ApiRepo --body $webapp

$swaToken = az staticwebapp secrets list --name $swa --resource-group $rg --query properties.apiKey --output tsv
$swaToken | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN --repo $UiRepo
gh variable set AZURE_SWA_CONFIGURED --repo $UiRepo --body "true"
gh variable set VITE_API_BASE --repo $UiRepo --body "https://$webapp.azurewebsites.net"

Write-Host ""
Write-Host "Done."
Write-Host "  API:  https://$webapp.azurewebsites.net   (deploys on the next push to main of $ApiRepo)"
Write-Host "  UI:   https://$uiHost                      (deploys on the next push to main of $UiRepo)"
Write-Host "  Push both repos, or run the Deploy workflows from the Actions tab, to go live."
