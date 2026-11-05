// TutorDesk on Azure, free-tier setup:
//   Azure Container Apps (consumption plan, scales to zero)  - inside the monthly free grant
//   Application Insights + Log Analytics (daily ingestion cap) - inside the free 5 GB/month
//   PostgreSQL on Neon's free plan                             - connection string passed in
//   A monthly budget that emails you if anything starts costing money
//
// Deploy (Azure Cloud Shell or any machine with the Azure CLI):
//   az deployment group create -g rg-tutordesk -f infra/main.bicep -p infra/main.parameters.json \
//     -p databaseConnectionString='<Neon .NET connection string>' \
//        jwtSigningKey="$(openssl rand -base64 48)" alertEmail='you@example.com'
// After that, GitHub Actions rolls out every new image (see docs/deploy-azure.md).

targetScope = 'resourceGroup'

@description('Short name used in resource names.')
param appName string = 'tutordesk'

param location string = resourceGroup().location

@description('Image for the first deployment. CI replaces it with the commit-tagged image on every merge to main.')
param containerImage string = 'ghcr.io/letriluan/software_project:latest'

@secure()
@description('Npgsql connection string, e.g. from Neon: Host=...;Database=neondb;Username=...;Password=...;SSL Mode=Require')
param databaseConnectionString string

@secure()
@minLength(32)
param jwtSigningKey string

@description('Seed demo tutors and students so the public demo has data.')
param seedDemoData bool = true

@description('Budget alerts go to this address.')
param alertEmail string

@description('Monthly budget in your billing currency. Alerts only; nothing is switched off.')
param monthlyBudget int = 5

@description('First day of the current month; used as the budget start date.')
param budgetStartDate string = utcNow('yyyy-MM-01')

var suffix = uniqueString(resourceGroup().id)

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // Hard stop on log ingestion (~3 GB/month at most) so logging can never run up a bill.
    workspaceCapping: { dailyQuotaGb: json('0.1') }
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
        { name: 'db-connection', value: databaseConnectionString }
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
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 5
              failureThreshold: 24 // allow up to 2 minutes for migrations + a sleeping Neon database to wake
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 15
              failureThreshold: 4
            }
          ]
        }
      ]
      scale: {
        // Zero replicas when idle = zero cost. Raising this keeps it warm but starts idle billing.
        minReplicas: 0
        maxReplicas: 2
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '50' } } }
        ]
      }
    }
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-${appName}'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actualOver50Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [alertEmail]
      }
      forecastOver100Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [alertEmail]
      }
    }
  }
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output containerAppName string = app.name
output applicationInsightsName string = insights.name
