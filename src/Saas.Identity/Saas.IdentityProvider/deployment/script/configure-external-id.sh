#!/usr/bin/env bash
#
# configure-external-id.sh
#
# Microsoft Entra External ID replacement for the three Azure AD B2C provisioning
# steps the kit used to run (create-azure-b2c.sh, config-b2c.sh, upload-ief-policies.sh).
#
# Azure AD B2C is end-of-sale for new subscriptions (2025-05-01), so the kit can no
# longer create a B2C tenant. Instead, the operator creates an Entra External ID
# tenant + the app registrations + a user flow MANUALLY in the portal, and this script
# simply CONSUMES those values: it writes the identity settings the rest of the
# deployment reads (.deployment.azureb2c.*, appRegistrations[].appId) and stores the
# web/app client secrets in Key Vault under the app name (same contract config-b2c.sh used).
#
# It does NOT create a tenant, app registrations, custom (IEF) policies, or policy keys —
# Entra External ID has no custom-policy support, and the registrations already exist.
#
# INPUTS (config.json):
#   .initConfig.entraExternalId.tenantId        e.g. d03528f1-261a-48bb-8937-2bd959ce9b8e
#   .initConfig.entraExternalId.tenantDomain    e.g. edulynkSaas.onmicrosoft.com
#   .initConfig.entraExternalId.apps            { "<app-name>": "<client/app id>", ... }
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

echo "Configuring Microsoft Entra External ID (replaces Azure AD B2C provisioning)." |
    log-output \
        --level info \
        --header "Entra External ID"

# ----------------------------------------------------------------------------
# 1. Read the External ID tenant inputs and derive instance/subdomain.
# ----------------------------------------------------------------------------
tenant_id="$(get-value ".initConfig.entraExternalId.tenantId")"
tenant_domain="$(get-value ".initConfig.entraExternalId.tenantDomain")"

if [[ -z "${tenant_id}" || "${tenant_id}" == "null" ||
      -z "${tenant_domain}" || "${tenant_domain}" == "null" ]]; then
    echo "Missing required '.initConfig.entraExternalId.tenantId' and/or '.tenantDomain' in ${CONFIG_FILE}." |
        log-output \
            --level error \
            --header "Critical Error"
    exit 1
fi

# Subdomain is the tenant domain without the '.onmicrosoft.com' suffix.
tenant_name="${tenant_domain%%.onmicrosoft.com}"

# Entra External ID (CIAM) authority host is <subdomain>.ciamlogin.com (NOT b2clogin.com).
instance="https://${tenant_name}.ciamlogin.com/"

echo "Tenant: ${tenant_domain} (${tenant_id}). Authority: ${instance}" |
    log-output --level info

# Mirror the values into the .deployment.azureb2c.* keys the downstream modules read.
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

    az keyvault secret set \
        --name "${app_name}" \
        --vault-name "${key_vault_name}" \
        --value "${secret}" \
        --only-show-errors >/dev/null ||
        { echo "Failed to set Key Vault secret for '${app_name}'." |
            log-output --level error --header "Critical Error"; exit 1; }

    echo "  secret stored for ${app_name}" | log-output --level success
done <<< "${secret_app_names}"

echo "Entra External ID configuration complete." |
    log-output \
        --level success \
        --header "Entra External ID"
