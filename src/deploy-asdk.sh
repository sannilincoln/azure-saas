
#!/usr/bin/env bash
#
# deploy-asdk.sh — Sequential deployment orchestrator for the Azure SaaS Dev Kit (ASDK)
# Repo: https://github.com/Azure/azure-saas
#
# WHAT THIS DOES
#   Runs the five ASDK modules in the only order that works, on the HOST machine:
#       1. Identity Foundation  (creates the shared App Service Plan, SQL Server + 2 DBs,
#                                Key Vault, App Config, B2C, OIDC creds — must be first)
#       2. Permissions API
#       3. Admin API
#       4. Signup Administration Web
#       5. SaaS Application Web
#   For each module it runs the kit's own  ./setup.sh  (builds the shared deploy
#   container) then  ./run.sh  (runs the deployment inside that container).
#
#   It also PINS the two SKUs you asked for, idempotently, before deploying:
#       - App Service Plan  -> Standard tier (default S1).  All 4 apps share this one plan.
#       - SQL databases     -> DTU Basic     (name=Basic, tier=Basic) for both databases.
#   (These are already the ASDK defaults; pinning guarantees intent if upstream changes.)
#
# WHAT THIS DOES NOT / CANNOT DO
#   - It is NOT fully unattended. The Identity Foundation stage is interactive
#     (Azure AD B2C login + a couple of portal steps). The script gates around it.
#   - App *code* is published by GitHub Actions (OIDC) in YOUR FORK. After each app
#     module, confirm its workflow run succeeded before continuing.
#   - Must run on the host with: bash, docker (running), Azure CLI (az, logged in),
#     GitHub CLI (gh, logged in), jq, python3 — NOT inside the ASDK container.
#
# USAGE
#   ./deploy-asdk.sh                 # full sequential deploy, prompts between stages
#   ./deploy-asdk.sh -y              # don't prompt between stages
#   ./deploy-asdk.sh --skip-setup    # skip ./setup.sh (image already built)
#   ./deploy-asdk.sh --no-sku-patch  # don't touch the bicep SKUs
#   ./deploy-asdk.sh --from admin    # resume starting at a module (identity|permissions|admin|signup|application)
#   ./deploy-asdk.sh --only permissions
#   APP_PLAN_SKU=S2 ./deploy-asdk.sh # override the Standard SKU (S1|S2|S3)
#
set -u -o pipefail

# ----------------------------------------------------------------------------
# Configuration
# ----------------------------------------------------------------------------
# App Service Plan SKU. Standard tier = S1 / S2 / S3. Default S1 (smallest Standard).
APP_PLAN_SKU="${APP_PLAN_SKU:-S1}"
# SQL database DTU tier. Basic is a fixed 5-DTU tier (name and tier are both "Basic").
SQL_DB_SKU_NAME="${SQL_DB_SKU_NAME:-Basic}"
SQL_DB_SKU_TIER="${SQL_DB_SKU_TIER:-Basic}"

# Module deploy directories, in mandatory order. Keyed name | relative deployment path.
MODULES=(
  "identity|src/Saas.Identity/Saas.IdentityProvider/deployment"
  "permissions|src/Saas.Identity/Saas.Permissions/deployment"
  "admin|src/Saas.Admin/deployment"
  "signup|src/Saas.SignupAdministration/deployment"
  "application|src/Saas.Application/deployment"
)

# Bicep files that own the SKUs (relative to repo root).
APP_PLAN_BICEP="src/Saas.Identity/Saas.IdentityProvider/deployment/bicep/IdentityFoundation/Module/appPlan.bicep"
SQL_BICEP="src/Saas.Identity/Saas.IdentityProvider/deployment/bicep/IdentityFoundation/Module/sqlDbs.bicep"

# Flags
ASSUME_YES=false
SKIP_SETUP=false
PATCH_SKU=true
FROM=""
ONLY=""

# ----------------------------------------------------------------------------
# Pretty logging
# ----------------------------------------------------------------------------
c_reset=$'\e[0m'; c_bold=$'\e[1m'; c_red=$'\e[31m'; c_grn=$'\e[32m'; c_ylw=$'\e[33m'; c_blu=$'\e[36m'
log()   { printf '%s\n' "${c_blu}[asdk]${c_reset} $*"; }
ok()    { printf '%s\n' "${c_grn}[ ok ]${c_reset} $*"; }
warn()  { printf '%s\n' "${c_ylw}[warn]${c_reset} $*"; }
err()   { printf '%s\n' "${c_red}[fail]${c_reset} $*" >&2; }
hr()    { printf '%s\n' "${c_bold}------------------------------------------------------------${c_reset}"; }
die()   { err "$*"; exit 1; }

# ----------------------------------------------------------------------------
# Argument parsing
# ----------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    -y|--yes)        ASSUME_YES=true; shift ;;
    --skip-setup)    SKIP_SETUP=true; shift ;;
    --no-sku-patch)  PATCH_SKU=false; shift ;;
    --from)          FROM="${2:-}"; shift 2 ;;
    --only)          ONLY="${2:-}"; shift 2 ;;
    -h|--help)       grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)               die "Unknown argument: $1 (use --help)" ;;
  esac
