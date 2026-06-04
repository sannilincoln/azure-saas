# Running the ASDK apps locally

The four apps run on your machine but pull all their settings from the **deployed**
Azure App Configuration store and **Azure Key Vault** (there is almost nothing in
`appsettings.json`). So "running locally" means: your local process + the cloud backend
of an existing deployment. You authenticate to Azure with your own `az login`, and the
app uses that credential to read Key Vault.

> This assumes an existing deployment. The examples use the resource names from the
> `asdk-test-x16w` deployment — replace the `-asdk-test-x16w` suffix with your own
> deployment's postfix if different.

## The apps and their local URLs

| App | Project | Local URL | Needs redirect URI? |
|-----|---------|-----------|---------------------|
| SaaS Application (customer app) | `src/Saas.Application/Saas.Application.Web` | `https://localhost:7066` | ✅ web |
| Signup Administration | `src/Saas.SignupAdministration/Saas.SignupAdministration.Web` | `https://localhost:5001` | ✅ web |
| Admin API | `src/Saas.Admin/Saas.Admin.Service` | `https://localhost:7041` | ❌ API |
| Permissions API | `src/Saas.Identity/Saas.Permissions/Saas.Permissions.Service_v1.1` | `https://localhost:7023` | ❌ API |

The web apps call the Admin API at `https://localhost:7041`, and the Admin API calls the
Permissions API. To exercise the full flow run all four; to just test sign-in / a web
page, the web app alone is often enough (the post-login onboarding error, for example,
happens before any API call).

## Prerequisites

- **.NET 8 SDK**
- **Azure CLI** (`az`), logged in to the subscription that holds the deployment
- **Access to the deployment's Azure resources** (resource group `rg-asdk-test-x16w`):
  - **Key Vault Secrets User** on the key vault (data-plane read — the app resolves Key
    Vault references at startup)
  - App Configuration is read via a connection string (an access key), so no extra RBAC
    is needed for it
- **Access to the Entra External ID tenant** only if you need to add a *new* localhost
  redirect URI. The localhost redirect URIs below are shared per app registration, so
  once they exist any developer using the same port is covered.

## One-time setup

```powershell
# 1. Sign in to Azure
az login

# 2. Grant yourself Key Vault data-plane read (once per developer).
#    Get your object id with:  az ad signed-in-user show --query id -o tsv
az role assignment create `
  --assignee <your-user-object-id> `
  --role "Key Vault Secrets User" `
  --scope /subscriptions/<sub-id>/resourceGroups/rg-asdk-test-x16w/providers/Microsoft.KeyVault/vaults/kv-asdk-test-x16w

# 3. Get the App Configuration READ-ONLY connection string
az appconfig credential list --name appconfig-asdk-test-x16w -g rg-asdk-test-x16w `
  --query "[?name=='Primary Read Only'].connectionString" -o tsv

# 4. Store it as a user secret for EACH project you intend to run.
#    (User secrets are per-project and stay off-disk-in-repo — never commit the string.)
cd src/Saas.SignupAdministration/Saas.SignupAdministration.Web
dotnet user-secrets set "ConnectionStrings:AppConfig" "<connection string from step 3>"
# repeat `dotnet user-secrets set ...` in each other project folder you run

# 5. Trust the local HTTPS dev certificate
dotnet dev-certs https --trust
```

### Redirect URIs (one-time, per app registration)

In the Entra External ID portal, on each **web** app registration → **Authentication** →
**Web** platform, ensure these exist:

- `saas-app` → `https://localhost:7066/signin-oidc`
- `signupadmin-app` → `https://localhost:5001/signin-oidc`

(The deployed redirect URIs — `https://<app>-asdk-test-x16w.azurewebsites.net/signin-oidc`
— stay as they are; you're just adding the localhost ones alongside.)

## Run

```powershell
cd src/Saas.SignupAdministration/Saas.SignupAdministration.Web
dotnet run
```

Then browse to the app's URL (e.g. `https://localhost:5001`). `ASPNETCORE_ENVIRONMENT`
defaults to `Development` via `launchSettings.json`, which turns on **detailed error
pages** (full exception + stack in the browser) and Swagger UI for the APIs.

To run several apps at once, open a terminal per project and `dotnet run` in each.

## Config versioning

App Configuration keys are labelled with a version (currently **`ver0.8.0`**). Each app
reads its own `Version` value from `appsettings.json` and only loads keys with that label.
If you bump the deployment version, update `appsettings.json` `Version` to match or the app
will load no settings.

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `Key Vault ... 403 Forbidden` at startup | Missing **Key Vault Secrets User** (setup step 2); allow ~1 min to propagate |
| `App config missing` / null connection string | `ConnectionStrings:AppConfig` user secret not set for **this** project (step 4) |
| `AADSTS50011` redirect URI mismatch at sign-in | The localhost redirect URI isn't registered, or you're on a different port |
| Browser warns the cert is untrusted | `dotnet dev-certs https --trust` (step 5) |
| App loads but settings look empty / null | `Version` in `appsettings.json` doesn't match the App Configuration label |
| Web app errors *after* sign-in calling the API | Run the Admin API (`:7041`) locally too, or it's the downstream/permissions layer |
