---
description: "What to add to your AGENTS.md so AI coding assistants use the Apilane SDK and the application API correctly. A second page covers managing an instance with an agent key."
---

# AI Agent Guidelines

If you use an AI coding assistant (Cursor, GitHub Copilot, Claude Code, etc.), append the following to the instruction file it reads in your project (`AGENTS.md`, or the file your assistant's documentation names) to help it use the Apilane application API and SDKs correctly. The block is about the data of an application: records, users, files, statistics and custom endpoints. An agent that manages the applications themselves (entities, security, reports) through the Portal needs other instructions: see [managing an instance with an agent key](ai_agent_portal.md).

````markdown
## Apilane SDK Best Practices

This project uses the Apilane SDK for backend operations. Apilane is a backend-as-a-service that provides data storage, user authentication, file management, and more via a REST API. The SDK wraps this API with a type-safe builder pattern.

### Rules That Prevent Most Mistakes

- Check every result before you use its value (`HasError` / `isError`).
- Update with only `ID` and the properties that change, never with a whole typed object.
- Property names are matched without regard to case when you write, but copy them exactly from the schema (`ID`, `Price`) and send each property once: the same property under two spellings (`Price` and `price`) is refused with `VALIDATION`.
- A list call returns 20 records unless you set a page size (1 to 1000, anything else becomes 1000): page through the rest.
- Roles decide what a caller may do. `UNAUTHORIZED`, or less data than you expected, usually means the role has no rule for it, not that the call is wrong.
- Use the Files calls for files, `AccountRegisterAsync` / `accountRegister` to create users, and a transaction for work that must succeed or fail as one.
- Never hardcode an auth token. The application token is not a secret (every client sends it), so the role rules are what protect the data.
- Entities, properties, security rules, custom endpoints and settings are changed in the Portal or its management API, never through the SDK.

### SDK Variants

| SDK | Package | Import |
|---|---|---|
| .NET | `Apilane.Net` (NuGet: `dotnet add package Apilane.Net`) | the `using` lines below |
| JavaScript | `apilane.js`: one ES module (browsers and Node.js 18+), copied into the project from `sdk/Apilane.Js/apilane.js` in the Apilane repository | `import { createApilaneService, DataGetListRequest, FilterItem, FilterOperator } from './apilane.js';` — import every class and enum you use |

```csharp
using Apilane.Net.Abstractions;   // IApilaneService
using Apilane.Net.Request;        // every request builder, e.g. DataGetListRequest
using Apilane.Net.Models.Data;    // FilterItem, SortItem, TransactionBuilder
using Apilane.Net.Models.Enums;   // FilterOperator, FilterLogic
using Apilane.Net.Models.Account; // LoginItem, RegisterItem, ApiUser
using Apilane.Net.Extensions;     // BuildErrorMessage, ToUnixTimestampMilliseconds
// UseApilane is in the namespace Microsoft.Extensions.DependencyInjection
```

Both SDKs offer the same operations on the same API, but the calling style differs. All rules below apply to both unless noted.

| | .NET | JavaScript |
|---|---|---|
| Result | `Either<T, ApilaneError>`: `HasError(out var error)`, `.Value` (throws on an error), `Match` | `ApilaneResult`: `isError`, `error`, `value` (throws on an error), `match` |
| Throw instead of returning the error | `.OnErrorThrowException(true)` (without the argument it does NOT throw: the default is `false`) | `.onErrorThrowException()` (the default is `true`) |
| Records | typed: `GetDataAsync<Product>` | plain objects: `result.value.Data` |
| Filters | `new FilterItem(...)` | `FilterItem.condition / and / or / group` |
| Sort | `new SortItem { Property = "Price", Direction = "ASC" }` | `new SortItem('Price', 'asc')`, `SortItem.asc(p)`, `SortItem.desc(p)` |
| Aggregate enum | `StatsAggregateRequest.DataAggregates` (nested) | `DataAggregates` |
| Transaction reference | `Post(entity, data, out var ref)` | `postWithRef(entity, data)` |
| Custom endpoint result | typed (`List<T>`, or a tuple for 2 to 5 result sets) | raw nested array |
| Cancellation | `CancellationToken` | `AbortSignal` |

---

### Setup

**.NET:**
```csharp
builder.Services.UseApilane(serverUrl, applicationToken);
// Inject IApilaneService via constructor — never instantiate directly
// For multi-app: UseApilane(..., serviceKey: "app1"), then inject [FromKeyedServices("app1")] IApilaneService
```

**JavaScript:**
```javascript
const apilane = createApilaneService({
    apiUrl: 'https://my.api.server',
    appToken: 'your-app-token',
    // optional: fetch: customFetch
});
```

---

### Roles and Access (Why You Get UNAUTHORIZED or Empty Results)

What a caller may do is decided on the server by role rules, never by the client code. The roles are `ANONYMOUS` (a call without a valid token; its rules apply to every caller), `AUTHENTICATED` (any signed-in user) and the custom roles of a user (the comma separated `Roles` of the `Users` entity). A rule belongs to one entity, custom endpoint or the schema and to one action (`get`, `post`, `put`, `delete`); it lists the properties the role may use, can limit the role to its own records and can carry a rate limit. Where no rule matches, the call is refused with `UNAUTHORIZED`.

- `UNAUTHORIZED`, or properties and records that are missing from an answer, usually mean a missing rule: change the rules in the Portal (Security), not the client code
- A rule limited to own records makes the other records invisible: they are not returned, and asked for by ID they answer `NOT_FOUND`
- `GetAccountUserDataAsync` / `getAccountUserData` returns the signed-in user and the rules that apply to the user's roles — use it to decide what to show

---

### Error Handling (CRITICAL)

All SDK methods return a result type for the errors the API reports — NEVER ignore return values or assume success. Network failures (no connection, timeout, abort) and, in .NET, an error body that is not JSON (for example from a proxy) are thrown as exceptions instead: wrap the calls in try/catch where that matters.

**.NET** — returns `Either<TSuccess, ApilaneError>`:
```csharp
var response = await apilane.GetDataAsync<Product>(request);

// ✅ Always check for errors first
if (response.HasError(out var error))
{
    // error.Code, error.Message, error.Property, error.Entity
    throw new Exception(error.Message);
}
var products = response.Value.Data;

// Alternative: throw automatically on error (pass true: without the argument it does NOT throw)
var responseOrThrow = await apilane.GetDataAsync<Product>(
    DataGetListRequest.New("Products").OnErrorThrowException(true));
```

**JavaScript** — returns `ApilaneResult`:
```javascript
const result = await apilane.getData(request);

// ✅ Option 1: Check isSuccess/isError
if (result.isError) {
    // result.error.Code, result.error.Message, result.error.buildErrorMessage()
    throw new Error(result.error.buildErrorMessage());
}
const products = result.value.Data;

// ✅ Option 2: Pattern match
result.match(
    data => console.log(data.Data),
    error => console.error(error.Code, error.Message)
);

// ✅ Option 3: Throw automatically
const resultOrThrow = await apilane.getData(
    DataGetListRequest.new('Products').onErrorThrowException());
```

Every error has `Code`, `Message`, `Property` and `Entity` (`Property` names the offending property for `REQUIRED`, `VALIDATION`, `UNIQUE_CONSTRAINT_VIOLATION` and `FOREIGN_KEY_CONSTRAINT_VIOLATION`). The HTTP status is 401 for `UNAUTHORIZED` and 400 for every other code (also `NOT_FOUND` and `RATE_LIMIT_EXCEEDED`): branch on `Code`, never on the status.

| Code | Meaning |
|---|---|
| `ERROR` | Generic failure: wrong login ('Invalid login attempt'), registration switched off, 'Not allowed' (POST on `Users`, any write to a read-only entity), unknown entity, file too large |
| `UNAUTHORIZED` | The caller's roles have no rule for this entity, property, endpoint or action, or the call needs a signed-in user. An expired or unknown token counts as anonymous and ends here |
| `UNCONFIRMED_EMAIL` | Sign-in with an unconfirmed email while the application does not allow it |
| `REQUIRED`, `VALIDATION`, `UNIQUE_CONSTRAINT_VIOLATION`, `FOREIGN_KEY_CONSTRAINT_VIOLATION` | A property value is rejected: see `Property` |
| `NOT_FOUND` | The record or custom endpoint does not exist, or is not visible to the caller |
| `EMPTY_BODY`, `NO_PROPERTIES_PROVIDED`, `NO_ID_PROVIDED` | POST/PUT without a body, without a writable property, or PUT without `ID` |
| `NO_RECORDS_FOUND_TO_DELETE` | The `ids` list holds no valid ID |
| `INVALID_FILTER_PARAMETER`, `INVALID_SORT_PARAMETER`, `INVALID_GROUPBY_PARAMETER` | A malformed filter, sort or groupBy, or a filter or sort on a property the caller may not read |
| `FILE_NOT_FOUND` | Download of an unknown file |
| `RATE_LIMIT_EXCEEDED` | The rate limit of the matching rule is used up; also one confirmation or password email per address per 5 minutes. Back off instead of retrying in a loop |
| `SERVICE_UNAVAILABLE` | The application is offline, or the caller's IP address is not allowed |

---

### Data Updates (CRITICAL — Most Common Mistake)

When updating records, send ONLY the `ID` and the properties being changed. NEVER send a full typed object — unset properties will overwrite existing values with their type defaults (`null`, `0`, `false`, empty string).

What the server does with an update: only the properties present in the body are written, and a property sent as `null` is cleared. Names are matched without regard to case (`id` selects the record, `price` writes `Price`), but a property sent twice with different capitals (`Price` and `price`) is refused with `VALIDATION`, naming it, and that object is not changed. System properties (`ID`, `Owner`, `Created`) and properties the caller's role may not write are dropped without an error; if none is left the answer is `NO_PROPERTIES_PROVIDED`, and a body without `ID` gives `NO_ID_PROVIDED`. The answer is the number of rows changed (`0` when nothing matched or the record is not visible to the caller). The body can also be an array of such objects to update several records; that is not atomic, use a transaction when it must be.

**.NET:**
```csharp
// ✅ Correct — anonymous object with only changed properties
await apilane.PutDataAsync(
    DataPutRequest.New("Products").WithAuthToken(token),
    new { ID = 5, Price = 12.99 });

// ✅ Correct — updating multiple properties
await apilane.PutDataAsync(
    DataPutRequest.New("Products").WithAuthToken(token),
    new { ID = 5, Price = 12.99, Name = "Updated Widget" });

// ❌ WRONG — typed object sends ALL properties, overwriting others with defaults
var product = new Product { ID = 5, Price = 12.99 };
// This will set Name=null, InStock=false, Category=null, etc.
await apilane.PutDataAsync(
    DataPutRequest.New("Products").WithAuthToken(token),
    product);
```

**JavaScript:**
```javascript
// ✅ Correct — plain object with only changed properties
await apilane.putData(
    DataPutRequest.new('Products').withAuthToken(token),
    { ID: 5, Price: 12.99 }
);

// ❌ WRONG — spreading a full object sends all fields
const product = await fetchProduct(5);
product.Price = 12.99;
await apilane.putData(
    DataPutRequest.new('Products').withAuthToken(token),
    product  // Sends every field, including unchanged ones
);
```

**This also applies to `AccountUpdate`:**
```csharp
// ✅ Correct
await apilane.AccountUpdateAsync<AppUser>(
    AccountUpdateRequest.New().WithAuthToken(token),
    new { Firstname = "John" });  // Only updates Firstname

// ❌ Wrong
await apilane.AccountUpdateAsync<AppUser>(
    AccountUpdateRequest.New().WithAuthToken(token),
    user);  // Overwrites all user fields
```

`AccountUpdate` writes only the custom properties you added to the `Users` entity: `Email`, `Username`, `Password` and `Roles` cannot be changed there. The model type (`AppUser`) must implement `IApiUser` (derive from `ApiUser`). To change a password call `PUT /api/Account/ChangePassword` with `{ "Password": "current", "NewPassword": "new" }` (8 to 20 characters): the SDKs have no method for it.

---

### Data Creation

- Use anonymous objects (.NET) or plain objects (JS) for POST operations
- System properties (`ID`, `Owner`, `Created`) are set by the server — do NOT include them. If they are sent they are ignored without an error. `Created` is a Unix timestamp in milliseconds (UTC); `Owner` is the ID of the caller (empty for an anonymous caller)
- Property names are matched without regard to case, and a name that is not a property of the entity is ignored (not stored, no error): copy the names from the schema, and send each property once (`Title` and `title` together are refused with `VALIDATION`)
- Date properties take a Unix timestamp (seconds or milliseconds) or `yyyy-MM-dd`, `yyyy-MM-dd HH:mm`, `yyyy-MM-dd HH:mm:ss`, `yyyy-MM-dd HH:mm:ss.fff`, and are returned as Unix milliseconds (`ToUnixTimestampMilliseconds()` / `UnixTimestampToDatetime()` in .NET, `dateToUnixTimestampMilliseconds()` / `unixTimestampToDate()` in JS)
- The response returns an array of created IDs
- The body can be an array of objects to create several records; the answer lists all new IDs. The records are created one by one and NOT atomically (those created before a failing one stay): use a transaction when it must be all or nothing
- `Users` cannot be created with the data endpoints (`ERROR`, 'Not allowed'): register users with `AccountRegisterAsync`. Read-only entities refuse POST, PUT and DELETE

```csharp
// .NET
var response = await apilane.PostDataAsync(
    DataPostRequest.New("Products").WithAuthToken(token),
    new { Name = "Widget", Price = 9.99, InStock = true });
long newId = response.Value.Single();
```
```javascript
// JavaScript
const result = await apilane.postData(
    DataPostRequest.new('Products').withAuthToken(token),
    { Name: 'Widget', Price: 9.99, InStock: true }
);
const newId = result.value[0];
```

---

### Data Retrieval

- ALWAYS set `.WithPageSize()` / `.withPageSize()` — a list call returns one page: `pageIndex` starts at 1, the default `pageSize` is 20 and the maximum is 1000 (a size below 1, `0` included, or above 1000 silently becomes 1000, and `0` does not mean "all"). Without a page size you get only the first 20 records, not all of them: page with `.WithPageIndex(n)` until a page comes back shorter than the size
- Use `.WithProperties()` / `.withProperties()` to select only needed columns — reduces bandwidth. `ID` is always returned; a property the caller may not read is left out without an error, but filtering or sorting on it fails with `INVALID_FILTER_PARAMETER` / `INVALID_SORT_PARAMETER`
- Use `.WithFilter()` / `.withFilter()` to filter server-side — never fetch all and filter in memory
- Without `.WithSort()` / `.withSort()` the entity's default sorting (set in the Portal) applies
- Only request total count (`.GetDataTotalAsync` / `.getDataTotal`) when needed (e.g., first page load) — it costs an extra DB query

```csharp
// .NET — good practice
var response = await apilane.GetDataAsync<Product>(
    DataGetListRequest.New("Products")
        .WithAuthToken(token)
        .WithPageSize(25)
        .WithPageIndex(1)
        .WithProperties("ID", "Name", "Price")
        .WithFilter(new FilterItem("InStock", FilterOperator.equal, true))
        .WithSort(new SortItem { Property = "Price", Direction = "ASC" }));
```
```javascript
// JavaScript — good practice
const result = await apilane.getData(
    DataGetListRequest.new('Products')
        .withAuthToken(token)
        .withPageSize(25)
        .withPageIndex(1)
        .withProperties('ID', 'Name', 'Price')
        .withFilter(FilterItem.condition('InStock', FilterOperator.equal, true))
        .withSort(new SortItem('Price', 'ASC'))
);
```

**`getAllData` / `GetAllDataAsync`** auto-paginates through all records — use it only for small bounded datasets, not open-ended tables.

**One record and delete:**
```csharp
// .NET — NOT_FOUND when the record does not exist or is not visible to the caller
var product = await apilane.GetDataByIdAsync<Product>(
    DataGetByIdRequest.New("Products", 5).WithAuthToken(token));

// .NET — the answer lists the IDs that were deleted
var deleted = await apilane.DeleteDataAsync(
    DataDeleteRequest.New("Products").AddIdToDelete(5).WithAuthToken(token));
```
```javascript
// JavaScript
const product = await apilane.getDataById(
    DataGetByIdRequest.new('Products', 5).withAuthToken(token));      // product.value is the record
const deleted = await apilane.deleteData(
    DataDeleteRequest.new('Products', [5]).withAuthToken(token));     // deleted.value is the array of deleted IDs
```

Delete skips IDs that do not exist or are not visible to the caller without an error, so the answer can be shorter than the list sent (or empty); `NO_RECORDS_FOUND_TO_DELETE` means the list held no valid ID.

---

### Record History

When an entity has change tracking enabled, Apilane stores a snapshot of a record's **previous values** every time it is updated. Use `GetHistoryByIdAsync` / `getHistoryById` to read a single record's history, paged and ordered newest-first.

It returns `{ Data, Total }`. Each `Data` entry contains the entity's property values **as they were before that change** (only the properties the caller has `get` access to), plus two metadata columns:

| Field | Description |
|---|---|
| `History_Record_Created` | Unix timestamp (ms) of the change |
| `History_Record_Owner` | The user ID that made the change (nullable) |
| *(entity properties)* | The record's values before the change, e.g. `Name`, `Price` |

```csharp
// .NET — supply a model with the entity's properties + the two metadata columns
var history = await apilane.GetHistoryByIdAsync<ProductHistory>(
    DataGetHistoryByIdRequest.New("Products", 5)
        .WithPageSize(20)
        .WithPageIndex(1)
        .WithAuthToken(token));
var entries = history.Value.Data;   // each: Name, Price, ..., History_Record_Created, History_Record_Owner
int total = history.Value.Total;
```
```javascript
// JavaScript
const history = await apilane.getHistoryById(
    DataGetHistoryByIdRequest.new('Products', 5)
        .withPageSize(20)
        .withPageIndex(1)
        .withAuthToken(token)
);
const entries = history.value.Data;
```

- Requires `get` access to the entity — only properties granted by that security are included in each snapshot
- Default page size is 10 and the maximum 1000 (a size below 1 or above 1000 becomes 1000); always set `.WithPageSize()` / `.withPageSize()` explicitly for predictable paging
- **Deleting a record captures a final snapshot.** When change tracking is enabled, deleting a record stores one last snapshot of its values before removal (in addition to retaining prior history)
- **History is resolved through the live record.** Once the record is deleted, this endpoint returns a `NOT_FOUND` error — the underlying history rows (including the final pre-deletion snapshot) are retained in the database but are no longer readable here (an admin can purge them from the Portal). The same `NOT_FOUND` is returned when the record exists but the caller's rule does not cover it (for example 'own records only'). An entity that never had change tracking returns an empty list (`Total` 0)

---

### Filtering

Use the SDK filter builders — never construct raw JSON filter strings.

**.NET:**
```csharp
// Simple condition
var simpleFilter = new FilterItem("Price", FilterOperator.less, 100);

// Compound filter (AND/OR)
var andFilter = new FilterItem(FilterLogic.AND, new List<FilterItem>
{
    new FilterItem("Price", FilterOperator.greaterorequal, 10),
    new FilterItem("InStock", FilterOperator.equal, true)
});

// Nested filter: Category = 'Electronics' AND (Price < 50 OR OnSale = true)
var nestedFilter = new FilterItem(FilterLogic.AND, new List<FilterItem>
{
    new FilterItem("Category", FilterOperator.equal, "Electronics"),
    new FilterItem(FilterLogic.OR, new List<FilterItem>
    {
        new FilterItem("Price", FilterOperator.less, 50),
        new FilterItem("OnSale", FilterOperator.equal, true)
    })
});
```

**JavaScript:**
```javascript
// Simple condition
const simpleFilter = FilterItem.condition('Price', FilterOperator.less, 100);

// Compound filter (AND/OR)
const andFilter = FilterItem.and(
    FilterItem.condition('Price', FilterOperator.greaterorequal, 10),
    FilterItem.condition('InStock', FilterOperator.equal, true)
);

// Nested filter
const nestedFilter = FilterItem.and(
    FilterItem.condition('Category', FilterOperator.equal, 'Electronics'),
    FilterItem.or(
        FilterItem.condition('Price', FilterOperator.less, 50),
        FilterItem.condition('OnSale', FilterOperator.equal, true)
    )
);
```

**Available operators:** `equal`, `notequal`, `greater`, `greaterorequal`, `less`, `lessorequal`, `startswith`, `endswith`, `contains`, `notcontains`

Always use the `FilterOperator` enum — never pass raw operator strings.

**Matching a set of IDs (`IN`):** there is no `IN` operator. On a **numeric** property, `contains` / `notcontains` with a comma-joined value compiles to SQL `IN (...)` / `NOT IN (...)`. This is the established Apilane "IN" idiom — build the value with `string.Join(",", ids)`:

```csharp
// .NET — WHERE ID IN (3, 7, 42)
var filter = new FilterItem("ID", FilterOperator.contains, string.Join(",", ids));
```
```javascript
// JavaScript — WHERE ID IN (3, 7, 42)
const filter = FilterItem.condition('ID', FilterOperator.contains, ids.join(','));
```

On a **string** property `contains` is a substring match (`LIKE`), not set membership. String values are matched literally: `%`, `_`, `[` and `\` in the value are not wildcards or escapes, so never escape them.

**Filter value rules:** `equal` / `notequal` with a `null` value test for NULL / NOT NULL (`FilterItem.condition('Price', FilterOperator.equal, null)`); no other operator accepts null. Boolean properties accept only `equal` and `notequal`; Date properties accept `equal`, `notequal`, `greater`, `greaterorequal`, `less` and `lessorequal`, with a Unix timestamp or the date formats listed under Data Creation; Number properties do not accept `startswith` / `endswith`. String matching ignores case (on SQLite only for A–Z; elsewhere the database collation decides, case-insensitive by default). Property names in a filter are not case-sensitive, but an unknown property, or one the caller may not read, fails the whole call with `INVALID_FILTER_PARAMETER`. The filter is part of the URL: keep an ID list for the IN idiom to a few hundred IDs per call (the request line is limited to 8 KB) and split longer lists.

---

### Users and Sign-In

Users are the `Users` entity, and they are created with the account calls, never with the data calls.

```csharp
// .NET
await apilane.AccountRegisterAsync(AccountRegisterRequest.New(
    new RegisterItem { Email = email, Username = username, Password = password }));   // the answer is the new user ID

var login = await apilane.AccountLoginAsync<ApiUser>(
    AccountLoginRequest.New(new LoginItem { Email = email, Password = password }));
// login.Value: AuthToken, AuthTokenID, User
```
```javascript
// JavaScript
await apilane.accountRegister(
    AccountRegisterRequest.new({ Email: email, Username: username, Password: password }));
const login = await apilane.accountLogin(
    AccountLoginRequest.new({ Email: email, Password: password }));
// login.value: AuthToken, AuthTokenID, User
```

- Register works only while the application setting 'Allow new users to register' is on, otherwise it answers `ERROR`. The body needs `Email`, `Username` and `Password` and may carry the other properties of `Users` (in .NET pass your own class that implements `IRegisterItem`: its extra properties are sent too). `Roles` cannot be set at registration
- Registration sends the confirmation email when that email template is active, which needs the application's SMTP settings. Sign-in with an unconfirmed email fails with `UNCONFIRMED_EMAIL` unless 'Allow users with an unconfirmed email to sign in' is on. A new confirmation email is requested with `GET /api/Email/RequestConfirmation?email=...`: one request per address per 5 minutes, and it answers OK even for an unknown address. The SDKs only build that address: `UrlFor_Email_RequestConfirmation(email)` / `urlForEmailRequestConfirmation(email)`
- Sign-in takes `Email` or `Username` plus `Password` (`Email` wins when both are sent). A wrong password and an unknown user both answer `ERROR`, 'Invalid login attempt'
- Forgot password: send the user to the page the application hosts (`UrlFor_Account_Manage_ForgotPassword()` / `urlForAccountManageForgotPassword()`) or call `GET /api/Email/ForgotPassword?email=...` (`UrlFor_Email_ForgotPassword(email)` / `urlForEmailForgotPassword(email)`, the page and the call share one limit of one request per address per 5 minutes). Both need the SMTP settings
- Sign out with `AccountLogoutAsync(AccountLogoutRequest.New(false).WithAuthToken(token))` / `accountLogout(AccountLogoutRequest.new().withAuthToken(token))`; pass `true` (`AccountLogoutRequest.New(true)` / `AccountLogoutRequest.new(true)`) to sign the user out of every client

---

### Auth Tokens

- Pass auth tokens via `.WithAuthToken(token)` / `.withAuthToken(token)` on each request, resolving the token from session/context/storage at call time
- For a stronger option that never sends the token, sign requests with `.WithSigning(keyId, token)` (see **Signed Requests** below)
- NEVER hardcode auth tokens — resolve them from session/context/storage at runtime
- A request without a token, or with an unknown, malformed or expired one, runs as the anonymous role: there is no 'token expired' error. A call that worked before and now answers `UNAUTHORIZED` means: sign in again
- The lifetime is the application setting `AuthTokenExpireMinutes` (Portal, Security: 'Auth token lifetime (minutes)'). It slides: a token that is used keeps living, so a user who calls the API regularly stays signed in
- `accountRenewAuthToken` / `AccountRenewAuthTokenAsync` is only needed to rotate the token: it answers like login (`AuthToken`, `AuthTokenID`, `User`) and the old token and its id stop working at once, so store both new values
- With 'Allow only one sign-in at a time' (`ForceSingleLogin`) a new sign-in ends the user's other sessions

---

### Signed Requests (don't send the token over the wire)

For a stronger scheme than bearer, **sign** each request instead of transmitting the token. The token (the secret) never leaves the client; the request carries only an HMAC signature plus a timestamp. This survives request logging / TLS termination, and a captured request is only usable inside a short time window.

Login returns the token **id** (`AuthTokenID`) alongside the token — use the id as the public key id and the token as the signing secret.

**.NET** — `.WithSigning(keyId, secret)` on any request. It cannot be combined with `.WithAuthToken` on the same request: whichever is called second throws (`InvalidOperationException`; in JS an `Error`):
```csharp
var login = (await apilane.AccountLoginAsync<ApiUser>(
    AccountLoginRequest.New(new LoginItem { Email = email, Password = password }))).Value;

var result = await apilane.GetDataAsync<Product>(
    DataGetListRequest.New("Products")
        .WithSigning(login.AuthTokenID, login.AuthToken)
        .WithPageSize(25));
```
```javascript
// JavaScript — .withSigning(keyId, secret)
const login = (await apilane.accountLogin(
    AccountLoginRequest.new({ Email: email, Password: password }))).value;

const result = await apilane.getData(
    DataGetListRequest.new('Products')
        .withSigning(login.AuthTokenID, login.AuthToken)
        .withPageSize(25));
```

- The client and server clocks must be in sync: the server accepts a timestamp within ±120 seconds of its own time (fixed, not configurable). A request that fails verification (wrong secret, stale timestamp, unknown key id) is not reported as a signature error: it runs as an anonymous call, so the symptom is `UNAUTHORIZED` (or, where the anonymous role may read, the data an anonymous caller sees). Only `Logout` answers `UNAUTHORIZED` for it
- Sign each request fresh (the timestamp is part of the signature) — don't cache and resend a signed request
- File **uploads** cannot be signed: `PostFileAsync` / `postFile` throw when the request carries `.WithSigning`, so use `.WithAuthToken` for them. `GetAllDataAsync` / `getAllData` cannot be signed either: page with `DataGetListRequest` instead
- After `AccountRenewAuthToken` use the new `AuthTokenID` together with the new `AuthToken`: the old pair no longer works
- Still resolve the token/keyId from session/storage at runtime — never hardcode them

---

### Transactions

Use transactions when multiple operations must succeed or fail atomically: if one fails, all are rolled back. A transaction has a time limit of 20 seconds, the role rules apply to every operation, and the `Files` entity is refused.

**Two types:**
- **Grouped** (`transactionData` / `TransactionDataAsync`) — batches all Posts, then Puts, then Deletes. Simpler but no cross-referencing. The answer is `{ Post: [new ids], Put: affected count, Delete: [deleted ids] }`. In .NET the body is an `InTransactionData` with lists of `InTransactionData.InTransactionSet` (`Entity`, `Data`) and `InTransactionData.InTransactionDelete` (`Entity`, `Ids`): there is no builder for it.
- **Ordered** (`transactionOperations` / `TransactionOperationsAsync`) — executes operations sequentially with cross-referencing via `$ref`. Use when a later operation depends on the ID of a record created earlier. The answer is `{ Results: [...] }` in the same order, each with `Action`, `Entity` and `Created` (Post), `Affected` (Put), `Deleted` (Delete) or `CustomResult` (Custom).

**.NET** — ordered with cross-references:
```csharp
var tx = new TransactionBuilder()
    .Post("Orders", new { CustomerName = "John" }, out var orderRef)
    .Post("OrderItems", new { OrderId = orderRef.Id(), Product = "Widget" })
    .Put("Orders", new { ID = orderRef.Id(), Status = "Active" })
    .Build();

var result = await apilane.TransactionOperationsAsync(
    DataTransactionOperationsRequest.New().WithAuthToken(token), tx);
```

**JavaScript** — ordered with cross-references:
```javascript
const builder = new TransactionBuilder();
const orderRef = builder.postWithRef('Orders', { CustomerName: 'John' });
builder
    .post('OrderItems', { OrderId: orderRef.id(), Product: 'Widget' })
    .put('Orders', { ID: orderRef.id(), Status: 'Active' });

const result = await apilane.transactionOperations(
    DataTransactionOperationsRequest.new().withAuthToken(token),
    builder.build()
);
```

- Cross-reference with `orderRef.Id()` (.NET) or `orderRef.id()` (JS) — never assume an ID before creation. The placeholder (`$ref:auto_0`) works only inside the data of a Post, Put or Custom operation, and only for an earlier Post: it becomes the first ID that Post created. It is NOT resolved in the `Ids` of a Delete. A text value of your own that starts with `$ref:` is taken as a reference, so never store such text
- The update rule applies inside transactions too — send only `ID` + changed properties in Put operations
- Delete: `.Delete("OldOrders", "1,2,3")` / `.delete('OldOrders', '1,2,3')`
- Transactions can also call Custom endpoints via `.Custom(endpointName, data)` / `.custom(endpointName, data)` — useful for server-side logic that depends on records created in the same transaction. The values in `data` are the endpoint's whole-number parameters and may be `$ref` placeholders

---

### Files

Files are a system entity with dedicated endpoints — do NOT use the regular data endpoints for files (the Data endpoints and transactions refuse the `Files` entity with `ERROR`).

```csharp
// .NET — upload, returns the new file ID
byte[] fileBytes = File.ReadAllBytes("photo.jpg");
var upload = await apilane.PostFileAsync(
    FilePostRequest.New().WithFileName("photo.jpg").WithAuthToken(token),
    fileBytes);
long? fileId = upload.Value;
```
```javascript
// JavaScript — upload (browser); the data is a Blob, ArrayBuffer or Uint8Array
const file = document.querySelector('input[type="file"]').files[0];
const upload = await apilane.postFile(
    FilePostRequest.new().withFileName('photo.jpg').withAuthToken(token),
    file
);
const fileId = upload.value;
```

- The server gives every file a new random `UID` (a GUID) and returns the file ID. `.WithFileUID()` and the JavaScript-only `.withPublicFlag()` are sent but ignored: do not use them to choose an identifier or to make a file public
- Who may upload, list, read or delete files is decided by the role rules of the `Files` entity (Portal: Security). A file is public when the role ANONYMOUS may `get` the `Files` entity
- An upload is limited by the application setting 'Maximum file size (KB)' (1 to 25600 KB), and some extensions are refused
- File records: `GetFilesAsync<T>(FileGetListRequest.New())` / `getFiles`, `GetFileByIdAsync<T>(FileGetByIdRequest.New(id))` / `getFileById`, `DeleteFileAsync(FileDeleteRequest.New().AddIdToDelete(id))` / `deleteFile`. They page like data (default 20, maximum 1000)
- The SDKs do not download the content. Build the address with `FileDownloadRequest.New().WithFileID(id).GetUrl(serverUrl)` (JS: `FileDownloadRequest.new().withFileID(id).getUrl(apiUrl)`; `WithFileUID(uid)` also works) and add `&appToken={applicationToken}` and, when the role needs a sign-in, `&authToken={token}`. Those query parameters work in `<img src>` and links, where no header can be set

---

### Custom Endpoints

Custom endpoints execute server-side SQL queries with parameterized inputs.

```csharp
// .NET
var result = await apilane.GetCustomEndpointAsync<List<UserSummary>>(
    CustomEndpointRequest.New("GetUserSummary")
        .WithParameter("UserID", 42)
        .WithAuthToken(token));
```
```javascript
// JavaScript
const result = await apilane.getCustomEndpoint(
    CustomEndpointRequest.new('GetUserSummary')
        .withParameter('UserID', 42)
        .withAuthToken(token)
);
```

- Endpoint names are letters only (`GetUserSummary`); parameter names are letters and `_` only. The endpoint name in the call is not case-sensitive, the parameter names are
- Parameters are `{Name}` placeholders in the SQL and take big integer (long) values only — no strings or other types. A parameter that is not sent, or is not a number, becomes `null` in the SQL
- `{Owner}` is not a parameter: it is replaced by the ID of the signed-in caller. For an anonymous caller it stays the text `{Owner}` and the query fails with `ERROR`
- All custom endpoints are GET requests. The answer is an array of result sets, each an array of rows (`[[row, row], [row]]`). `GetCustomEndpointAsync<T>` returns the first set (`T` is a list, or one row type), `<T1, T2, ...>` up to five. The row keys are the column labels the database returns: alias columns so that they equal your model's property names exactly
- The endpoint name must match a custom endpoint configured in the Apilane Portal. The role rules of the endpoint (Portal: Security) decide who may call it. The SQL itself is not restricted by the entity or property rules, so it returns whatever it selects: use `{Owner}` or the needed IDs to limit the rows to what the caller may see
- A call has a time limit of 20 seconds

---

### Stats & Aggregation

```csharp
// .NET — aggregate (top of the file: using static Apilane.Net.Request.StatsAggregateRequest;
// DataAggregates is nested in StatsAggregateRequest)
var stats = await apilane.GetStatsAggregateAsync<List<OrderStats>>(
    StatsAggregateRequest.New("Orders")
        .WithProperty("Total", DataAggregates.Sum)
        .WithProperty("ID", DataAggregates.Count)
        .WithGroupBy("Status")
        .WithPageSize(100)
        .WithAuthToken(token));

// one row per group: { "Status": "Open", "Total_sum": 120.5, "ID_count": 3 }
public class OrderStats
{
    public string? Status { get; set; }
    public decimal? Total_sum { get; set; }
    public long ID_count { get; set; }
}
```
```javascript
// JavaScript — aggregate (DataAggregates is imported from './apilane.js')
const stats = await apilane.getStatsAggregate(
    StatsAggregateRequest.new('Orders')
        .withProperty('Total', DataAggregates.Sum)
        .withProperty('ID', DataAggregates.Count)
        .withGroupBy('Status')
        .withPageSize(100)
        .withAuthToken(token)
);
// stats.value is the array of rows
```

**Available aggregates:** `Count`, `Min`, `Max`, `Sum`, `Avg`. The answer is an array with one row per group. The result columns are named `{Property}_{aggregate in lower case}` (`Total_sum`, `ID_count`) and a group column keeps the property name. `WithGroupBy` takes a comma separated list; a date property accepts a part: `Created.year`, `.month`, `.day`, `.hour`, `.minute`, `.second` (column `Created_year`). The rows come back ordered by the aggregates, largest first; `WithSort(true)` is meant to reverse that but does not (the server reads only `asc` and `desc`, the SDKs send `true` / `false`), so sort the rows yourself. The default page size is 20 and the maximum 1000.

Reports in the Portal are saved Stats/Aggregate calls: the 'View API endpoint' menu of a report panel shows the call behind each series, so you can read the same numbers from code.

For distinct values use `GetStatsDistinctAsync<T>(StatsDistinctRequest.New("Orders", "Status"))` / `getStatsDistinct(StatsDistinctRequest.new('Orders', 'Status'))`; the answer is an array of rows such as `{ "Status": "Open" }`.

---

### Request Builder Pattern

All requests use a fluent builder pattern — do not construct request objects manually.

| .NET Pattern | JavaScript Pattern |
|---|---|
| `RequestType.New("Entity").WithX().WithY()` | `RequestType.new('Entity').withX().withY()` |
| `.WithAuthToken(token)` | `.withAuthToken(token)` |
| `.WithFilter(filter)` | `.withFilter(filter)` |
| `.WithSort(sort)` | `.withSort(sort)` |
| `.WithPageSize(n)` | `.withPageSize(n)` |
| `.WithPageIndex(n)` | `.withPageIndex(n)` |
| `.WithProperties("A", "B")` | `.withProperties('A', 'B')` |
| `.OnErrorThrowException(true)` | `.onErrorThrowException()` |

---

### Request Cancellation

**JavaScript** — pass an `AbortSignal` as the last argument of any service method (`getData(request, signal)`, `postData(request, data, signal)`). An aborted call rejects with an `AbortError`; it does not return an `ApilaneResult`:
```javascript
const controller = new AbortController();
setTimeout(() => controller.abort(), 5000);
const result = await apilane.getData(request, controller.signal);
```

**.NET** — every async method ends with a `CancellationToken` (the data GET methods have a `JsonSerializerOptions?` before it: `GetDataAsync<Product>(request, null, cancellationToken)`). Cancelling throws `OperationCanceledException`.

---

### Application Schema

Retrieve entity definitions, property types, and app settings at runtime. The caller's role needs a `get` rule on 'The application schema' (Portal: Security), otherwise the answer is `UNAUTHORIZED`:
```csharp
// .NET
var schema = await apilane.GetApplicationSchemaAsync(
    DataGetSchemaRequest.New().WithAuthToken(token));
var entities = schema.Value.Entities;
```
```javascript
// JavaScript
const schema = await apilane.getApplicationSchema(
    DataGetSchemaRequest.new().withAuthToken(token)
);
const entities = schema.value.Entities;
```

The answer holds the settings (`AuthTokenExpireMinutes`, `AllowUserRegister`, `AllowLoginUnconfirmedEmail`, `ForceSingleLogin`, `MaxAllowedFileSizeInKB`) and `Entities`: `Name`, `IsSystem`, `IsReadOnly`, `ChangeTracking`, and `Properties` (`Name`, `Type` = `String`, `Number`, `Boolean` or `Date`, `Required`, `Minimum`, `Maximum`, `DecimalPlaces`, `Encrypted`, `ValidationRegex`, `IsPrimaryKey`, `IsSystem`). It does not tell which properties are unique or foreign keys; in .NET ignore `Online`, `IsUnique`, `FK_Entity` and `FK_Entity_FirstStringProperty` of the model, the server never sends them. Useful for dynamic form generation, validation, or introspection.

---

### When to Use Custom Endpoints

If a feature requires **multiple sequential API calls** to assemble a result, consider moving that logic into a **custom endpoint** on the Apilane Portal instead. Custom endpoints run server-side SQL directly against the database, returning exactly the data you need in a single round-trip.

**Signs you need a custom endpoint:**
- You're making 3+ API calls in sequence where each depends on the previous result
- You're fetching a list and then looping to fetch related records one by one (N+1 pattern)
- You need a JOIN across entities that the standard Get endpoint can't express
- You need aggregated/computed data that combines multiple entities
- A dashboard or report needs data from several entities in one response
- You're filtering by a subquery (e.g., "get orders where the customer's country is X")

**Example — before (multiple client-side calls):**
```javascript
// ❌ Bad — 3 round-trips, N+1 on order items
const orders = await apilane.getData(
    DataGetListRequest.new('Orders')
        .withFilter(FilterItem.condition('CustomerID', FilterOperator.equal, customerId))
        .withAuthToken(token)
);
for (const order of orders.value.Data) {
    const items = await apilane.getData(
        DataGetListRequest.new('OrderItems')
            .withFilter(FilterItem.condition('OrderID', FilterOperator.equal, order.ID))
            .withAuthToken(token)
    );
    order.Items = items.value.Data;
}
const customer = await apilane.getDataById(
    DataGetByIdRequest.new('Users', customerId).withAuthToken(token)
);
```

**Example — after (single custom endpoint):**

Create a custom endpoint `GetCustomerOrderDetails` in the Portal with:
```sql
SELECT [ID], [Email], [Username] FROM [Users] WHERE [ID] = {CustomerID};
SELECT o.[ID], o.[Created], o.[Status], o.[Total]
    FROM [Orders] o WHERE o.[CustomerID] = {CustomerID};
SELECT oi.[ID], oi.[OrderID], oi.[Product], oi.[Qty], oi.[Price]
    FROM [OrderItems] oi
    INNER JOIN [Orders] o ON oi.[OrderID] = o.[ID]
    WHERE o.[CustomerID] = {CustomerID}
```

Then call it with a single request:
```csharp
// .NET — single round-trip, typed multi-result
var result = await apilane
    .GetCustomEndpointAsync<List<Customer>, List<Order>, List<OrderItem>>(
        CustomEndpointRequest.New("GetCustomerOrderDetails")
            .WithParameter("CustomerID", customerId)
            .WithAuthToken(token));

if (result.HasError(out var error))
{
    throw new Exception(error.Message);
}

var (customers, orders, items) = result.Value;
```
```javascript
// JavaScript — single round-trip, raw multi-result
const result = await apilane.getCustomEndpoint(
    CustomEndpointRequest.new('GetCustomerOrderDetails')
        .withParameter('CustomerID', customerId)
        .withAuthToken(token)
);
// result.value is a nested array: [[customer], [orders...], [items...]]
```

**Benefits:**
- Single HTTP round-trip instead of N+1 calls
- JOINs and subqueries run inside the database — far more efficient than client-side assembly
- Reduces client-side complexity and error handling surface
- Server-side SQL can leverage database indexes and query optimization
- Multiple result sets in one response via semicolon-separated SQL statements

**Constraints to keep in mind:**
- The parameter, `{Owner}`, result and time-limit rules are the ones listed under **Custom Endpoints** above
- Security (role-based access) is configured per-endpoint in the Portal
- Write the SQL for the storage provider of your application (SQLite, SQL Server, MySQL or PostgreSQL): the text is sent to the database as written. Quote identifiers the way that database does: `[Users]` on SQLite and SQL Server, `` `Users` `` on MySQL, `"Users"` on PostgreSQL (case-sensitive). The example above is in the SQL Server / SQLite form

---

### Without the SDK

For another language, or for what the SDKs do not wrap (`PUT /api/Account/ChangePassword`, `GET /api/Email/RequestConfirmation`, downloading a file), call the REST API directly. Send the application token in the `x-application-token` header (or the `appToken` query parameter) and the user's auth token as `Authorization: Bearer {token}` (or the `authToken` query parameter, for links where no header can be set). Every error answers `{ "Code", "Message", "Property", "Entity" }`. The endpoints are listed in the REST API Reference (https://docs.apilane.com/api_reference/) and in the Swagger UI of the API server (`{server}/swagger`).

---

### Common Pitfalls

| Pitfall | Consequence | Fix |
|---|---|---|
| Sending full typed object on update | Overwrites unset fields with defaults | Use anonymous/plain objects with only changed properties |
| Ignoring error results | Silent failures, data inconsistency | Always check `HasError`/`isError` before accessing `.Value`/`.value` |
| Not paging | Only the first 20 records come back and the rest are silently missing | Set `.WithPageSize()` (max 1000), page with `.WithPageIndex()`, use `GetDataTotalAsync` when the total is needed |
| Selecting all properties | Excess bandwidth | Use `.WithProperties()` with only needed columns |
| Filtering in memory | Fetches too much data from DB | Use `.WithFilter()` for server-side filtering |
| Including system props in POST body | Silently ignored, the server sets them | Never send `ID`, `Owner`, `Created` on create |
| The same property twice in a POST/PUT body with different case (`Price` and `price`) | The body is refused with `VALIDATION` naming the property (a single wrong-case name still works) | Copy names exactly from the schema (`ID`, `Price`) and send each property once |
| Hardcoding auth tokens | Security risk, breaks on expiry | Resolve from session/context/storage at runtime |
| Treating `UNAUTHORIZED` as 'wrong password' | It means the role has no rule, or the token is unknown or expired | Check the role rules, then sign in again |
| Branching on HTTP 404 / 429 | Never happens: every error except `UNAUTHORIZED` is HTTP 400 | Branch on `Code` |
| Using data endpoints for files | Errors | Use dedicated file endpoints (`PostFileAsync`/`postFile`, etc.) |
| Using `WithPublicFlag` / `WithFileUID` | Ignored by the server | Use the role rules of `Files` |
| Creating many records with one array POST | Not atomic | Use `TransactionOperationsAsync` |
| Assuming IDs before creation | Wrong references | Use `TransactionBuilder` cross-references (`$ref`) |
| Using `$ref` in a Delete | Not resolved | Delete by the known IDs |
| Trusting a custom endpoint to hide data | The SQL ignores entity rules | Filter on `{Owner}` or the needed IDs in the SQL |
| Requesting total count on every page | Double DB queries | Only request on first page or when pager needs it |
````

## Managing an instance with an agent key

The block above is about the data of an application. A script or an AI agent can also manage the applications themselves (entities, properties, security, custom endpoints, reports) through the Portal's management API, with an agent key. How to give an agent its key, what it may and may not do, and what to put in its instructions are on the page about [managing an instance with an agent key](ai_agent_portal.md).
