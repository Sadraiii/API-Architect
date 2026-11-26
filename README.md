# API Architect

A local tool for working with your own APIs. Import an OpenAPI spec, browse it, and send requests to the API,
including ones running on localhost.

You can also design a new API in a project: define its models and endpoints, check the generated OpenAPI
reference, and download a .NET 11 minimal API project that builds and runs. The handlers are stubs for you to fill in.

Runs on your machine only. One user, no cloud services.

## Requirements

- .NET SDK 11
- Node.js 22
- Docker (for PostgreSQL)

## Run it

```bash
docker compose up -d                      # PostgreSQL on 127.0.0.1:5432
dotnet dev-certs https --trust            # first time only
cd web && npm install && npm run build && cd ..
dotnet run --project src/ApiArchitect.Api --launch-profile https
```

Open https://localhost:7149 and register. The database is created on first start.

Stop with Ctrl+C and `docker compose down`. Data is kept in the `pgdata` volume (`down -v` deletes it).

The app runs outside Docker so it can reach APIs on your localhost. Rebuild `web/` after pulling front-end changes.

To get a spec from your own ASP.NET Core API, use `/openapi/v1.json` (served when the app calls `AddOpenApi()` and
`MapOpenApi()`). Import it by URL and use Re-import on the spec page to pull the latest version and see what changed.
Import API on the Projects page creates a project from a spec in one step.

## Client code

`Client code` in the sidebar (or the button on a specification) gives you C# to call one of your APIs from a new
Visual Studio project. Pick the operations and a template:

- Connector: a Console App that calls the API once. No NuGet packages.
- Worker: a Worker Service that calls it on a timer. Needs `Microsoft.Extensions.Http`.

You get an `ApiClient` class, the model classes, a `Program.cs` (and `Worker.cs`), and the steps to set the project
up. Keys go in an environment variable or user secrets, never in the code.

## Settings

In `src/ApiArchitect.Api/appsettings*.json`, or override with `dotnet user-secrets` or environment variables.

| Setting | Default | |
| --- | --- | --- |
| `ConnectionStrings:Database` | localhost, user and password `postgres` | Matches `docker-compose.yml` |
| `Execution:AllowPrivateNetworks` | `true` | Lets requests reach localhost and your LAN. Set to `false` if anyone else can reach the app. |
| `Execution:TimeoutSeconds` | `30` | Per request |
| `Execution:MaxResponseBytes` | 5 MB | Larger responses are cut off |
| `Execution:HistoryLimitPerProject` | `100` | Saved requests in history |

The rest are in `ExecutionOptions.cs` and `appsettings.json`. Cloud metadata addresses such as 169.254.169.254 are
always blocked, and redirects are shown, not followed.

Environment secrets are encrypted with ASP.NET Core Data Protection. Its keys are kept in your user profile
(`~/.aspnet/DataProtection-Keys`, or `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` on Windows). Delete them and saved
secrets have to be entered again.

## Develop

```bash
cd web && npm run dev       # http://localhost:5173, proxies /api to the running app
dotnet test                 # needs the Docker database running
```

Tests run against a real PostgreSQL database, created and dropped per run. Set `TEST_DATABASE` to use another server.

Add a migration after changing entities:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/ApiArchitect.Api --output-dir Data/Migrations
```

## Layout

- `src/ApiArchitect.Api` – ASP.NET Core minimal API, EF Core, Identity. One folder per feature under `Features/`.
- `tests/ApiArchitect.IntegrationTests` – xUnit tests using `WebApplicationFactory`.
- `web` – React + TypeScript + Vite. Built into the API's `wwwroot`.

New features follow `Features/Projects`:
- Filter every query by the signed-in user. Another user's data returns 404.
- Add a test where a second user tries to read, change and delete the first user's data.
- Errors are ProblemDetails. Validate request records with DataAnnotations.

## Not done yet

- No email confirmation or password reset.
- Spec import is JSON only (OpenAPI 3.0–3.2).
- Not set up for public hosting.
- Postman import skips scripts, form-data bodies and collection variables (it lists what it skipped).