done

confirm() {
  # confirm "message" -> returns 0 to proceed, exits 0 if user declines
  $ASSUME_YES && return 0
  local reply
  read -r -p "${c_bold}$1${c_reset} [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]] && return 0
  log "Stopping at your request."; exit 0
}

# ----------------------------------------------------------------------------
# Locate repo root
# ----------------------------------------------------------------------------
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" \
  || die "Run this from inside your forked azure-saas git repo."
cd "$REPO_ROOT" || die "Cannot cd to repo root: $REPO_ROOT"
log "Repo root: ${REPO_ROOT}"

# ----------------------------------------------------------------------------
# Preflight checks
# ----------------------------------------------------------------------------
preflight() {
  hr; log "Preflight checks"
  local missing=0
  for bin in bash docker az gh jq python3; do
    if command -v "$bin" >/dev/null 2>&1; then ok "found: $bin"
    else err "missing required tool: $bin"; missing=1; fi
  done
  (( missing == 0 )) || die "Install the missing tools above and re-run."

  # Docker daemon must be running.
  docker info >/dev/null 2>&1 && ok "docker daemon is running" \
    || die "Docker daemon not reachable. Start Docker Desktop / dockerd."

  # Azure CLI must be logged in.
  if az account show >/dev/null 2>&1; then
    ok "az logged in (subscription: $(az account show --query name -o tsv 2>/dev/null))"
  else
    die "Azure CLI not logged in. Run: az login"
  fi

  # GitHub CLI must be logged in (the kit deploys app code via GitHub Actions).
  if gh auth token >/dev/null 2>&1; then ok "gh authenticated"
  else die "GitHub CLI not authenticated. Run: gh auth login"; fi

  # Warn if this looks like the upstream repo rather than a fork.
  local origin; origin="$(git config --get remote.origin.url 2>/dev/null || true)"
  log "git origin: ${origin:-<none>}"
  if [[ "$origin" == *"Azure/azure-saas"* ]]; then
    warn "origin points at Azure/azure-saas. The kit must run from YOUR FORK so it can"
    warn "create OIDC credentials and trigger GitHub Actions. Fork the repo first."
    confirm "Continue anyway?"
  fi
  ok "Preflight complete."
}

# ----------------------------------------------------------------------------
# SKU enforcement (idempotent) — only edits the resources we care about.
# ----------------------------------------------------------------------------
patch_skus() {
  $PATCH_SKU || { warn "Skipping SKU patch (--no-sku-patch)."; return 0; }
  hr; log "Pinning SKUs: App Service Plan='${APP_PLAN_SKU}' (Standard), SQL DBs='${SQL_DB_SKU_NAME}/${SQL_DB_SKU_TIER}' (DTU Basic)"

  [[ -f "$APP_PLAN_BICEP" ]] || die "Not found: $APP_PLAN_BICEP (unexpected repo layout)"
  [[ -f "$SQL_BICEP"      ]] || die "Not found: $SQL_BICEP (unexpected repo layout)"

  APP_PLAN_BICEP="$APP_PLAN_BICEP" SQL_BICEP="$SQL_BICEP" \
  APP_PLAN_SKU="$APP_PLAN_SKU" SQL_DB_SKU_NAME="$SQL_DB_SKU_NAME" SQL_DB_SKU_TIER="$SQL_DB_SKU_TIER" \
  python3 - <<'PY' || die "SKU patch failed."
import os, re, sys

plan_path = os.environ["APP_PLAN_BICEP"]
sql_path  = os.environ["SQL_BICEP"]
plan_sku  = os.environ["APP_PLAN_SKU"]
sku_name  = os.environ["SQL_DB_SKU_NAME"]
sku_tier  = os.environ["SQL_DB_SKU_TIER"]

# --- App Service Plan: only the Microsoft.Web/serverfarms resource's sku.name ---
src = open(plan_path).read()
# Match the serverfarms resource block, then its first  sku: { name: '...' }
pat = re.compile(
    r"(resource\s+\w+\s+'Microsoft\.Web/serverfarms@[^']+'\s*=\s*\{"
    r".*?sku:\s*\{\s*name:\s*')[^']*(')",
    re.DOTALL,
)
new, n = pat.subn(lambda m: m.group(1) + plan_sku + m.group(2), src, count=1)
if n != 1:
    sys.exit(f"Could not locate App Service Plan sku block in {plan_path}")
open(plan_path, "w").write(new)
print(f"  appPlan.bicep: App Service Plan sku.name -> {plan_sku}")

# --- SQL: every Microsoft.Sql/servers/databases resource -> name + tier ---
src = open(sql_path).read()
pat = re.compile(
    r"(resource\s+\w+\s+'Microsoft\.Sql/servers/databases@[^']+'\s*=\s*\{"
    r".*?sku:\s*\{\s*name:\s*')[^']*('\s*tier:\s*')[^']*(')",
    re.DOTALL,
)
new, n = pat.subn(lambda m: m.group(1) + sku_name + m.group(2) + sku_tier + m.group(3), src)
if n < 1:
    sys.exit(f"Could not locate any SQL database sku block in {sql_path}")
open(sql_path, "w").write(new)
print(f"  sqlDbs.bicep:  {n} SQL database sku block(s) -> {sku_name}/{sku_tier}")
PY
  ok "SKUs pinned. Review with: git diff -- '$APP_PLAN_BICEP' '$SQL_BICEP'"
}

