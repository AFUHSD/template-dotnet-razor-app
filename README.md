# template-dotnet-razor-app

Starter for an ASP.NET Core Razor Pages app (.NET 10) that lives under a sub-path of a shared host, signs
people in through a platform sign-in app on the same origin, and reads SQL Server with Integrated Security.
Created by Agua Fria Union High School District for our own apps, and written to work for any district.

## What this template gives you

- **Platform sign-in, config-driven** (`Program.cs`, `Options/AuthOptions.cs`): reads a shared cookie issued
  by a sign-in app on the same origin (shared DataProtection key ring + application name). Unauthenticated
  requests go to the platform sign-in with a `returnUrl`. For local work, a fixed development identity that is
  **refused at startup outside Development**.
- **Authorization re-resolved per request** (`Services/Access/`): the cookie carries identity only;
  `IAccessResolver` decides roles on every request from the app's own source of truth. The example rule
  (an allowed email domain) fails closed and is marked `TODO` for your real rule. No access sends people to
  a Denied page without signing them out of the shared session.
- **Health endpoints**: `/health` is liveness (no checks, no database); `/health/ready` checks required
  configuration (naming missing **keys**, never values) and SQL reachability, without leaking server names.
- **Structured logging** through `ILogger` (JSON console outside Development) and an **audit-event sink**
  (`IEventLog`) with a no-op default, ready for your organization's central event store.
- **Embedded feedback control** (`Pages/Shared/_Feedback.cshtml`, `Services/Feedback/`): a Feedback button on
  every page that files what the person typed, with the page they were on, as a help-desk ticket through the
  help desk's HTTP intake; or to the audit-event sink until a help desk is configured. Behind a feature flag,
  anti-forgery, a size cap and a per-person rate limit, with a redactor that keeps tokens, connection
  strings, email addresses and student numbers out of the ticket. See [Feedback control](#feedback-control).
- **Sub-path hosting** (`PathBase`) with a test that fails if any link escapes to the parent site.
- **Accessibility baseline**: `lang`, skip link, landmarks, one `h1`, visible focus, AA contrast.
- **Tests that mean something**: a static guard that every Razor Page with a code-behind declares `@model`
  (without it the handlers silently never run), health endpoint tests, and real page tests through the
  whole pipeline.
- **Strict build**: `Directory.Build.props` with Nullable, TreatWarningsAsErrors and recommended analyzers.
- **CI** calling the organization's reusable workflows, **Dependabot** (NuGet + Actions, weekly, grouped
  minor/patch, majors ignored, 2-day cooldown), MIT LICENSE, SECURITY.md, a map-style CLAUDE.md.

## First 10 minutes

1. **Create your repo** from this template (**Use this template** on GitHub), named per your naming rule.
2. **Rename**: `App.slnx`, `src/App.Web`, `tests/App.Web.Tests`, the `App.Web` namespaces, and "My App" in
   `Pages/Shared/_Layout.cshtml` and `Pages/Index.cshtml`. Update the paths in `App.slnx` and `CLAUDE.md`.
3. **Set the repo's custom properties and topics** so rulesets and CI gates apply from day one:
   ```bash
   gh api -X PATCH repos/<org>/<repo>/properties/values \
     -f 'properties[][property_name]=tier' -f 'properties[][value]=c-experiment'
   # repeat for owner=<github-login> and lifecycle=active
   gh repo edit <org>/<repo> --add-topic dotnet --add-topic razor-pages
   ```
4. **Configure**: read `src/App.Web/appsettings.Example.json`. Set real values on the host as environment
   variables (`Auth__KeyRingPath`, `ConnectionStrings__Default`, ...). Never put them in `appsettings.json`.
5. **Write your access rule**: replace `DomainAccessResolver` (and its check in `ConfigurationHealthCheck`).
6. **Run it**: `dotnet run --project src/App.Web`, open <http://localhost:5080/my-app/>, and check
   <http://localhost:5080/health/ready> tells you which settings are missing. If the page sends you to
   `/login` instead, the app is not running as Development: a machine-level `DOTNET_ENVIRONMENT` overrides the
   launch profile. Clear it for that shell (`Remove-Item Env:DOTNET_ENVIRONMENT` in PowerShell).
7. **Verify green**: `dotnet build App.slnx -warnaserror && dotnet test App.slnx`.
8. **Replace the example page and its tests** with your first real page. Never delete a test without adding
   its replacement in the same pull request; CI fails a repo with zero tests.
9. **Prune `CLAUDE.md`** to your app, and turn on private vulnerability reporting in the repo settings.

## Commands

| Task | Command |
|---|---|
| Build (CI gate) | `dotnet build App.slnx -warnaserror` |
| Test | `dotnet test App.slnx` |
| Run locally | `dotnet run --project src/App.Web` |
| Run under a sub-path | `dotnet run --project src/App.Web -- --PathBase=my-app` |
| Publish | `dotnet publish src/App.Web -c Release -o publish` |

## Configuration

