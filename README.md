# API Architect

A hosted workspace for C# and .NET developers to import OpenAPI specifications, read their API documentation,
and send and save HTTP requests against their APIs.

**Status: Milestone 1 (foundation).** Accounts, sign-in and projects work. Specifications, the API reference,
the request runner, collections and environments come in the next milestones.

## Stack

| Part | Choice |
| --- | --- |
| API | ASP.NET Core on .NET 11, minimal APIs |
| Data | EF Core 11 + PostgreSQL (Npgsql) |
| Auth | ASP.NET Core Identity, application cookie only |
| Web app | React 19 + TypeScript + Vite + Tailwind CSS 4, TanStack Query, React Router |
| API docs (this app's own) | Built-in OpenAPI generation (OpenAPI 3.2) + Scalar, Development only |
| Tests | xUnit v3 on Microsoft.Testing.Platform, `WebApplicationFactory`, real PostgreSQL |

The web app is a single-page app that ASP.NET Core serves from `wwwroot`, so the browser only ever talks to one
origin. That's why there is no CORS configuration and no tokens in the browser: the auth cookie is
`HttpOnly`, `Secure` and `SameSite=Strict`.

## Running locally

Prerequisites: .NET SDK 11.0.100+, Node.js 22+, PostgreSQL 16+.

1. Start PostgreSQL. The Development connection string in `src/ApiArchitect.Api/appsettings.Development.json`
   expects `localhost:5432`, user `postgres`, password `postgres`. Change it there or override it with
   `dotnet user-secrets set ConnectionStrings:Database "..."` in `src/ApiArchitect.Api`.
2. Run the API. In Development it applies EF Core migrations on startup.
   ```bash
   dotnet run --project src/ApiArchitect.Api --launch-profile https
   ```
3. Run the web app with hot reload. Vite proxies `/api` to the API on `https://localhost:7149`.
   ```bash
   cd web
   npm install
   npm run dev
   ```
   Open http://localhost:5173.

To try the production layout instead, run `npm run build` in `web/` (it writes to `src/ApiArchitect.Api/wwwroot`)
and open the API's own URL.

The API's OpenAPI document is at `/openapi/v1.json` and its Scalar reference at `/scalar` (Development only).

## Tests

```bash
dotnet test
```

Integration tests create a throwaway database on the PostgreSQL server in `TEST_DATABASE`
(default: `Host=localhost;Port=5432;Username=postgres;Password=postgres`) and drop it afterwards.
CI runs the same tests against a PostgreSQL service container.

## Repository layout

```
src/ApiArchitect.Api/
  Program.cs                 composition root: services, middleware, endpoint mapping
  Data/                      AppDbContext, AppUser, Migrations/
  Features/<Feature>/        one folder per feature: entity, contracts, endpoints
  Infrastructure/            cross-cutting helpers (claims, security headers, time, options)
tests/ApiArchitect.IntegrationTests/
web/src/
  api/                       fetch client and one TanStack Query module per feature
  components/                shared UI (ui.tsx), app shell, forms
  pages/                     one component per route
```

It's a single API project on purpose. The app is mostly data access plus a few focused services, so separate
Domain/Application/Infrastructure projects would add indirection without protecting anything. If a feature
grows real business rules, give it its own service class inside its feature folder first.

## Conventions for new features

Follow `Features/Projects` as the reference implementation.

**Endpoints**
- One static `XxxEndpoints` class per feature with a `MapXxxEndpoints` extension, called from `Program.cs`.
- Group routes with `MapGroup("/api/...")`, `.WithTags(...)` and `.RequireAuthorization()`.
- Handlers are private static methods that return `TypedResults` with a `Results<...>` union, so OpenAPI shows
  every status code.
- Take a `CancellationToken` and pass it to every EF Core call.

**Ownership (the important one)**
- Every row a user owns traces back to a `Project` with an `OwnerId`. Every query filters by the caller's id
  from `user.GetUserId()`; for child entities, filter through the parent (`x.Project.OwnerId == userId`).
- Someone else's resource returns **404, never 403**, so the API never confirms an id exists.
- Every feature needs an integration test where a second user tries to get, update and delete the first user's
  data and gets 404 (see `Another_users_project_is_not_found_for_every_operation`).

**Requests, validation and errors**
- Request bodies are `record`s with DataAnnotations (`[Required]`, `[StringLength]`, `[AllowedValues]`).
  `builder.Services.AddValidation()` turns failures into a 400 `ValidationProblem` automatically.
- Response contracts are separate records. Never return an entity.
- All errors are RFC 9457 ProblemDetails (`AddProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages`).
  Don't put exception messages or stack traces in responses.

**Data**
- Ids are `Guid.CreateVersion7()`. Timestamps are `DateTimeOffset` in UTC from the injected `TimeProvider`,
  via `clock.GetUtcNowForStorage()` (microsecond precision, matching PostgreSQL).
- Configure entities in `AppDbContext.OnModelCreating`: max lengths, the cascade from the owning project, and an
  index for each list query.
- Add a migration per change:
  `dotnet ef migrations add <Name> --project src/ApiArchitect.Api --output-dir Data/Migrations`
  (`dotnet tool restore` first).

**Web app**
- Add `web/src/api/<feature>.ts` with typed hooks over `api<T>()`. Errors arrive as `ApiError`; render them with
  `<ErrorMessage error={...} />`, which lists validation messages.
- Only add a sidebar item once its page works. No placeholder screens.

## Collections and environments

- **Collections** hold saved requests, optionally in nested folders (up to 10 levels). Requests keep the order they
  were created or imported in. A request can be duplicated, edited and moved between folders.
- **Environments** (Local, Staging, ...) hold a base URL, variables, headers sent with every request, and default
  auth for requests set to "Use the environment's auth". The active environment is picked in the UI and remembered
  per project in the browser.
- **Variables** are written `{{name}}` in a request's URL, query parameters, headers, body and auth. `{{baseUrl}}`
  is the environment's base URL, and a URL that's just a path is appended to it. Substitution is one pass, and an
  unknown variable is an error rather than being sent as literal text. See `RequestResolver`.
- **Secrets**: a variable marked secret, and the environment's auth token or key, are encrypted with ASP.NET Core
  Data Protection (one protector purpose per project) and never returned by the API. To keep a stored secret, send
  its value as `null`. Auth values typed on a single request are plain text, so the UI points people at
  `{{secretVariable}}` instead.
- **Postman**: `POST /api/projects/{projectId}/collections/import` takes a Postman v2.0 or v2.1 collection (up to
  10 MB, 2,000 requests). Raw, URL-encoded and GraphQL bodies, bearer and API key auth, headers, query parameters and
  folders carry over; scripts, form-data bodies, other auth types and collection variables are reported back as
  warnings. `GET /api/collections/{id}/export` downloads a v2.1 file.

## Security notes

- Passwords: Identity hashing, minimum 12 characters, lockout after 5 failures for 15 minutes.
- Register and login are rate limited per client IP (`RateLimiting:AuthPermitsPerMinute`, default 10).
- Response headers: `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; HSTS
  outside Development. A Content-Security-Policy comes once Scalar and Monaco are in.
- Registering with an email that's already taken says so. That's normal for sign-up forms but does reveal that
  the account exists; email confirmation would remove it.

## Not done yet, needed before going live

- **Azure deployment.** Not set up. Hosting needs an App Service (or Container Apps) plus Azure Database for
  PostgreSQL, the connection string in Key Vault or app settings, and migrations run as a deploy step.
- **Data Protection keys.** The auth cookie and environment secrets are encrypted with them. With more than one
  instance, or after a redeploy, they must be persisted (for example Azure Blob Storage, protected with Key Vault).
  Losing them signs everyone out and makes every stored environment secret unreadable; the app then asks people
  to enter those secrets again.
- **Forwarded headers.** Behind Azure's front end, the rate limiter needs `ForwardedHeaders` configured to see
  the real client IP.
- **Email.** No confirmation or password reset yet; both need an email sender.
