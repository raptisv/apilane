---
description: "Start an Apilane instance with Docker Compose, log in to the Portal, create your first application and entity, and make your first API call."
---

# Getting Started

An Apilane Instance consists of two services (Portal + API) and can be deployed in any environment.

## 1. Start the Services

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml) to spin up both services. The Portal and the API share an **installation key**: the first command generates one into a `.env` file next to the compose file, only if it is not there yet. Keep that file for every later run.

```bash
grep -qs "^APILANE_INSTALLATION_KEY=." .env || echo "APILANE_INSTALLATION_KEY=$(openssl rand -hex 32)" >> .env
docker-compose -p apilane up -d
```

Without a POSIX shell (for example on Windows), create the `.env` file by hand: one line, `APILANE_INSTALLATION_KEY=` followed by a random value of 30 to 100 characters without spaces.

This starts:

- **Portal** on [http://localhost:5000](http://localhost:5000) — management UI (and the management API it is built on, under `/api/v1`)
- **API** on [http://localhost:5001](http://localhost:5001) — REST API for client applications

!!!warning "Addresses between the services"
    `ApiUrl` (Portal) and `PortalUrl` (API) are addresses the two services call, and `127.0.0.1` inside a container is the container itself, so the sample values of the compose file do not connect the two containers. Set `ApiUrl` to an address that both the Portal container and your browser can reach (the browser calls the API server directly for data, files and reports), for example `http://192.168.1.20:5001`, and `PortalUrl` to an address the API container can reach, on the compose network `http://apilanewebportal:5000`. `ApiUrl` only seeds the first server under **Instance > Servers** on the first start: to change the address later, edit the server there. `PortalUrl` is also the address end users are sent to after they confirm their email, unless the application has a **Redirect URL**, so a `PortalUrl` that browsers cannot reach needs one.

## 2. Log in to the Portal

Open [http://localhost:5000](http://localhost:5000) and log in with the default credentials:

- **Email:** `admin@admin.com`
- **Password:** `admin`

!!!warning "Important"
    After the first login, change the admin password (user menu, **Change password**) and, if the Portal can be reached by anyone else, switch off **Allow new users to register on this instance** under **Instance > Settings**: it is on after a first start. `AdminEmail` only decides the address of the account created on the first start; its password is `admin` whatever the address, and changing the setting later has no effect.

## 3. Create Your First Application

1. In the Portal, open **Applications** and click **New application**
2. Enter a name (e.g., `MyApp`)
3. Select a **Server** (the API service)
4. Choose a **Database type** (SQLite is recommended for getting started)
5. Click **Save**

Your application is now ready and its **Entities** tab opens. Note the **Application Token** — you'll need it for API calls. The **Info** button of the application shows it.

## 4. Define an Entity

1. Open your application in the Portal
2. On the **Entities** tab click **New entity**
3. Name it `Products`
4. Open the entity and add its properties with **New property**:
    - `Name` (String, Required)
    - `Price` (Number)
    - `InStock` (Boolean)

## 5. Allow Access to the Entity

A new entity is private: the API refuses every call that no access rule allows, so the calls below would answer `UNAUTHORIZED`. Add a rule for this walkthrough:

1. Open the **Security** tab of the application
2. Under **Access rules**, pick `Products` in the list of items
3. In the **Anonymous** row switch on **GET**, **POST**, **PUT** and **DELETE**
4. For **GET**, **POST** and **PUT** click **Select all** under **Properties** (a rule without properties returns only the `ID` on GET and refuses POST and PUT)
5. Click **Save**

!!!warning "Only for this walkthrough"
    Anonymous access lets anyone who knows the Application Token read and change the records. Once you have users, replace it with rules for **Authenticated** or a role. See [Security](developer_guide/security.md).

## 6. Make Your First API Call

With the entity and its access rule in place, you can start making API calls. Replace `{appToken}` with your Application Token.

**Create a record:**

```bash
curl -X POST "http://localhost:5001/api/Data/Post?entity=Products" \
  -H "x-application-token: {appToken}" \
  -H "Content-Type: application/json" \
  -d '{"Name": "Widget", "Price": 9.99, "InStock": true}'
```

**Response:** `[1]` — an array containing the new record's ID.

**Read records:**

```bash
curl "http://localhost:5001/api/Data/Get?entity=Products" \
  -H "x-application-token: {appToken}"
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
curl -X PUT "http://localhost:5001/api/Data/Put?entity=Products" \
  -H "x-application-token: {appToken}" \
  -H "Content-Type: application/json" \
  -d '{"ID": 1, "Price": 12.99}'
```

**Delete a record:**

```bash
curl -X DELETE "http://localhost:5001/api/Data/Delete?entity=Products&ids=1" \
  -H "x-application-token: {appToken}"
```

## 7. Register a User

To use authenticated endpoints, first make sure **Allow new users to register** is on (the **Security** tab of the application in the Portal), then:

```bash
curl -X POST "http://localhost:5001/api/Account/Register" \
  -H "x-application-token: {appToken}" \
  -H "Content-Type: application/json" \
  -d '{"Email": "user@example.com", "Username": "john", "Password": "SecurePass123!"}'
```

**Login and get an auth token:**

```bash
curl -X POST "http://localhost:5001/api/Account/Login" \
  -H "x-application-token: {appToken}" \
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

Use the `AuthToken` in subsequent requests via the `Authorization` header. This call works with the Anonymous rule of step 5, because a signed-in user also gets what Anonymous has:

```bash
curl "http://localhost:5001/api/Data/Get?entity=Products" \
  -H "x-application-token: {appToken}" \
  -H "Authorization: Bearer a1b2c3d4-..."
```

## Environment Variables

Regardless of deployment method (Docker, k8s, cloud), you can override default settings via environment variables, using `__` for nesting (for example `OpenTelemetry__Tracing__Enabled`).

The "Compose file" column shows the values of [docker-compose.yaml](assets/docker-compose.yaml). A setting that is not set there takes the development default of the `appsettings.json` inside the image (`http://localhost:5000` and `http://localhost:5001`, and a Windows `FilesPath`), so in a container always set `Url` to `http://0.0.0.0:{port}` and `FilesPath` to a mounted volume.

### Portal

| Variable | Compose file | Description |
|---|---|---|
| `Url` | `http://0.0.0.0:5000` | URL where the Portal is served |
| `ApiUrl` | `http://127.0.0.1:5001` | The first API server, as the Portal and the browsers reach it (see the warning in step 1). It seeds the first server under **Instance > Servers** on the first start only; later, edit the server there. |
| `FilesPath` | `/etc/apilanewebportal` | Path for the Portal database (SQLite) and its data protection keys |
| `InstallationKey` | `${APILANE_INSTALLATION_KEY}` | Shared secret between Portal and API, 30 to 100 characters without spaces (what **Instance > Settings** accepts). The compose file reads it from `APILANE_INSTALLATION_KEY`. Generate it with `openssl rand -hex 32` and **never reuse a published example**: the `appsettings.json` in the image holds the placeholder `REPLACE-WITH-A-LONG-RANDOM-SECRET`, and a Portal started with it only logs a `SECURITY` warning and stores that publicly known value. It only seeds the key stored in the Portal database on first start; see "Changing the installation key" below. |
| `AdminEmail` | `admin@admin.com` | Email of the administrator account, created on the first start with the password `admin`. Changing the setting later has no effect. |
| `InstanceTitle` | *(not set)* | Name shown in the Portal, `Apilane` by default. It seeds the setting on the first start; later change it under **Instance > Settings** (3 to 16 characters). |
| `AuthCookieDomain` | *(not set)* | Domain of the login cookie, for example `.example.com` to share it across subdomains. Without it the cookie belongs to the current host. |
| `AccountRateLimit__PermitLimit`, `AccountRateLimit__WindowSeconds` | *(not set)* | Calls of sign in, sign up and password-reset request allowed per IP address and window: 30 per 60 seconds by default. See [Deployment](deployment.md#production-considerations). |
| `AllowedHosts` | *(not set)* | The host names the Portal answers for. See [Deployment](deployment.md#production-considerations). |

### API

| Variable | Compose file | Description |
|---|---|---|
| `Url` | `http://0.0.0.0:5001` | URL where the API is served |
| `PortalUrl` | `http://127.0.0.1:5000` | The Portal, as the API reaches it (see the warning in step 1) |
| `FilesPath` | `/etc/apilanewebapi/Files` | Path for the SQLite databases of the applications and, with the `LocalFileSystem` storage provider, their uploaded files |
| `InstallationKey` | `${APILANE_INSTALLATION_KEY}` | Must be identical to the key stored in the Portal (**Instance > Settings**); on a new installation that is the value of `APILANE_INSTALLATION_KEY` in the compose file. The `appsettings.json` in the image holds the placeholder `REPLACE-WITH-A-LONG-RANDOM-SECRET`; an API started with it only logs a `SECURITY` warning. |
| `FileStorage__Provider`, `FileStorage__ConnectionString`, `FileStorage__BucketName` | *(not set)* | Where uploaded files are stored: `LocalFileSystem` (default), `GoogleCloudStorage`, `AwsS3` or `AzureBlobStorage`. See [File Storage Providers](developer_guide/file_storage_providers.md). |
| `Clustering__*` | *(not set)* | Orleans clustering. API servers in different containers or on different hosts cannot join one cluster yet: see the warning under [Deployment](deployment.md#orleans-clustering). |
| `InvalidFilesExtentions` | *(not set)* | The file extensions that cannot be uploaded. See [Deployment](deployment.md#production-considerations). |

### Both services

| Variable | Compose file | Description |
|---|---|---|
| `MinThreads` | *(not set)* | Minimum number of worker threads of the .NET thread pool; `50` in the `appsettings.json` of the image |
| `OpenTelemetry__*` | *(not set)* | Metrics and tracing. See [Deployment](deployment.md#metrics-and-tracing). |
| `ASPNETCORE_ENVIRONMENT` | *(not set)* | `Production` or `Development`; without it both services run as `Development`. See [Deployment](deployment.md#production-considerations). |

!!!warning "Changing the installation key"
    The Portal uses the key stored in its database (**Instance > Settings**), both to check the API servers and to call them; its own `InstallationKey` setting only seeds it on first start. To change the key of an existing installation, update it under Instance > Settings, set the same value as `InstallationKey` for the API servers, and restart them. The Portal does not need a restart. Until the API servers run with the new key, the Portal and the API servers refuse each other's calls. With the docker-compose file, put the new value in `APILANE_INSTALLATION_KEY` in the `.env` file and run `docker-compose -p apilane up -d`; keep that file in step with the stored key, because an API container recreated with the old value refuses the Portal.

!!!info "Next steps"
    - Configure [Security](developer_guide/security.md) rules for your entities
    - Set up [Email Templates](developer_guide/email_templates.md) for user registration
    - Explore the full [REST API Reference](api_reference.md)
    - Integrate with the [.NET SDK](developer_guide/sdk.md)
