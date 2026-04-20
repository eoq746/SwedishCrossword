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

@description('Create ACR pull role assignment. Set to true for first-time manual deploy, false for CI/CD (requires Owner or User Access Administrator).')
param createRoleAssignment bool = true

var sqlServerName = '${appName}-sql-${suffix}'
var sqlDbName = '${appName}-db'

// Deterministic suffix for globally unique names
var suffix = uniqueString(resourceGroup().id, appName)
var acrName = toLower(replace('${appName}${suffix}', '-', ''))
var storageAccountName = toLower(take(replace('${appName}st${suffix}', '-', ''), 24))

// Ensure minimum length requirements
var acrNameFinal = length(acrName) < 5 ? '${acrName}acr' : acrName
var storageAccountNameFinal = length(storageAccountName) < 3 ? '${storageAccountName}st' : storageAccountName

// ---------------------------------------------------------------------------
// Azure Container Registry (Basic SKU, no admin user)
// ---------------------------------------------------------------------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
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
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-identity'
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

// Allow Azure services (Container Apps) to connect
resource sqlFirewallAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (deployDatabase) {
  parent: sqlServer
  name: 'AllowAllAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
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
    autoPauseDelay: 60 // auto-pause after 60 min inactivity
    minCapacity: json('0.5')
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

var sqlConnectionString = deployDatabase ? 'Server=tcp:${sqlServer!.properties.fullyQualifiedDomainName},1433;Database=${sqlDbName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' : ''

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
        submissionTokenSecret != '' ? [
          {
            name: 'submissiontoken-secret'
            value: submissionTokenSecret
          }
        ] : [],
        googleClientId != '' ? [
          { name: 'google-client-id', value: googleClientId }
          { name: 'google-client-secret', value: googleClientSecret }
        ] : [],
        microsoftClientId != '' ? [
          { name: 'microsoft-client-id', value: microsoftClientId }
          { name: 'microsoft-client-secret', value: microsoftClientSecret }
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
            submissionTokenSecret != '' ? [
              { name: 'SubmissionToken__Secret', secretRef: 'submissiontoken-secret' }
            ] : [],
            googleClientId != '' ? [
              { name: 'Authentication__Google__ClientId', secretRef: 'google-client-id' }
              { name: 'Authentication__Google__ClientSecret', secretRef: 'google-client-secret' }
            ] : [],
            microsoftClientId != '' ? [
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

@description('Resource group name (for CI/CD reference)')
output resourceGroup string = az.resourceGroup().name
