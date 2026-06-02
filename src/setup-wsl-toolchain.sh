#!/usr/bin/env bash
#
# setup-wsl-toolchain.sh — Provision the ASDK deploy toolchain inside a WSL2 Ubuntu distro.
#
# WHY THIS EXISTS
#   The Azure SaaS Dev Kit (and ./deploy-asdk.sh) only run on Linux or macOS.
#   On Windows that means WSL2 — NOT Git Bash / MSYS, where OSTYPE=msys and the
#   kit's get-os() bails out. Run this ONCE inside a fresh Ubuntu WSL distro to
#   install everything deploy-asdk.sh needs, then clone your fork and deploy.
#
# WHAT IT INSTALLS / CHECKS
#   - Azure CLI (az)      via Microsoft's apt repo
#   - GitHub CLI (gh)     via GitHub's apt repo
#   - jq, python3, unzip, git, ca-certificates, curl
#   - docker CLI reachability (provided by Docker Desktop's WSL integration)
#
# USAGE  (run INSIDE Ubuntu WSL, not Git Bash / PowerShell)
#   wsl -d Ubuntu                 # from Windows, drop into the distro
#   cd ~                          # stay on the Linux fs, not /mnt/c
#   bash setup-wsl-toolchain.sh   # provision the toolchain
#
set -u -o pipefail

# ----------------------------------------------------------------------------
# Pretty logging (mirrors deploy-asdk.sh)
# ----------------------------------------------------------------------------
c_reset=$'\e[0m'; c_bold=$'\e[1m'; c_red=$'\e[31m'; c_grn=$'\e[32m'; c_ylw=$'\e[33m'; c_blu=$'\e[36m'
log()  { printf '%s\n' "${c_blu}[wsl ]${c_reset} $*"; }
ok()   { printf '%s\n' "${c_grn}[ ok ]${c_reset} $*"; }
warn() { printf '%s\n' "${c_ylw}[warn]${c_reset} $*"; }
err()  { printf '%s\n' "${c_red}[fail]${c_reset} $*" >&2; }
hr()   { printf '%s\n' "${c_bold}------------------------------------------------------------${c_reset}"; }
die()  { err "$*"; exit 1; }

# ----------------------------------------------------------------------------
# 0. Refuse to run anywhere but a real Linux/WSL environment.
# ----------------------------------------------------------------------------
hr; log "${c_bold}ASDK WSL toolchain setup${c_reset}"
case "${OSTYPE:-}" in
  linux-gnu*) ok "Linux environment detected (OSTYPE=${OSTYPE})." ;;
  *) die "OSTYPE=${OSTYPE:-unknown}. Run this INSIDE Ubuntu WSL, not Git Bash/MSYS/macOS." ;;
esac
if grep -qiE "(microsoft|wsl)" /proc/version 2>/dev/null; then
  ok "Running under WSL."
else
  warn "Could not confirm WSL via /proc/version — continuing (looks like Linux)."
fi
if [[ "$(pwd)" == /mnt/* ]]; then
  warn "You are on a Windows mount ($(pwd)). Clone/run the kit from the Linux fs"
  warn "(e.g. ~/azure-saas) to avoid path, permission and CRLF problems."
fi

SUDO=""
if [[ "$(id -u)" -ne 0 ]]; then
  command -v sudo >/dev/null 2>&1 || die "Need root or sudo to install packages."
  SUDO="sudo"
fi

# ----------------------------------------------------------------------------
# 1. Base packages.
# ----------------------------------------------------------------------------
hr; log "Installing base packages (apt)…"
export DEBIAN_FRONTEND=noninteractive
$SUDO apt-get update -y || die "apt-get update failed."
$SUDO apt-get install -y \
  ca-certificates curl apt-transport-https lsb-release gnupg \
  git jq python3 python3-pip unzip \
  || die "Base package install failed."
ok "Base packages installed."

# ----------------------------------------------------------------------------
# 2. Azure CLI (Microsoft apt repo) — skip if already present.
# ----------------------------------------------------------------------------
hr
if command -v az >/dev/null 2>&1; then
  ok "az already installed ($(az version --query '\"azure-cli\"' -o tsv 2>/dev/null))."
else
  log "Installing Azure CLI…"
  curl -sL https://aka.ms/InstallAzureCLIDeb | $SUDO bash || die "Azure CLI install failed."
  ok "Azure CLI installed ($(az version --query '\"azure-cli\"' -o tsv 2>/dev/null))."
fi

# ----------------------------------------------------------------------------
# 3. GitHub CLI (GitHub apt repo) — skip if already present.
# ----------------------------------------------------------------------------
hr
if command -v gh >/dev/null 2>&1; then
  ok "gh already installed ($(gh --version | head -n1))."
else
  log "Installing GitHub CLI…"
  $SUDO mkdir -p -m 755 /etc/apt/keyrings
  curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
    | $SUDO tee /etc/apt/keyrings/githubcli-archive-keyring.gpg >/dev/null \
    || die "Failed to fetch GitHub CLI keyring."
  $SUDO chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
    | $SUDO tee /etc/apt/sources.list.d/github-cli.list >/dev/null
  $SUDO apt-get update -y && $SUDO apt-get install -y gh || die "GitHub CLI install failed."
  ok "GitHub CLI installed ($(gh --version | head -n1))."
fi

# ----------------------------------------------------------------------------
# 4. Docker reachability (provided by Docker Desktop WSL integration).
# ----------------------------------------------------------------------------
hr; log "Checking Docker…"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  ok "docker CLI works and the daemon is reachable."
else
  warn "docker is not reachable inside this distro."
  warn "Fix: Docker Desktop → Settings → Resources → WSL Integration →"
  warn "     enable this distro, then 'Apply & Restart' and re-open the shell."
fi

# ----------------------------------------------------------------------------
# 5. Summary + next steps.
# ----------------------------------------------------------------------------
hr; ok "${c_bold}Toolchain check complete.${c_reset}"
log "Versions:"
for b in git jq python3 az gh docker; do
  if command -v "$b" >/dev/null 2>&1; then
    printf '  %-8s %s\n' "$b" "$($b --version 2>/dev/null | head -n1)"
  else
    printf '  %-8s %s\n' "$b" "${c_red}MISSING${c_reset}"
  fi
done
hr
log "Next steps (inside this WSL distro):"
log "  1. az login        # interactive"
log "  2. gh auth login   # interactive"
log "  3. git clone https://github.com/sannilincoln/azure-saas.git ~/azure-saas"
log "  4. cd ~/azure-saas && ./src/deploy-asdk.sh"
