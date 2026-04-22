targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

param location string
param resourcePrefix string
param environment string
param aspSku string
param tags object

@description('Service Bus namespace name. The Function App uses managed identity (no connection string) to authenticate against Service Bus, so we only need the FQDN.')
param serviceBusNamespaceName string

// ── Naming ────────────────────────────────────────────────────────────────────
// Storage account: lowercase alphanumeric only, max 24 chars

var cleanPrefix        = toLower(replace(resourcePrefix, '-', ''))
var storageAccountName = take('${cleanPrefix}fn${uniqueString(resourceGroup().id)}', 24)
var keyVaultName       = take('${cleanPrefix}kv${uniqueString(resourceGroup().id)}', 24)
var aspName            = '${resourcePrefix}-asp-${environment}'
var functionAppName    = '${resourcePrefix}-func-${environment}'
var appInsightsName    = '${resourcePrefix}-appi-${environment}'
var logAnalyticsName   = '${resourcePrefix}-law-${environment}'

// ── Built-in Role Definition IDs ──────────────────────────────────────────────

var keyVaultSecretsUserRoleId      = '4633458b-17de-408a-b874-0445c86b69e6'
var storageBlobDataOwnerRoleId     = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributorId  = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorId  = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

// ── Storage Account (Function App trigger state, timer coordination) ───────────

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// Roles: Function App managed identity → Storage (keyless AzureWebJobsStorage auth)
resource storageBlobOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageBlobDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageQueueDataContributorId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageTableDataContributorId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Log Analytics Workspace ───────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Application Insights ──────────────────────────────────────────────────────

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ── Key Vault (for PAT, API keys stored as Key Vault references) ──────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ── App Service Plan (Windows, Dedicated) ─────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: aspName
  location: location
  tags: tags
  sku: {
    name: aspSku
  }
  properties: {
    reserved: false // Windows
  }
}

// ── Function App (.NET 10 isolated, Windows ASP hosting) ─────────────────────

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      // .NET 10 isolated worker — if deployment fails, check Azure Functions .NET 10 support status
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // App settings are managed by the separate `functionAppSettings` resource below
      // so they can dependsOn the Key Vault role assignment without creating a cycle.
    }
  }
}

// ── Role: Key Vault Secrets User → Function App ───────────────────────────────

resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, functionApp.id, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── App Settings (separate resource so it can dependsOn the KV role) ─────────
// IMPORTANT: a `sites/config 'appsettings'` resource REPLACES all app settings on the
// Function App, so every setting (including system ones) must be listed here.
// Secret names in Key Vault use `--` (KV disallows `:` and `__`); app setting names use
// `__` which the .NET configuration provider maps to `:` in IConfiguration.

resource functionAppSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    // Keyless storage auth via managed identity (no connection string needed)
    AzureWebJobsStorage__accountName: storage.name
    AzureWebJobsStorage__credential: 'managedidentity'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    WEBSITE_RUN_FROM_PACKAGE: '1'

    // ── Service Bus (keyless via managed identity) ──
    // Connection name "ServiceBus" used by ServiceBusTrigger / ServiceBusOutput bindings
    // in the work item indexing pipeline (WorkItemIndexer/Fetch/BuildDocuments/Upload).
    ServiceBus__fullyQualifiedNamespace: '${serviceBusNamespaceName}.servicebus.windows.net'

    // ── Application secrets sourced from Key Vault ──
    Foundry__Endpoint:                  '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/Foundry--Endpoint)'
    AzureSearch__Endpoint:              '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureSearch--Endpoint)'
    AzureSearch__ApiKey:                '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureSearch--ApiKey)'
    AzureOpenAI__Endpoint:              '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureOpenAI--Endpoint)'
    AzureOpenAI__ApiKey:                '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureOpenAI--ApiKey)'
    AzureOpenAI__ApiVersion:            '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureOpenAI--ApiVersion)'
    AzureOpenAI__EmbeddingDeployment:   '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureOpenAI--EmbeddingDeployment)'
    AzureDevOps__Organization:          '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureDevOps--Organization)'
    AzureDevOps__Project:               '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureDevOps--Project)'
    AzureDevOps__PersonalAccessToken:   '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureDevOps--PersonalAccessToken)'
  }
  // Ensure RBAC propagation finishes before the Function App tries to resolve the
  // @Microsoft.KeyVault(...) references on cold start.
  dependsOn: [
    kvSecretsUserRole
  ]
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output functionAppName string        = functionApp.name
output functionAppUrl string         = 'https://${functionApp.properties.defaultHostName}'
output functionAppPrincipalId string = functionApp.identity.principalId
output keyVaultName string           = keyVault.name
output keyVaultUri string            = keyVault.properties.vaultUri
output storageAccountName string     = storage.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
