# Blackwall

Blackwall is a self-hosted Discord anti-spam system. It combines a real-time bot that detects and removes spam messages with a web dashboard for managing guild configurations — all backed by Discord OAuth2 authentication.

This bot objective is to provide a simple and effective way to manage spam in your server, with a focus on ease of use and customization.
Tools provided by the community and improving upon the existing ones.

> **Don't want to self-host?** You can add the bot to your server directly at [blackwall.observer](https://blackwall.observer).

## Features

- **Real-time spam detection** — rate limiting, duplicate message detection, mention flooding, invite link blocking, and suspicious link filtering.
- **Per-guild configuration** — each server gets its own spam thresholds managed through the dashboard.
- **Discord OAuth2 login** — users authenticate via Discord; JWT tokens are issued for API/Web access.
- **Guild permission sync** — a background service periodically synchronises guild manager permissions from Discord.
- **Web dashboard** — Blazor Server UI for guild owners and managers to configure spam rules.

## Account Scoring

Account Scoring evaluates new members' account metadata the moment they join the server and assigns a hidden **threat score**. It works independently of anti-raid — you can enable it even if velocity lockdowns are off.

### Scoring Factors

Each factor adds points to the user's total score:

| Factor | Condition | Points |
|--------|-----------|--------|
| **Account age** | Less than 1 day old | +3 |
| | Less than 7 days old | +2 |
| | Less than 30 days old | +1 |
| **Profile picture** | Default avatar (no custom avatar set) | +2 |
| **Username — numeric only** | Username consists entirely of digits | +2 |
| **Username — gibberish** | Username is 6+ alphanumeric characters with no discernible pattern | +2 |
| **Username — consecutive consonants** | Username contains 5+ consecutive consonants | +1 |

### Threat Levels

The total score maps to a threat level:

| Level | Score Range | Behavior |
|-------|-------------|----------|
| **Low** | 0–1 | No action taken |
| **Medium** | 2–3 | Moderators are alerted in the audit log channel |
| **High** | 4+ | Moderators are alerted in the audit log channel |

### Configurable Actions

For medium and high risk accounts, you can independently enable **auto-timeout**:

- **Auto-timeout medium risk** — automatically places medium-risk users in a timeout on join
- **Auto-timeout high risk** — automatically places high-risk users in a timeout on join

If auto-timeout is disabled for a given level, moderators are alerted only and can manually decide whether to intervene. The timeout duration is configurable (default: 10 minutes).

All alerts are sent to the configured **audit log channel** as an embed containing the user, their score, account age, the specific risk factors that contributed, and the action taken.

## Architecture

The solution is split into five projects:

| Project | Description |
|---------|-------------|
| `Blackwall.Core` | Shared entities, DTOs, and configuration models |
| `Blackwall.Infrastructure` | PostgreSQL persistence (EF Core) and Redis caching |
| `Blackwall.Bot` | Discord gateway client — spam detection, guild events, background sync |
| `Blackwall.Api` | ASP.NET Core Web API — OAuth flows, JWT auth, guild management endpoints |
| `Blackwall.Web` | Blazor Server dashboard — authenticates against the API |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 17+
- Redis 7+
- A Discord application with bot and OAuth2 credentials

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/<your-username>/Blackwall.git
cd Blackwall
```

### 2. Configure environment variables

```bash
cp .env.example .env
```

Edit `.env` and fill in the required values:

**Infrastructure**

| Variable | Description |
|----------|-------------|
| `POSTGRES__CONNECTION_STRING` | PostgreSQL connection string |
| `REDIS__CONNECTION_STRING` | Redis connection string |

**Discord**

| Variable | Description                                                                          |
|----------|--------------------------------------------------------------------------------------|
| `DISCORD__BOT_TOKEN` | Discord bot token                                                                    |
| `DISCORD__CLIENT_ID` | Discord OAuth2 client ID                                                             |
| `DISCORD__CLIENT_SECRET` | Discord OAuth2 client secret                                                         |
| `DISCORD__REDIRECT_URI` | OAuth2 callback URL (e.g. `https://public-url.tld/api/auth/discord/callback`)        |
| `DISCORD__BOT_PERMISSIONS` | Bot permission integer (default `10308992462014` — matches the listed permissions below; minimal permissions are `1374389610518`) |
| `DISCORD__BOT_SCOPES` | Bot invite scopes (default `bot applications.commands`)                              |
| `DISCORD__LOGIN_SCOPES` | OAuth2 login scopes (default `identify guilds`)                                      |

