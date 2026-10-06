---
description: "Deploy Apilane with Docker or Kubernetes: images, volumes, multi-server setups, health checks, observability and production considerations."
---

# Deployment

An `Apilane Instance` consists of 2 services that can be deployed in any environment:

| Service | Image | Default Port | Purpose |
|---|---|---|---|
| **Apilane Portal** | `raptis/apilane:portal-10.3.0` | `5000` | Admin dashboard for managing applications, entities, security, and reports |
| **Apilane API** | `raptis/apilane:api-10.3.0` | `5001` | REST API server that client applications connect to |

!!!info "Environment variables"
    Please visit [environment variables](getting_started.md#environment-variables) section for a description of the available environment variables.

---

## Docker

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml). It reads the shared installation key from `APILANE_INSTALLATION_KEY`: the first command stores a random one in a `.env` file next to the compose file, only if it is not there yet. Keep that file for every later run.

```bash
grep -qs "^APILANE_INSTALLATION_KEY=." .env || echo "APILANE_INSTALLATION_KEY=$(openssl rand -hex 32)" >> .env
docker-compose -p apilane up -d
```

Without a POSIX shell, create the `.env` file by hand (see [Getting Started](getting_started.md)).

This will set up both the Portal and the API services on Docker.
You may then access the portal on [http://localhost:5000](http://localhost:5000).

Before you create an application, set `ApiUrl` and `PortalUrl` in the compose file to addresses the two containers can reach: `127.0.0.1` inside a container is the container itself. See [Getting Started](getting_started.md) for the details.

### Docker images

Both images are based on the official `mcr.microsoft.com/dotnet/aspnet:10.0` runtime image. The build process uses a multi-stage Dockerfile with the `mcr.microsoft.com/dotnet/sdk:10.0` image for compilation.

| Image Tag | Service |
|---|---|
| `raptis/apilane:portal-{version}` | Portal |
| `raptis/apilane:api-{version}` | API |

Every release is published as `portal-{version}` and `api-{version}`; the versions are listed in the [CHANGELOG](https://github.com/raptisv/apilane/blob/main/CHANGELOG.md). Use the same version for both images.

### Volumes

Both services use persistent volumes to store data:

| Service | Volume | Container Path | Contents |
|---|---|---|---|
| Portal | `apilane-portal-data` | `/etc/apilanewebportal` | Portal SQLite database, data protection keys |
| API | `apilane-api-data` | `/etc/apilanewebapi` | Application databases (SQLite), uploaded files (if using LocalFileSystem storage) |

!!!warning "Data persistence"
    Without proper volume mapping, all application data — databases, uploaded files, and configuration — will be lost when containers are recreated.

!!!info "Cloud storage"
    If using cloud file storage providers (Google Cloud Storage, AWS S3, Azure Blob Storage), uploaded files are stored in the cloud bucket/container. The API volume is only required for SQLite application databases. See [File Storage Providers](developer_guide/file_storage_providers.md) for configuration.

---

## Kubernetes

Apilane can be deployed to Kubernetes, on premise or in any cloud provider. There are many deployment configurations required so it is impossible to provide a commonly acceptable YAML sample.

!!!warning "Persistent storage"
    For Kubernetes deployments, all instances of the `Apilane API` service must be able to access the application files, and the `Apilane Portal` runs as a single instance: it keeps one SQLite database, and the progress of running clones in memory. Since on-disk files in a container are ephemeral, a [persistent volume](https://kubernetes.io/docs/concepts/storage/persistent-volumes/) is required. You will have to map the following paths from environment variables:

    - For Portal map `FilesPath`: it holds the Portal database and its data protection keys
    - For API map `FilesPath`: it holds the SQLite databases of the applications (`{FilesPath}/{application token}/{application token}.db`) and, with the `LocalFileSystem` storage provider, their uploaded files. It is only unnecessary when every application uses SQL Server, MySQL or PostgreSQL and the uploaded files go to a cloud storage provider.

    Both can point to the same path since the files are not conflicting.

!!!tip "Cloud storage for multi-instance deployments"
    For multi-instance deployments (mind the warning under [Orleans clustering](#orleans-clustering)), consider using cloud file storage (Google Cloud Storage, AWS S3, Azure Blob Storage) instead of shared persistent volumes. Cloud storage eliminates the need for volume sharing and provides better scalability. See [File Storage Providers](developer_guide/file_storage_providers.md) for configuration.

### Multi-server setup

An Apilane Instance can consist of more than one API server. For example, you might have separate servers for testing and production. Visit the [Server](developer_guide/server_overview.md) page for more details on this concept. Each server is its own deployment with its own address. Several instances of one server behind a single address are limited: see the warning under [Orleans clustering](#orleans-clustering).

!!!info "Rate limiting in multi-instance deployments"
    When running multiple API instances (horizontal scaling, with the limits described in the warning under [Orleans clustering](#orleans-clustering)), be aware that [rate limiting](developer_guide/security.md#rate-limiting) is enforced per instance, not cluster-wide. Each API server maintains its own independent rate limit counters in memory. The effective rate limit across all instances is approximately the configured limit multiplied by the number of instances, depending on load balancer distribution.

---

## Health checks

Both services expose health check endpoints that can be used by load balancers, Kubernetes probes, or monitoring systems:

| Endpoint | Purpose | Use Case |
|---|---|---|
| `/health/liveness` | Service is running | Kubernetes `livenessProbe` |
| `/health/readiness` | Service is ready to accept requests | Kubernetes `readinessProbe` |

The API service readiness check includes a `Portal` connectivity check — the API must be able to reach the Portal to function correctly. It only requests the `/health/liveness` of the Portal: a wrong installation key is not detected by it. The readiness check of the Portal has no checks of its own and answers as soon as the Portal runs. Both services answer with JSON (`Status`, `Application`, `Version`, `Source`, `Entries`).

If you set `AllowedHosts` on the Portal, its probes must send an allowed `Host` header: Kubernetes sends the address of the pod unless the probe sets `httpHeaders` (`Host`).

### Example Kubernetes probes

```yaml
livenessProbe:
  httpGet:
    path: /health/liveness
    port: 5001
  initialDelaySeconds: 10
  periodSeconds: 30

readinessProbe:
  httpGet:
    path: /health/readiness
    port: 5001
  initialDelaySeconds: 15
  periodSeconds: 10
```

---

## Additional endpoints

The API service exposes utility endpoints:

| Endpoint | Description |
|---|---|
| `/` | ASCII art banner confirming the service is live |
| `/Version` | Returns JSON with the current API version, e.g. `{"Version": "10.3.0"}` |
| `/swagger` | Swagger UI for interactive API documentation |
| `/metrics` | OpenTelemetry Prometheus scraping endpoint, without authentication; answers 404 when `OpenTelemetry:Metrics:Enabled` is false |

The Portal serves its UI at the root of its host name and keeps these addresses for itself:

| Endpoint | Description |
|---|---|
| `/api/v1` | The management API the UI is built on |
| `/api/internal` | Called by the API services only, with the installation key |
| `/swagger` | Swagger UI of the management API, for signed-in Portal users |
| `/health/liveness`, `/health/readiness` | Health checks, see [Health checks](#health-checks) |
| `/metrics` | OpenTelemetry Prometheus scraping endpoint, without authentication; answers 404 when `OpenTelemetry:Metrics:Enabled` is false |

Every other address is a screen of the UI (`/apps`, `/account/login`, `/admin/servers`, ...). The Portal must be served at the root of its host name, not under a sub-path. Addresses, settings and behaviour of the Portal are described in the [Portal README](https://github.com/raptisv/apilane/blob/main/src/Apilane.Portal.Ui/README.md).

---

## Observability

### Logging

Both services use [Serilog](https://serilog.net/) for structured logging, configured through the `Serilog` configuration section. The committed `appsettings.json` holds secret-free defaults (console logging at `Information`, OpenTelemetry tracing off) and is the only settings file in the Docker images. Override any setting with an `appsettings.{Environment}.json` next to the binaries (for Docker, mount it into `/app`) or with environment variables, using `__` for nesting (for example `OpenTelemetry__Tracing__Enabled=true`). `{Environment}` is the value of `ASPNETCORE_ENVIRONMENT` (see [Production considerations](#production-considerations)): `appsettings.Production.json` is only read when the variable says `Production`.

### Metrics and tracing

Both services support OpenTelemetry for metrics and distributed tracing:

| Feature | Configuration |
|---|---|
| **Metrics** | Enabled via `OpenTelemetry:Metrics:Enabled` (on in the shipped `appsettings.json`). When it is `false`, `/metrics` answers 404 |
| **Tracing** | Enabled via `OpenTelemetry:Tracing:Enabled` (off), with configurable endpoint (`Url`), sample ratio (`SampleRatio`, default `0.1`) and `LogSpans` (default `true`: every finished span is also written to the log) |
| **Prometheus** | Metrics scraping at `/metrics`, without authentication |

### Orleans clustering

The API uses [Microsoft Orleans](https://github.com/dotnet/orleans) for distributed actor state management. Orleans requires a clustering provider for multi-server deployments. The system supports three clustering options, chosen by the `Type` setting:

1. **Localhost** (default) - For single-server or development environments
2. **Redis** - For production multi-server deployments using Redis
3. **AdoNet** - For production multi-server deployments using SQL databases (SQL Server, MySQL, PostgreSQL)

!!!warning "Several API servers"
    Every API server currently advertises the address `127.0.0.1` to the cluster, and it cannot be configured. Redis and AdoNet clustering therefore cannot connect API servers that run in different containers or on different hosts.

#### Clustering Configuration

The clustering behavior is configured via the `Clustering` section in `appsettings.{Environment}.json`:

```json
"Clustering": {
  "ClusterId": "apilane_api_cluster",
  "ServiceId": "apilane_api_service",
  "Type": "Localhost",
  "SiloPort": 11111,
  "GatewayPort": 30000
}
```

#### Configuration Options

| Setting | Description | Default |
|---|---|---|
| `ClusterId` | Unique identifier for the Orleans cluster | `apilane_api_cluster` |
| `ServiceId` | Unique identifier for the Orleans service | `apilane_api_service` |
| `Type` | Clustering type: `Localhost`, `Redis`, or `AdoNet` | `Localhost` |
| `SiloPort` | Grain-to-grain communication port | `11111` |
| `GatewayPort` | Client-to-silo communication port | `30000` |

#### Redis Clustering

To use Redis for clustering (read the warning above first: instances in different containers or on different hosts cannot join one cluster yet):

```json
"Clustering": {
  "ClusterId": "apilane_api_cluster",
  "ServiceId": "apilane_api_service",
  "Type": "Redis",
  "SiloPort": 11111,
  "GatewayPort": 30000,
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

#### AdoNet Clustering

For production deployments using SQL databases:

```json
"Clustering": {
  "ClusterId": "apilane_api_cluster",
  "ServiceId": "apilane_api_service",
  "Type": "AdoNet",
  "SiloPort": 11111,
  "GatewayPort": 30000,
  "AdoNet": {
    "ConnectionString": "Server=localhost;Database=OrleansDb;User Id=sa;Password=YourPassword;",
    "Invariant": "Microsoft.Data.SqlClient"
  }
}
```

**Supported Invariants** (the provider of each one ships in the API image). Always set `Invariant`: the default of the setting is `System.Data.SqlClient`, which is not in this list.
- `Microsoft.Data.SqlClient` - SQL Server
- `MySql.Data.MySqlConnector` - MySQL
- `Npgsql` - PostgreSQL

**Database Setup**: The database and tables must be created before starting the API. See [Orleans ADO.NET documentation](https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/adonet-configuration) for setup scripts.

#### Clustering Type Selection Logic

If no `Clustering` section is present in configuration, the system defaults to **Localhost** clustering.

If the `Type` is specified, the system attempts to use that clustering provider. If the required configuration is missing (e.g., `Redis.ConnectionString` for Redis type), the system throws an exception at startup.

!!!info "Grain observability"
    There is no bundled Orleans dashboard. Use the [OpenTelemetry metrics and tracing](#metrics-and-tracing) described above (Prometheus `/metrics`, OTLP traces) for cluster and grain-level observability.

---

## Production considerations

- **CORS**: The API allows all origins, methods, and headers by default. For production, consider placing the API behind a reverse proxy (e.g., Nginx, Traefik) with stricter CORS policies. Keep the Portal's origin allowed: the Portal UI calls the API straight from the browser (data browser, files, reports), so each server's address under **Instance > Servers** must also be reachable from the browsers of Portal users.
- **File caching**: File downloads (via `/files/download`) are served with a `Cache-Control: max-age=31536000` (1 year) header for optimal caching.
- **Thread pool**: Both services support a `MinThreads` environment variable to tune the .NET thread pool minimum worker threads for high-throughput scenarios.
- **Blocked file extensions**: The API service has a configurable list of invalid file extensions (`InvalidFilesExtentions`, note the spelling) to prevent uploading potentially dangerous files. The default is `.exe .vbs .msi .jar .bat .cmd .vbe .js .jsp .lnk`; write entries in lower case, with the dot. A list in configuration is merged by position: to add `.php` with an environment variable use the next free index (`InvalidFilesExtentions__10=.php`), because `InvalidFilesExtentions__0=.php` replaces `.exe`.
- **InstallationKey**: The API services' `InstallationKey` must equal the key stored in the Portal (**Instance > Settings**); the Portal's own `InstallationKey` setting only seeds it on first start. The key authenticates the calls in both directions. To change it, see "Changing the installation key" in [Getting started](getting_started.md#environment-variables). Both services log a `SECURITY` warning at start when the key is not set, is a publicly known value, is still the placeholder of `appsettings.json`, or is shorter than 30 characters; the Portal logs a second one for the key stored in its database, and an information message when its setting differs from the stored key (the key was rotated).
- **Environment**: Set `ASPNETCORE_ENVIRONMENT=Production` on both services. Only the exact, case-sensitive words `Production` and `Development` are recognised, and without a recognised value both services run as `Development`: the compose file does not set it. In `Development` both services show the developer exception page (with the exception details) for unhandled errors, and the Portal does not send HSTS.
- **HTTPS**: Both services speak plain HTTP; end TLS at a reverse proxy or ingress. The Portal builds the links of its mails (password reset) from the scheme and host of the request, so behind a TLS proxy also set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (under the condition of the sign-in rate limit bullet below), or the links start with `http://`.
- **Upgrade both services together**: The API calls the Portal's internal API (`/api/internal`) and sends users to a page of the Portal UI after they confirm their email. Neither is versioned, so a Portal and its API services must run the same version.
- **Portal sign-in rate limit**: The Portal's `/api/v1` sign in, sign up and password-reset request endpoints together allow 30 calls per 60 seconds per client address (`AccountRateLimit__PermitLimit`, `AccountRateLimit__WindowSeconds`). Behind a reverse proxy the Portal sees the proxy's address, so all visitors share one budget. Either raise the limit, or set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the Portal — only when the proxy is the only way to reach the Portal and it overwrites `X-Forwarded-For`, because the header is otherwise trusted from any sender.
- **AllowedHosts (Portal)**: The link in a password-reset email is built from the host of the request. Set `AllowedHosts` on the Portal to its public host name (and the host used in the API's `PortalUrl`), so a request with a forged `Host` header is refused instead of producing a link to another site.

---

## Backup

What an instance keeps, and where:

| What | Where | Backup |
|---|---|---|
| Portal database: users, applications, settings, audit log | `Apilane.db` in the Portal's `FilesPath` | **Instance > Settings**, section **Backup database**, button **Download backup** (a consistent copy) |
| Data protection keys of the Portal | The other files in the Portal's `FilesPath` | Copy the Portal volume |
| SQLite databases of the applications, and files with the `LocalFileSystem` provider | The API's `FilesPath`: `{application token}/{application token}.db` and `{application token}/files` | Copy the API volume |
| Databases on SQL Server, MySQL and PostgreSQL, and cloud storage buckets | Your own servers and accounts | Apilane does not store them: back them up with their own tools |

!!!info "Folder name on Linux"
    Uploaded files are written to a folder named `files`, but **Rebuild** and the storage size shown on the application card look in a folder named `Files`. On Linux, and so in Docker, where file names are case-sensitive, a rebuilt application keeps its old uploaded files on the volume and the storage size does not count them. **Delete** removes the whole folder of the application and is not affected.

!!!warning "The Portal backup holds every secret"
    The file contains password hashes, the installation key, and the connection strings, encryption keys and mail passwords of every application. Store it as carefully as the instance itself. Every download is written to the audit log of the instance.

---

## Upgrading

1. Make a [backup](#backup) first.
2. Read the [CHANGELOG](https://github.com/raptisv/apilane/blob/main/CHANGELOG.md) for the versions in between. 10.1.0 and 10.2.0 contain breaking changes: since 10.1.0 the installation key and the Portal user's token are accepted only in headers, and 10.2.0 replaced the Razor Portal with the current UI and management API, with new page addresses that the old ones do not redirect to.
3. Change the image tag of **both** services to the new version and recreate the containers. A Portal and its API services must run the same version.
4. Keep the `.env` file: the API servers must keep running with the installation key the Portal stores.
5. On start the Portal adds the tables and indexes its database lacks.
