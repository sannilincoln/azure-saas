#!/usr/bin/env bash
#
# configure-external-id.sh
#
# Configures the deployment's identity as multitenant Microsoft Entra *Workforce*
# (Azure AD). This product is sold on Azure Marketplace, so every buyer and user
# already has an Azure AD tenant and signs in from it — the authority is the common
# multitenant endpoint (login.microsoftonline.com) and the tenant is 'organizations'.
#
# There is NO per-product identity tenant to provision. This replaces both the old
# Azure AD B2C provisioning (create-azure-b2c.sh, config-b2c.sh, upload-ief-policies.sh)
# AND the interim Entra External ID (CIAM / ciamlogin.com) authority. The operator
# registers the app registrations MANUALLY in the portal as *multitenant* apps in the
# publisher's Azure AD tenant; this script simply CONSUMES those values: it writes the
# identity settings the rest of the deployment reads (.deployment.azureb2c.*,
# appRegistrations[].appId) and stores the web/app client secrets in Key Vault under
# the app name (same Key Vault contract the kit has always used).
#
# It does NOT create a tenant, app registrations, custom (IEF) policies, or policy keys.
#
# NOTE: the .initConfig.entraExternalId.* input keys and the .deployment.azureb2c.*
# output keys are kept by name to avoid an infra-coupled rename (they are wired through
# bicep + Key Vault secret references); only their *values* are Workforce now.
#
# INPUTS (config.json):
#   .initConfig.entraExternalId.tenantId        publisher Azure AD tenant the apps are registered in
#   .initConfig.entraExternalId.tenantDomain    e.g. edulynkSaas.onmicrosoft.com
#   .initConfig.entraExternalId.apps            { "<app-name>": "<client/app id>", ... } (multitenant apps)
#
# SECRETS (gitignored file, mounted via the config volume — NOT committed):
#   config/external-id.secrets.json             { "<app-name>": "<client secret>", ... }
#
set -u -e -o pipefail

# shellcheck disable=SC1091
{
    source "${ASDK_DEPLOYMENT_SCRIPT_PROJECT_BASE}/constants.sh"
    source "$SHARED_MODULE_DIR/config-module.sh"
    source "$SHARED_MODULE_DIR/colors-module.sh"
    source "$SHARED_MODULE_DIR/log-module.sh"
}

SECRETS_FILE="${CONFIG_DIR}/external-id.secrets.json"

echo "Configuring Microsoft Entra Workforce (multitenant) identity." |
    log-output \
        --level info \
        --header "Entra Workforce"

# ----------------------------------------------------------------------------
# 1. Read the identity inputs and set the Workforce (multitenant) authority.
#    The apps are registered as multitenant in the publisher tenant; sign-in is
#    accepted from any Azure AD tenant, so the authority is the common endpoint
#    and the tenant is 'organizations' (NOT a B2C/CIAM ciamlogin.com authority).
# ----------------------------------------------------------------------------
tenant_id="$(get-value ".initConfig.entraExternalId.tenantId")"
tenant_domain="$(get-value ".initConfig.entraExternalId.tenantDomain")"

if [[ -z "${tenant_id}" || "${tenant_id}" == "null" ||
      -z "${tenant_domain}" || "${tenant_domain}" == "null" ]]; then
    echo "Missing required '.initConfig.entraExternalId.tenantId' (publisher tenant) and/or '.tenantDomain' in ${CONFIG_FILE}." |
        log-output \
            --level error \
            --header "Critical Error"
    exit 1
fi

# Subdomain is the tenant domain without the '.onmicrosoft.com' suffix.
tenant_name="${tenant_domain%%.onmicrosoft.com}"

# Workforce multitenant authority — the common endpoint, NOT a per-tenant
# ciamlogin.com (CIAM) or b2clogin.com (B2C) host.
instance="https://login.microsoftonline.com/"

echo "Workforce identity for ${tenant_domain} (apps registered in publisher tenant ${tenant_id})." |
    log-output --level info
echo "Authority: ${instance} (multitenant; tenant 'organizations')." |
    log-output --level info