# ----------------------------------------------------------------------------
# Deploy one module: ./setup.sh (build container) then ./run.sh
# ----------------------------------------------------------------------------
deploy_module() {
  local name="$1" dir="$2"
  hr
  log "MODULE: ${c_bold}${name}${c_reset}  ->  ${dir}"
  [[ -d "$dir" ]]            || die "Module dir not found: $dir"
  [[ -f "$dir/run.sh" ]]    || die "Missing run.sh in $dir"

  (
    cd "$dir" || exit 1
    chmod +x ./*.sh 2>/dev/null || true

    if ! $SKIP_SETUP && [[ -f ./setup.sh ]]; then
      log "[$name] Running ./setup.sh (builds shared deploy container + folders)…"
      ./setup.sh || { err "[$name] setup.sh failed"; exit 1; }
    else
      warn "[$name] Skipping setup.sh"
    fi

    log "[$name] Running ./run.sh (runs deployment inside the container)…"
    log "[$name] NOTE: this stage is interactive — follow on-screen Azure/B2C prompts."

    # Capture output while preserving the interactive TTY (tee doesn't touch stdin).
    run_log="$(mktemp 2>/dev/null)" || run_log="/tmp/asdk-${name}-run.log"
    ./run.sh 2>&1 | tee "$run_log"
    rc=${PIPESTATUS[0]}
    if (( rc != 0 )); then err "[$name] run.sh failed (exit $rc)"; rm -f "$run_log"; exit 1; fi

    # Guard: the kit's run.sh exits 0 even when it only printed "fill in your
    # config and run again" without deploying anything. Treat that as a failure
    # so we don't cascade into the next module with empty/null outputs.
    if grep -qiE 'add required initial settings|run this script again' "$run_log"; then
      rm -f "$run_log"
      err "[$name] run.sh exited 0 but reported INCOMPLETE configuration — nothing was deployed."
      err "[$name] Fill in the required fields in '$dir/config/config.json' (the initConfig object),"
      err "[$name] then resume with: $0 --from $name"
      exit 1
    fi
    rm -f "$run_log"
  ) || die "Module '$name' failed. Fix the error above, then resume with: $0 --from $name"

  ok "[$name] module finished."
  if [[ "$name" != "identity" ]]; then
    log "[$name] App code publishes via GitHub Actions in your fork."
    log "[$name] Watch it with:  gh run list   /   gh run watch"
    confirm "Has the '${name}' GitHub Action completed successfully? Continue to next module?"
  else
    confirm "Identity Foundation done (RG, shared App Service Plan, SQL, B2C, OIDC). Continue?"
  fi
}

# ----------------------------------------------------------------------------
# Build the run list honouring --from / --only
# ----------------------------------------------------------------------------
build_run_list() {
  local started=false
  RUN_LIST=()
  for entry in "${MODULES[@]}"; do
    local name="${entry%%|*}"
    if [[ -n "$ONLY" ]]; then
      [[ "$name" == "$ONLY" ]] && RUN_LIST+=("$entry")
      continue
    fi
    if [[ -n "$FROM" && "$started" == false ]]; then
      [[ "$name" == "$FROM" ]] && started=true || continue
    fi
    RUN_LIST+=("$entry")
  done
  if [[ ${#RUN_LIST[@]} -eq 0 ]]; then
    die "Nothing to run. Check --from/--only value (valid: identity permissions admin signup application)."
  fi
}

# ----------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------
main() {
  hr; log "${c_bold}Azure SaaS Dev Kit — sequential deployment${c_reset}"
  preflight
  patch_skus

  build_run_list
  hr; log "Will deploy these modules in order:"
  for entry in "${RUN_LIST[@]}"; do log "  • ${entry%%|*}"; done
  confirm "Proceed with the deployment above?"

  for entry in "${RUN_LIST[@]}"; do
    deploy_module "${entry%%|*}" "${entry##*|}"
  done

  hr
  ok "${c_bold}All requested modules deployed.${c_reset}"
  log "Shared App Service Plan SKU: ${APP_PLAN_SKU} (Standard)  |  SQL DBs: ${SQL_DB_SKU_NAME}/${SQL_DB_SKU_TIER} (DTU Basic)"
  log "Final check: confirm every GitHub Action run is green (gh run list) and browse the apps."
}

main "$@"