| Key (environment variable) | Required | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | yes | App database, Integrated Security (no password) |
| `Auth__UsePlatformLogin` | yes | `true` everywhere but a developer workstation |
| `Auth__KeyRingPath` | yes | Shared DataProtection key ring folder (read access for the app identity) |
| `Auth__CookieName`, `Auth__ApplicationName` | yes | Must match the platform sign-in app exactly |
| `Auth__PlatformLoginPath`, `Auth__PlatformLogoutPath` | no | Origin-absolute sign-in/out paths |
| `Auth__SessionHours` | no | Shared session length; must match every app on the origin |
| `Access__AllowedEmailDomain` | example rule | Replace with your own rule's settings |
| `Feedback__Enabled` | no | Show the Feedback control. Unset = Development only; set `true` to switch it on for a deployed app |
| `Feedback__AppName` | with the control | The name the help desk files tickets against |
| `Feedback__ServiceHubIntakeUrl` | no | The help desk's intake endpoint. Unset = feedback goes to `IEventLog` instead |
| `Feedback__ServiceHubIntakeKey` | with the URL | Shared key sent as a bearer token; readiness names it when missing |
| `Feedback__AllowScreenshots` | no | `false` removes the screenshot option entirely (default `true`) |
| `Feedback__MaxPerWindow`, `Feedback__WindowMinutes` | no | Per-person rate limit (default 5 per 10 minutes) |
| `PathBase` | Kestrel only | Simulates the IIS sub-application locally |

## Feedback control

A **Feedback** button sits in the bottom corner of every page for a signed-in person. It opens a dialog with
one text box (2,000 characters), an optional *Include a screenshot* checkbox (off by default), Send and
Cancel. On Send the app `POST`s to its own `/feedback` endpoint, which is behind the platform sign-in and the
app role like every other route, validates the anti-forgery token, caps the body (1.6 MB), the message
(2,000 characters) and the screenshot (PNG, 1 MB, checked by signature), applies a per-person rate limit
(5 per 10 minutes, in memory), and hands the ticket to the configured `IFeedbackSink`.

**What is captured.** The app name (`Feedback__AppName`), the app's informational version from the build, the
page route and query string the person was on, the signed-in person's email (as the ticket's requester), the
browser's user-agent string, the message, and the screenshot if one was taken. **What is not.** Session
replay, cookies, headers, form contents, or anything from other tabs.

**Redaction.** Before anything leaves the app, the route and the message go through `FeedbackRedactor`, which
replaces connection-string fragments, bearer tokens and JWTs, secret-shaped query parameters (`token`, `key`,
`sig`, `code`, ...), opaque strings of 32+ token characters, email addresses, and any run of 5 to 9 digits
(student numbers, state ids). It is deliberately over-eager: a redacted zip code costs nothing. The tests in
`FeedbackRedactorTests` plant each of these in a route and assert they are gone.

**Screenshots.** The screenshot is taken with the browser's own screen-sharing picker
(`getDisplayMedia`), so the person chooses what to share and the browser shows its own consent prompt.
The dialog warns, next to the checkbox, that a screenshot includes everything on screen and can include
student data, and tells the person to leave it unticked if any is showing. **Decide for your organization
whether screenshots should be allowed at all**; `Feedback__AllowScreenshots=false` removes the option.

**Sinks.**

- `ServiceHubFeedbackSink` is used when `Feedback__ServiceHubIntakeUrl` is set. It `POST`s JSON to the URL
  with `Authorization: Bearer <Feedback__ServiceHubIntakeKey>`:
  `{ app, appVersion, route, userEmail, userAgent, message, screenshotDataUrl?, submittedUtc }`
  and expects `201 { ticketId, url }`. A ticket is **never written to the help desk's database directly**:
  a ticket the help desk did not create itself notifies nobody. The intake files the ticket as `userEmail`
  so the help desk talks to the reporter directly. The reference implementation is ServiceHub's
  `POST /api/intake/feedback`.
- `LoggingFeedbackSink` is the default. It writes the whole ticket (minus screenshot bytes) to `IEventLog` as
  one `feedback-message` event, so nothing is lost while the help desk is not yet wired up.

**EventLogs rows.** Whichever sink is in use, every submission writes one `feedback` event through
`IEventLog` with status `Sent` or `Failed`, the identity, and a detail object of `app`, `appVersion`,
`route` (redacted), `sink`, `reference` (the ticket number, or `logged`), `error`, `screenshot` (bool) and
`messageLength`. The message text itself is only in the `feedback-message` event the logging sink writes.
Implement `IEventLog` against your organization's store and both rows land there.

**Feature flag.** `Feedback__Enabled` unset means on in Development and off everywhere else, so a deployed app
shows the control only when the host says `true`. Rollback is removing the flag: the partial, the endpoint and
the help section stay in the build and do nothing. The Help page has a *Sending feedback* section written
for the person using the app.

## Credit

The structure of this template (what it gives you, the first 10 minutes, a map-style `CLAUDE.md`, thin CI
callers, tests as a CI gate) follows the templates published by
[Peninsula School District](https://github.com/psd401) under the MIT license. Thank you.

## License

MIT. See [LICENSE](./LICENSE). Report security issues privately: see [SECURITY.md](./SECURITY.md).
