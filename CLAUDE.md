# CLAUDE.md: template-dotnet-razor-app

Map, not manual. Change this file in the same PR that changes the convention it describes.

## Stack
- .NET 10 (`net10.0`), ASP.NET Core Razor Pages, `App.slnx`. `Directory.Build.props`: Nullable, ImplicitUsings, TreatWarningsAsErrors, analyzers at `latest-recommended`.
- Microsoft.Data.SqlClient with Integrated Security (no stored SQL passwords). ILogger for diagnostics (JSON console outside Development).
- xunit + WebApplicationFactory. Hosted under a sub-path (`/{app}`) behind a platform sign-in app on the same origin.

## Commands
```bash
dotnet build App.slnx -warnaserror      # CI gate: zero warnings
dotnet test App.slnx                    # CI gate: fails on zero tests
dotnet run --project src/App.Web        # http://localhost:5080/my-app/ as developer@example.org
```

## Map
- `src/App.Web/Program.cs`: auth (platform cookie, or the Development-only identity), fallback policy, health, PathBase.
- `Services/Access/`: `IAccessResolver` (per-request roles; `DomainAccessResolver` is the example rule to replace), `AccessResolutionMiddleware`, `DevelopmentAuthenticationHandler`.
- `Services/Health/`: `/health` (liveness, no checks) and `/health/ready` (`ConfigurationHealthCheck` + `SqlReadinessHealthCheck`).
- `Services/Events/IEventLog.cs`: audit-event sink; `NoOpEventLog` is the default.
- `Options/`: typed settings. `appsettings.json` = safe defaults only; `appsettings.Example.json` documents every key.
- `Pages/`: `Index` (example), `Staff/Help` (`/staff/help`), `Denied`, `Error`, `Shared/_Layout`.
- `tests/App.Web.Tests/`: `PageModelDirectiveTests` (static guard), `HealthEndpointTests`, `IndexPageTests`.

## Conventions
- Every setting is config: a typed option, a line in `appsettings.Example.json`, a readiness check if the app cannot work without it. Same PR.
- The cookie carries identity only. Roles come from `IAccessResolver` on every request and are never written back.
- Fail closed: no match means no roles; `UsePlatformLogin=false` is refused outside Development.
- Every `.cshtml` with a `.cshtml.cs` declares `@model`. Without it the handlers silently never run.
- Links: tag helpers (`asp-page`) or `~/` in static markup; `Url.Content("~/...")` in a dynamic href. The platform sign-in/out paths are the only origin-absolute URLs.
- Every feature adds a section to `/staff/help` in the same PR.
- Accessibility: `lang`, skip link, one `h1`, header/nav/main/footer landmarks, visible `:focus-visible`, 4.5:1 contrast.
- Readiness output names config KEYS, never values or exception messages (they name servers).

## Anti-patterns (fail review)
- A server name, IP, account name or connection string in source, tests or docs. Placeholders only: `<sql-server>`.
- A password in any connection string; SQL logins where Integrated Security works.
- Roles or campuses written into the shared cookie; signing a person out of the shared cookie to deny access.
- Deleting or skipping a test to get green; replacing the example test without adding its replacement.
- A `#pragma warning disable` or `NoWarn` without a comment saying why.
- A parallel logging database: implement `IEventLog` against the organization's existing store.

## PR evidence bar
Paste `dotnet build -warnaserror` and `dotnet test` summaries (counts) in the PR. Bug fixes include a test that failed before the fix. UI changes include the accessibility checks above.
