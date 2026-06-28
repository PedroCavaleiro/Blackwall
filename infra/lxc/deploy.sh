#!/usr/bin/env bash
# Blackwall — deploy published artifacts to a remote LXC
# Usage: ./deploy.sh <lxc-ip> [ssh-user]
# Example: ./deploy.sh 192.168.1.50
# Example: ./deploy.sh 192.168.1.50 root
set -euo pipefail

LXC_HOST="${1:?Usage: $0 <lxc-ip> [ssh-user]}"
SSH_USER="${2:-root}"
REMOTE="$SSH_USER@$LXC_HOST"
APP_DIR="/opt/blackwall"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$REPO_ROOT/.publish"

info()    { echo -e "\e[34m[INFO]\e[0m  $*"; }
success() { echo -e "\e[32m[OK]\e[0m    $*"; }

# ─── Publish ──────────────────────────────────────────────────────────────────
info "Publishing Blackwall.Api..."
dotnet publish "$REPO_ROOT/src/Blackwall.Api/Blackwall.Api.csproj" \
    -c Release -o "$OUT_DIR/api" --nologo -v q

info "Publishing Blackwall.Web..."
dotnet publish "$REPO_ROOT/src/Blackwall.Web/Blackwall.Web.csproj" \
    -c Release -o "$OUT_DIR/web" --nologo -v q

success "Publish complete."

# ─── Upload ───────────────────────────────────────────────────────────────────
info "Uploading API to $REMOTE:$APP_DIR/api/ ..."
rsync -az --delete "$OUT_DIR/api/" "$REMOTE:$APP_DIR/api/"

info "Uploading Web to $REMOTE:$APP_DIR/web/ ..."
rsync -az --delete "$OUT_DIR/web/" "$REMOTE:$APP_DIR/web/"

success "Upload complete."

# ─── Fix ownership and restart ────────────────────────────────────────────────
info "Fixing ownership and restarting services..."
ssh "$REMOTE" "chown -R blackwall:blackwall $APP_DIR/api $APP_DIR/web && \
               systemctl restart blackwall-api blackwall-web && \
               systemctl is-active blackwall-api && \
               systemctl is-active blackwall-web"

success "Deployment done. Both services are running."

rm -rf "$OUT_DIR"
