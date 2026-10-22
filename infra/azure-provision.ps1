<#
.SYNOPSIS
  Creates the Azure resources SocialMeter runs on and wires GitHub Actions to them. Run from this repo's root after
  `az login` and `gh auth login`. Idempotent: re-running updates settings without recreating resources.

.DESCRIPTION
  Resources (one resource group):
    - App Service plan (Linux, B1) + Web App (.NET 8)   the API
    - Azure SQL server + database on the free offer      the database (auto-pauses when idle)
    - Static Web App (Free)                              the Vue front end

  Secrets come from the API project's local user secrets (dotnet user-secrets): the API keys become Web App
  application settings, and Sql:AdminPassword is the database admin password (prompted for if absent). Nothing is
  printed. The GitHub repositories get the publish profile / deployment token as secrets and the app names as
  variables, which is what .github/workflows/deploy.yml in each repo expects.

.PARAMETER Prefix
  Globally unique name prefix for the resources (lowercase letters, digits, hyphens), e.g. "socialmeter-jf".
#>
param(
  [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]{3,30}$')] [string] $Prefix,
  [string] $Location = "eastus2",
  [string] $ApiRepo = "Jfrancioso/SocialMeter",
  [string] $UiRepo = "Jfrancioso/SocialMeter.Ui",
  [string] $SqlAdmin = "socialmeter_admin"
)

$ErrorActionPreference = "Stop"
# PowerShell 7.4+ would turn every non-zero az exit code into a terminating error, which breaks the "does it exist" checks.
$PSNativeCommandUseErrorActionPreference = $false

$rg     = "$Prefix-rg"
$plan   = "$Prefix-plan"
$webapp = "$Prefix-api"
$sqlSrv = "$Prefix-sql"
$sqlDb  = "SocialMeterDb"
$swa    = "$Prefix-ui"

