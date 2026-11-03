# Premesti Se

Development skeleton for the [.NET / VeloxDB / React architecture](docs/architecture.md).

## Start the stack

Prerequisites: Docker Engine or Docker Desktop with Compose v2+, internet access
for the initial images/packages, and approximately 2 GB available RAM. The pinned
VeloxDB distribution is **x64**; ARM hosts need Docker's `linux/amd64` emulation.
No production credentials or host .NET/Node installation are needed for Compose.

From the repository root:

```sh
docker compose config
docker compose up
```

Compose builds missing images, initializes persistent storage, deploys database
assemblies, and waits for database and SMTP readiness before starting the API.
For a detached start that waits for readiness: `docker compose up -d --wait`.

| Component | URL / address |
|---|---|
| React SPA served by API | http://localhost:8080 |
| Database-backed health | http://localhost:8080/api/health |
| Mailpit browser UI | http://localhost:8025 |
| Development SMTP | localhost:1025 (API uses `mailpit:1025`) |
| VeloxDB client protocol | localhost:7568 |

Published ports are bound to loopback. Mailpit accepts development mail without
authentication. The API's `Smtp` configuration points at Mailpit; sending business
emails will be added with the outbox story.

```sh
curl -i http://localhost:8080/api/health
docker compose logs -f
docker compose down
```

Shutdown preserves named database volumes. `docker compose down -v` **deletes
development database data**. After source changes, use `docker compose up --build`.

## Projects and dependency boundaries

- `src/PremestiSe.Api`: ASP.NET Core Minimal API, health checks, static SPA hosting.
- `src/PremestiSe.Domain`: pure C# domain library, no database dependencies.
- `src/PremestiSe.Db.Model`: infrastructure-only `ConnectivityProbe` model.
- `src/PremestiSe.Db.Api`: read-only database operation querying that model.
- `src/PremestiSe.Db.Contracts`: shared operation interface and result DTO.
- `src/PremestiSe.Importer`: buildable console entry point for future import work.
- `tests/PremestiSe.Domain.Tests`: ready for domain tests when logic is introduced.
- `tests/PremestiSe.Api.Tests`: real-stack HTTP, database protocol, and SMTP checks.
- `tests/PremestiSe.Api.UnitTests`: isolated database-health behavior tests.
- `web`: React 18 + Vite + TypeScript starter and Playwright smoke tests.
- `deploy`: Dockerfiles, VeloxDB configuration, and verification script.

### VeloxDB runtime and deployment

VeloxDB **0.4.0** is the .NET 8-compatible release used here. Its official
[tagged sources](https://github.com/VeloxDB/VeloxDB/tree/0.4.0) target `net8.0`;
0.5/0.6 target .NET 9. The official release ZIP is checked against a pinned SHA-256
in the Dockerfile. Server, model, and protocol packages all use the same version.
The pinned .NET SDK is 8.0.422 and runtime is 8.0.28.

The distribution's `vlxdbsrv.dll --init-persistence /var/lib/veloxdb
--update-assemblies /assemblies` mechanism initializes logs/snapshots on the named
data volume and deploys the four `PremestiSe.*` model/API/dependency assemblies
before opening the execution endpoint. Repeated starts detect unchanged assemblies.
The execution endpoint binds to `0.0.0.0` inside the container; administration stays
on container loopback. A named logging volume is also provided.

Both Compose database readiness and `/api/health` execute `IConnectivityApi.Check`
over the real VeloxDB client protocol. It enumerates the deployed model inside a
read transaction, even when the table is empty. No seed or business data is needed.
Healthy responses are HTTP 200:

```json
{"status":"Healthy","checks":{"database":{"status":"Healthy"}}}
```

Database failure returns HTTP 503 with `Unhealthy` status and no exception or
connection details. The default HTTP wait and connection/retry budgets are three
seconds. The API reconnects after database restart. Unknown `/api` routes return
JSON 404s; browser navigation uses the SPA fallback.

## Local development and configuration

Optional host tools: .NET SDK **8.0.422**, Node **22.23.x**, npm **10.9.x**.
NuGet package locks and `web/package-lock.json` are committed.

Start the database and mail server, then run the backend:

```sh
docker compose up -d --wait veloxdb mailpit
ASPNETCORE_URLS=http://localhost:8080 dotnet run --project src/PremestiSe.Api
```

For frontend development (a second terminal):

```sh
cd web
npm ci
npm run dev
```

Open http://localhost:5173. Vite proxies `/api` to http://localhost:8080; override
with `API_PROXY_TARGET`. The built SPA uses same-origin requests in Compose.

ASP.NET configuration can be overridden with `Database__Address`,
`Database__TimeoutSeconds` (1–30), `Smtp__Host`, `Smtp__Port`, and `Smtp__From`.
Host defaults live in `src/PremestiSe.Api/appsettings.json`; Compose overrides
the service addresses. Local `dotnet run` uses the Vite server for the frontend.

Set port overrides in an optional root `.env` (ignored by Git):

```dotenv
APP_PORT=8080
DATABASE_PORT=7568
MAILPIT_UI_PORT=8025
SMTP_PORT=1025
```

## Build and verify

GitHub Actions builds and checks both applications on every pull request and on
pushes to `main`. See the [CI guide](docs/ci.md) for service-independent unit tests,
format/lint commands, downloadable artifacts, and required merge protection.

With the full stack running, host SDK commands are:

```sh
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-restore
```

SDK-container equivalents (work from the repository root on any Docker host):

```sh
docker run --rm -v "$PWD:/workspace" -w /workspace mcr.microsoft.com/dotnet/sdk:8.0.422 sh -c 'dotnet restore --locked-mode && dotnet build --no-restore'
docker run --rm --network premesti-se_default -v "$PWD:/workspace" -w /workspace -e API_URL=http://api:8080 -e DATABASE_ADDRESS=veloxdb:7568 -e SMTP_HOST=mailpit mcr.microsoft.com/dotnet/sdk:8.0.422 dotnet test --no-restore
```

The domain suite is intentionally empty; its test runner reports no tests until
domain behavior is added. `PremestiSe.Api.Tests` requires the actual running Compose
stack; `PremestiSe.Api.UnitTests` runs without services and is the CI unit suite.
Defaults target the standard host ports; override `API_URL`, `DATABASE_ADDRESS`,
and `SMTP_HOST` if running tests against different addresses.

Reproducible full acceptance check (Bash, run from root):

```sh
bash deploy/verify.sh
```

This builds/starts the stack, builds/tests .NET, verifies healthy DB operation and
HTTP results, stops VeloxDB, asserts HTTP 503 within the bounded timeout and a
failed protocol operation, restarts it, and verifies recovery with the **same API
container**. It then uses the pinned Playwright container to build the frontend
and verify a real health request and healthy UI, plus loading/unavailable/retry
behavior with an intercepted 503. Mailpit UI and SMTP are tested in all phases.
The script leaves the stack running and restores VeloxDB if outage testing fails.

For frontend-only build and browser checks against the running stack:

```sh
cd web
npm ci
npm run build
npx playwright install --with-deps chromium
npm run test:e2e
```

Set `APP_URL` for a nondefault browser target. CI artifact publication is described
in the [CI guide](docs/ci.md); production deployment remains a separate story.