> **Tip:** Using the default `10308992462014` keeps permissions fine-grained and matches the full Discord permissions listed for Blackwall. The minimal set is `1374389610518`.

**Google Safe Browsing**

| Variable | Description |
|----------|-------------|
| `SAFE_BROWSING__ENABLED` | Enable or disable Google Safe Browsing (default `true`). When `false`, the Safe Browsing card is hidden in the dashboard and all checks are skipped. |
| `SAFE_BROWSING__API_KEY` | Google Safe Browsing API key. Get one from the [Google Cloud Console](https://console.cloud.google.com/) by enabling the **Safe Browsing API** and creating an API key under **APIs & Services → Credentials**. |
| `SAFE_BROWSING__BASE_URL` | Safe Browsing API base URL (default `https://safebrowsing.googleapis.com/v5`) |

**JWT**

| Variable | Description |
|----------|-------------|
| `JWT__ISSUER` | Token issuer claim (default `Api`) |
| `JWT__AUDIENCE` | Token audience claim (default `Web`) |
| `JWT__SECRET` | Symmetric key for signing JWTs (min 32 characters) |

**Application**

| Variable | Description |
|----------|-------------|
| `APP__ENC_KEY` | AES encryption key for stored Discord tokens |
| `APP__ENC_IV` | AES initialisation vector |
| `APP__DISABLE_NEW_USERS` | When `true`, only the instance owner can register new accounts (existing users can still log in). If `APP__INSTANCE_OWNER` is not set, no new users can register. Defaults to `false`. |
| `APP__PRIVATE_INSTANCE` | When `true` (and `APP__DISABLE_NEW_USERS` is `false`), only users listed in `AllowedUsers` (in `appsettings.json`) and the instance owner can register. Defaults to `false`. |
| `APP__INSTANCE_OWNER` | Discord user ID of the instance owner. The owner is always allowed to register when `APP__DISABLE_NEW_USERS` is `true`, and is also allowed when `APP__PRIVATE_INSTANCE` is `true` (even if not listed in `AllowedUsers`). Optional — the bot starts without it, but no one can register if `APP__DISABLE_NEW_USERS` is `true` and this is unset. |

**API**

| Variable | Description |
|----------|-------------|
| `API__BASE_URL` | Base URL of the API (default `http://localhost:7001` — not recommended to change) |
| `API__PORT` | Port the API listens on (default `7001`) |

**Web**

| Variable | Description |
|----------|-------------|
| `WEB__BASEURL` | Public-facing URL of the Web dashboard (e.g. `https://public-url.tld`) |
| `WEB__PORT` | Port the Web dashboard listens on (default `7002`) |

**Guild Sync**

| Variable | Description |
|----------|-------------|
| `GUILDSYNC__ENABLED` | Enable the guild permission sync background service (default `true`) |
| `GUILDSYNC__INTERVALMINUTES` | Sync interval in minutes (default `15`) |

### 3. Configure `appsettings.json`

In `src/Blackwall.Api/appsettings.json`, set the `AllowedUsers` array to the Discord user IDs permitted to register when `APP__PRIVATE_INSTANCE=true`:

```json
"AllowedUsers": ["123456789012345678", "987654321098765432"]
```

> **Note:** The instance owner (set via `APP__INSTANCE_OWNER`) is always allowed to register even if not listed in `AllowedUsers`.

## Deployment

### Option A: Docker Compose

The quickest way to get everything running. Requires only Docker and Docker Compose.

```bash
docker compose up --build
```

This starts PostgreSQL, Redis, the API (with embedded bot), the Web dashboard, and an Nginx reverse proxy on port 80.

### Option B: Proxmox LXC

The `infra/lxc/` directory contains scripts for provisioning a Debian 13 LXC container and deploying published builds:

1. **Provision the container** — run `setup.sh` as root inside the LXC. It installs .NET 10 runtime, PostgreSQL 17, Redis, nginx, creates the `blackwall` system user, database, and systemd services.

   ```bash
   scp -r infra/lxc/ root@<lxc-ip>:/tmp/blackwall-setup
   ssh root@<lxc-ip> "bash /tmp/blackwall-setup/setup.sh"
   ```

2. **Place your `.env`** at `/opt/blackwall/.env` on the LXC with the credentials printed by the setup script.

3. **Deploy** — from your dev machine, publish and upload both projects:

   ```bash
   ./infra/lxc/deploy.sh <lxc-ip>
   ```

   This builds Release artifacts, uploads them via rsync, fixes ownership, and restarts the systemd services.

   > **Preserving local config across deploys:** The deploy script excludes `.env` and `appsettings.Production.json` from rsync, so user customizations are never overwritten. `appsettings.json` (base defaults) is always synced, meaning structural changes from the repo propagate automatically. To override any settings locally, create `appsettings.Production.json` in `/opt/blackwall/api/` and/or `/opt/blackwall/web/` with only the keys you want to change — ASP.NET Core merges it on top of the base file at runtime (the services already run with `ASPNETCORE_ENVIRONMENT=Production`). For example:
   >
   > ```json
   > {
   >   "Blacklists": {
   >     "Defaults": ["https://example.com/custom-blocklist.txt"]
   >   },
   >   "AllowedUsers": ["123456789012345678"]
   > }
   > ```

### Option C: Bare metal / VM

Install the prerequisites directly on the host machine and run the projects natively. Useful for development or when you want full control.

```bash
# Start PostgreSQL and Redis on the host (or point to remote instances in .env)

# Run the API + Bot
dotnet run --project src/Blackwall.Api

# Run the Web dashboard (separate terminal)
dotnet run --project src/Blackwall.Web
```

For production, publish Release builds and host them behind a reverse proxy:

```bash
dotnet publish src/Blackwall.Api -c Release -o /opt/blackwall/api
dotnet publish src/Blackwall.Web -c Release -o /opt/blackwall/web
```

### Discord Permissions
#### Default Set `10308992462014`
`Admin`,
`Manager Server`,
`Manage Roles`,
`Manage Channels`,
`Kick Members`,
`Ban Members`,
`Manage Nicknames`,
`Change Nickname`,
`Manage Expressions`,
`Manage Webhooks`,
`View Audit Log`,
`View Channels`,
`Moderate Members`,
`Send Messages`,
`Send Messages In Threads`,
`Menage Messages`,
`Embed Links`,
`Read Message History`,
`Use External Emoji`,
`Use External Stickers`,
`Connect`,
`Move Members`

#### Minimal Set `1374389610518`
`Manage Channels`, 
`Kick members`, 
`Ban Members`, 
`Moderate Members`, 
`Send Messages`, 
`Send Messages In Threads`,
`Manage Messages`, 
`Read Message History`

## API Documentation

When `ENABLE_DOCS=true` is set in your environment, the API exposes an OpenAPI spec and Scalar UI:

- **OpenAPI spec** — `/openapi/v1.json`
- **Scalar UI** — `/scalar/v1`

## Project Structure

```
Blackwall/
├── src/
│   ├── Blackwall.Api/          # Web API + Discord bot host
│   ├── Blackwall.Bot/          # Bot worker, handlers, background services
│   ├── Blackwall.Core/         # Entities, DTOs, configuration
│   ├── Blackwall.Infrastructure/ # EF Core DbContext, Redis cache
│   └── Blackwall.Web/          # Blazor Server dashboard
├── infra/                      # Infrastructure scripts
├── nginx/                      # Nginx reverse proxy config
├── docker-compose.yml
├── Dockerfile.api
├── Dockerfile.web
└── .env.example
```

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
