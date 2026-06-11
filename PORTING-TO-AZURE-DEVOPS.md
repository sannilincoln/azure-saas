# Porting CI/CD from GitHub Actions to Azure DevOps

Status: **pipelines added; ADO-side setup + cutover pending** (as of 2026-06-11). This moves the
*deploy automation* (and the repo) from GitHub to Azure DevOps. Apps + infra are unchanged.

## What's done (in this repo)
- `.azuredevops/templates/deploy-app.yml` — shared build+publish+deploy template (UseDotNet 8.x →
  `dotnet publish` zip → `AzureWebApp@1`, `appType: webAppLinux`).
- `.azuredevops/{admin-api,permissions-api,saas-app,signupadmin}.yml` — 4 thin pipelines that
  `extends:` the template, each with its web-app name, project path, and a **dependency-aware
  `main` path filter** (shared-lib changes fan out to every dependent app).
- The 5 GitHub workflows (4 deploys + Hugo docs→GH Pages) were removed. The docs site is **not**
  ported (markdown stays in-repo, publishing dropped).

## What stays unchanged
All 4 App Services, App Configuration, Key Vault, the user-assigned managed identity, and the
infra/bicep deploy (the manual WSL container flow — GitHub never did infra; ADO won't either).

## The 4 apps
| App | Web App (Linux) | Project | Pipeline file |
| --- | --- | --- | --- |
| Admin API | `admin-api-asdk-test-x16w` | `src/Saas.Admin/Saas.Admin.Service/Saas.Admin.Service.csproj` | `.azuredevops/admin-api.yml` |
| Permissions API | `api-permission-asdk-test-x16w` | `src/Saas.Identity/Saas.Permissions/Saas.Permissions.Service_v1.1/Saas.Permissions.Service.csproj` | `.azuredevops/permissions-api.yml` |
| SaaS App | `saas-app-asdk-test-x16w` | `src/Saas.Application/Saas.Application.Web/Saas.Application.Web.csproj` | `.azuredevops/saas-app.yml` |
| Sign-up Admin | `signupadmin-app-asdk-test-x16w` | `src/Saas.SignupAdministration/Saas.SignupAdministration.Web/Saas.SignupAdministration.Web.csproj` | `.azuredevops/signupadmin.yml` |

## ADO-side setup (do once, in the portal)
1. **Push the repo to ADO Repos** and make it primary:
   `git remote add ado <ado-repo-url> && git push ado --all && git push ado --tags`.
2. **ARM service connection** — Project Settings → Service connections → Azure Resource Manager →
   **Workload Identity federation (manual)**:
   - Subscription `a353af1c-9dd6-4de9-af57-3bae4b638eee`, Tenant
     `e77b3a11-2e95-4f9e-973b-918d466ba68d`, App (client) id
     `2b9ea452-51a8-4494-9f3f-ac16f433edad` (`oidc-workflow-asdk-test-x16w`).
   - **Name it `asdk-azure`** (that's the `azureSubscription` variable in the 4 pipeline files — or
     rename the variable to match your chosen name).
   - ADO shows an **Issuer** + **Subject** (`sc://<org>/<project>/asdk-azure`).
3. **Federated credential** — Azure portal → App registrations → `oidc-workflow-asdk-test-x16w` →
   Certificates & secrets → Federated credentials → *Other issuer* → paste the Issuer + Subject from
   step 2. The app already has **Contributor** on `rg-asdk-test-x16w`; no RBAC grant needed.
4. **Create 4 pipelines** — Pipelines → New → Azure Repos Git → *Existing Azure Pipelines YAML file* →
   pick each `.azuredevops/*.yml`. Run each **once manually** to validate; afterwards the `main`
   path filters drive auto-deploy.

## Identity federation: GitHub vs ADO
GitHub OIDC used subject `repo:sannilincoln/azure-saas:ref:refs/heads/main`. ADO uses
`sc://<org>/<project>/<connection-name>`. We **reuse the same app registration** and just add the ADO
federated credential next to the GitHub one. After ADO is proven, delete the GitHub credential.

## Cutover (after all 4 pipelines are green)
- Delete the GitHub `repo:sannilincoln/azure-saas:ref:refs/heads/main` federated credential from
  `oidc-workflow-asdk-test-x16w`.
- Archive/decommission the GitHub repo. The 3 GitHub secrets (`AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`)
  are obsolete — the service connection replaces them.
- `.github/ISSUE_TEMPLATE/` is GitHub-only and inert in ADO; remove it whenever convenient.

## Verification
- Each pipeline run green; `AzureWebApp@1` reports success.
- Health: admin-api `/api/marketplace/subscriptions` → 401; permissions → 401; signupadmin `/` → 200;
  saas-app `/` → 200.
- Functional: sign in to signupadmin → `/Publisher/Subscriptions` (Owner) loads; saas-app `/{route}`
  resolves a real tenant.
- Path-filter sanity: a commit touching only `src/Saas.Lib/Saas.Shared/**` triggers **all 4** pipelines;
  one touching only `src/Saas.Marketplace/**` triggers **admin-api only**.

## Notes / pitfalls
- App Services are **Linux** → `appType: webAppLinux` (already set in the template).
- The old GitHub workflows had a `{{`→`${{` publish-path quirk; the ADO template uses clean
  `$(Build.ArtifactStagingDirectory)` paths instead.
- The **3 plaintext-shared client secrets** remain an open security cleanup item, independent of this port.
- The marketplace **email-notification** feature is paused awaiting Exchange 365 SMTP creds — unrelated
  to this port.
