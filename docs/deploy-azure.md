# Deploying TutorDesk to Azure

The CI/CD pipeline publishes a Docker image to GitHub Container Registry on every merge to `main`.
Once the steps below are done, it also rolls the image out to Azure Container Apps and smoke-tests
`/health/ready` on the live URL.

Expect about 30 minutes for the one-time setup. All commands use the Azure CLI (`az login` first).

## 1. Create the infrastructure

```bash
az group create -n rg-tutordesk -l australiaeast

az deployment group create -g rg-tutordesk -f infra/main.bicep -p infra/main.parameters.json \
  -p postgresAdminPassword="$(openssl rand -base64 24)" \
     jwtSigningKey="$(openssl rand -base64 48)"
```

This creates:

| Resource | Why |
|---|---|
| Container Apps environment + app `ca-tutordesk` | Runs the image; scales to zero when idle (`minReplicas: 0`) |
| PostgreSQL Flexible Server (Burstable B1ms) | App database. `btree_gist` is allow-listed for the overlap constraint |
| Log Analytics + Application Insights | Logs, requests, exceptions and the custom `tutordesk.*` metrics |

The app runs its EF Core migrations on startup, so the first revision creates the schema itself.

> **Cost:** the B1ms Postgres server is the main cost (roughly AUD 20–30 a month; check the
> Azure pricing calculator). To keep it near zero, point `ConnectionStrings__Default` at a
> free Neon or Supabase Postgres instead, and delete the Postgres resources from `main.bicep`.

## 2. Make the image pullable

After the first CI run on `main`, open the repo's **Packages → software_project → Package settings**
and set visibility to **Public**. Container Apps can then pull it without credentials.

## 3. Let GitHub Actions deploy (OIDC, no stored passwords)

```bash
SUB=$(az account show --query id -o tsv)
APP_ID=$(az ad app create --display-name gh-tutordesk-deploy --query appId -o tsv)
az ad sp create --id "$APP_ID"
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/rg-tutordesk"

az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:letriluan/software_project:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

In GitHub, go to **Settings → Secrets and variables → Actions → Variables** and add:

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | `$APP_ID` |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `$SUB` |
| `AZURE_RESOURCE_GROUP` | `rg-tutordesk` |
| `AZURE_CONTAINER_APP` | `ca-tutordesk` |

Then create an environment called `production` (**Settings → Environments**). You can add yourself
as a required reviewer if you want to approve each deploy.

Push to `main`, and the **Deploy to Azure Container Apps** job runs after the tests pass.

## 4. Monitoring

In Application Insights:

- **Failures** shows exceptions. Each API error response carries a `traceId`, so you can find the
  matching trace.
- **Metrics → Custom (tutordesk)** has `tutordesk.lessons.booked`, `tutordesk.lessons.conflicts`
  and `tutordesk.lessons.attendance`, split by outcome.
- **Availability**: add a standard test that pings `/health/ready` every 5 minutes. The same check
  also keeps the app warm if you want to avoid cold starts.
