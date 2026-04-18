#!/usr/bin/env pwsh
# ---------------------------------------------------------------------------
# setup-sql-user.ps1 — One-time setup: create a contained database user for
#                       your Entra identity so you can run maintenance scripts
#                       like reset-data.ps1 without swapping the SQL AAD admin.
#
# How it works:
#   1. Temporarily sets YOU as the SQL AAD admin (replacing the managed identity)
#   2. Creates a contained database user for your Entra account with db_owner
#   3. Immediately restores the managed identity as the SQL AAD admin
#
# The admin swap lasts only a few seconds. This is a ONE-TIME operation.
# After this, your Entra identity is a permanent db_owner — no more swaps needed.
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - SqlServer PowerShell module (Install-Module SqlServer)
#   - Access to the resource group
#
# Usage:
#   ./scripts/setup-sql-user.ps1
#   ./scripts/setup-sql-user.ps1 -UserEmail someone@contoso.com
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [string]$ResourceGroup = 'rg-svensktkorsord',
    [string]$AppName       = 'svensktkorsord',

    # The Entra UPN (email) of the user to grant access.
    # Defaults to the currently signed-in user.
    [string]$UserEmail = ''
)

$ErrorActionPreference = 'Stop'

# Ensure the SqlServer module is available
if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    Write-Error "The 'SqlServer' PowerShell module is required. Install it with: Install-Module SqlServer -Scope CurrentUser"
    exit 1
}
Import-Module SqlServer -ErrorAction Stop

# ---------------------------------------------------------------------------
# 1. Resolve the SQL Server and database
# ---------------------------------------------------------------------------
Write-Host "`n=== Resolving Azure SQL server ===" -ForegroundColor Cyan

$sqlServer = az sql server list `
    --resource-group $ResourceGroup `
    --query "[?contains(name, '$AppName')].name | [0]" `
    --output tsv

if (-not $sqlServer) {
    Write-Error "Could not find a SQL server in resource group '$ResourceGroup'."
    exit 1
}
Write-Host "  SQL server: $sqlServer"

$sqlDb = az sql db list `
    --resource-group $ResourceGroup `
    --server $sqlServer `
    --query "[?name != 'master'].name | [0]" `
    --output tsv

if (-not $sqlDb) {
    Write-Error "Could not find a user database on server '$sqlServer'."
    exit 1
}
Write-Host "  Database:   $sqlDb"

$serverFqdn = "$sqlServer.database.windows.net"

# ---------------------------------------------------------------------------
# 2. Resolve the current user and the managed identity
# ---------------------------------------------------------------------------
Write-Host "`n=== Resolving identities ===" -ForegroundColor Cyan

$currentUser = az ad signed-in-user show `
    --query '{displayName:displayName, id:id, upn:userPrincipalName}' `
    --output json | ConvertFrom-Json

if (-not $UserEmail) { $UserEmail = $currentUser.upn }
Write-Host "  User to add: $UserEmail"

$identityName = "$AppName-identity"
$miJson = az identity show `
    --resource-group $ResourceGroup `
    --name $identityName `
    --query '{name:name, principalId:principalId}' `
    --output json | ConvertFrom-Json
Write-Host "  Managed identity: $identityName ($($miJson.principalId))"

# Validate email for bracket-quoting safety
if ($UserEmail -notmatch '^[^''\[\]]+$') {
    Write-Error "User email contains invalid characters."
    exit 1
}

# ---------------------------------------------------------------------------
# 3. Add a temporary firewall rule
# ---------------------------------------------------------------------------
Write-Host "`n=== Adding temporary firewall rule ===" -ForegroundColor Cyan
$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org')
$firewallRuleName = "setup-sql-user-$([guid]::NewGuid().ToString('N').Substring(0,8))"
az sql server firewall-rule create `
    --resource-group $ResourceGroup `
    --server $sqlServer `
    --name $firewallRuleName `
    --start-ip-address $myIp `
    --end-ip-address $myIp `
    --output none
Write-Host "  Added rule '$firewallRuleName' for IP $myIp"

try {
    # ---------------------------------------------------------------------------
    # 4. Temporarily set the current user as SQL AAD admin
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Temporarily setting you as SQL AAD admin ===" -ForegroundColor Yellow
    az sql server ad-admin create `
        --resource-group $ResourceGroup `
        --server $sqlServer `
        --display-name $currentUser.displayName `
        --object-id $currentUser.id `
        --output none
    Write-Host "  Set '$($currentUser.displayName)' as SQL AAD admin"
    Write-Host "  Waiting for propagation..." -ForegroundColor Yellow
    Start-Sleep -Seconds 15

    # Get a fresh token now that we're admin
    $tokenJson = az account get-access-token --resource https://database.windows.net/ --output json | ConvertFrom-Json
    $accessToken = $tokenJson.accessToken

    # ---------------------------------------------------------------------------
    # 5. Create the contained database user
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Creating contained database user ===" -ForegroundColor Cyan

    $sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$UserEmail')
BEGIN
    CREATE USER [$UserEmail] FROM EXTERNAL PROVIDER;
    ALTER ROLE db_owner ADD MEMBER [$UserEmail];
    PRINT 'Created user and granted db_owner.';
END
ELSE
BEGIN
    ALTER ROLE db_owner ADD MEMBER [$UserEmail];
    PRINT 'User already exists - ensured db_owner membership.';
END
"@

    Invoke-Sqlcmd -ServerInstance $serverFqdn -Database $sqlDb -AccessToken $accessToken -Query $sql
    Write-Host "  Done! '$UserEmail' now has db_owner on [$sqlDb]." -ForegroundColor Green
}
finally {
    # ---------------------------------------------------------------------------
    # 6. ALWAYS restore the managed identity as SQL AAD admin
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Restoring managed identity as SQL admin ===" -ForegroundColor Cyan
    az sql server ad-admin create `
        --resource-group $ResourceGroup `
        --server $sqlServer `
        --display-name $identityName `
        --object-id $miJson.principalId `
        --output none
    Write-Host "  Restored '$identityName' as SQL AAD admin" -ForegroundColor Green

    # ---------------------------------------------------------------------------
    # 7. Remove the temporary firewall rule
    # ---------------------------------------------------------------------------
    Write-Host "`n=== Removing temporary firewall rule ===" -ForegroundColor Cyan
    az sql server firewall-rule delete `
        --resource-group $ResourceGroup `
        --server $sqlServer `
        --name $firewallRuleName `
        --output none 2>$null
    Write-Host "  Removed rule '$firewallRuleName'"
}

Write-Host ""
Write-Host "Setup complete! You can now run scripts/reset-data.ps1 from your local machine." -ForegroundColor Cyan
Write-Host ""
