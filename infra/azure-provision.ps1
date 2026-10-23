<#
.SYNOPSIS
  Creates the Azure resources SocialMeter runs on and wires GitHub Actions to them. Run from this repo's root after
  `az login` and `gh auth login`. Idempotent: re-running updates settings without recreating resources.

.DESCRIPTION
  Resources (one resource group):
    - App Service plan (Linux, B1) + Web App (.NET 8)   the API
    - Azure SQL server + Basic database (~$4.90/month)  the database (always on, 2 GB), public access off
    - Virtual network + private endpoint + private DNS  how the API reaches the database (~$8/month)
    - Static Web App (Free)                              the Vue front end
  Monitoring (Application Insights, alerts) and the Cloudflare side are set up separately; see docs/OPERATIONS.md.

  Secrets come from the API project's local user secrets (dotnet user-secrets): the API keys become Web App
  application settings, and Sql:AdminPassword is the database admin password (prompted for if absent). Nothing is
  printed. The API repository gets the deploy identity's ids as variables (its deploy signs in with OIDC, no secret);
  the UI repository gets the Static Web App deployment token as a secret. That is what .github/workflows/deploy.yml in
  each repo expects.

.PARAMETER Prefix
  Globally unique name prefix for the resources (lowercase letters, digits, hyphens), e.g. "socialmeter-jf".