function Invoke-Az {
  # Runs az and stops the script with the command's own message when it fails. Returns stdout only, as one string:
  # az writes warnings to stderr even on success, and those arrive here as ErrorRecords that must not pollute values.
  param([Parameter(ValueFromRemainingArguments)] [string[]] $Args)
  $raw = & az @Args 2>&1
  $stdout = ($raw | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
  if ($LASTEXITCODE -ne 0) { throw "az $($Args[0..1] -join ' ') failed:`n$($raw -join "`n")" }
  return $stdout
}

function Test-Az {
  # True when an `az ... show` succeeds, i.e. the resource exists.
  param([Parameter(ValueFromRemainingArguments)] [string[]] $Args)
  & az @Args --output none 2>$null | Out-Null
  return $LASTEXITCODE -eq 0
}

# ---- Local user secrets: API keys and, if present, the SQL admin password ----
$secrets = @{}
foreach ($line in (dotnet user-secrets list --project SocialMeter 2>$null)) {
  if ($line -match '^\s*([^=]+?)\s*=\s*(.*)$') { $secrets[$Matches[1].Trim()] = $Matches[2] }
}
if ($secrets.ContainsKey("Sql:AdminPassword")) {
  $sqlPasswordPlain = $secrets["Sql:AdminPassword"]
  Write-Host "==> SQL admin password: using Sql:AdminPassword from user secrets"
} else {
  $secure = Read-Host -Prompt "SQL admin password for $SqlAdmin (16+ chars, mixed case, digit, symbol)" -AsSecureString
  $sqlPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

Write-Host "==> Resource group $rg in $Location"
Invoke-Az group create --name $rg --location $Location --output none | Out-Null

# ---- Database: Azure SQL free offer (auto-pauses; the first request after a pause is slow, which is fine here) ----
Write-Host "==> SQL server $sqlSrv"
if (-not (Test-Az sql server show --name $sqlSrv --resource-group $rg)) {
  Invoke-Az sql server create --name $sqlSrv --resource-group $rg --location $Location `
    --admin-user $SqlAdmin --admin-password $sqlPasswordPlain --output none | Out-Null
}
Invoke-Az sql server firewall-rule create --resource-group $rg --server $sqlSrv --name AllowAzureServices `
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none | Out-Null

Write-Host "==> SQL database $sqlDb (free offer)"
if (-not (Test-Az sql db show --name $sqlDb --server $sqlSrv --resource-group $rg)) {
  Invoke-Az sql db create --name $sqlDb --server $sqlSrv --resource-group $rg `
    --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 `
    --use-free-limit --free-limit-exhaustion-behavior AutoPause --output none | Out-Null
}
$connectionString = "Server=tcp:$sqlSrv.database.windows.net,1433;Initial Catalog=$sqlDb;User ID=$SqlAdmin;Password=$sqlPasswordPlain;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

# ---- API: App Service (B1 keeps the background workers alive; the free tier does not allow Always On) ----
Write-Host "==> App Service plan $plan and web app $webapp"
Invoke-Az appservice plan create --name $plan --resource-group $rg --is-linux --sku B1 --output none | Out-Null
if (-not (Test-Az webapp show --name $webapp --resource-group $rg)) {
  Invoke-Az webapp create --name $webapp --resource-group $rg --plan $plan --runtime "DOTNETCORE:8.0" --output none | Out-Null
}
Invoke-Az webapp config set --name $webapp --resource-group $rg --always-on true --output none | Out-Null
# New App Service apps ship with basic publishing credentials disabled; the publish-profile deploy in GitHub Actions needs them.
Invoke-Az resource update --resource-group $rg --name scm --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies `
  --parent "sites/$webapp" --set properties.allow=true --output none | Out-Null
Invoke-Az webapp update --name $webapp --resource-group $rg --https-only true --output none | Out-Null
Invoke-Az webapp config connection-string set --name $webapp --resource-group $rg --connection-string-type SQLAzure `
  --settings "DefaultConnection=$connectionString" --output none | Out-Null

# ---- Front end: Static Web App (Free) ----
Write-Host "==> Static Web App $swa"
if (-not (Test-Az staticwebapp show --name $swa --resource-group $rg)) {
  Invoke-Az staticwebapp create --name $swa --resource-group $rg --location $Location --sku Free --output none | Out-Null
}
$uiHost = (Invoke-Az staticwebapp show --name $swa --resource-group $rg --query defaultHostname --output tsv).Trim()

# ---- Application settings: environment, CORS, and every API key from user secrets (ASP.NET uses __ for :) ----
Write-Host "==> Application settings (keys copied from dotnet user-secrets, not printed)"
$settings = @(
  "ASPNETCORE_ENVIRONMENT=Production",
  "Cors__AllowedOrigins__0=https://$uiHost",
  # Conservative spend defaults for a fresh deployment; raise them in the portal once traffic and cost are known.
  "X__MaxResults=20",
  "SocialMeter__MaxLivePullsPerHour=6",
  "SocialMeter__TrackedListPages=1"
)
foreach ($key in $secrets.Keys) {
  if ($key -like "Sql:*") { continue }                     # the password lives in the connection string only
  $settings += "$($key -replace ':', '__')=$($secrets[$key])"
}
Invoke-Az webapp config appsettings set --name $webapp --resource-group $rg --settings @settings --output none | Out-Null

# ---- GitHub: the deploy workflows read these ----
Write-Host "==> GitHub secrets and variables"
$publishProfile = Invoke-Az webapp deployment list-publishing-profiles --name $webapp --resource-group $rg --xml
$publishProfile | gh secret set AZURE_WEBAPP_PUBLISH_PROFILE --repo $ApiRepo
gh variable set AZURE_WEBAPP_NAME --repo $ApiRepo --body $webapp

$swaToken = (Invoke-Az staticwebapp secrets list --name $swa --resource-group $rg --query properties.apiKey --output tsv).Trim()
$swaToken | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN --repo $UiRepo
gh variable set AZURE_SWA_CONFIGURED --repo $UiRepo --body "true"
gh variable set VITE_API_BASE --repo $UiRepo --body "https://$webapp.azurewebsites.net"

Write-Host ""
Write-Host "Done."
Write-Host "  API:  https://$webapp.azurewebsites.net   (deploys on the next push to main of $ApiRepo)"
Write-Host "  UI:   https://$uiHost                      (deploys on the next push to main of $UiRepo)"
Write-Host "  Push both repos, or run the Deploy workflows from the Actions tab, to go live."
