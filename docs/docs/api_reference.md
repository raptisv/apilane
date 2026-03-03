# REST API Reference

All API endpoints follow the pattern:

```
https://{api-host}/api/{Controller}/{Action}?{params}
```

The **Application Token** is required on every request, passed as either:

```
- Query parameter: `?appToken={token}`
- Header: `x-application-token: {token}`
```

Authenticated endpoints additionally require the user's **AuthToken** via the `Authorization` header.

---

## Authentication

### Login

Authenticate a user and receive an auth token.

```
POST /api/Account/Login
x-application-token: {appToken}
```

**Body:**
```json
{ "Email": "user@example.com", "Password": "secret" }
```

You can use `Email` or `Username` to log in (or both).

**Response:**
```json
{
  "AuthToken": "a1b2c3d4-e5f6-...",
  "User": { "ID": 1, "Email": "user@example.com", "Username": "john", ... }
}
```

### Register

Create a new application user.

```
POST /api/Account/Register
x-application-token: {appToken}
```

**Body:**
```json
{ "Email": "user@example.com", "Username": "john", "Password": "SecurePass123!" }
```

You can include any custom properties defined on the `Users` entity.

**Response:** The new user's ID.

### Get User Data

Retrieve the authenticated user's profile and security rules.

```
GET /api/Account/UserData
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

### Update User

Update the authenticated user's custom properties. System properties (Email, Username, Password, Roles) cannot be updated from this endpoint.

```
PUT /api/Account/Update
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

**Body:**
```json
{ "Firstname": "John", "Lastname": "Doe" }
```

### Change Password

```
PUT /api/Account/ChangePassword
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

**Body:**
```json
{ "Password": "currentPassword", "NewPassword": "newPassword" }
```

### Renew Auth Token

Replace the current token with a new one. The old token is invalidated.

```
GET /api/Account/RenewAuthToken
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

**Response:** A new auth token string.

### Logout

Invalidate the current auth token.

