# API Architect

A personal workspace for C# and .NET developers to import OpenAPI specifications, read their API documentation,
and send and save HTTP requests against their APIs. It runs on your own machine, so it can call APIs on your
localhost and local network.

**Status:** accounts, sign-in, projects and the request runner work. Specifications, the API reference,
collections and environments come in the next milestones.

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

## Running it

Prerequisites: [Docker Desktop](https://www.docker.com/products/docker-desktop/), .NET SDK 11.0.100+, Node.js 22+.

1. Start PostgreSQL in Docker (first run downloads the image; data is kept in the `pgdata` volume):
   ```bash
   docker compose up -d
   ```
2. Build the web app once (it writes to `src/ApiArchitect.Api/wwwroot`):
   ```bash
   cd web && npm install && npm run build && cd ..
   ```
3. Run the app. It applies database migrations on startup.
   ```bash
   dotnet run --project src/ApiArchitect.Api --launch-profile https
   ```
   Open https://localhost:7149. If the browser warns about the certificate, run `dotnet dev-certs https --trust` once.

Stop PostgreSQL with `docker compose down`. Your data stays in the volume; `docker compose down -v` deletes it.

The app runs outside Docker on purpose: inside a container, `localhost` means the container itself, so it
couldn't reach the APIs you're developing on your machine.

**Working on the web app?** Run `npm run dev` in `web/` alongside step 3 and open http://localhost:5173 for hot
reload. Vite forwards `/api` to the app on `https://localhost:7149`.

The app's own OpenAPI document is at `/openapi/v1.json` and its Scalar reference at `/scalar`.

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

## Security notes

- Passwords: Identity hashing, minimum 12 characters, lockout after 5 failures for 15 minutes.
- Register and login are rate limited per client IP (`RateLimiting:AuthPermitsPerMinute`, default 10).
- Response headers: `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; HSTS
  outside Development. A Content-Security-Policy comes once Scalar and Monaco are in.
- Registering with an email that's already taken says so. That's normal for sign-up forms but does reveal that
  the account exists; email confirmation would remove it.

## Sending requests (Features/Execution)

Each project has a request page (`/projects/{id}/request`) that sends GET, POST, PUT, PATCH and DELETE requests
from the server (the app running on your machine) and shows status, headers, body (JSON pretty-printed and highlighted), timing and the request
that was sent. Endpoints:

| Endpoint | Purpose |
| --- | --- |
| `POST /api/projects/{projectId}/execute` | Send an ad-hoc request and record it |
| `GET /api/projects/{projectId}/executions?requestId=` | History, newest first |
| `GET /api/executions/{id}` | One stored execution |
| `DELETE /api/projects/{projectId}/executions` | Clear history |

```bash
curl -b cookies.txt -X POST https://localhost:7149/api/projects/$PROJECT_ID/execute \
  -H "Content-Type: application/json" \
  -d '{"method":"GET","url":"https://api.github.com/zen","auth":{"kind":"Bearer","value":"<token>"}}'
```

Invalid input is a 400 `ValidationProblem`. A failure of the *target* (blocked address, DNS, refused connection,
TLS, timeout) is still a 200, with `statusCode: null` and `error: { code, message }`, and it is recorded.

Other features call the service directly: `RequestExecutor.ExecuteAsync(OutboundRequest, ct)` then
`ExecutionHistory.RecordAsync(projectId, requestId, request, result, ct)`. Pass credentials as `RequestAuth`, and
any other secret you substituted into the URL, headers or body as `OutboundRequest.SecretValues`, so history masks them.

**Safety rules, and why**

API Architect runs locally for one person, so by default requests may reach `localhost` and your LAN: testing
your own APIs is the main use. The rest of the guard stays on.

- **Address policy at connect time.** `OutboundAddressPolicy` runs in `SocketsHttpHandler.ConnectCallback`: it
  resolves the host, checks every address, then connects to the checked address, so DNS rebinding can't swap the
  answer after the check. Cloud metadata endpoints (169.254.169.254, fd00:ec2::254 and friends) and
  unspecified/multicast addresses are always refused.
- **`Execution:AllowPrivateNetworks`** (default `true`). Set it to `false` if the app is ever reachable by anyone
  else: otherwise anyone who can sign in can make it call anything on your network. With it off, loopback,
  RFC 1918, link-local, CGNAT and IPv6 equivalents are refused too, except CIDR ranges in `Execution:AllowedNetworks`.
- **No redirects followed** (a 3xx comes back with its `Location`), **no proxy, no cookies** kept between requests.
- **Limits** (section `Execution`): 30 s total timeout, 10 s connect, 5 MB response read (the rest is dropped and the
  body marked truncated), 1 MB request body, 60 executions per user per minute.
- **History**: the newest 100 per project; bodies stored up to 64 KB. Auth values and well-known credential headers
  (`Authorization`, `Cookie`, `X-Api-Key`, ...) are masked, as is every occurrence of a secret value. Request bodies
  are otherwise stored as written.
- **Logging** records method, host, status, duration and error code only, never the path, query, headers or bodies.

## Not done yet

- No email confirmation or password reset. For a single-user local install that's fine; you can reset a password
  by deleting the user row and registering again.
- The app is meant for your machine only. Putting it on the internet would need persisted Data Protection keys,
  forwarded-header handling for the rate limiter, and `Execution:AllowPrivateNetworks` set to `false`.
