#!/usr/bin/env bash
# Blackwall — deploy published artifacts to a remote LXC
# Usage: ./deploy.sh <lxc-ip> [ssh-user]
set -euo pipefail

LXC_HOST="${1:?Usage: $0 <lxc-ip> [ssh-user]}"
SSH_USER="${2:-root}"
REMOTE="$SSH_USER@$LXC_HOST"
APP_DIR="/opt/blackwall"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$REPO_ROOT/.publish"

# Define a temporary socket file for the SSH master connection
SSH_SOCKET="/tmp/blackwall_deploy_socket_$$"

info()    { echo -e "\e[34m[INFO]\e[0m  $*"; }
success() { echo -e "\e[32m[OK]\e[0m    $*"; }

# Clean up the SSH master connection and output dir when the script exits (success or fail)
cleanup() {
    info "Closing SSH master connection and cleaning up..."
    ssh -S "$SSH_SOCKET" -O exit "$REMOTE" 2>/dev/null || true
    rm -rf "$OUT_DIR"
}
trap cleanup EXIT

# ─── Publish ──────────────────────────────────────────────────────────────────
info "Publishing Blackwall.Api..."
dotnet publish "$REPO_ROOT/src/Blackwall.Api/Blackwall.Api.csproj" \
    -c Release -o "$OUT_DIR/api" --nologo -v q

info "Publishing Blackwall.Web..."
dotnet publish "$REPO_ROOT/src/Blackwall.Web/Blackwall.Web.csproj" \
    -c Release -o "$OUT_DIR/web" --nologo -v q

success "Publish complete."

# ─── Open Master SSH Connection ───────────────────────────────────────────────
info "Opening master SSH connection to $REMOTE..."
# This is the ONLY time you will be prompted for a password.
ssh -M -S "$SSH_SOCKET" -fnNT "$REMOTE"
success "Master connection established."

# ─── Upload ───────────────────────────────────────────────────────────────────
info "Uploading API and Web to $REMOTE:$APP_DIR/ ..."
# Using a single rsync command to sync the parent directory saves time.
# The -e flag tells rsync to use our existing SSH socket.
# The --exclude flags prevent user-customized files from being deleted or overwritten.
# .env holds secrets; appsettings.Production.json holds local config overrides.
# appsettings.json (base defaults) is still synced so structural changes propagate.
rsync -az -e "ssh -S $SSH_SOCKET" --delete \
    --exclude=".env" \
    --exclude="appsettings.Production.json" \
    --exclude="modules" \
    "$OUT_DIR/" "$REMOTE:$APP_DIR/"

success "Upload complete."

# ─── Fix ownership and restart ────────────────────────────────────────────────
info "Fixing ownership and restarting services..."
# We pass the -S flag so ssh uses the master socket and doesn't ask for a password.
ssh -S "$SSH_SOCKET" "$REMOTE" "chown -R blackwall:blackwall $APP_DIR/api $APP_DIR/web && \
               mkdir -p $APP_DIR/modules && chown blackwall:blackwall $APP_DIR/modules && chmod 700 $APP_DIR/modules && \
               if [ -f $APP_DIR/.env ]; then chown root:root $APP_DIR/.env && chmod 600 $APP_DIR/.env; fi && \
               systemctl restart blackwall-api blackwall-web && \
               systemctl is-active blackwall-api && \
               systemctl is-active blackwall-web"

success "Deployment done. Both services are running."