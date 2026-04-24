// ---------------------------------------------------------------------------
// Azure Container Apps infrastructure for SwedishCrossword API
// ---------------------------------------------------------------------------
// Deploys: ACR, Managed Identity, Log Analytics, Container Apps Environment,
//          Storage Account + Azure Files share (SMB), Azure SQL with Entra-only
//          authentication, and the Container App with a volume mount for
//          persistent puzzle data.
//
// NOTE:   The app requires Azure SQL in non-Development environments.
//         SQLite is only available for local development.
//         The managed identity is set as the Entra ID admin on the SQL Server
//         — no SQL authentication passwords are used.
//
// Usage (first time):
//   az group create -n rg-svensktkorsord -l swedencentral
//   az deployment group create -g rg-svensktkorsord -f infra/main.bicep
//
// The CI/CD workflow (deploy-azure.yml) handles image build + app updates.
// ---------------------------------------------------------------------------

@minLength(3)
@maxLength(50)
@description('Base name used to derive all resource names')
param appName string = 'svensktkorsord'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Container image tag — set by CI/CD, defaults to empty for first deploy')
param imageTag string = ''

@secure()
@description('HMAC secret for submission token signing. Pass via CI/CD or manual deploy.')
param submissionTokenSecret string = ''

@secure()
@description('Google OAuth client ID for social login.')
param googleClientId string = ''

@secure()
@description('Google OAuth client secret for social login.')
param googleClientSecret string = ''

@secure()
@description('Microsoft OAuth client ID for social login.')
param microsoftClientId string = ''

@secure()
@description('Microsoft OAuth client secret for social login.')
param microsoftClientSecret string = ''

@description('Comma-separated list of admin user IDs (SHA-256 hashes).')
param adminUserIds string = ''

@description('Deploy Azure SQL resources. Set to true when SQL is needed.')
param deployDatabase bool = true

@description('Container Apps environment static outbound IP. Pin the SQL firewall to this single address. Get with: az containerapp env show -g <rg> -n <env> --query properties.staticIp -o tsv. Leave empty to skip the firewall rule (e.g. on a brand-new deploy where the env does not yet exist).')
param containerAppOutboundIp string = ''

@description('Optional developer workstation IP for ad-hoc DB access (migrations, hot-fixes, debugging). Get with: (Invoke-RestMethod ifconfig.me/ip).Trim(). Leave empty in CI.')
param developerWorkstationIp string = ''

@description('Create role assignments (AcrPull on ACR + Key Vault Secrets User on the Key Vault) for the managed identity. Set to true for first-time manual deploy AND any time new role-assignment-bearing resources are added (e.g. when Key Vault was introduced). Set to false for CI/CD which typically lacks Owner / User Access Administrator. If CI/CD reports "Unable to get value using Managed identity ... unable to fetch secret", run a manual deploy once with createRoleAssignment=true (or grant the Key Vault Secrets User role to the managed identity manually) and then re-run CI/CD.')
param createRoleAssignment bool = true

var sqlServerName = '${appName}-sql-${suffix}'
var sqlDbName = '${appName}-db'

// Deterministic suffix for globally unique names
var suffix = uniqueString(resourceGroup().id, appName)
var acrName = toLower(replace('${appName}${suffix}', '-', ''))
var storageAccountName = toLower(take(replace('${appName}st${suffix}', '-', ''), 24))

// Ensure minimum length requirements
var acrNameFinal = length(acrName) < 5 ? '${acrName}acr' : acrName
// take(..., 24) re-clamps after the optional 'st' suffix so the padded value
// never exceeds the storage account 24-char limit (fixes BCP335).
var storageAccountNameFinal = take(length(storageAccountName) < 3 ? '${storageAccountName}st' : storageAccountName, 24)

// ---------------------------------------------------------------------------
// Azure Container Registry (Basic SKU, no admin user)
// ---------------------------------------------------------------------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  // uniqueString() always returns 13 chars, so '${appName}${suffix}' is
  // always >= 13 chars after replace('-', '') — the take(..., 50) only
  // shortens, never empties. Linter can't infer this lower bound.
  #disable-next-line BCP334
  name: take(acrNameFinal, 50)
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
  }
}

// ---------------------------------------------------------------------------
// User-Assigned Managed Identity (for ACR pull and Azure SQL access)
// ---------------------------------------------------------------------------
// Make the user-assigned identity name deterministic but unique per
// subscription to avoid cross-tenant name collisions after directory moves.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-identity-${uniqueString(subscription().id)}'
  location: location
}

