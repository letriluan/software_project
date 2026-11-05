# Deploying TutorDesk to Azure (free setup)

| Piece | Service | Cost |
|---|---|---|
| App (API + React) | Azure Container Apps, consumption plan, scales to zero | $0 inside the monthly free grant (180k vCPU-s, 360k GiB-s, 2M requests) |
| Database | Neon Postgres, free plan (1 GB, 100 compute-hours/month) | $0 |
| Monitoring | Application Insights + Log Analytics, capped at 0.1 GB/day | $0 inside the free 5 GB/month |
| Image | GitHub Container Registry, public package | $0 |
| Safety net | Azure budget, emails you at 50% of $5 | $0 |

Trade-off: after a quiet period the first request takes a few seconds while the app and the database wake up.

## 1. Neon database (5 minutes)

1. Sign up at neon.com and create a project: Postgres 16 or newer, region **AWS Asia Pacific (Sydney)**.
2. Click **Connect**. Turn **Connection pooling off** (EF Core migrations want a direct connection), choose **.NET** as the format, and copy the string. It looks like:
   `Host=ep-xxxx.ap-southeast-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=...;SSL Mode=Require`

The app creates its tables on first start, including the `btree_gist` overlap constraint.

## 2. Make the image public (1 minute)

On GitHub: your profile → **Packages → software_project → Package settings → Change visibility → Public**.
Azure can then pull `ghcr.io/letriluan/software_project:latest` without credentials.

## 3. Create the Azure resources (10 minutes)

Sign in at portal.azure.com and open **Cloud Shell** (the `>_` icon, choose Bash). Then:

```bash
git clone https://github.com/letriluan/software_project && cd software_project
az group create -n rg-tutordesk -l australiaeast

az deployment group create -g rg-tutordesk -f infra/main.bicep -p infra/main.parameters.json \
  -p databaseConnectionString='PASTE_NEON_STRING_HERE' \
     jwtSigningKey="$(openssl rand -base64 48)" \
     alertEmail='you@example.com' \
  --query properties.outputs.appUrl.value -o tsv
```

The last line prints the live URL. Open it: the first load can take up to a minute while the app migrates and seeds the database. Sign in with `tutor@tutordesk.demo` / `Demo!2345`.

## 4. Let GitHub Actions deploy (10 minutes)

Still in Cloud Shell:

```bash
SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
APP_ID=$(az ad app create --display-name gh-tutordesk-deploy --query appId -o tsv)
az ad sp create --id "$APP_ID" > /dev/null
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/rg-tutordesk" > /dev/null
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:letriluan/software_project:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}' > /dev/null
echo "AZURE_CLIENT_ID=$APP_ID"; echo "AZURE_TENANT_ID=$TENANT"; echo "AZURE_SUBSCRIPTION_ID=$SUB"
```

In the GitHub repo:

1. **Settings → Environments → New environment** named `production`.
2. **Settings → Secrets and variables → Actions → Variables tab → New repository variable**, five times:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | printed above |
| `AZURE_TENANT_ID` | printed above |
| `AZURE_SUBSCRIPTION_ID` | printed above |
| `AZURE_RESOURCE_GROUP` | `rg-tutordesk` |
| `AZURE_CONTAINER_APP` | `ca-tutordesk` |

No passwords are stored: GitHub proves its identity to Azure with a short-lived OIDC token, and only for the `production` environment of this repo.

## 5. Check it

**Actions → CI/CD → Run workflow** (or push any commit). After the tests, *Deploy to Azure Container Apps* rolls out the new image and fails the run if `/health/ready` doesn't come back healthy.

## Monitoring

- **Application Insights → Failures**: exceptions. Every API error includes a `traceId` that matches the trace here.
- **Metrics → custom namespace `TutorDesk`**: `tutordesk.lessons.booked`, `tutordesk.lessons.conflicts`, `tutordesk.lessons.attendance`.
- **Cost Management → Budgets**: the `budget-tutordesk` alert.

## Removing everything

```bash
az group delete -n rg-tutordesk --yes
```

Then delete the Neon project and the `gh-tutordesk-deploy` app registration (Entra ID → App registrations).
