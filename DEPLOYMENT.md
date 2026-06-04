# Deploying & Hosting the Azure SaaS Dev Kit (Entra External ID edition)

This guide walks a brand-new engineer through deploying and hosting this fork of the
[Azure SaaS Dev Kit (ASDK)](https://github.com/Azure/azure-saas) from scratch.

This fork has been **migrated from Azure AD B2C to Microsoft Entra External ID**, because
**Azure AD B2C is end-of-sale for new customers as of 2025-05-01** — new subscriptions can
no longer create a B2C tenant, which the upstream kit requires. See
[`memory`/the migration notes] and the "What changed for External ID" section below.

---

## Table of contents

1. [Architecture overview](#1-architecture-overview)
2. [Prerequisites](#2-prerequisites)
3. [Set up the host environment (WSL2)](#3-set-up-the-host-environment-wsl2)
4. [Fork the repo](#4-fork-the-repo)
5. [Create the Entra External ID tenant + app registrations](#5-create-the-entra-external-id-tenant--app-registrations)
6. [Configure the deployment](#6-configure-the-deployment)
7. [Run the deployment](#7-run-the-deployment)
8. [Deploy the application code (GitHub Actions)](#8-deploy-the-application-code-github-actions)
9. [Post-deployment configuration](#9-post-deployment-configuration)
10. [Verify (sign up & sign in)](#10-verify-sign-up--sign-in)
11. [What changed for External ID](#11-what-changed-for-external-id)
12. [Troubleshooting](#12-troubleshooting)
13. [Running locally](#13-running-locally)

---

## 1. Architecture overview

The kit deploys five modules into **one resource group**, in this mandatory order:

1. **Identity Foundation** — shared App Service Plan, SQL Server + 2 databases, Key Vault,
   App Configuration, Application Insights, managed identity, and the **identity wiring to
   Entra External ID**. Also creates the OIDC federated credential for GitHub Actions.
2. **Permissions API** — app service + config.
3. **Admin API** — app service + config.
4. **Signup Administration Web** — app service + config.
5. **SaaS Application Web** — app service + config.

Two-phase model, important to understand:

- **Infrastructure** (App Services, config, secrets) is provisioned by the deployment
  scripts you run on your host (in WSL).
- **Application code** (the .NET binaries) is published separately by **GitHub Actions**
  in your fork, authenticating to Azure via **OIDC**. Until the Actions run, the App
  Services are empty.

Configuration lives in **Azure App Configuration** (with secrets as **Key Vault**
references), not in `appsettings.json`. The apps read it at runtime via managed identity.

---

## 2. Prerequisites

### Accounts & access
- An **Azure subscription** where you are **Owner** (the kit creates role assignments;
  Contributor is not enough).
- Permission to **create an Entra External ID tenant** (and tenant-creation not blocked by
  your directory's user settings).
- A **GitHub account** to host your fork (Actions must be enabled).

### Tools (installed inside WSL2 Ubuntu — see next section)
- `bash`, `docker` (Docker Desktop), Azure CLI `az`, GitHub CLI `gh`, `jq`, `python3`
- The deployment runs **inside a Linux container**, so it must run on **Linux/WSL2 or
  macOS** — **not** Git Bash / MSYS on Windows (the kit's `get-os` only accepts
  `linux-gnu*`/`darwin*`).

### Knowledge of the cost
- Default SKUs: App Service Plan **Standard S1** (shared by all 4 apps), SQL databases
  **DTU Basic**. These are pinned by `deploy-asdk.sh` and cost real money — delete the
  resource group when done experimenting.

---

## 3. Set up the host environment (WSL2)

On Windows, do everything inside an **Ubuntu WSL2** distro. Git Bash will not work.

```powershell
wsl --install -d Ubuntu      # from Windows PowerShell; create a Linux user when prompted
```

Then **inside Ubuntu**, install the toolchain. This repo ships a helper:

```bash
cp /mnt/c/<path-to-clone>/src/setup-wsl-toolchain.sh ~
bash ~/setup-wsl-toolchain.sh   # installs az, gh, jq, python3, git; checks docker
```

Enable Docker for the distro: **Docker Desktop → Settings → Resources → WSL Integration →
enable Ubuntu → Apply & Restart**.

Authenticate:
```bash
az login
gh auth login
```

> **Token-cache note (important):** the deployment containers mount your `~/.azure`
> read-only. On WSL the Azure CLI may *encrypt* its token cache, which the container can't
> read (`User ... does not exist in MSAL token cache`). Disable encryption once:
> ```bash
> az config set core.encrypt_token_cache=false
> rm -f ~/.azure/msal_token_cache.bin
> az login        # writes a plaintext ~/.azure/msal_token_cache.json
> ```

> **File ownership note:** the containers run as root and write into the mounted repo, so
> later `git` operations may fail with "permission denied". Fix with:
> `sudo chown -R "$USER:$USER" ~/azure-saas`.

---

## 4. Fork the repo

The kit **must** run from **your fork** (it creates OIDC credentials and triggers Actions
in the origin repo).

```bash
# create the fork on GitHub, then clone it INTO the Linux filesystem (not /mnt/c)
git clone https://github.com/<your-user>/azure-saas.git ~/azure-saas
cd ~/azure-saas
gh repo set-default <your-user>/azure-saas
```

---

## 5. Create the Entra External ID tenant + app registrations

These are **manual portal steps** (the kit no longer provisions B2C). Do them in the
**Entra External ID tenant** you create.

1. **Create the tenant:** Portal → *Microsoft Entra External ID* → **Create tenant** →
   choose **External** configuration → pick your subscription + a resource group + region.
   Record its **Primary domain** (e.g. `mytenant.onmicrosoft.com`) and **Tenant ID** (GUID).

2. **Create a sign-up / sign-in user flow:** External Identities → **User flows** → New →
   enable **Email with password** → collect the attributes you want (Display Name, etc.).

3. **Register four apps** (note each **Application (client) ID**; create a **client secret**
   for the two web apps + the permissions API):

   | App | Type | Key settings |
   |-----|------|--------------|
   | `admin-api` | Web API | Expose an API; add scopes `tenant.read`, `tenant.global.read`, `tenant.write`, `tenant.global.write`, `tenant.delete`, `tenant.global.delete`. Default Application ID URI `api://<clientId>`. |
   | `permissions-api` | Web API | Client secret; Graph app-roles `User.Read.All`, `Application.ReadWrite.OwnedBy` (grant admin consent). |
   | `signupadmin-app` | Web app | Client secret; redirect URI `https://signupadmin-app-<prefix>-<name>-<postfix>.azurewebsites.net/signin-oidc`; API permission → `admin-api` (all 6 scopes). |
   | `saas-app` | Web app | Client secret; redirect URI `https://saas-app-<prefix>-<name>-<postfix>.azurewebsites.net/signin-oidc`; API permission → `admin-api` `tenant.read`. |

4. **Associate the user flow** with each app: External Identities → User flows → *your
   flow* → **Applications** → add all four.

> You won't know the exact `<prefix>-<name>-<postfix>` app-service hostnames until the
> first deploy assigns the postfix. You can add/adjust the redirect URIs after step 7.

---

## 6. Configure the deployment

All deployment config lives in `src/Saas.Identity/Saas.IdentityProvider/deployment/config/`.
`config.json` is gitignored (created from `config-template.json` on first run).

### 6a. `config.json` — initConfig

Fill the four core fields plus the External ID block:

```bash
cd ~/azure-saas/src/Saas.Identity/Saas.IdentityProvider/deployment

# core identity inputs (pulled from your az login)
jq --arg sub "$(az account show --query id -o tsv)" \
   --arg ten "$(az account show --query tenantId -o tsv)" \
   --arg upi "$(az ad signed-in-user show --query id -o tsv)" \
   --arg loc "eastus" \
   '.initConfig.subscriptionId=$sub
  | .initConfig.tenantId=$ten
  | .initConfig.userPrincipalId=$upi
  | .initConfig.location=$loc' \
   config/config.json > config/config.json.tmp && mv config/config.json.tmp config/config.json

# External ID inputs (tenant + the 4 client IDs from step 5)
jq '.initConfig.entraExternalId = {
  "tenantId": "<external-id-tenant-guid>",
  "tenantDomain": "<mytenant>.onmicrosoft.com",
  "apps": {
    "admin-api": "<admin-api-client-id>",
    "permissions-api": "<permissions-api-client-id>",
    "signupadmin-app": "<signupadmin-app-client-id>",
    "saas-app": "<saas-app-client-id>"
  }
}' config/config.json > config/config.json.tmp && mv config/config.json.tmp config/config.json
```

> `initConfig.tenantId` is your **home** Azure AD tenant (where the subscription lives),
> **not** the External ID tenant. The External ID tenant goes in `entraExternalId.tenantId`.

### 6b. Client secrets (gitignored file)

Create `config/external-id.secrets.json` (already in `.gitignore`) with the three secrets
from step 5. It is read at deploy time and stored into Key Vault; it is never committed.

```bash
cat > config/external-id.secrets.json <<'EOF'
{
  "saas-app": "<saas-app-client-secret>",
  "signupadmin-app": "<signupadmin-app-client-secret>",
  "permissions-api": "<permissions-api-client-secret>"
}
EOF
chmod 600 config/external-id.secrets.json
```

---

## 7. Run the deployment

From the repo root:

```bash
cd ~/azure-saas
bash src/deploy-asdk.sh           # full sequential deploy; prompts between modules
# resume a single stage:  bash src/deploy-asdk.sh --from identity
```

What it does:
- **Preflight** (tools, docker, az/gh login, confirms you're on a fork).
- **Pins SKUs** (App Service Plan S1, SQL Basic) — idempotent.
- Runs each module's `setup.sh` (builds the deploy container) then `run.sh` (runs the
  deployment inside it).
- The **Identity Foundation** stage runs `configure-external-id.sh` instead of the old B2C
  scripts: it writes the External ID settings into config, sets the app client IDs, and
  stores the client secrets into Key Vault.

Between app modules the script asks whether the GitHub Action completed — you can answer
**`y` to continue deploying all infrastructure first**, then run the Actions in one batch
(section 8). The infra modules don't depend on prior apps' code being live.

A successful Identity Foundation run ends with `IdentityFoundationDeployment ...
provisioningState: Succeeded` and sets the GitHub repo secrets `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.

---

## 8. Deploy the application code (GitHub Actions)

The App Services are empty until the deploy workflows run. Each `*-deploy.yml` workflow:
- triggers on **`workflow_dispatch`** only (you launch it manually),
- authenticates to Azure with **OIDC**, and
- **checks out and deploys the `main` branch**.

The OIDC **federated credential is bound to `refs/heads/main`**, so the workflows must run
on **`main`**, and `main` must contain the correct workflow files (the deploy step patches
each workflow's `AZURE_WEBAPP_NAME` to the real app-service name).

```bash
# bring the deployment branch into main, then trigger the workflows on main
git checkout main && git merge <your-branch> && git push origin main

gh workflow run permissions-api-deploy.yml      --ref main
gh workflow run admin-service-api-deploy.yml    --ref main
gh workflow run signup-administration-deploy.yml --ref main
gh workflow run saas-app-deploy.yml             --ref main

gh run list      # confirm each run is ✓ green
gh run watch
```

> Pushing to `main` does **not** auto-trigger them — they're `workflow_dispatch`. Run them
> explicitly with `--ref main` (or via the Actions tab → Run workflow → branch `main`).

---

## 9. Post-deployment configuration

1. **Redirect URIs** (if not already exact): on the `saas-app` and `signupadmin-app`
   registrations (Authentication → Web), confirm:
   - `https://saas-app-<...>.azurewebsites.net/signin-oidc`
   - `https://signupadmin-app-<...>.azurewebsites.net/signin-oidc`
2. **User flow linkage:** confirm the sign-up/sign-in user flow lists both web apps under
   its **Applications** (otherwise sign-in fails with a "no user flow" error).
3. **Rotate any secrets** that were shared in plaintext during setup.

---

## 10. Verify (sign up & sign in)

Browse to the **Signup Administration** app
(`https://signupadmin-app-<...>.azurewebsites.net`). You should be redirected to
`https://<mytenant>.ciamlogin.com/...`, be able to **sign up** (email + password), and land
back in the app authenticated. The **SaaS Application** app works the same way for end
users.

---

## 11. What changed for External ID

Code/deployment changes this fork carries vs. upstream:

- **Deployment provisioning:** `configure-external-id.sh` replaces `create-azure-b2c.sh`,
  `config-b2c.sh`, and `upload-ief-policies.sh` in the Identity Foundation `start.sh`. It
  consumes a pre-created External ID tenant instead of creating a B2C tenant. External ID
  has **no custom (IEF) policies** — sign-in uses a portal **user flow**.
- **Auth config:** the `SignUpSignInPolicyId` setting is removed from the per-module
  `deployConfigEntries.bicep` + `map-to-config-entries-parameters.py`. With no B2C policy
  and `Instance = https://<tenant>.ciamlogin.com/`, `Microsoft.Identity.Web` (2.16) treats
  it as standard Entra/CIAM auth. The config section name is still literally `AzureB2C`.
- **Claims:** B2C put the directory object-id GUID in `sub`/`NameIdentifier`. External
  ID's `sub` is an opaque pairwise id; the GUID is in **`oid`**. `ApplicationUser.NameIdentifier`
  (both web apps) now reads `oid` as a fallback. (`NameIdentifierClaimsTransformation`
  covers the API bearer-token path.)
- **Secrets:** the two web apps + permissions API authenticate with **client secrets**
  (stored in Key Vault by `configure-external-id.sh`), matching the kit's existing
  `ClientSecret` wiring.

---

## 12. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `Unsupported OS` / `log-output: command not found` at container build | Running under Git Bash/MSYS. Use **WSL2 Ubuntu**. |
| `Docker daemon not reachable` | Start Docker Desktop; enable WSL Integration for the distro. |
| `permission denied ... /var/run/docker.sock` | `sudo usermod -aG docker $USER`, then `wsl --shutdown` and reopen. |
| `Authorization failed ... roleAssignments/write` | Your user is not **Owner** on the subscription. Grant Owner. |
| B2C `"empty or invalid content"` creating tenant | Expected — B2C is end-of-sale; this fork uses External ID instead. |
| `User ... does not exist in MSAL token cache` | `az config set core.encrypt_token_cache=false`, remove `msal_token_cache.bin`, `az login`. |
| `Permission denied` running a `.sh` | Missing exec bit; `chmod +x <script>` or run via `bash <script>`. |
| `git checkout main` blocked by local changes | The SKU bicep files are re-patched each run; `git checkout -- <them>` or `git stash`. |
| `AADSTS50011` redirect URI mismatch | Redirect URI on the app registration doesn't match the app-service URL. |
| No user flow / `AADSTS500200` | The user flow isn't linked to the app (section 9.2). |
| `ArgumentNullException: NameIdentifier` after sign-in | The `oid` claim handling — ensure you're on this fork's `ApplicationUser` fix. |
| GitHub Action `azure/login` fails (OIDC) | Workflow not run on `main` (federated credential is bound to `refs/heads/main`). |

---

## 13. Running locally

See [`LOCAL-DEVELOPMENT.md`](./LOCAL-DEVELOPMENT.md) for running the apps on your machine
against a deployed backend (App Configuration + Key Vault), including ports, user-secrets,
Key Vault access, and localhost redirect URIs.
