# AI Agent Guidelines

If you use AI coding assistants (Cursor, GitHub Copilot, Claude Code, etc.), append the following to your project's `AGENTS.md` file to help them use the Apilane SDK correctly.

````markdown
## Apilane SDK Best Practices

This project uses the Apilane SDK for backend operations. Apilane is a backend-as-a-service that provides data storage, user authentication, file management, and more via a REST API. The SDK wraps this API with a type-safe builder pattern.

### SDK Variants

| SDK | Package | Import |
|---|---|---|
| .NET | `Apilane.Net` (NuGet) | `using Apilane.Net;` |
| JavaScript | `apilane.js` (ESM file) | `import { createApilaneService } from './apilane.js';` |

Both SDKs have identical API surface and behavior. All rules below apply to both unless noted.

---

### Setup

**.NET:**
```csharp
builder.Services.UseApilane(serverUrl, applicationToken);
// Inject IApilaneService via constructor — never instantiate directly
// For multi-app: UseApilane(..., serviceKey: "app1")
```

**JavaScript:**
```javascript
const apilane = createApilaneService({
    apiUrl: 'https://my.api.server',
    appToken: 'your-app-token',
    authTokenProvider: async () => getStoredToken(), // optional global provider
});
```

---

### Error Handling (CRITICAL)

All SDK methods return a result type — NEVER ignore return values or assume success.

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

