# Blackwall

Blackwall is a self-hosted anti-spam and moderation platform for **Discord** and **Twitch**. It combines real-time bots that detect and remove spam messages with a web dashboard for managing server and channel configurations — all backed by Discord and Twitch OAuth2 authentication.

This bot objective is to provide a simple and effective way to manage spam in your community, with a focus on ease of use and customization.
Tools provided by the community and improving upon the existing ones.

> **Don't want to self-host?** You can add the bot to your server directly at [blackwall.observer](https://blackwall.observer).

> Full Wiki is available at [pedrocavaleiro.github.io/Blackwall](https://pedrocavaleiro.github.io/Blackwall).

## Features

- **Real-time spam detection** — sliding-window rate limiting, sliding-window duplicate detection with cross-channel support, mention flooding, invite link blocking, and suspicious link filtering.
- **Content Guard** — banned word filtering with Levenshtein fuzzy matching and leetspeak normalisation, Zalgo text blocking, copypasta hash detection, and invisible character scrubbing.
- **Link blocking** — AdGuard-format blacklist support with custom domains, whitelist mode, and Google Safe Browsing real-time threat checks via a locally synced Global Cache.
- **Anti-raid** — velocity-based join detection that automatically pauses invites when a raid threshold is reached, with configurable cooldown.
- **Account scoring** — evaluates new members on join based on account age, profile picture, and username patterns; assigns a threat score with optional auto-timeout.
- **NetWatchSnare** — trap channels that automatically punish users who post in them, with configurable actions per trap.
- **Lockdown** — instantly deny send permissions for @everyone across all channels, manually or automatically on infraction.
- **Ban management** — share, sync, and import bans across Blackwall-managed servers with auto-sync support.
- **Message audit** — records deleted messages and infraction events with configurable retention for review in the dashboard.
- **Bot allowlist** — only explicitly trusted bots bypass detection; all other bot-flagged accounts are scanned.
- **Dry run mode** — test detection rules without taking action against users.
- **Per-guild / per-channel configuration** — each Discord server and Twitch channel gets its own spam thresholds, module toggles, and actions managed through the dashboard.
- **Discord & Twitch OAuth2 login** — users authenticate via Discord or Twitch; JWT tokens are issued for API/Web access.
- **Guild permission sync** — a background service periodically synchronises guild manager permissions from Discord.
- **Twitch ban sync** — a background service synchronises shared ban lists across Blackwall-managed Twitch channels.
- **Web dashboard** — Blazor Server UI for guild/channel owners and managers to configure spam rules visually.
- **Third-party modules** — extend Blackwall's detection capabilities with community-built modules. Modules are compiled .NET assemblies loaded dynamically from Git repositories, supporting Discord, Twitch, or both platforms simultaneously.

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

## NetWatchSnare

NetWatchSnare provides **trap channels** — channels where any user who posts in them is automatically punished. This is useful for channels that should never receive messages, such as announcement channels where only admins post, or decoy channels designed to catch bots and spammers scanning the channel list.

### How It Works

- Each trap channel is configured individually with its own action, timeout duration, and message delete days.
- When a user sends a message in a trap channel, the message is immediately deleted and the configured action is applied.
- Trap channels are checked early in the message processing pipeline — before any other spam detection runs.
- The action is applied even if the guild is in dry-run mode (the trap always fires).

### Configuration

Each trap channel has:

| Setting | Description |
|---------|-------------|
| **Channel** | The Discord channel to monitor. |
| **Action** | What happens to the user: delete only, timeout, kick, or ban. |
| **Timeout duration** | How long the user is timed out (when action is timeout). |
| **Message delete days** | Days of message history to prune when banning (0–7). |

## Content Guard

Content Guard is the advanced toxicity and evasion filtering layer. It goes beyond simple keyword matching by detecting intentional obfuscation, Unicode abuse, and coordinated copypasta attacks. Each algorithm can be toggled independently — if all algorithms are disabled, only the basic banned word list is enforced. Disabling Content Guard entirely turns off all filtering including the banned word list.

### Algorithms

| Algorithm | Settings | Description |
|-----------|----------|-------------|
| **Levenshtein fuzzy matching** | Edit distance threshold (1–5) | Uses fuzzy string matching to catch intentional misspellings and leetspeak (e.g. replacing `i` with `1` or `e` with `3`). Tokens within the edit distance threshold of a banned word are flagged. Leetspeak normalisation is always applied before comparison. |
| **Invisible character scrubbing** | — | Strips zero-width spaces, zero-width joiners, word joiners, BOM, and left/right-to-right marks from messages before evaluating banned words. Prevents bypass via invisible Unicode injection. |
| **Zalgo / clutter blocking** | Max consecutive combining marks (1–10) | Blocks messages containing excessive Unicode combining characters (Zalgo text) that lag mobile clients and visually destroy the chat window. |
| **Copypasta hashing** | Min length (50–5000), distinct user threshold (≥2), time window (10–3600 s) | Calculates a SHA-256 hash of incoming large text blocks. If the same block is posted by multiple distinct users within the time window, all matching messages are flagged. Targets coordinated text spam during live streams. |

### Banned Words

A per-guild editable list of banned words. Words are matched case-insensitively after leetspeak normalisation. Add individual words or short phrases (spaces are preserved). When fuzzy matching is enabled, close variations of banned words are also caught.

### Module Actions

Content Guard has its own configurable action (delete only, timeout, kick, or ban), timeout duration, message delete days (when banning), and auto-lockdown toggle — independent from other detection filters.

## Third-Party Modules

Blackwall supports third-party modules that extend its detection capabilities. Modules are .NET assemblies that implement the `IBlackwallModule` interface and are loaded dynamically from public Git repositories. A module can target Discord, Twitch, or both platforms.

### How It Works

1. A module author publishes a Git repository with a `blackwall-module.json` manifest and a `src/` project directory.
2. A server/channel owner installs the module via the web dashboard — either from the curated catalog or by providing a Git URL directly.
3. Blackwall clones the repo, runs `dotnet build`, and loads the compiled assembly.
4. On every incoming message, each enabled module's `EvaluateAsync` method is called with a 5-second timeout.
5. If a module returns a `ModuleVerdict`, the configured action (delete, timeout, ban) is applied.

### Installation Controls

Instance administrators can control third-party module installation via two environment variables:

- **`MODULES__ALLOW_THIRD_PARTY`** — master switch. When `false`, all module installation is disabled (both catalog and Git URL). The web dashboard shows a "disabled by administrator" message instead of the install UI.
- **`MODULES__CATALOG_ONLY`** — when `ALLOW_THIRD_PARTY` is `true`, setting this to `true` restricts installations to modules listed in the curated catalog only. The manual Git URL input is hidden from the dashboard, and the API rejects Git URLs not present in the registry.

### Module SDK

The module SDK is available as the `Blackwall.Modules.Abstractions` project, which defines:

- `IBlackwallModule` — the interface modules implement
- `ModuleMessageContext` — platform-agnostic message context (includes `Platform` field for Discord/Twitch awareness)
- `ModuleVerdict` — the verdict returned when a violation is detected
- `ModuleSettings` — typed wrapper for module settings
- `BlackwallModuleManifest` — manifest structure with settings schema and platform support
- `ModulePlatform` — enum with `Discord` and `Twitch` values

A shared runtime (`Blackwall.Modules.Runtime`) handles module loading, assembly isolation via `AssemblyLoadContext`, evaluation with timeout, and settings hot-reload.

### Example Module

An example module (`EmojiSpamModule`) is included in `examples/Blackwall.EmojiSpamModule/`. See its [README](examples/Blackwall.EmojiSpamModule/README.md) for a complete guide on creating third-party modules.

## Architecture

The solution is split into the following projects:

| Project | Description |
|---------|-------------|
| `Blackwall.Core` | Shared entities, DTOs, and configuration models |
| `Blackwall.Infrastructure` | PostgreSQL persistence (EF Core) and Redis caching |
| `Blackwall.Bot.Discord` | Discord gateway client — spam detection, guild events, background sync |
| `Blackwall.Bot.Twitch` | Twitch IRC client — spam detection, channel moderation, module evaluation |
| `Blackwall.Api` | ASP.NET Core Web API — OAuth flows, JWT auth, guild/channel management endpoints |
| `Blackwall.Web` | Blazor Server dashboard — authenticates against the API |
| `Blackwall.Modules.Abstractions` | Module SDK — interfaces, manifests, and data structures for third-party modules |
| `Blackwall.Modules.Runtime` | Shared runtime — module loading, evaluation, and build helpers |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 17+
- Redis 7+
- A Discord application with bot and OAuth2 credentials
- A Twitch application with OAuth2 credentials (for Twitch support)

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

Edit `.env` and fill in the required values. You can use the **[.env Generator](https://pedrocavaleiro.github.io/Blackwall/env-generator.html)** to interactively build your `.env` file with helpful descriptions and one-click key generation. The full **[wiki](https://pedrocavaleiro.github.io/Blackwall/)** covers installation, configuration, and deployment in detail.

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

**Twitch**

| Variable | Description |
|----------|-------------|
| `TWITCH__CLIENT_ID` | Twitch OAuth2 client ID |
| `TWITCH__CLIENT_SECRET` | Twitch OAuth2 client secret |
| `TWITCH__REDIRECT_URI` | OAuth2 callback URL for user login (e.g. `https://public-url.tld/api/auth/twitch/callback`) |
| `TWITCH__LOGIN_SCOPES` | OAuth2 login scopes (default `openid user:read:email`) |
| `TWITCH__BOT_SCOPES` | Scopes requested when a channel owner installs the bot |
| `TWITCH__BOT_REDIRECT_URI` | OAuth2 callback URL for bot authorization flow |
| `TWITCH__BOT_USERNAME` | Dedicated bot account username for IRC and moderation API |
| `TWITCH__BOT_ACCESS_TOKEN` | Bot account OAuth access token (without `oauth:` prefix) |
| `TWITCH__BOT_REFRESH_TOKEN` | Bot account OAuth refresh token for auto-renewal |

> **Tip:** Generate Twitch bot tokens at [twitchtokengenerator.com](https://twitchtokengenerator.com/) using a dedicated bot account with `chat:read` and `chat:edit` scopes. The bot account must be added as a moderator in each channel.

**Twitch Sync**

| Variable | Description |
|----------|-------------|
| `TWITCH_SYNC__ENABLED` | Enable the Twitch ban sync background service (default `true`) |
| `TWITCH_SYNC__INTERVAL_MINUTES` | Sync interval in minutes (default `15`) |

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
| `API__PROTECTION_ENABLED` | When `true`, all API endpoints require an `X-API-Key` header (default `true`). Exempt endpoints: `/api/auth/discord/callback`, `/api/system/health`, and `/health`. |
| `API__KEY` | The API key sent via the `X-API-Key` header. Required when `API__PROTECTION_ENABLED` is `true`. Must match between API and Web. Format: `bw_k_` followed by 64 alphanumeric characters (`a-zA-Z0-9`). |

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

**Module Registry**

| Variable | Description |
|----------|-------------|
| `MODULES__REGISTRY_URL` | URL of the curated module catalog index JSON (default `https://raw.githubusercontent.com/PedroCavaleiro/Blackwall.Modules/main/index.json`) |
| `MODULES__REGISTRY_CACHE_MINUTES` | Cache duration for the registry index in minutes (default `15`) |
| `MODULES__ALLOW_THIRD_PARTY` | Master switch for third-party module installation. When `false`, all module installation is disabled — both catalog and Git URL (default `true`) |
| `MODULES__CATALOG_ONLY` | When `ALLOW_THIRD_PARTY` is `true`, restricts installations to modules from the curated catalog only. Hides the manual Git URL input and rejects Git URLs not in the registry (default `true`) |

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
│   ├── Blackwall.Api/                  # Web API + Discord/Twitch bot host
│   ├── Blackwall.Bot.Discord/          # Discord bot worker, handlers, background services
│   ├── Blackwall.Bot.Twitch/           # Twitch IRC client, moderation, module evaluation
│   ├── Blackwall.Core/                 # Entities, DTOs, configuration
│   ├── Blackwall.Infrastructure/       # EF Core DbContext, Redis cache
│   ├── Blackwall.Modules.Abstractions/ # Module SDK — interfaces, manifests, data structures
│   ├── Blackwall.Modules.Runtime/      # Shared runtime — module loading, evaluation, build helpers
│   └── Blackwall.Web/                  # Blazor Server dashboard
├── examples/
│   └── Blackwall.EmojiSpamModule/      # Example third-party module (Discord + Twitch)
├── infra/                              # Infrastructure scripts
├── nginx/                              # Nginx reverse proxy config
├── docker-compose.yml
├── Dockerfile.api
├── Dockerfile.web
└── .env.example
```

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