// AcrPull role: 7f951dda-4ed3-4680-a7ca-43fe172d538d
var acrPullRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (createRoleAssignment) {
  name: guid(acr.id, identity.id, acrPullRoleId)
  scope: acr
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleId
  }
}

// ---------------------------------------------------------------------------
// Key Vault (RBAC mode) for application secrets
// ---------------------------------------------------------------------------
// Secrets land here once (per rotation) and the Container App reads them via
// its managed identity. This removes plaintext values from the Container App
// revision template and gives us audit, soft-delete, and independent rotation.
var kvName = take('kv-${appName}-${suffix}', 24)

resource vault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: kvName
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

// Key Vault Secrets User: 4633458b-17de-408a-b874-0445c86b69e6
var kvSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

resource kvSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (createRoleAssignment) {
  name: guid(vault.id, identity.id, kvSecretsUserRoleId)
  scope: vault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: kvSecretsUserRoleId
  }
}

// Bootstrap / rotate secrets only when a value is supplied this run.
// Once written, the value persists in KV across deployments; the Container App
// continues to read the latest version even if no value is passed next run.
resource kvSubmissionToken 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (submissionTokenSecret != '') {
  parent: vault
  name: 'submissiontoken-secret'
  properties: {
    value: submissionTokenSecret
  }
}

resource kvGoogleClientId 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (googleClientId != '') {
  parent: vault
  name: 'google-client-id'
  properties: {
    value: googleClientId
  }
}

resource kvGoogleClientSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (googleClientSecret != '') {
  parent: vault
  name: 'google-client-secret'
  properties: {
    value: googleClientSecret
  }
}

resource kvMicrosoftClientId 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (microsoftClientId != '') {
  parent: vault
  name: 'microsoft-client-id'
  properties: {
    value: microsoftClientId
  }
}

resource kvMicrosoftClientSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = if (microsoftClientSecret != '') {
  parent: vault
  name: 'microsoft-client-secret'
  properties: {
    value: microsoftClientSecret
  }
}

// Whether the corresponding KV secret is expected to exist. On a deploy that
// doesn't supply a value, we still wire ACA to KV so it picks up the previously
// stored value. Override these flags only if you ever need to deploy ACA before
// the KV secret has been bootstrapped.
// These are plain booleans — the linter's secret-name heuristic false-positives
// on words like 'Token', 'Secret', 'Auth', 'Client'. Suppressed with rationale.
@description('Wire submission-token secret reference into the Container App. Default: true if a value is supplied this run.')
#disable-next-line secure-secrets-in-params
param hasSubmissionTokenSecret bool = submissionTokenSecret != ''

@description('Wire Google OAuth secret references into the Container App.')
#disable-next-line secure-secrets-in-params
param hasGoogleAuth bool = googleClientId != ''

@description('Wire Microsoft OAuth secret references into the Container App.')
#disable-next-line secure-secrets-in-params
param hasMicrosoftAuth bool = microsoftClientId != ''

// ---------------------------------------------------------------------------
// Log Analytics Workspace
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ---------------------------------------------------------------------------
// Storage Account + Azure Files share for persistent data (/data volume)
// ---------------------------------------------------------------------------
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  // uniqueString() always returns 13 chars, so the input to take(..., 24) is
  // always >= 13 chars — the take() only shortens, never empties. Linter can't
  // infer this lower bound.
  #disable-next-line BCP334
  name: storageAccountNameFinal
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    // Soft-delete recovers accidentally deleted shares within the retention
    // window. 30 days strikes a balance between recoverability and storage
    // cost (deleted shares continue to incur storage charges until purged).
    shareDeleteRetentionPolicy: {
      enabled: true
      days: 30
    }
  }
}

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: 'crossword-data'
  properties: {
    shareQuota: 1 // 1 GB — more than enough for JSON files
  }
}

// ---------------------------------------------------------------------------
// Azure SQL Server + Free tier database (Entra-only authentication)
// ---------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = if (deployDatabase) {
  name: take(sqlServerName, 63)
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      login: identity.name
      sid: identity.properties.principalId
      tenantId: tenant().tenantId
      principalType: 'Application'
    }
  }
}

// Enable Entra-only authentication as a separate child resource so that the
// server and its Entra admin are fully provisioned before this setting is applied.
resource sqlAadOnlyAuth 'Microsoft.Sql/servers/azureADOnlyAuthentications@2023-08-01-preview' = if (deployDatabase) {
  parent: sqlServer
  name: 'Default'
  properties: {
    azureADOnlyAuthentication: true
  }
}