#>
param(
  [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]{3,30}$')] [string] $Prefix,
  [string] $Location = "centralus",   # East US 2 refused new SQL servers for this subscription
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

# ---- Database: Azure SQL Basic (always on, flat price). The free serverless offer was tried first: a public site keeps
# it awake, so its 100,000 monthly vCore-seconds ran out in days and it would pause until the 1st. ----
Write-Host "==> SQL server $sqlSrv"
if (-not (Test-Az sql server show --name $sqlSrv --resource-group $rg)) {
  Invoke-Az sql server create --name $sqlSrv --resource-group $rg --location $Location `
    --admin-user $SqlAdmin --admin-password $sqlPasswordPlain --output none | Out-Null
}
# No "allow Azure services" firewall rule: it let any Azure customer's resources reach the login prompt. The API reaches
# the database through a private endpoint instead (below), and public access is switched off once that is in place.

# ---- Private network: the API joins it (outbound), the database gets a private address in it (2026-10-23) ----
$vnet = "$Prefix-vnet"
Write-Host "==> Virtual network $vnet (subnets: app for the API, private-endpoints for the database)"
if (-not (Test-Az network vnet show --name $vnet --resource-group $rg)) {
  Invoke-Az network vnet create --name $vnet --resource-group $rg --location $Location --address-prefixes 10.20.0.0/16 `
    --subnet-name app --subnet-prefixes 10.20.1.0/26 --output none | Out-Null
  Invoke-Az network vnet subnet update --vnet-name $vnet --name app --resource-group $rg `
    --delegations Microsoft.Web/serverFarms --output none | Out-Null
  Invoke-Az network vnet subnet create --vnet-name $vnet --name private-endpoints --resource-group $rg `
    --address-prefixes 10.20.2.0/27 --output none | Out-Null
}
$dnsZone = "privatelink.database.windows.net"
if (-not (Test-Az network private-dns zone show --name $dnsZone --resource-group $rg)) {
  Invoke-Az network private-dns zone create --name $dnsZone --resource-group $rg --output none | Out-Null
  Invoke-Az network private-dns link vnet create --zone-name $dnsZone --name "$vnet-link" --resource-group $rg `
    --virtual-network $vnet --registration-enabled false --output none | Out-Null
}
$sqlPe = "$sqlSrv-pe"
Write-Host "==> Private endpoint $sqlPe for the database"
if (-not (Test-Az network private-endpoint show --name $sqlPe --resource-group $rg)) {
  $sqlId = (Invoke-Az sql server show --name $sqlSrv --resource-group $rg --query id --output tsv).Trim()
  Invoke-Az network private-endpoint create --name $sqlPe --resource-group $rg --location $Location --vnet-name $vnet `
    --subnet private-endpoints --private-connection-resource-id $sqlId --group-id sqlServer `
    --connection-name "$sqlPe-conn" --output none | Out-Null
  # The zone group writes the A record, so the server's usual name resolves to the private address inside the network.
  Invoke-Az network private-endpoint dns-zone-group create --endpoint-name $sqlPe --name default --resource-group $rg `
    --private-dns-zone $dnsZone --zone-name sql --output none | Out-Null
}

Write-Host "==> SQL database $sqlDb (Basic)"
if (-not (Test-Az sql db show --name $sqlDb --server $sqlSrv --resource-group $rg)) {
  Invoke-Az sql db create --name $sqlDb --server $sqlSrv --resource-group $rg `
    --edition Basic --service-objective Basic --max-size 2GB --output none | Out-Null
}
$connectionString = "Server=tcp:$sqlSrv.database.windows.net,1433;Initial Catalog=$sqlDb;User ID=$SqlAdmin;Password=$sqlPasswordPlain;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

# ---- API: App Service (B1 keeps the background workers alive; the free tier does not allow Always On) ----
Write-Host "==> App Service plan $plan and web app $webapp"
Invoke-Az appservice plan create --name $plan --resource-group $rg --is-linux --sku B1 --output none | Out-Null
if (-not (Test-Az webapp show --name $webapp --resource-group $rg)) {
  Invoke-Az webapp create --name $webapp --resource-group $rg --plan $plan --runtime "DOTNETCORE:8.0" --output none | Out-Null
}
Invoke-Az webapp config set --name $webapp --resource-group $rg --always-on true --output none | Out-Null
# Client affinity off: one stateless instance needs no sticky sessions, and it would set ARRAffinity cookies on every response.
Invoke-Az webapp update --name $webapp --resource-group $rg --https-only true --client-affinity-enabled false --output none | Out-Null
Invoke-Az webapp config connection-string set --name $webapp --resource-group $rg --connection-string-type SQLAzure `
  --settings "DefaultConnection=$connectionString" --output none | Out-Null

# Join the API to the network so it reaches the database's private endpoint. Only private traffic goes through it:
# "application traffic" (all outbound) is switched back off, because a new network has no route to the internet and
# TMDB, X, Bluesky and Claude would stop answering.
Write-Host "==> API joins $vnet (private traffic only), then database public access off"
$subnetId = (Invoke-Az webapp show --name $webapp --resource-group $rg --query virtualNetworkSubnetId --output tsv).Trim()
if (-not $subnetId) {
  Invoke-Az webapp vnet-integration add --name $webapp --resource-group $rg --vnet $vnet --subnet app --output none | Out-Null
}
$webappId = (Invoke-Az webapp show --name $webapp --resource-group $rg --query id --output tsv).Trim()
Invoke-Az resource update --ids $webappId --set properties.outboundVnetRouting.applicationTraffic=false --output none | Out-Null
Invoke-Az sql server update --name $sqlSrv --resource-group $rg --enable-public-network false --output none | Out-Null

# ---- Deploys without a stored password: GitHub's OIDC token, trusted only for the API repo's production environment ----
$deployId = "$Prefix-deploy"
Write-Host "==> Deploy identity $deployId (GitHub OIDC; Website Contributor on $webapp only)"
if (-not (Test-Az identity show --name $deployId --resource-group $rg)) {
  Invoke-Az identity create --name $deployId --resource-group $rg --location $Location --output none | Out-Null
}
$identity = Invoke-Az identity show --name $deployId --resource-group $rg --output json | ConvertFrom-Json
# GitHub's token names the owner and the repository by name and by immutable id. The ids matter: a repository that is
# deleted and re-created under the same name by someone else gets a new id, so the trust does not carry over.
$owner, $repoName = $ApiRepo.Split('/')
$ownerId = (gh api "repos/$ApiRepo" --jq .owner.id).Trim()
$repoId = (gh api "repos/$ApiRepo" --jq .id).Trim()
$subject = "repo:$owner@$ownerId/$repoName@$($repoId):environment:production"
$fedVerb = if (Test-Az identity federated-credential show --name github-production --identity-name $deployId --resource-group $rg) { "update" } else { "create" }
Invoke-Az identity federated-credential $fedVerb --name github-production --identity-name $deployId --resource-group $rg `
  --issuer https://token.actions.githubusercontent.com --subject $subject --audiences api://AzureADTokenExchange --output none | Out-Null
Invoke-Az role assignment create --assignee-object-id $identity.principalId --assignee-principal-type ServicePrincipal `
  --role "Website Contributor" --scope $webappId --output none | Out-Null
# With the identity in place, password-based deploys (Kudu and FTP basic auth, the publish profile) stay off.
foreach ($policy in "scm", "ftp") {
  Invoke-Az resource update --ids "$webappId/basicPublishingCredentialsPolicies/$policy" --set properties.allow=false --output none | Out-Null
}

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
  "Guardrails__MaxLivePullsPerHour=6",
  "SocialMeter__TrackedListPages=1"
)
foreach ($key in $secrets.Keys) {
  if ($key -like "Sql:*") { continue }                     # the password lives in the connection string only
  $settings += "$($key -replace ':', '__')=$($secrets[$key])"
}
Invoke-Az webapp config appsettings set --name $webapp --resource-group $rg --settings @settings --output none | Out-Null

# ---- GitHub: the deploy workflows read these ----
Write-Host "==> GitHub secrets and variables"
# The API deploy signs in with OIDC, so it needs ids, not secrets.
gh variable set AZURE_WEBAPP_NAME --repo $ApiRepo --body $webapp
gh variable set AZURE_CLIENT_ID --repo $ApiRepo --body $identity.clientId
gh variable set AZURE_TENANT_ID --repo $ApiRepo --body $identity.tenantId
gh variable set AZURE_SUBSCRIPTION_ID --repo $ApiRepo --body ((Invoke-Az account show --query id --output tsv).Trim())

$swaToken = (Invoke-Az staticwebapp secrets list --name $swa --resource-group $rg --query properties.apiKey --output tsv).Trim()
$swaToken | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN --repo $UiRepo
gh variable set AZURE_SWA_CONFIGURED --repo $UiRepo --body "true"
gh variable set VITE_API_BASE --repo $UiRepo --body "https://$webapp.azurewebsites.net"

Write-Host ""
Write-Host "Done."
Write-Host "  API:  https://$webapp.azurewebsites.net   (deploys on the next push to main of $ApiRepo)"
Write-Host "  UI:   https://$uiHost                      (deploys on the next push to main of $UiRepo)"
Write-Host "  Push both repos, or run the Deploy workflows from the Actions tab, to go live."
