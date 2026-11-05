// TutorDesk on Azure: Container Apps (scale to zero) + PostgreSQL Flexible Server
// + Application Insights. Deploy once with:
//   az deployment group create -g rg-tutordesk -f infra/main.bicep -p infra/main.parameters.json \
//     -p postgresAdminPassword=<secret> jwtSigningKey=<secret>
// After that, GitHub Actions rolls out new images (see docs/deploy-azure.md).

targetScope = 'resourceGroup'

@description('Short name used in resource names.')
param appName string = 'tutordesk'

param location string = resourceGroup().location

@description('Image for the first deployment; CI replaces it on every merge to main.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

param postgresAdminUser string = 'tutordeskadmin'

@secure()
param postgresAdminPassword string

@secure()
@minLength(32)
param jwtSigningKey string

@description('Seed demo tutors/students so the public demo has data.')
param seedDemoData bool = true

var suffix = uniqueString(resourceGroup().id)
var dbName = 'tutordesk'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-${appName}-${suffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresAdminUser
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
  }
}

// The lesson overlap constraint needs btree_gist, which Azure requires allow-listing.
resource pgExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: { value: 'BTREE_GIST', source: 'user-override' }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: dbName
  dependsOn: [pgExtensions]
}

// 0.0.0.0 - 0.0.0.0 = "allow Azure services" (Container Apps egress). Use VNet integration for stricter setups.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${appName}-${suffix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${appName}'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'db-connection'
          value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${dbName};Username=${postgresAdminUser};Password=${postgresAdminPassword};SSL Mode=Require'
        }
        { name: 'jwt-signing-key', value: jwtSigningKey }
        { name: 'appinsights-connection', value: insights.properties.ConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'app'
          image: containerImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__Default', secretRef: 'db-connection' }
            { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection' }
            { name: 'Seed__DemoData', value: string(seedDemoData) }
            { name: 'Api__ExposeDocs', value: 'true' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0 // scale to zero when idle; the first request after a pause is a cold start
        maxReplicas: 2
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '50' } } }
        ]
      }
    }
  }
  dependsOn: [database, allowAzure]
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output containerAppName string = app.name
output applicationInsightsName string = insights.name