// Firewall: narrow allowlist instead of the magic 0.0.0.0 rule. The previous
// 'AllowAllAzureIps' rule permitted connection attempts from every Azure
// customer's outbound IP space. We now restrict to:
//   1) The Container Apps environment's static outbound IP (the one client
//      that actually needs to reach the DB).
//   2) Optional developer workstation IP for ad-hoc admin access.
//
// Auth is still Entra-only (no SQL passwords), so even if the firewall is
// bypassed via subnet spoofing the attacker still needs a valid managed
// identity token. This is defense-in-depth.
resource sqlFirewallContainerApp 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (deployDatabase && containerAppOutboundIp != '') {
  parent: sqlServer
  name: 'AllowContainerAppEnv'
  properties: {
    startIpAddress: containerAppOutboundIp
    endIpAddress: containerAppOutboundIp
  }
}

resource sqlFirewallDev 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (deployDatabase && developerWorkstationIp != '') {
  parent: sqlServer
  name: 'AllowDeveloperWorkstation'
  properties: {
    startIpAddress: developerWorkstationIp
    endIpAddress: developerWorkstationIp
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = if (deployDatabase) {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368 // 32 GB (free tier limit)
    // autoPauseDelay omitted: Free Limit databases require the default
    // auto-pause delay (60 min) and reject custom values.
    minCapacity: json('0.5')
    useFreeLimit: true
    // BillOverUsage keeps the database online once the monthly free
    // vCore-second quota (~100k s) is exhausted, billing per-second over the
    // free allowance instead of pausing the DB until the next month. The
    // 60-min idle auto-pause still applies, so cost stays near zero when idle.
    freeLimitExhaustionBehavior: 'BillOverUsage'
  }
}

// Point-in-time restore window: 35 days (the maximum, free of charge for the
// underlying differential backups; only outbound restore costs apply).
resource sqlPitrPolicy 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01-preview' = if (deployDatabase) {
  parent: sqlDb
  name: 'default'
  properties: {
    retentionDays: 35
    diffBackupIntervalInHours: 24
  }
}

// Long-term retention is intentionally NOT configured here.
// Azure rejects LTR on serverless databases with auto-pause enabled
// (LtrConfigPolicyUnsupportedIfAutoPauseEnabled), and the Free tier forces
// auto-pause on. Re-enable LTR if/when this DB is migrated to a paid tier
// without auto-pause:
//
//   resource sqlLtrPolicy 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-08-01-preview' = if (deployDatabase) {
//     parent: sqlDb
//     name: 'default'
//     properties: {
//       weeklyRetention: 'P4W'
//       monthlyRetention: 'P12M'
//       yearlyRetention: 'P7Y'
//       weekOfYear: 1
//     }
//   }

// Connection Timeout=60 gives the serverless DB time to resume from auto-pause
// (cold-start typically takes 30–60s). ConnectRetryCount/Interval cover broken
// connections after login; initial-login retries are handled in code.
var sqlConnectionString = deployDatabase ? 'Server=tcp:${sqlServer!.properties.fullyQualifiedDomainName},1433;Database=${sqlDbName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;ConnectRetryCount=10;ConnectRetryInterval=10;' : ''

// ---------------------------------------------------------------------------
// Container Apps Environment
// ---------------------------------------------------------------------------
resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Link the Azure Files share to the environment
resource envStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: containerEnv
  name: 'crossworddata'
  properties: {
    azureFile: {
      accountName: storageAccount.name
      accountKey: storageAccount.listKeys().keys[0].value
      shareName: fileShare.name
      accessMode: 'ReadWrite'
    }
  }
}

// On first deploy imageTag is empty — use a public placeholder so the Container
// App can be created before anything is pushed to ACR.  CI/CD always provides a
// real tag, which overrides this.
var hasImage = imageTag != ''
var containerImage = hasImage
  ? '${acr.properties.loginServer}/${appName}:${imageTag}'
  : 'mcr.microsoft.com/k8se/quickstart:latest'

var adminIdList = adminUserIds != '' ? split(adminUserIds, ',') : []