```
GET /api/Account/Logout?everywhere=false
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

| Parameter | Default | Description |
|---|---|---|
| `everywhere` | `false` | If `true`, invalidates **all** auth tokens for the user across all sessions |

**Response:** The number of tokens deleted.

---

## Data

### Get Records

Retrieve a paginated list of records from an entity.

```
GET /api/Data/Get?entity={entity}
x-application-token: {appToken}
```

| Parameter | Required | Default | Description |
|---|---|---|---|
| `entity` | Yes | — | Entity name |
| `pageIndex` | No | `1` | Page number |
| `pageSize` | No | `20` | Records per page (0–1000) |
| `filter` | No | `null` | JSON [filter expression](developer_guide/filtering_sorting.md#filtering) |
| `sort` | No | `null` | JSON [sort expression](developer_guide/filtering_sorting.md#sorting) |
| `properties` | No | all | Comma-separated property names to return |
| `getTotal` | No | `false` | Include total count in response |

**Response:**
```json
{
  "Data": [ { "ID": 1, "Name": "Widget", ... } ],
  "Total": 42
}
```

`Total` is only included when `getTotal=true`.

### Get Record by ID

```
GET /api/Data/GetByID?entity={entity}&id={id}
x-application-token: {appToken}
```

| Parameter | Required | Description |
|---|---|---|
| `entity` | Yes | Entity name |
| `id` | Yes | Record ID |
| `properties` | No | Comma-separated property names |

### Get Record History

Retrieve historical versions of a record (requires [change tracking](developer_guide/entities_properties.md#change-tracking) to be enabled).

```
GET /api/Data/GetHistoryByID?entity={entity}&id={id}
x-application-token: {appToken}
```

| Parameter | Required | Default | Description |
|---|---|---|---|
| `entity` | Yes | — | Entity name |
| `id` | Yes | — | Record ID |
| `pageIndex` | No | `1` | Page number |
| `pageSize` | No | `10` | Records per page |

### Create Records

```
POST /api/Data/Post?entity={entity}
x-application-token: {appToken}
Content-Type: application/json
```

**Body** — a single object or array of objects:

```json
{ "Name": "Widget", "Price": 9.99 }
```

**Response:** Array of created record IDs — `[1]` or `[1, 2, 3]`

### Update Records

```
PUT /api/Data/Put?entity={entity}
x-application-token: {appToken}
Content-Type: application/json
```

**Body** — must include `ID`:

```json
{ "ID": 1, "Price": 12.99 }
```

**Response:** Count of affected records.

### Delete Records

```
DELETE /api/Data/Delete?entity={entity}&ids=1,2,3
x-application-token: {appToken}
```

| Parameter | Required | Description |
|---|---|---|
| `entity` | Yes | Entity name |
| `ids` | Yes | Comma-separated record IDs |

**Response:** Array of deleted IDs.

### Get Application Schema

Retrieve the full application schema including all entities, properties, and configuration.

```
GET /api/Data/Schema
x-application-token: {appToken}
```

**Response:**
```json
{
  "AuthTokenExpireMinutes": 60,
  "AllowLoginUnconfirmedEmail": true,
  "ForceSingleLogin": false,
  "Online": true,
  "AllowUserRegister": true,
  "MaxAllowedFileSizeInKB": 5120,
  "Entities": [
    {
      "Name": "Products",
      "IsSystem": false,
      "ChangeTracking": false,
      "Properties": [
        { "Name": "ID", "Type": "Number", "IsPrimaryKey": true, "IsSystem": true },
        { "Name": "Name", "Type": "String", "Required": true, "Maximum": 255 },
        { "Name": "Price", "Type": "Number", "DecimalPlaces": 2 }
      ]
    }
  ]
}
```

!!!info "Security"
    Access to the Schema endpoint is controlled separately in the application's [Security](developer_guide/security.md) settings.

### Transaction (Grouped)

Execute multiple create/update/delete operations atomically. See [Transactions](developer_guide/transactions.md).

```
POST /api/Data/Transaction
x-application-token: {appToken}
```

### TransactionOperations (Ordered)

Execute ordered operations with cross-referencing. See [Transactions](developer_guide/transactions.md#transactionoperations-ordered-with-cross-referencing).

```
POST /api/Data/TransactionOperations
x-application-token: {appToken}
```

---

## Files

Files are managed separately from regular entities. See [Files](developer_guide/files.md) for detailed usage.

### Upload File

```
POST /api/Files/Post
x-application-token: {appToken}
Content-Type: multipart/form-data
```

| Parameter | Required | Default | Description |
|---|---|---|---|
| `uid` | No | Auto-generated | Custom unique identifier |
| `public` | No | `false` | Make file publicly accessible |

**Response:** The new file's ID.

### Download File

```
GET /api/Files/Download?fileID={id}
x-application-token: {appToken}
```

```
GET /api/Files/Download?fileUID={uid}
x-application-token: {appToken}
```

Returns the raw file binary. Can be used directly in `<img>` tags.

### List File Records

```
GET /api/Files/Get
x-application-token: {appToken}
```

Supports the same `pageIndex`, `pageSize`, `filter`, `sort`, `properties`, and `getTotal` parameters as Data/Get.

### Get File Record by ID

```
GET /api/Files/GetByID?id={id}
x-application-token: {appToken}
```

### Delete Files

```
DELETE /api/Files/Delete?ids=1,2,3
x-application-token: {appToken}
```

---

## Stats

### Aggregate

Run aggregate functions against an entity's data.

```
GET /api/Stats/Aggregate?entity={entity}&properties={props}
x-application-token: {appToken}
```

| Parameter | Required | Default | Description |
|---|---|---|---|
| `entity` | Yes | — | Entity name |
| `properties` | Yes | — | Comma-separated `Property.Function` pairs (e.g., `Price.Sum,Price.Avg,ID.Count`) |
| `pageIndex` | No | `1` | Page number |
| `pageSize` | No | `20` | Records per page |
| `filter` | No | `null` | JSON filter expression |
| `groupBy` | No | `null` | Property name to group by |
| `orderDirection` | No | `DESC` | Sort direction: `ASC` or `DESC` |

**Supported functions:** `Count`, `Min`, `Max`, `Sum`, `Avg`

**Example:**

```
GET /api/Stats/Aggregate?entity=Orders&properties=Total.Sum,Total.Avg,ID.Count&groupBy=Status
x-application-token: {appToken}
```

### Distinct

Get distinct values of a property.

```
GET /api/Stats/Distinct?entity={entity}&property={property}
x-application-token: {appToken}
```

| Parameter | Required | Description |
|---|---|---|
| `entity` | Yes | Entity name |
| `property` | Yes | Property name |
| `filter` | No | JSON filter expression |

---

## Custom Endpoints

Call a custom SQL endpoint by name.

```
GET /api/Custom/{name}?{params}
x-application-token: {appToken}
```

Parameters wrapped in `{braces}` in the custom endpoint's SQL query are automatically bound from query parameters. For example, a query `SELECT * FROM Users WHERE ID = {UserID}` is called with `?UserID=42`.

!!!info "Note"
    Parameters can only be of type big integer (long) to prevent SQL injection. All custom endpoints are HTTP GET requests.

---

## Email

### Request Confirmation Email

Send (or re-send) a confirmation email to the user.

```
GET /api/Email/RequestConfirmation?email={email}
x-application-token: {appToken}
```

### Forgot Password Email

Send a password reset email to the user.

```
GET /api/Email/ForgotPassword?email={email}
x-application-token: {appToken}
```

!!!info "Note"
    Both endpoints return success even if the email doesn't exist, to prevent user enumeration.
