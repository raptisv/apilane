---
description: "Reference for the Apilane REST API: authentication, data, files, stats, custom endpoints and email, with parameters and examples."
---

# REST API Reference

!!!info "Application API"
    This page describes the API of an application, served by the API server. Its Swagger UI is at `https://{api-host}/swagger`, and it answers calls from any origin, so a browser app can call it directly. The Portal's management API (`/api/v1`) is a separate API, see [Additional endpoints](deployment.md#additional-endpoints).

All API endpoints follow the pattern:

```
https://{api-host}/api/{Controller}/{Action}?{params}
```

The **Application Token** is required on every request, passed as either:

- Query parameter: `?appToken={token}`
- Header: `x-application-token: {token}` (it wins when both are sent)

A signed-in user's **AuthToken** is sent in one of three ways:

- Header: `Authorization: Bearer {authToken}`
- A signed request: the headers `x-auth-keyid`, `x-auth-timestamp` and `x-auth-signature`, so the token itself is never sent. See [Authenticating requests](developer_guide/security.md#authenticating-requests)
- Query parameter: `?authToken={authToken}`, only for a plain link that cannot set headers, such as an `<img>` tag. The token ends up in the address, so use the header everywhere else

For data, file, stats, custom endpoint and schema calls, the application's [Security](developer_guide/security.md) rules decide whether a token is needed: a call works without one only when its rule grants the action to the `ANONYMOUS` role. A missing, unknown or expired token counts as no token: the call then gets only what `ANONYMOUS` may do, and is refused with `UNAUTHORIZED` when that is not enough. Login, Register and the email calls need no token; Get User Data, Update User, Change Password and Renew Auth Token always need one.

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

You can log in with `Email` or with `Username`. When both are sent, only the `Email` is used.

**Response:**
```json
{
  "AuthToken": "a1b2c3d4-e5f6-...",
  "AuthTokenID": 42,
  "User": { "ID": 1, "Email": "user@example.com", "Username": "john", ... }
}
```

`AuthTokenID` is the public id of the token. Keep it if the client [signs its requests](developer_guide/security.md#authenticating-requests).

A wrong email, username or password answers `ERROR` ("Invalid login attempt"). An email that is not a valid address answers `VALIDATION`, and a missing password, or neither email nor username, answers `REQUIRED`. `UNCONFIRMED_EMAIL` is answered when the user has not confirmed the email and the application does not [allow that to sign in](developer_guide/security.md#allow-users-with-an-unconfirmed-email-to-sign-in).

### Register

Create a new application user. No token is needed, but the application must [allow new users to register](developer_guide/security.md#register), otherwise the call fails with `ERROR` ("Register is not allowed").

```
POST /api/Account/Register
x-application-token: {appToken}
```

**Body:**
```json
{ "Email": "user@example.com", "Username": "john", "Password": "SecurePass123!" }
```

`Email` must be a valid address (`VALIDATION` otherwise), and `Email`, `Username` and `Password` follow the validation of the `Users` entity: `Email` and `Username` need 4 to 100 characters and `Password` 8 to 400. You can include any custom properties defined on the `Users` entity. `Roles` is ignored. If the [email confirmation template](developer_guide/email_templates.md) is active, a confirmation email is sent after the user is created.

**Response:** The new user's ID.

### Get User Data

Retrieve the authenticated user's profile and security rules.

```
GET /api/Account/UserData
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

**Response:**
```json
{
  "User": { "ID": 1, "Email": "user@example.com", "Username": "john", ... },
  "Security": [
    { "Role": "AUTHENTICATED", "Name": "Products", "Type": "Entity", "Action": "get", "Properties": ["Name", "Price"] }
  ]
}
```

`Security` lists the rules that apply to the user's roles, plus the `ANONYMOUS` and `AUTHENTICATED` roles. `Type` is `Entity`, `CustomEndpoint` or `Schema`, and `Action` is `get`, `post`, `put` or `delete`.

### Update User

Update the authenticated user's custom properties. System properties (Email, Username, Password, Roles) cannot be updated from this endpoint. At least one custom property is required (`NO_PROPERTIES_PROVIDED`). Property names are matched without regard to case (`nickname` writes `Nickname`), and a property that is in the body twice with different capitals (`Nickname` and `nickname`) is refused with `VALIDATION`, naming the property, before anything is written.

```
PUT /api/Account/Update
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

**Body:**
```json
{ "Firstname": "John", "Lastname": "Doe" }
```

**Response:** The updated user.

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

`Password` must be the current password, otherwise the call answers `VALIDATION`. `NewPassword` must have 8 to 20 characters (`VALIDATION` otherwise). Success invalidates all of the user's existing auth tokens, including this request's token, and outstanding password-reset links. Sign in again with the new password.

**Response:** `true`.

### Renew Auth Token

Replace the current token with a new one. The old token is invalidated.

```
GET /api/Account/RenewAuthToken
x-application-token: {appToken}
Authorization: Bearer {authToken}
```

**Response:** The same object as login: the user, the new `AuthToken` and its `AuthTokenID`.

```json
{ "User": { "ID": 1, "Email": "user@example.com" }, "AuthToken": "...", "AuthTokenID": 42 }
```

A client that [signs its requests](developer_guide/security.md) must keep the new `AuthTokenID` as well, because the old ID stops working together with the old token. A signed request can be used here too: the token being renewed is then the one whose ID signed the request.

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
| `pageIndex` | No | `1` | Page number, from 1 (a lower value is replaced by 1) |
| `pageSize` | No | `20` | Records per page, 1 to 1000 (a value below 1, `0` included, or above 1000 is replaced by 1000) |
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

**Response:** The record, as one object (not wrapped in `Data`). `ID` is always included, even when `properties` leaves it out. A record that does not exist, or that the caller may not read, answers `NOT_FOUND`.

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
| `pageSize` | No | `10` | Records per page, 1 to 1000 (a value below 1 or above 1000 is replaced by 1000) |

**Response:**
```json
{
  "Data": [ { "Name": "Widget", "Price": 9.99, "History_Record_Created": 1700000000000, "History_Record_Owner": 7 } ],
  "Total": 3
}
```

Entries are newest first. Each holds the values the record had before the change (only the properties the caller may read), plus `History_Record_Created` (Unix milliseconds) and `History_Record_Owner` (the id of the user who made the change, or `null`). `Total` is always returned. History is read through the live record: once the record is deleted the call answers `NOT_FOUND`.

### Create Records

```
POST /api/Data/Post?entity={entity}
x-application-token: {appToken}
Content-Type: application/json
```

**Body** — a single object or array of objects. Property names are matched without regard to case (`title` writes `Title`), a property that is in an object twice with different capitals (`Title` and `title`) is refused with `VALIDATION` (that object is not created), and `ID`, `Owner` and `Created` are set by the server. An array is not atomic: when one object fails, the ones before it stay created. Use [Transaction](#transaction-grouped) when all must succeed or none.

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

**Body** — an object with `ID` and the properties to change, or an array of such objects (not atomic: the response counts the records of all of them):

```json
{ "ID": 1, "Price": 12.99 }
```

Only the properties present are updated. Property names, `ID` included, are matched without regard to case (`price` writes `Price`, `id` selects the record). A property or `ID` that is in the object twice with different capitals (`Price` and `price`) is refused with `VALIDATION`, naming it (that object is not changed). `ID` is required (`NO_ID_PROVIDED`).

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

**Response:** Array of the IDs that were actually deleted (empty when none were). Records that do not exist, or that the caller may not delete, are skipped without an error. `ids` without any valid id answers `NO_RECORDS_FOUND_TO_DELETE`.

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
  "AllowUserRegister": true,
  "MaxAllowedFileSizeInKB": 5120,
  "Entities": [
    {
      "Name": "Products",
      "IsSystem": false,
      "IsReadOnly": false,
      "ChangeTracking": false,
      "Properties": [
        { "Name": "ID", "Type": "Number", "IsPrimaryKey": true, "IsSystem": true, "Required": true, "Encrypted": false },
        { "Name": "Name", "Type": "String", "IsPrimaryKey": false, "IsSystem": false, "Required": true, "Encrypted": false, "Maximum": 255 },
        { "Name": "Price", "Type": "Number", "IsPrimaryKey": false, "IsSystem": false, "Required": false, "Encrypted": false, "DecimalPlaces": 2 }
      ]
    }
  ]
}
```

`Description`, `Minimum`, `Maximum`, `DecimalPlaces` and `ValidationRegex` are left out when an entity or property has none. The system entities (`Users`, `AuthTokens`, `Files`) are listed too.

!!!info "Security"
    Access to the Schema endpoint is controlled separately in the application's [Security](developer_guide/security.md) settings.

### Transaction (Grouped)

Execute multiple create/update/delete operations atomically. See [Transactions](developer_guide/transactions.md).

```
POST /api/Data/Transaction
x-application-token: {appToken}
Content-Type: application/json
```

**Body:**
```json
{
  "Post": [ { "Entity": "Orders", "Data": { "CustomerName": "John" } } ],
  "Put": [ { "Entity": "Products", "Data": { "ID": 5, "Stock": 48 } } ],
  "Delete": [ { "Entity": "TempRecords", "Ids": "10,11" } ]
}
```

**Response:** `{ "Post": [1], "Put": 1, "Delete": [10, 11] }`: the created IDs, the count of updated records and the deleted IDs.

All Posts run first, then all Puts, then all Deletes, in one database transaction that is given 20 seconds: if one operation fails, nothing is kept. The `Files` entity cannot be used.

### TransactionOperations (Ordered)

Execute ordered operations with cross-referencing. See [Transactions](developer_guide/transactions.md#transactionoperations-ordered-with-cross-referencing).

```
POST /api/Data/TransactionOperations
x-application-token: {appToken}
Content-Type: application/json
```

**Body:** `{ "Operations": [ ... ] }`. The limit of 20 seconds and the `Files` restriction are the same, and `Custom` operations are accepted too.

---

## Files

Files are managed separately from regular entities. See [Files](developer_guide/files.md) for detailed usage.

### Upload File

```
POST /api/Files/Post
x-application-token: {appToken}
Content-Type: multipart/form-data
```

Send the file as the multipart form field `fileUpload`. The file name of that part becomes the file's `Name`. There are no other parameters: the file's `UID` is generated by the server and is in its file record.

The upload is refused when the file is larger than the application's maximum file size (`MaxAllowedFileSizeInKB`, see [Get Application Schema](#get-application-schema)) or has a blocked extension (by default `.exe`, `.vbs`, `.msi`, `.jar`, `.bat`, `.cmd`, `.vbe`, `.js`, `.jsp`, `.lnk`; the server setting `InvalidFilesExtentions`). An upload cannot be signed: authenticate it with the bearer token. Who may upload is a rule on `Files`, see [File security](developer_guide/files.md#file-security).

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

Send one of `fileID` or `fileUID`, not both. Returns the raw file binary, with the file name in `Content-Disposition`. The caller needs `get` access to `Files`, like for any file call.

An `<img>` tag sends no headers, so put the tokens in the address: `/api/Files/Download?fileID={id}&appToken={appToken}`, and, for a file that `ANONYMOUS` may not read, also `&authToken={authToken}` (the auth token then shows in the address, so only do that when you must).

Responses carry `Cache-Control: max-age=31536000`, so a browser may keep a downloaded file for a year.

### List File Records

```
GET /api/Files/Get
x-application-token: {appToken}
```

Supports the same `pageIndex`, `pageSize`, `filter`, `sort`, `properties`, and `getTotal` parameters as Data/Get (but no `entity`).

**Response:**
```json
{
  "Data": [ { "ID": 7, "Name": "photo.jpg", "UID": "f2b1c0de-...", "Owner": 3, "Size": 0.42, "Created": 1700000000000 } ],
  "Total": 1
}
```

`Size` is in MB, `Created` is in Unix milliseconds and `Total` is only there with `getTotal=true`.

### Get File Record by ID

```
GET /api/Files/GetByID?id={id}
x-application-token: {appToken}
```

**Response:** One file record, as in the list above.

### Delete Files

```
DELETE /api/Files/Delete?ids=1,2,3
x-application-token: {appToken}
```

**Response:** Array of the deleted IDs, as for Data/Delete. The stored files are removed too.

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
| `pageSize` | No | `20` | Records per page, 1 to 1000 (a value below 1 or above 1000 is replaced by 1000) |
| `filter` | No | `null` | JSON filter expression |
| `groupBy` | No | `null` | Comma-separated properties to group by. A date property may take a `.year`, `.month`, `.day`, `.hour`, `.minute` or `.second` suffix (e.g., `Created.year`), returned as `Created_year`. Any other suffix returns `INVALID_GROUPBY_PARAMETER` |
| `orderDirection` | No | `DESC` | Sort direction: `ASC` or `DESC` |

**Supported functions:** `Count`, `Min`, `Max`, `Sum`, `Avg`

**Example:**

```
GET /api/Stats/Aggregate?entity=Orders&properties=Total.Sum,Total.Avg,ID.Count&groupBy=Status
x-application-token: {appToken}
```

**Response:** An array of rows, one per group (a single row without `groupBy`). Each aggregate column is named `{Property}_{function}` with the function in lower case, and each group column is named after the property, or `Created_year` for a date suffix:

```json
[ { "Status": "Open", "Total_sum": 1250.5, "Total_avg": 125.05, "ID_count": 10 } ]
```

Rows are ordered by the aggregate values (`orderDirection`), not by the group columns. Function names are not case-sensitive.

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

**Response:** An array with one object per distinct value, keyed by the property name:

```json
[ { "Category": "Tools" }, { "Category": "Toys" } ]
```

---

## Custom Endpoints

Call a custom SQL endpoint by name. Like any other call, it needs the `get` permission on the endpoint in the application's [Security](developer_guide/security.md) rules.

```
GET /api/Custom/{name}?{params}
x-application-token: {appToken}
```

Parameters wrapped in `{braces}` in the custom endpoint's SQL query are automatically bound from query parameters. A name has letters and underscores only, and the query string must spell it exactly as the SQL does, capitals included. For example, a query `SELECT * FROM Users WHERE ID = {UserID}` is called with `?UserID=42`. A parameter that is missing, or whose value is not a whole number, is replaced by `null`.

`{Owner}` is not a parameter: it is replaced by the ID of the signed-in user who made the call. For a call without a signed-in user it is not replaced and the database refuses the query.

**Response:** An array of result sets, one array of rows per result set: `[ [ { "ID": 42, "Email": "..." } ] ]`. The query runs in a transaction that is given 20 seconds: if it fails or times out, nothing it did is kept (on MySQL, statements that change tables or columns are committed at once).

!!!info "Note"
    Parameters can only be of type big integer (long) to prevent SQL injection. All custom endpoints are HTTP GET requests.

---

## Email

### Request Confirmation Email

Send (or re-send) a confirmation email to the user. The link in the email works for 1 hour: request it again for a new one. Nothing is sent when the email is already confirmed.

```
GET /api/Email/RequestConfirmation?email={email}
x-application-token: {appToken}
```

### Forgot Password Email

Send a password reset email to the user. The link in the email works for 24 hours.

```
GET /api/Email/ForgotPassword?email={email}
x-application-token: {appToken}
```

!!!info "Note"
    Both endpoints answer `"OK"` even if no user has that email, to prevent user enumeration. An `email` that is not a valid address answers `VALIDATION`, and the call fails with `ERROR` when the application has no [email settings](developer_guide/email_templates.md). Only one email per address and kind is sent every 5 minutes: a second request inside that time answers `RATE_LIMIT_EXCEEDED`.

---

## Errors

A call that fails answers a JSON object. The HTTP status is 401 for `UNAUTHORIZED` and 400 for every other code, `RATE_LIMIT_EXCEEDED` included:

```json
{ "Code": "VALIDATION", "Message": "Maximum 255 characters", "Entity": "Products", "Property": "Name" }
```

`Entity` and `Property` name the entity and the property the error is about, when there is one. Branch on `Code`, not on `Message`.

| Code | Meaning |
|---|---|
| `ERROR` | Anything else that went wrong, for example a wrong login. An unexpected server error answers "Something went wrong" |
| `UNAUTHORIZED` | The caller may not do this, or the call needs a signed-in user |
| `UNCONFIRMED_EMAIL` | The user must confirm the email before signing in |
| `SERVICE_UNAVAILABLE` | The application is offline, or the caller's IP address is not allowed |
| `RATE_LIMIT_EXCEEDED` | A rate limit rule or the email limit was hit |
| `REQUIRED`, `VALIDATION` | A property is missing or its value breaks a rule (`Property` names it) |
| `UNIQUE_CONSTRAINT_VIOLATION`, `FOREIGN_KEY_CONSTRAINT_VIOLATION` | A unique or foreign key constraint refused the value |
| `NOT_FOUND`, `FILE_NOT_FOUND` | The record, custom endpoint or file does not exist, or the caller may not see it |
| `EMPTY_BODY`, `NO_PROPERTIES_PROVIDED`, `NO_ID_PROVIDED`, `NO_RECORDS_FOUND_TO_DELETE` | The body or the ids of a create, update or delete call are missing |
| `INVALID_FILTER_PARAMETER`, `INVALID_SORT_PARAMETER`, `INVALID_GROUPBY_PARAMETER` | The `filter`, `sort` or `groupBy` value cannot be used |