var adminEnvVars = [for (id, i) in adminIdList: {
  name: 'Authorization__AdminUserIds__${i}'
  value: id
}]
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: union(
        hasSubmissionTokenSecret ? [
          {
            name: 'submissiontoken-secret'
            keyVaultUrl: '${vault.properties.vaultUri}secrets/submissiontoken-secret'
            identity: identity.id
          }
        ] : [],
        hasGoogleAuth ? [
          {
            name: 'google-client-id'
            keyVaultUrl: '${vault.properties.vaultUri}secrets/google-client-id'
            identity: identity.id
          }
          {
            name: 'google-client-secret'
            keyVaultUrl: '${vault.properties.vaultUri}secrets/google-client-secret'
            identity: identity.id
          }
        ] : [],
        hasMicrosoftAuth ? [
          {
            name: 'microsoft-client-id'
            keyVaultUrl: '${vault.properties.vaultUri}secrets/microsoft-client-id'
            identity: identity.id
          }
          {
            name: 'microsoft-client-secret'
            keyVaultUrl: '${vault.properties.vaultUri}secrets/microsoft-client-secret'
            identity: identity.id
          }
        ] : []
      )
      registries: [
        {
          server: acr.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: union(
            [
              { name: 'Storage__PuzzlePath', value: '/data/puzzles' }
              { name: 'Storage__LeaderboardPath', value: '/data/leaderboard' }
              { name: 'SWEDISH_CROSSWORD_CACHE_PATH', value: '/data/cache' }
              { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            ],
            sqlConnectionString != '' ? [
              { name: 'ConnectionStrings__Leaderboard', value: sqlConnectionString }
            ] : [],
            hasSubmissionTokenSecret ? [
              { name: 'SubmissionToken__Secret', secretRef: 'submissiontoken-secret' }
            ] : [],
            hasGoogleAuth ? [
              { name: 'Authentication__Google__ClientId', secretRef: 'google-client-id' }
              { name: 'Authentication__Google__ClientSecret', secretRef: 'google-client-secret' }
            ] : [],
            hasMicrosoftAuth ? [
              { name: 'Authentication__Microsoft__ClientId', secretRef: 'microsoft-client-id' }
              { name: 'Authentication__Microsoft__ClientSecret', secretRef: 'microsoft-client-secret' }
            ] : [],
            adminEnvVars
          )
          volumeMounts: [
            {
              volumeName: 'data'
              mountPath: '/data'
            }
          ]
          // Health probes are only attached when a real image is deployed.
          // The placeholder `quickstart` image used on first-time deploy
          // listens on port 80 and has no /api/health endpoint, which would
          // cause the Startup probe to fail and the revision never to become
          // Ready.
          probes: hasImage ? [
            {
              type: 'Liveness'
              httpGet: {
                path: '/api/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 30
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/api/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Startup'
              httpGet: {
                path: '/api/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              timeoutSeconds: 5
              failureThreshold: 30
            }
          ] : []
        }
      ]
      scale: {
        // Keep at least 1 replica running to avoid cold-start latency that
        // degrades Core Web Vitals (TTFB/LCP) and hurts SEO rankings.
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'data'
          storageType: 'AzureFile'
          storageName: envStorage.name
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// Resource group lock
// ---------------------------------------------------------------------------
// CanNotDelete prevents accidental teardown of the entire resource group
// (the most common cause of total outage in single-RG apps). Reads and
// updates remain unrestricted, so CI/CD continues to function normally.
//
// Locks require Microsoft.Authorization/locks/write permission, which the
// CI service principal (Contributor) does NOT have. Same pattern as
// createRoleAssignment: apply once manually with the command below, then
// pass enableResourceGroupLock=false in CI so the deployment skips this
// resource on subsequent runs.
//
//   az lock create --name protect-rg --resource-group rg-svensktkorsord \
//                  --lock-type CanNotDelete \
//                  --notes "Production resource group..."
//
// To remove the lock for an intentional teardown:
//   az lock delete --name protect-rg --resource-group rg-svensktkorsord
@description('Apply a CanNotDelete lock on the resource group via this template. Requires Owner or User Access Administrator. Set to false in CI; apply the lock once manually with `az lock create` instead.')
param enableResourceGroupLock bool = true

resource rgLock 'Microsoft.Authorization/locks@2020-05-01' = if (enableResourceGroupLock) {
  name: 'protect-rg'
  properties: {
    level: 'CanNotDelete'
    notes: 'Production resource group. Remove this lock only for intentional teardown.'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
@description('ACR login server (e.g. myacr.azurecr.io)')
output acrLoginServer string = acr.properties.loginServer

@description('ACR resource name')
output acrName string = acr.name

@description('Container App FQDN')
output appUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'

@description('Container App auto-generated FQDN (for CNAME records)')
output appFqdn string = containerApp.properties.configuration.ingress.fqdn

@description('Domain verification ID — use as TXT record value for asuid.{subdomain}')
output customDomainVerificationId string = containerApp.properties.customDomainVerificationId

@description('Container Apps Environment name')
output environmentName string = containerEnv.name

@description('Key Vault name (where app secrets live)')
output keyVaultName string = vault.name

@description('Key Vault URI')
output keyVaultUri string = vault.properties.vaultUri

@description('Resource group name (for CI/CD reference)')
output resourceGroup string = az.resourceGroup().name