# Mirror the values into the .deployment.azureb2c.* keys the downstream modules read.
# tenantId stays the publisher (app-home) tenant; the runtime config emitters set the
# multitenant 'organizations' value independently (see map-to-config-entries-parameters.py).
put-value ".deployment.azureb2c.name" "${tenant_name}"
put-value ".deployment.azureb2c.domainName" "${tenant_domain}"
put-value ".deployment.azureb2c.tenantId" "${tenant_id}"
put-value ".deployment.azureb2c.instance" "${instance}"

# ----------------------------------------------------------------------------
# 2. Copy the manually-registered app (client) IDs into appRegistrations[].appId.
#    For apps that expose scopes, default the identifier URI to api://<appId>
#    (the Entra default) when one isn't already set.
# ----------------------------------------------------------------------------
echo "Applying app registration client IDs from config." |
    log-output --level info

app_names="$(get-value ".initConfig.entraExternalId.apps | keys[]")"
while IFS= read -r app_name; do
    [[ -z "${app_name}" ]] && continue

    app_id="$(get-value ".initConfig.entraExternalId.apps[\"${app_name}\"]")"
    if [[ -z "${app_id}" || "${app_id}" == "null" ]]; then
        echo "No client ID provided for '${app_name}', skipping." | log-output --level warning
        continue
    fi

    # Only write if this app actually exists in appRegistrations.
    if [[ "$(get-app-value "${app_name}" "name")" != "${app_name}" ]]; then
        echo "'${app_name}' is not in .appRegistrations, skipping." | log-output --level warning
        continue
    fi

    put-app-id "${app_name}" "${app_id}"
    echo "  ${app_name} -> appId ${app_id}" | log-output --level info

    # Set the identifier URI for APIs that expose scopes to the Entra default,
    # api://<appId>. NOTE: populate-configuration-manifest (init step) pre-sets a
    # B2C-style URI (https://<tenant>.onmicrosoft.com/...), so we must OVERRIDE it
    # unconditionally. The downstream web apps request scopes as
    # "<applicationIdUri>/<scope>" (e.g. api://<adminApiAppId>/tenant.read), so this
    # must match the "Application ID URI" shown on the app's "Expose an API" page.
    # If you customised that URI in the portal, change this to match.
    scopes_len="$(get-app-value "${app_name}" "scopes | length")"
    if [[ "${scopes_len}" =~ ^[0-9]+$ ]] && (( scopes_len > 0 )); then
        put-app-value "${app_name}" "applicationIdUri" "api://${app_id}"
        echo "  ${app_name} -> applicationIdUri api://${app_id}" | log-output --level info
    fi
done <<< "${app_names}"

# ----------------------------------------------------------------------------
# 3. Store client secrets in Key Vault (secret name == app name), matching the
#    contract config-b2c.sh used. Secrets come from a gitignored local file so
#    they are never committed to the repo or written into config.json.
# ----------------------------------------------------------------------------
key_vault_name="$(get-value ".deployment.keyVault.name")"

if [[ ! -s "${SECRETS_FILE}" ]]; then
    echo "Secrets file '${SECRETS_FILE}' not found or empty." |
        log-output --level error --header "Critical Error"
    echo "Create it (gitignored) with: { \"saas-app\": \"...\", \"signupadmin-app\": \"...\", \"permissions-api\": \"...\" }" |
        log-output --level info
    exit 1
fi

echo "Storing client secrets in Key Vault '${key_vault_name}'." |
    log-output --level info

secret_app_names="$(jq --raw-output 'keys[]' "${SECRETS_FILE}")"
while IFS= read -r app_name; do
    [[ -z "${app_name}" ]] && continue

    secret="$(jq --raw-output --arg n "${app_name}" '.[$n]' "${SECRETS_FILE}")"
    if [[ -z "${secret}" || "${secret}" == "null" ]]; then
        echo "Empty secret for '${app_name}', skipping." | log-output --level warning
        continue
    fi

    # Use --value=... (not space-separated) so secrets beginning with '-' are not
    # mis-parsed by the az CLI as an option.
    az keyvault secret set \
        --name "${app_name}" \
        --vault-name "${key_vault_name}" \
        --value="${secret}" \
        --only-show-errors >/dev/null ||
        { echo "Failed to set Key Vault secret for '${app_name}'." |
            log-output --level error --header "Critical Error"; exit 1; }

    echo "  secret stored for ${app_name}" | log-output --level success
done <<< "${secret_app_names}"

echo "Entra Workforce identity configuration complete." |
    log-output \
        --level success \
        --header "Entra Workforce"
