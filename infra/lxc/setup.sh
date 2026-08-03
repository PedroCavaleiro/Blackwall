#!/usr/bin/env bash
# Blackwall — Proxmox LXC provisioning script
# Target: Debian 13 (Trixie)
# Run as root inside the LXC container.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_USER="blackwall"
APP_DIR="/opt/blackwall"
NGINX_SITE="blackwall"

# ─── Helpers ──────────────────────────────────────────────────────────────────
info()    { echo -e "\e[34m[INFO]\e[0m  $*"; }
success() { echo -e "\e[32m[OK]\e[0m    $*"; }
warn()    { echo -e "\e[33m[WARN]\e[0m  $*"; }

# ─── System update ────────────────────────────────────────────────────────────
info "Updating system packages..."
apt-get update -qq
apt-get upgrade -y -qq
apt-get install -y -qq curl wget gnupg2 lsb-release ca-certificates openssl rsync

# ─── .NET 10 ASP.NET Core Runtime ─────────────────────────────────────────────
if ! command -v dotnet &>/dev/null || [[ "$(dotnet --version 2>/dev/null | cut -d. -f1)" -lt 10 ]]; then
    info "Installing .NET 10 ASP.NET Core Runtime..."
    wget -q "https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb" \
        -O /tmp/packages-microsoft-prod.deb
    dpkg -i /tmp/packages-microsoft-prod.deb
    rm /tmp/packages-microsoft-prod.deb
    apt-get update -qq
    apt-get install -y aspnetcore-runtime-10.0
    success ".NET 10 installed: $(dotnet --version)"
else
    success ".NET already installed: $(dotnet --version)"
fi

# ─── PostgreSQL 17 ────────────────────────────────────────────────────────────
if ! command -v psql &>/dev/null; then
    info "Installing PostgreSQL 17..."
    install -d /usr/share/postgresql-common/pgdg
    curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
    echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] \
https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list
    apt-get update -qq
    apt-get install -y postgresql-17
    systemctl enable postgresql
    systemctl start postgresql
    success "PostgreSQL 17 installed."
else
    success "PostgreSQL already installed: $(psql --version)"
fi

# ─── Redis ────────────────────────────────────────────────────────────────────
if ! command -v redis-server &>/dev/null; then
    info "Installing Redis..."
    curl -fsSL https://packages.redis.io/gpg \
        | gpg --dearmor -o /usr/share/keyrings/redis-archive-keyring.gpg
    echo "deb [signed-by=/usr/share/keyrings/redis-archive-keyring.gpg] \
https://packages.redis.io/deb $(lsb_release -cs) main" \
        > /etc/apt/sources.list.d/redis.list
    apt-get update -qq
    apt-get install -y redis-server
    # Bind to localhost only
    sed -i 's/^bind .*/bind 127.0.0.1 -::1/' /etc/redis/redis.conf
    systemctl enable redis-server
    systemctl start redis-server
    success "Redis installed."
else
    success "Redis already installed."
fi

# ─── nginx ────────────────────────────────────────────────────────────────────
if ! command -v nginx &>/dev/null; then
    info "Installing nginx..."
    apt-get install -y nginx
    success "nginx installed."
else
    success "nginx already installed."
fi

# ─── Application user and directories ────────────────────────────────────────
info "Creating app user and directories..."
if ! id -u "$APP_USER" &>/dev/null; then
    useradd --system --shell /usr/sbin/nologin \
            --home-dir "$APP_DIR" --create-home "$APP_USER"
fi
mkdir -p "$APP_DIR/api" "$APP_DIR/web" "$APP_DIR/modules"
chown -R "$APP_USER:$APP_USER" "$APP_DIR"
chmod 700 "$APP_DIR/modules"
success "User '$APP_USER' and directories ready at $APP_DIR."

# ─── PostgreSQL: create DB and user ──────────────────────────────────────────
DB_PASS=$(openssl rand -hex 32)
info "Provisioning PostgreSQL database..."
if sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='blackwall'" | grep -q 1; then
    warn "PostgreSQL role 'blackwall' already exists — skipping creation."
else
    sudo -u postgres psql -c "CREATE USER blackwall WITH PASSWORD '$DB_PASS';"
    sudo -u postgres psql -c "CREATE DATABASE blackwall OWNER blackwall;"
    success "Database 'blackwall' created."
    echo ""
    echo "  ┌─────────────────────────────────────────────────────────────────┐"
    echo "  │  SAVE THESE — you'll need them in your .env                     │"
    echo "  │                                                                 │"
    echo "  │  POSTGRES__CONNECTION_STRING=Host=localhost;Database=blackwall; │"
    echo "  │    Username=blackwall;Password=$DB_PASS  │"
    echo "  │  REDIS__CONNECTION_STRING=localhost:6379                        │"
    echo "  │  API__BASE_URL=http://localhost:7001                            │"
    echo "  └─────────────────────────────────────────────────────────────────┘"
    echo ""
fi

# ─── systemd services ────────────────────────────────────────────────────────
info "Installing systemd services..."
cp "$SCRIPT_DIR/blackwall-api.service" /etc/systemd/system/blackwall-api.service
cp "$SCRIPT_DIR/blackwall-web.service" /etc/systemd/system/blackwall-web.service
systemctl daemon-reload
systemctl enable blackwall-api blackwall-web
success "systemd services installed."

# ─── nginx site ──────────────────────────────────────────────────────────────
info "Configuring nginx..."
cp "$SCRIPT_DIR/nginx.conf" "/etc/nginx/sites-available/$NGINX_SITE"
ln -sf "/etc/nginx/sites-available/$NGINX_SITE" "/etc/nginx/sites-enabled/$NGINX_SITE"
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl enable nginx
systemctl reload nginx
success "nginx configured."

# ─── Done ────────────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo " Blackwall LXC provisioning complete."
echo ""
echo " Next steps:"
echo "   1. Place your .env at:    $APP_DIR/.env"
echo "      Secure it with:"
echo "        chown root:root $APP_DIR/.env && chmod 600 $APP_DIR/.env"
echo "      (systemd reads it as root via EnvironmentFile=; the blackwall user cannot read it)"
echo "   2. Deploy API build to:   $APP_DIR/api/"
echo "   3. Deploy Web build to:   $APP_DIR/web/"
echo "   4. Module directory:      $APP_DIR/modules/ (chmod 700, owned by blackwall)"
echo "   5. Run Entity Framework migrations (as blackwall-api)"
echo "   6. Start services:"
echo "        systemctl start blackwall-api blackwall-web"
echo "   7. Check logs:"
echo "        journalctl -u blackwall-api -f"
echo "        journalctl -u blackwall-web -f"
echo "═══════════════════════════════════════════════════════════════"
