# Getting Started

An Apilane Instance consists of two services (Portal + API) and can be deployed in any environment.

## 1. Start the Services

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml) to spin up both services:

```bash
docker-compose -p apilane up -d
```

This starts:

- **Portal** on [http://localhost:5000](http://localhost:5000) — management UI
- **API** on [http://localhost:5001](http://localhost:5001) — REST API for client applications

## 2. Log in to the Portal

Open [http://localhost:5000](http://localhost:5000) and log in with the default credentials:

- **Email:** `admin@admin.com`
- **Password:** `admin`

!!!warning "Important"
    Change the admin password immediately after first login. You can also override the admin email via the `AdminEmail` environment variable before deployment.

## 3. Create Your First Application

1. In the Portal, navigate to **Applications** and click **Create**
2. Enter a name (e.g., `MyApp`)
3. Select a **Server** (the API service)
4. Choose a **Storage Provider** (SQLite is recommended for getting started)
5. Click **Create**

Your application is now ready. Note the **Application Token** — you'll need it for API calls.

## 4. Define an Entity

1. Open your application in the Portal
2. Navigate to **Entities** and click **Create**
3. Name it `Products`
4. Add properties:
    - `Name` (String, Required)
    - `Price` (Number)
    - `InStock` (Boolean)

## 5. Make Your First API Call

With your entity created, you can start making API calls. Replace `{appToken}` with your Application Token.

**Create a record:**

```bash
curl -X POST "http://localhost:5001/api/Data/Post?appToken={appToken}&entity=Products" \
  -H "Content-Type: application/json" \
  -d '{"Name": "Widget", "Price": 9.99, "InStock": true}'
```

**Response:** `[1]` — an array containing the new record's ID.

**Read records:**

```bash
curl "http://localhost:5001/api/Data/Get?appToken={appToken}&entity=Products"
```

**Response:**
```json
{
  "Data": [
    { "ID": 1, "Name": "Widget", "Price": 9.99, "InStock": true, "Owner": null, "Created": 1704067200000 }
  ]
}
```

**Update a record:**

```bash
curl -X PUT "http://localhost:5001/api/Data/Put?appToken={appToken}&entity=Products" \
  -H "Content-Type: application/json" \
  -d '{"ID": 1, "Price": 12.99}'
```

**Delete a record:**

```bash
curl -X DELETE "http://localhost:5001/api/Data/Delete?appToken={appToken}&entity=Products&ids=1"
```

## 6. Register a User

To use authenticated endpoints, first allow user registration in the Portal under **Security**, then:

```bash
curl -X POST "http://localhost:5001/api/Account/Register?appToken={appToken}" \
  -H "Content-Type: application/json" \
  -d '{"Email": "user@example.com", "Username": "john", "Password": "SecurePass123!"}'
```

**Login and get an auth token:**

```bash
curl -X POST "http://localhost:5001/api/Account/Login?appToken={appToken}" \
  -H "Content-Type: application/json" \
  -d '{"Email": "user@example.com", "Password": "SecurePass123!"}'
```

**Response:**
```json
{
  "AuthToken": "a1b2c3d4-...",
  "User": { "ID": 1, "Email": "user@example.com", "Username": "john", ... }
}
```

Use the `AuthToken` in subsequent requests via the `Authorization` header:

```bash
curl "http://localhost:5001/api/Data/Get?appToken={appToken}&entity=Products" \
  -H "Authorization: Bearer a1b2c3d4-..."
```

## Environment Variables

Regardless of deployment method (Docker, k8s, cloud), you can override default settings via environment variables.

### Portal

| Variable | Default | Description |
|---|---|---|
| `Url` | `http://0.0.0.0:5000` | URL where the Portal is served |
| `ApiUrl` | `http://127.0.0.1:5001` | URL to the initial API service |
| `FilesPath` | `/etc/apilanewebportal` | Path for Portal database files (SQLite) |
| `InstallationKey` | `8dc64403-...` | Shared key between Portal and API. **Change this and keep it secret.** |
| `AdminEmail` | `admin@admin.com` | Admin email, created on first deployment. Change before deploying. |

### API

| Variable | Default | Description |
|---|---|---|
| `Url` | `http://0.0.0.0:5001` | URL where the API is served |
| `PortalUrl` | `http://127.0.0.1:5000` | URL to the Portal |
| `FilesPath` | `/etc/apilanewebapi/Files` | Path for API-generated files |
| `InstallationKey` | `8dc64403-...` | Must match the Portal's key |

!!!info "Next steps"
    - Configure [Security](developer_guide/security.md) rules for your entities
    - Set up [Email Templates](developer_guide/email_templates.md) for user registration
    - Explore the full [REST API Reference](api_reference.md)
    - Integrate with the [.NET SDK](developer_guide/sdk.md)
