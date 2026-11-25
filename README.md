# API Architect

API Architect is a personal workspace for C# and .NET developers. You import an API's OpenAPI specification, read
it as interactive documentation, and send real requests to the API, all in one place. It runs on your own
machine, so it can call the APIs you're building on `localhost` and your local network.

There's no AI in it, and it sends nothing to third-party services.

## Contents

- [Features](#features)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
- [Everyday use](#everyday-use)
- [Configuration](#configuration)
- [How it's built](#how-its-built)
- [Project structure](#project-structure)
- [Development](#development)
- [Conventions for new features](#conventions-for-new-features)
- [Security model](#security-model)
- [Limitations](#limitations)

## Features

| Feature | What you can do | Status |
| --- | --- | --- |
| Accounts | Register, sign in and out. Every project is private to its owner. | On `main` |
| Projects | Create, edit and delete projects, one per API you work on, each with a target framework. | On `main` |
| OpenAPI import | Import OpenAPI 3.0, 3.1 or 3.2 JSON, replace it with a new version, export it. | PR #1 |
| API reference | Read the spec as a Scalar reference: tags, operations, examples, models. | PR #1 |
| Endpoint explorer | Search and filter endpoints by text, method and tag, and expand any one to see its parameters, body, responses and auth. | PR #1 |
| Schema viewer | Every model's properties, types, required fields and validation rules, plus which schemas and endpoints use it. | PR #1 |
| Request runner | Send GET, POST, PUT, PATCH and DELETE with query parameters, headers, JSON or other bodies, and bearer or API key auth. See status, headers, formatted body, timing and the exact request sent. | PR #2 |
| Request history | The last 100 requests per project, with credentials masked. | PR #2 |
| Collections | Save requests in collections with nested folders. Duplicate, edit, move and run them. | PR #3 |
| Environments | Local, Staging and so on, each with a base URL, `{{variables}}`, shared headers and default auth. Secrets are encrypted. | PR #3 |
| Postman | Import Postman v2.0/v2.1 collections and export v2.1. | PR #3 |

"PR #n" means the feature is built and tested but still in review. This README describes the app as it will be
once those three pull requests are merged.

## Prerequisites

| Tool | Version | Used for |
| --- | --- | --- |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/11.0) | 11.0.100 or later | Building and running the app |
| [Node.js](https://nodejs.org/) | 22 or later | Building the web front end |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Any recent | Running PostgreSQL |

If you'd rather not use Docker, any PostgreSQL 16+ server works. Point the connection string at it (see
[Configuration](#configuration)).

## Getting started

Do these once.

**1. Clone the repository**

```bash
git clone https://github.com/Sadraiii/API-Architect.git
cd API-Architect
```

**2. Start PostgreSQL**

```bash
docker compose up -d
```

This starts PostgreSQL 17 on `127.0.0.1:5432`, so only your own machine can reach it. The data lives in a Docker
volume named `pgdata` and survives restarts.

**3. Trust the .NET development certificate** (once per machine)

```bash
dotnet dev-certs https --trust
```

**4. Build the web front end**

```bash
cd web
npm install
npm run build
cd ..
```

This writes the compiled app into `src/ApiArchitect.Api/wwwroot`, where ASP.NET Core serves it.

**5. Run API Architect**

```bash
dotnet run --project src/ApiArchitect.Api --launch-profile https
```

On first start it creates the database tables. Open **https://localhost:7149** and register an account.

### Why the app isn't in Docker

Inside a container, `localhost` means the container itself, so a containerized API Architect couldn't reach the
APIs running on your machine. Only the database runs in Docker.

## Everyday use

**Start:** `docker compose up -d`, then `dotnet run --project src/ApiArchitect.Api --launch-profile https`.

**Stop:** `Ctrl+C` in the terminal running the app, then `docker compose down`. Your data stays.
`docker compose down -v` deletes the database volume as well, so use that only to start from scratch.

**Import your own API's spec:** ASP.NET Core apps that call `builder.Services.AddOpenApi()` and `app.MapOpenApi()`
serve their spec at `/openapi/v1.json`. Save that file, then in API Architect open a project and choose
**Import OpenAPI**.

**Call your local API:** open a project and choose **Send a request**, then use a URL like
`http://localhost:5000/todos`. With an environment selected, you can write just `/todos` and it's added to the
environment's base URL.

**After pulling new code:** run `npm run build` in `web/` again if anything under `web/` changed. The app
applies new database migrations by itself on startup.

## Configuration

Settings live in `src/ApiArchitect.Api/appsettings.json` and `appsettings.Development.json`. Override any of them
without editing files by using [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
(`dotnet user-secrets set "Section:Key" "value"` in `src/ApiArchitect.Api`) or environment variables
(`Section__Key=value`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Database` | `Host=localhost;Port=5432;Database=api_architect;Username=postgres;Password=postgres` | PostgreSQL connection. The default matches `docker-compose.yml`. |
| `RateLimiting:AuthPermitsPerMinute` | `10` | Register and sign-in attempts allowed per IP address per minute. |
| `Execution:AllowPrivateNetworks` | `true` | Lets requests reach `localhost` and private networks (192.168.x.x, 10.x.x.x, Docker networks). Leave it on for local use. **Turn it off if anyone else can ever reach the app**; see [Security model](#security-model). |
| `Execution:AllowedNetworks` | `[]` | With `AllowPrivateNetworks` off, CIDR ranges that are still allowed, such as `10.1.0.0/16`. |
| `Execution:TimeoutSeconds` | `30` | Total time for one request, including reading the response. |
| `Execution:ConnectTimeoutSeconds` | `10` | Time allowed to open the connection. |
| `Execution:MaxResponseBytes` | `5242880` (5 MB) | Response bytes read. Anything beyond is dropped and the body is marked truncated. |
| `Execution:MaxRequestBodyBytes` | `1048576` (1 MB) | Largest request body you can send. |
| `Execution:MaxStoredBodyBytes` | `65536` (64 KB) | Body bytes kept in history. The response you see live isn't cut. |
| `Execution:HistoryLimitPerProject` | `100` | Requests kept in history per project. Older ones are deleted. |
| `Execution:PermitsPerMinute` | `60` | Requests you can send per minute. |

## How it's built

| Part | Technology |
| --- | --- |
| Backend | ASP.NET Core on .NET 11, minimal APIs |
| Database | PostgreSQL through EF Core 11 (Npgsql) |
| Sign-in | ASP.NET Core Identity with its cookie (no tokens in the browser) |
| Front end | React 19, TypeScript, Vite, Tailwind CSS 4, TanStack Query, React Router |
| OpenAPI parsing | Microsoft.OpenApi |
| API reference view | Scalar (`@scalar/api-reference-react`), running fully offline |
| Tests | xUnit v3 on Microsoft.Testing.Platform, `WebApplicationFactory`, a real PostgreSQL database |

**One process, one address.** ASP.NET Core serves both the JSON API (under `/api`) and the compiled React app. The
browser only ever talks to `https://localhost:7149`, so there's no CORS setup, and the sign-in cookie can be
`HttpOnly` and `SameSite=Strict`.

**Requests go through the server.** When you press **Send**, the browser asks the API Architect backend to make
the call. That's what lets it reach your localhost APIs, apply timeouts and size limits, mask credentials in
history, and avoid the CORS problems a browser-only tool hits. Scalar's own "send" button is hidden for the same
reason.

**One backend project.** The app is mostly data access plus a few focused services (OpenAPI parsing, the request
executor, variable substitution). Separate Domain, Application and Infrastructure projects would add layers without
protecting anything, so the code is grouped by feature instead.

### API overview

All endpoints are under `/api`, need you to be signed in (except register and login), and return errors as
[RFC 9457 ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457). In Development, the app's own OpenAPI document
is at `/openapi/v1.json` and its Scalar reference at `/scalar`.

| Area | Endpoints |
| --- | --- |
| Auth | `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` |
| Projects | `GET`/`POST /api/projects`, `GET`/`PUT`/`DELETE /api/projects/{id}` |
| Specifications | `GET`/`POST /api/projects/{projectId}/specifications`, plus get, replace, export and delete by id |
| Sending requests | `POST /api/projects/{projectId}/execute`, `GET /api/projects/{projectId}/executions`, `GET /api/executions/{id}`, `DELETE /api/projects/{projectId}/executions` |
| Collections | collections, folders and saved requests under `/api/projects/{projectId}/collections`, plus Postman import and export |
| Environments | `/api/projects/{projectId}/environments` |

The `/scalar` page lists the exact routes and bodies. An example call:

```bash
# Sign in and keep the cookie
curl -k -c cookies.txt -X POST https://localhost:7149/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"your passphrase here"}'

# List your projects
curl -k -b cookies.txt https://localhost:7149/api/projects
```

## Project structure

```
API-Architect/
├── docker-compose.yml             PostgreSQL for local use
├── ApiArchitect.slnx              Solution
├── global.json                    Pins the .NET SDK and the test runner
├── Directory.Build.props           net11.0, nullable on, warnings as errors
├── src/ApiArchitect.Api/
│   ├── Program.cs                 Composition root: services, middleware, endpoint mapping
│   ├── Data/                      AppDbContext, AppUser, Migrations/
│   ├── Features/
│   │   ├── Auth/                  Register, login, logout, current user
│   │   ├── Projects/              Project CRUD (the reference implementation)
│   │   ├── Specifications/        OpenAPI import, validation, export
│   │   ├── Execution/             Request executor, outbound address policy, history
│   │   ├── Collections/           Collections, folders, saved requests, Postman import/export
│   │   └── Environments/          Environments, variables, encrypted secrets
│   └── Infrastructure/            Small cross-cutting helpers (claims, headers, time, options)
├── tests/ApiArchitect.IntegrationTests/
└── web/
    ├── vite.config.ts             Dev server and the /api proxy; builds into the API's wwwroot
    └── src/
        ├── api/                   Fetch client and one TanStack Query module per feature
        ├── components/            Shared UI, app shell, editors and viewers
        └── pages/                 One component per route
```

Each feature folder holds everything for that feature: the entity, request and response records, endpoints, and
any service it needs.

## Development

**Front end with hot reload.** Keep the app running (step 5), and in a second terminal:

```bash
cd web
npm run dev
```

Open **http://localhost:5173**. Vite forwards `/api` calls to `https://localhost:7149`.

**Tests**

```bash
dotnet test
```

The integration tests start the real app in memory and use a real PostgreSQL database. Each run creates a
throwaway database and drops it afterwards. They use the server in the `TEST_DATABASE` environment variable,
defaulting to `Host=localhost;Port=5432;Username=postgres;Password=postgres`, which is the Docker database, so
`docker compose up -d` is all you need. To re-run a single test class:

```bash
dotnet test -- --filter-class "*ProjectTests"
```

Type-check the front end with `npm run typecheck` in `web/`.

**Database migrations.** After changing an entity or `AppDbContext`:

```bash
dotnet tool restore
dotnet ef migrations add <DescriptiveName> --project src/ApiArchitect.Api --output-dir Data/Migrations
```

The app applies pending migrations when it starts in Development.

**CI.** GitHub Actions (`.github/workflows/ci.yml`) builds the front end, builds the solution with warnings as
errors, and runs every test against a PostgreSQL service container on each push to `main` and each pull request.

**Commits** follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`,
`build:`...).

## Conventions for new features

`Features/Projects` is the reference implementation. Copy its shape.

**Endpoints**
- One static `XxxEndpoints` class per feature with a `MapXxxEndpoints` extension method, called from `Program.cs`.
- Group routes with `MapGroup("/api/...")`, `.WithTags(...)` and `.RequireAuthorization()`.
- Handlers are private static methods returning `TypedResults` inside a `Results<...>` union, so the generated
  OpenAPI lists every status code.
- Accept a `CancellationToken` and pass it to every EF Core and HTTP call.

**Ownership (the rule that matters most)**
- Everything a user owns hangs off a `Project` with an `OwnerId`. Every query filters by the caller's id from
  `user.GetUserId()`. For child data, filter through the parent: `x.Project.OwnerId == userId`.
- Someone else's resource returns **404, never 403**, so the API never confirms that an id exists.
- Every feature has an integration test in which a second user tries to read, change and delete the first user's
  data and gets 404 each time. See `Another_users_project_is_not_found_for_every_operation`.

**Requests, validation and errors**
- Request bodies are `record`s with DataAnnotations (`[Required]`, `[StringLength]`, `[AllowedValues]`).
  `builder.Services.AddValidation()` turns failures into a 400 `ValidationProblem` automatically.
- Responses use their own records. Never return an EF entity.
- Every error is ProblemDetails. Never put exception messages or stack traces in a response.

**Data**
- Ids are `Guid.CreateVersion7()`. Times are UTC `DateTimeOffset` from the injected `TimeProvider`, through
  `clock.GetUtcNowForStorage()`, which rounds to the microsecond PostgreSQL stores.
- Configure each entity in `AppDbContext.OnModelCreating`: max lengths, a cascade delete from its project, and an
  index for each list query.
- Secrets that must be readable later are encrypted with ASP.NET Core Data Protection. Secrets that are only
  checked, like passwords, are hashed by Identity. Neither is ever returned by the API or written to logs.

**Front end**
- Add `web/src/api/<feature>.ts` with typed hooks over `api<T>()`. Errors arrive as `ApiError`. Show them with
  `<ErrorMessage error={...} />`, which lists each validation message.
- Add a sidebar or page entry only once it works. No placeholder screens.

## Security model

API Architect is built to run on your machine, for you. The protections below matter even then, and some matter a
lot more if that ever changes.

**Accounts.** Passwords are hashed by ASP.NET Core Identity, need at least 12 characters, and lock the account for
15 minutes after 5 wrong attempts. Register and sign-in are rate limited. The sign-in cookie is `HttpOnly`,
`Secure` and `SameSite=Strict`, so page scripts can't read it and other sites can't send it.

**Your data.** Every project, spec, request, history entry and environment is filtered by owner on every query.

**Sending requests.**
- The address check runs when the connection opens. The host is resolved, every address is checked, and the
  connection goes to the checked address, so a DNS answer can't change between the check and the connect (DNS
  rebinding).
- Cloud metadata addresses (such as `169.254.169.254`) are always blocked. Private and loopback addresses are
  allowed while `Execution:AllowPrivateNetworks` is `true`.
- Redirects aren't followed. You see the 3xx and its `Location`. There's no proxy, and no cookies are kept between
  requests.
- Timeouts, response size caps and a per-user rate limit apply to every request.
- History masks credentials: auth values, well-known headers such as `Authorization`, `Cookie` and `X-Api-Key`,
  and every secret variable's value wherever it appears.
- Logs record only method, host, status, duration and error code. They never contain paths, query strings,
  headers or bodies.

**Imported specs.** Parsed with external `$ref` loading off, so importing a file never makes the server fetch a
URL. Size limits apply. Scalar runs with its telemetry, AI and cloud features off and loads nothing from Scalar's
servers.

**Environment secrets** are encrypted with ASP.NET Core Data Protection. The keys are stored by .NET in your user
profile (`~/.aspnet/DataProtection-Keys` on macOS and Linux, `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` on
Windows). Back them up along with the database. If they're lost, you're signed out and the app asks you to enter
each stored secret again.

**If you ever expose it to other people**, for example on a server or your LAN, first:
1. Set `Execution:AllowPrivateNetworks` to `false`. Otherwise anyone who can sign in can make the app call
   anything on your network.
2. Persist and protect the Data Protection keys somewhere that survives redeploys.
3. Configure forwarded headers if it sits behind a reverse proxy, so rate limits see real client addresses.
4. Add email confirmation (see below).

## Limitations

- **Local and single-user by design.** It isn't set up for public hosting (see the checklist just above).
- **No email.** There's no email confirmation or password reset. Registering with an email that's already used
  says so, which reveals that the account exists. That's fine on your own machine, but email confirmation is
  needed before others can use it.
- **Import is JSON only.** OpenAPI 3.0, 3.1 and 3.2 are supported. YAML and Swagger 2.0 files aren't yet.
- **No "Try it" from the docs yet.** Endpoints in the explorer don't open in the request editor pre-filled yet.
  That link comes once the specification and request-runner pull requests are merged.
- **Postman import is partial.** Scripts, form-data bodies, other auth types and collection variables aren't
  imported. The import tells you what it skipped.
- **No deployment setup.** There's no Dockerfile for the app or hosting configuration, because it runs locally.

## License

[MIT](LICENSE)