// Alternative: throw automatically on error
var response = await apilane.GetDataAsync<Product>(
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
const result = await apilane.getData(
    DataGetListRequest.new('Products').onErrorThrowException());
```

**Common error codes:** `ERROR`, `UNAUTHORIZED`, `NOT_FOUND`, `REQUIRED`, `UNCONFIRMED_EMAIL`, `UNIQUE_CONSTRAINT_VIOLATION`, `FOREIGN_KEY_CONSTRAINT_VIOLATION`, `RATE_LIMIT_EXCEEDED`, `VALIDATION`, `EMPTY_BODY`, `NO_ID_PROVIDED`

---

### Data Updates (CRITICAL — Most Common Mistake)

When updating records, send ONLY the `ID` and the properties being changed. NEVER send a full typed object — unset properties will overwrite existing values with their type defaults (`null`, `0`, `false`, empty string).

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

---

### Data Creation

- Use anonymous objects (.NET) or plain objects (JS) for POST operations
- System properties (`ID`, `Owner`, `Created`, `Created_Date`) are set automatically by the server — do NOT include them
- The response returns an array of created IDs

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

- ALWAYS use `.WithPageSize()` / `.withPageSize()` — never fetch unbounded result sets
- Use `.WithProperties()` / `.withProperties()` to select only needed columns — reduces bandwidth
- Use `.WithFilter()` / `.withFilter()` to filter server-side — never fetch all and filter in memory
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

---

### Filtering

Use the SDK filter builders — never construct raw JSON filter strings.

**.NET:**
```csharp
// Simple condition
var filter = new FilterItem("Price", FilterOperator.less, 100);

// Compound filter (AND/OR)
var filter = new FilterItem(FilterLogic.AND, new List<FilterItem>
{
    new FilterItem("Price", FilterOperator.greaterorequal, 10),
    new FilterItem("InStock", FilterOperator.equal, true)
});

// Nested filter: Category = 'Electronics' AND (Price < 50 OR OnSale = true)
var filter = new FilterItem(FilterLogic.AND, new List<FilterItem>
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
const filter = FilterItem.condition('Price', FilterOperator.less, 100);

// Compound filter (AND/OR)
const filter = FilterItem.and(
    FilterItem.condition('Price', FilterOperator.greaterorequal, 10),
    FilterItem.condition('InStock', FilterOperator.equal, true)
);

// Nested filter
const filter = FilterItem.and(
    FilterItem.condition('Category', FilterOperator.equal, 'Electronics'),
    FilterItem.or(
        FilterItem.condition('Price', FilterOperator.less, 50),
        FilterItem.condition('OnSale', FilterOperator.equal, true)
    )
);
```

**Available operators:** `equal`, `notequal`, `greater`, `greaterorequal`, `less`, `lessorequal`, `startswith`, `endswith`, `contains`, `notcontains`

Always use the `FilterOperator` enum — never pass raw operator strings.

---

### Auth Tokens

- Pass auth tokens via `.WithAuthToken(token)` / `.withAuthToken(token)` on each request
- Or register a global auth token provider to avoid passing it manually every time:

**.NET** — implement `IApilaneAuthTokenProvider`:
```csharp
public class MyAuthProvider : IApilaneAuthTokenProvider
{
    public Task<string?> GetAuthTokenAsync()
        => Task.FromResult(GetTokenFromSession());
}
// Register before UseApilane
builder.Services.AddSingleton<IApilaneAuthTokenProvider, MyAuthProvider>();
```

**JavaScript** — pass `authTokenProvider` to `createApilaneService`:
```javascript
const apilane = createApilaneService({
    apiUrl, appToken,
    authTokenProvider: async () => localStorage.getItem('authToken'),
});
```

- NEVER hardcode auth tokens — resolve them from session/context/storage at runtime
- Auth tokens expire after the configured `AuthTokenExpireMinutes` — handle token renewal or re-login
- Use `accountRenewAuthToken` / `AccountRenewAuthTokenAsync` to get a fresh token before expiry

---

### Transactions

Use transactions when multiple operations must succeed or fail atomically.

**Two types:**
- **Grouped** (`transactionData` / `TransactionDataAsync`) — batches all Posts, then Puts, then Deletes. Simpler but no cross-referencing.
- **Ordered** (`transactionOperations` / `TransactionOperationsAsync`) — executes operations sequentially with cross-referencing via `$ref`. Use when a later operation depends on the ID of a record created earlier.

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

- Cross-reference with `orderRef.Id()` (.NET) or `orderRef.id()` (JS) — never assume an ID before creation
- The update rule applies inside transactions too — send only `ID` + changed properties in Put operations
- Transactions can also call Custom endpoints via `.Custom(endpointName, data)` — useful for server-side logic that depends on records created in the same transaction

---

### Files

Files are a system entity with dedicated endpoints — do NOT use the regular data endpoints for files.

```csharp
// .NET — upload
byte[] fileBytes = File.ReadAllBytes("photo.jpg");
await apilane.PostFileAsync(
    FilePostRequest.New().WithFileName("photo.jpg").WithPublicFlag(false).WithAuthToken(token),
    fileBytes);
```
```javascript
// JavaScript — upload (browser)
const file = document.querySelector('input[type="file"]').files[0];
await apilane.postFile(
    FilePostRequest.new().withFileName('photo.jpg').withPublicFlag(false).withAuthToken(token),
    file
);
```

- Set `.WithPublicFlag(true)` / `.withPublicFlag(true)` only for publicly accessible files
- Use `.WithFileUID(uid)` / `.withFileUID(uid)` for stable external identifiers (e.g., `user-avatar-123`)
- List, get by ID, and delete files using `FileGetListRequest`, `FileGetByIdRequest`, `FileDeleteRequest` respectively

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

- Parameters are big integer (long) type only — no strings or other types
- All custom endpoints are GET requests
- The endpoint name must match a custom endpoint configured in the Apilane Portal

---

### Stats & Aggregation

```csharp
// .NET — aggregate
var stats = await apilane.GetStatsAggregateAsync<MyResult>(
    StatsAggregateRequest.New("Orders")
        .WithProperty("Total", DataAggregates.Sum)
        .WithProperty("ID", DataAggregates.Count)
        .WithGroupBy("Status")
        .WithAuthToken(token));
```
```javascript
// JavaScript — aggregate
const stats = await apilane.getStatsAggregate(
    StatsAggregateRequest.new('Orders')
        .withProperty('Total', DataAggregates.Sum)
        .withProperty('ID', DataAggregates.Count)
        .withGroupBy('Status')
        .withAuthToken(token)
);
```

**Available aggregates:** `Count`, `Min`, `Max`, `Sum`, `Avg`

For distinct values use `GetStatsDistinctAsync` / `getStatsDistinct`.

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

### Request Cancellation (JavaScript only)

Use `AbortController` to cancel in-flight requests:
```javascript
const controller = new AbortController();
setTimeout(() => controller.abort(), 5000);
const result = await apilane.getData(request, controller.signal);
```

---

### Application Schema

Retrieve entity definitions, property types, and app configuration at runtime:
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

Useful for dynamic form generation, validation, or introspection.

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
var (customers, orders, items) = await apilane
    .GetCustomEndpointAsync<List<Customer>, List<Order>, List<OrderItem>>(
        CustomEndpointRequest.New("GetCustomerOrderDetails")
            .WithParameter("CustomerID", customerId)
            .WithAuthToken(token));
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
- Custom endpoint parameters are **big integer (long) only** — no string parameters
- All custom endpoints are **GET requests**
- Security (role-based access) is configured per-endpoint in the Portal
- Write the SQL for your specific storage provider (SQLite, SQL Server, or MySQL)

---

### Common Pitfalls

| Pitfall | Consequence | Fix |
|---|---|---|
| Sending full typed object on update | Overwrites unset fields with defaults | Use anonymous/plain objects with only changed properties |
| Ignoring error results | Silent failures, data inconsistency | Always check `HasError`/`isError` before accessing `.Value`/`.value` |
| Fetching all data without pagination | Timeouts, OOM, slow responses | Always use `.WithPageSize()` |
| Selecting all properties | Excess bandwidth | Use `.WithProperties()` with only needed columns |
| Filtering in memory | Fetches too much data from DB | Use `.WithFilter()` for server-side filtering |
| Including system props in POST body | Ignored or errors | Never send `ID`, `Owner`, `Created`, `Created_Date` on create |
| Hardcoding auth tokens | Security risk, breaks on expiry | Use auth token provider or resolve from session |
| Using data endpoints for files | Errors | Use dedicated file endpoints (`PostFileAsync`/`postFile`, etc.) |
| Assuming IDs before creation | Wrong references | Use `TransactionBuilder` cross-references (`$ref`) |
| Requesting total count on every page | Double DB queries | Only request on first page or when pager needs it |
````
