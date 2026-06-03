# Deployment

An `Apilane Instance` consists of 2 services that can be deployed in any environment:

| Service | Image | Default Port | Purpose |
|---|---|---|---|
| **Apilane Portal** | `raptis/apilane:portal-8.4.9` | `5000` | Admin dashboard for managing applications, entities, security, and reports |
| **Apilane API** | `raptis/apilane:api-8.4.9` | `5001` | REST API server that client applications connect to |

!!!info "Environment variables"
    Please visit [environment variables](getting_started.md#environment-variables) section for a description of the available environment variables.

---

## Docker

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml) using the command:

```bash
docker-compose -p apilane up -d
```

This will set up both the Portal and the API services on Docker.
You may then access the portal on [http://localhost:5000](http://localhost:5000).

### Docker images

Both images are based on the official `mcr.microsoft.com/dotnet/aspnet:8.0` runtime image. The build process uses a multi-stage Dockerfile with the `mcr.microsoft.com/dotnet/sdk:8.0` image for compilation.

| Image Tag | Service |
|---|---|
| `raptis/apilane:portal-{version}` | Portal |
| `raptis/apilane:api-{version}` | API |

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
    For Kubernetes deployments, all instances of the `Apilane Portal` and `Apilane API` services must be able to access the application files. Since on-disk files in a container are ephemeral, a [persistent volume](https://kubernetes.io/docs/concepts/storage/persistent-volumes/) is required. You will have to map the following paths from environment variables:

    - For Portal map `FilesPath`
    - For API map `FilesPath` (only required if using `LocalFileSystem` storage provider)

    Both can point to the same path since the files are not conflicting.

!!!tip "Cloud storage for multi-instance deployments"
    For production multi-instance deployments, consider using cloud file storage (Google Cloud Storage, AWS S3, Azure Blob Storage) instead of shared persistent volumes. Cloud storage eliminates the need for volume sharing and provides better scalability. See [File Storage Providers](developer_guide/file_storage_providers.md) for configuration.

### Multi-server setup

An Apilane Instance can consist of more than one API server. For example, you might have separate servers for testing and production. Visit the [Server](developer_guide/server_overview.md) page for more details on this concept.

!!!info "Rate limiting in multi-instance deployments"
    When running multiple API instances (horizontal scaling), be aware that [rate limiting](developer_guide/security.md#rate-limiting) is enforced per instance, not cluster-wide. Each API server maintains its own independent rate limit counters in memory. The effective rate limit across all instances is approximately the configured limit multiplied by the number of instances, depending on load balancer distribution.

---

## Health checks

Both services expose health check endpoints that can be used by load balancers, Kubernetes probes, or monitoring systems:

| Endpoint | Purpose | Use Case |
|---|---|---|
| `/health/liveness` | Service is running | Kubernetes `livenessProbe` |
| `/health/readiness` | Service is ready to accept requests | Kubernetes `readinessProbe` |

The API service readiness check includes a `Portal` connectivity check — the API must be able to reach the Portal to function correctly.

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
| `/Version` | Returns JSON with the current API version, e.g. `{"Version": "8.4.9"}` |
| `/swagger` | Swagger UI for interactive API documentation |
| `/metrics` | OpenTelemetry Prometheus scraping endpoint |

---

## Observability

### Logging

Both services use [Serilog](https://serilog.net/) for structured logging. Logging configuration is driven by `appsettings.{Environment}.json` via the `Serilog` configuration section.

### Metrics and tracing

The API service supports OpenTelemetry for metrics and distributed tracing:

| Feature | Configuration |
|---|---|
| **Metrics** | Enabled via `OpenTelemetry:Metrics:Enabled` |
| **Tracing** | Enabled via `OpenTelemetry:Tracing:Enabled`, with configurable endpoint (`Url`) and sample ratio (`SampleRatio`, default `0.1`) |
| **Prometheus** | Metrics scraping at `/metrics` |

### Orleans Dashboard

The API uses [Microsoft Orleans](https://github.com/dotnet/orleans) for distributed actor state management. Orleans requires a clustering provider for multi-server deployments. The system supports three clustering options with automatic fallback:

1. **Localhost** (default) - For single-server or development environments
2. **Redis** - For production multi-server deployments using Redis
3. **AdoNet** - For production multi-server deployments using SQL databases (SQL Server, MySQL, PostgreSQL)

#### Clustering Configuration

The clustering behavior is configured via the `Clustering` section in `appsettings.{Environment}.json`:

```json
"Clustering": {
  "ClusterId": "apilane_api_cluster",
  "ServiceId": "apilane_api_service",
  "Type": "Localhost",
  "SiloPort": 11111,
  "GatewayPort": 30000,
  "DashboardPort": 8080
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
| `DashboardPort` | Orleans dashboard UI port | `8080` |

#### Redis Clustering

For production deployments with multiple API instances, configure Redis clustering:

```json
"Clustering": {
  "ClusterId": "apilane_api_cluster",
  "ServiceId": "apilane_api_service",
  "Type": "Redis",
  "SiloPort": 11111,
  "GatewayPort": 30000,
  "DashboardPort": 8080,
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
  "DashboardPort": 8080,
  "AdoNet": {
    "ConnectionString": "Server=localhost;Database=OrleansDb;User Id=sa;Password=YourPassword;",
    "Invariant": "System.Data.SqlClient"
  }
}
```

**Supported Invariants**:
- `System.Data.SqlClient` - SQL Server
- `MySql.Data.MySqlClient` - MySQL
- `Npgsql` - PostgreSQL

**Database Setup**: The database and tables must be created before starting the API. See [Orleans ADO.NET documentation](https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/adonet-configuration) for setup scripts.

#### Clustering Type Selection Logic

If no `Clustering` section is present in configuration, the system defaults to **Localhost** clustering.

If the `Type` is specified, the system attempts to use that clustering provider. If the required configuration is missing (e.g., `Redis.ConnectionString` for Redis type), the system throws an exception at startup.

The Orleans cluster exposes a dashboard (default port `8080`) that can be useful for debugging in development environments.

---

## Production considerations

- **CORS**: The API allows all origins, methods, and headers by default. For production, consider placing the API behind a reverse proxy (e.g., Nginx, Traefik) with stricter CORS policies.
- **File caching**: File downloads (via `/files/download`) are served with a `Cache-Control: max-age=31536000` (1 year) header for optimal caching.
- **Thread pool**: Both services support a `MinThreads` environment variable to tune the .NET thread pool minimum worker threads for high-throughput scenarios.
- **Blocked file extensions**: The API service has a configurable list of invalid file extensions (`InvalidFilesExtentions`) to prevent uploading potentially dangerous files.
- **InstallationKey**: The `InstallationKey` must be the same value for both Portal and API — it is used for secure communication between the two services.
