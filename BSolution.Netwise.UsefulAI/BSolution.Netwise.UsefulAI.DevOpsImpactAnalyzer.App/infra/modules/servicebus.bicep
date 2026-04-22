targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

param location string
param resourcePrefix string
param environment string
param tags object

@description('Principal ID of the Function App system-assigned managed identity for RBAC assignments')
param functionAppPrincipalId string

@description('Namespace name. Must be globally unique. Provided by main.bicep so the Function App can pre-compute its FQDN.')
param serviceBusNamespaceName string

@description('SKU of the Service Bus namespace. Standard supports queues + topics; Basic only queues.')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Standard'

// ── Built-in Role Definition IDs ──────────────────────────────────────────────

// Sender + Receiver scoped to the namespace (queues inherit). Owner not required.
var serviceBusDataSenderRoleId   = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

// ── Service Bus Namespace ─────────────────────────────────────────────────────

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespaceName
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

// ── Queues ────────────────────────────────────────────────────────────────────
// Pipeline indeksacji work itemów: workitem-ids → workitem-details → workitem-documents
// MaxDeliveryCount = 5, lockDuration = 5 min — wystarcza na embedding + DevOps API calls.
// EnableDeadLettering on lock expiration ułatwia diagnostykę zakleszczonych wiadomości.

var queueNames = [
  'workitem-ids'
  'workitem-details'
  'workitem-documents'
]

resource queues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for queueName in queueNames: {
  parent: serviceBus
  name: queueName
  properties: {
    lockDuration: 'PT5M'
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    requiresSession: false
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    enableBatchedOperations: true
    maxDeliveryCount: 5
    enablePartitioning: false
    enableExpress: false
  }
}]

// ── RBAC: Function App MI → Service Bus (sender + receiver) ───────────────────

resource sbSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBus
  name: guid(serviceBus.id, functionAppPrincipalId, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource sbReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBus
  name: guid(serviceBus.id, functionAppPrincipalId, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output serviceBusName string                = serviceBus.name
output serviceBusFullyQualifiedNamespace string = '${serviceBus.name}.servicebus.windows.net'
output queueNames array                     = queueNames
