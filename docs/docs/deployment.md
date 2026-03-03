# Deployment

An `Apilane Instance` consists of 2 services that can be deployed in any environment:

| Service | Image | Default Port | Purpose |
|---|---|---|---|
| **Apilane Portal** | `raptis/apilane:portal-8.4.9` | `5000` | Admin dashboard for managing applications, entities, security, and reports |
| **Apilane API** | `raptis/apilane:api-8.4.9` | `5001` | REST API server that client applications connect to |

!!!info "Environment variables"
    Please visit [environment variables](/getting_started/#environment-variables) section for a description of the available environment variables.

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
| API | `apilane-api-data` | `/etc/apilanewebapi` | Application databases (SQLite), uploaded files |

!!!warning "Data persistence"
    Without proper volume mapping, all application data — databases, uploaded files, and configuration — will be lost when containers are recreated.

---

## Kubernetes

Apilane can be deployed to Kubernetes, on premise or in any cloud provider. There are many deployment configurations required so it is impossible to provide a commonly acceptable YAML sample.

!!!warning "Persistent storage"
    For Kubernetes deployments, all instances of the `Apilane Portal` and `Apilane API` services must be able to access the application files. Since on-disk files in a container are ephemeral, a [persistent volume](https://kubernetes.io/docs/concepts/storage/persistent-volumes/) is required. You will have to map the following paths from environment variables:

    - For Portal map `FilesPath`
    - For API map `FilesPath`

    Both can point to the same path since the files are not conflicting.

### Multi-server setup

An Apilane Instance can consist of more than one API server. For example, you might have separate servers for testing and production. Visit the [Server](/developer_guide/server_overview) page for more details on this concept.

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

The API uses [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans/) for distributed actor state management. The Orleans cluster exposes a dashboard (default port `8080`) that can be useful for debugging in development environments.

| Orleans Setting | Default | Description |
|---|---|---|
| `SiloPort` | `11111` | Grain-to-grain communication |
| `GatewayPort` | `30000` | Client-to-silo communication |
| `DashboardPort` | `8080` | Orleans dashboard UI |

---

## Production considerations

- **CORS**: The API allows all origins, methods, and headers by default. For production, consider placing the API behind a reverse proxy (e.g., Nginx, Traefik) with stricter CORS policies.
- **File caching**: File downloads (via `/files/download`) are served with a `Cache-Control: max-age=31536000` (1 year) header for optimal caching.
- **Thread pool**: Both services support a `MinThreads` environment variable to tune the .NET thread pool minimum worker threads for high-throughput scenarios.
- **Blocked file extensions**: The API service has a configurable list of invalid file extensions (`InvalidFilesExtentions`) to prevent uploading potentially dangerous files.
- **InstallationKey**: The `InstallationKey` must be the same value for both Portal and API — it is used for secure communication between the two services.
