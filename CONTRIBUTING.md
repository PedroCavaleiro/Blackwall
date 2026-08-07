# Contributing to Blackwall

Thanks for your interest in contributing! This document outlines how to get started.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker & Docker Compose (for PostgreSQL and Redis)
- A Discord application with bot and OAuth2 credentials

## Development Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/<your-username>/Blackwall.git
   cd Blackwall
   ```

2. **Create your environment file**

   ```bash
   cp .env.example .env
   ```

   Fill in the required values — see the [README](README.md#2-configure-environment-variables) for details.

3. **Start infrastructure**

   ```bash
   docker compose up postgres redis -d
   ```

4. **Run the projects**

   ```bash
   # API + Bot
   dotnet run --project src/Blackwall.Api

   # Web dashboard (separate terminal)
   dotnet run --project src/Blackwall.Web
   ```

## Project Layout

| Project | Purpose |
|---------|---------|
| `Blackwall.Core` | Entities, DTOs, configuration — no dependencies on other projects |
| `Blackwall.Infrastructure` | EF Core DbContext, Redis cache |
| `Blackwall.Bot.Discord` | Discord gateway client, event handlers, background services |
| `Blackwall.Api` | ASP.NET Core Web API, OAuth flows, JWT auth |
| `Blackwall.Web` | Blazor Server dashboard |

## Branching

- **`main`** — stable, deployable code.
- **Feature branches** — create a branch from `main` for your work, using the format `feature/<short-description>` or `fix/<short-description>`.

## Making Changes

1. Create a feature branch from `main`.
2. Make your changes in small, focused commits.
3. Follow the existing code style — the codebase uses file-scoped namespaces, primary constructors, and XML doc comments on public APIs.
4. Ensure the solution builds without warnings:

   ```bash
   dotnet build Blackwall.sln --warnaserrors
   ```

5. Test your changes locally against a running instance.

## Pull Requests

- Keep PRs focused on a single change.
- Provide a clear description of what the PR does and why.
- Reference any related issues.
- Make sure the solution builds cleanly before submitting.

## Code Style

- **File-scoped namespaces** — `namespace Foo;` not `namespace Foo { }`.
- **Primary constructors** — prefer them for dependency injection.
- **XML doc comments** — required on all public types and members.
- **Sealed classes** — seal classes that are not designed for inheritance.
- **Records** — use `record` for immutable DTOs.

## Reporting Issues

- Use GitHub Issues to report bugs or request features.
- Include steps to reproduce for bugs.
- Describe the expected vs actual behaviour.

## License

By contributing, you agree that your contributions will be licensed under the [GNU General Public License v3.0](LICENSE).
