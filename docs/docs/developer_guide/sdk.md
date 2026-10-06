---
description: "Use Apilane from .NET with the Apilane.Net NuGet package: setup, accounts, data operations, filtering, transactions and files."
---

# SDK (.NET)

Apilane offers a .NET SDK that simplifies integration with the Apilane API. It provides a type-safe, builder-pattern interface for the account, data, transaction, file, stats, custom endpoint and schema calls. Changing a password and downloading a file are not wrapped: call [Change Password](../api_reference.md#change-password) and [Download File](../api_reference.md#download-file) over HTTP. The two email calls are available as URL builders, see [URL Helpers](#url-helpers).

[![NuGet](https://img.shields.io/nuget/v/Apilane.Net.svg?style=flat&label=Apilane.Net)](https://www.nuget.org/packages/Apilane.Net)

!!!info "JavaScript"
    The JavaScript SDK is a single file, `apilane.js`, with its own [README](https://github.com/raptisv/apilane/blob/main/sdk/Apilane.Js/README.md). The [AI agent guidelines](ai_agent_guidelines.md) show the calls in .NET and in JavaScript.

## Installation

Add the latest [Apilane.Net NuGet package](https://www.nuget.org/packages/Apilane.Net) to your project:

```bash
dotnet add package Apilane.Net
```

The package targets .NET Standard 2.0 and depends on `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http` and `System.Text.Json`, which NuGet adds for you.

## Setup

Register the Apilane services in your dependency injection container:

```csharp
string serverUrl = "https://my.api.server";
string applicationToken = "23d4444f-b56e-4c5a-98ed-ef251796a238";

builder.Services.UseApilane(serverUrl, applicationToken);
```

Then inject `IApilaneService` wherever you need it:

```csharp
private readonly IApilaneService _apilaneService;

public MyController(IApilaneService apilaneService)
{
    _apilaneService = apilaneService;
}
```

### Advanced Setup Options

The `UseApilane` method accepts an optional service key for registering multiple apps:

```csharp
builder.Services.UseApilane(
    serverUrl,
    applicationToken,
    serviceKey: "app1"   // Keyed registration for multi-app scenarios
);
```

### Providing the auth token

Pass the auth token per request via `.WithAuthToken(token)` on any request builder, resolving it
from your own session/context/storage at call time:

```csharp
var data = await _apilaneService.GetDataAsync<Product>(
    DataGetListRequest.New("Products").WithAuthToken(token));
```

Or sign requests with `.WithSigning(keyId, token)`, where `keyId` is the `AuthTokenID` and `token` the `AuthToken` returned at login, so the token is never sent at all (see [Security » Authenticating requests](security.md#authenticating-requests)). A request uses one of the two: calling both on the same request throws `InvalidOperationException`. Two calls cannot be signed: `PostFileAsync` (it throws) and `GetAllDataAsync`, whose `DataGetAllRequest` only has `WithAuthToken`.

### Keyed Services (Multi-App)

If your application connects to multiple Apilane applications, use keyed registrations:

```csharp
builder.Services.UseApilane(serverUrl, tokenApp1, serviceKey: "app1");
builder.Services.UseApilane(serverUrl, tokenApp2, serviceKey: "app2");

// Inject with key
public MyService([FromKeyedServices("app1")] IApilaneService app1Service) { ... }
```

## Error Handling

All SDK methods return an `Either<TSuccess, ApilaneError>` result. Check for errors before accessing the value:

```csharp
var response = await _apilaneService.GetDataAsync<Product>(
    DataGetListRequest.New("Products").WithAuthToken(authToken));

if (response.HasError(out var error))
{
    // Handle error — error.Message contains details
    Console.WriteLine($"Error: {error.Message}");
    return;
}

// Access the successful result
List<Product> products = response.Value.Data;
```

Alternatively, use `OnErrorThrowException` to throw on errors:

```csharp
var response = await _apilaneService.GetDataAsync<Product>(
    DataGetListRequest.New("Products")
        .WithAuthToken(authToken)
        .OnErrorThrowException(true));
```

## Account

### Login

```csharp
// Your own user class: it must implement IApiUser, the easiest way is to extend ApiUser
public class AppUser : ApiUser
{
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
}

var loginResponse = await _apilaneService.AccountLoginAsync<AppUser>(
    AccountLoginRequest.New(new LoginItem { Email = "user@example.com", Password = "secret" }));

if (!loginResponse.HasError(out var error))
{
    string authToken = loginResponse.Value.AuthToken;
    long authTokenId = loginResponse.Value.AuthTokenID; // needed only if you sign requests
    AppUser user = loginResponse.Value.User;
}
```

Set `Email` or `Username` on the `LoginItem`; when both are set, the email is used.

### Register

```csharp
var registerResponse = await _apilaneService.AccountRegisterAsync(
    AccountRegisterRequest.New(new RegisterItem
    {
        Email = "new@example.com",
        Username = "newuser",
        Password = "Pass123!"
    }));

long newUserId = registerResponse.Value;
```

Registering needs no auth token. To send custom properties of the `Users` entity, pass your own class that implements `IRegisterItem` (every public property of it is sent).

### Get User Data

```csharp
var userDataResponse = await _apilaneService.GetAccountUserDataAsync<AppUser>(
    AccountUserDataRequest.New()
        .WithAuthToken(authToken));
```

### Update User

```csharp
var updateResponse = await _apilaneService.AccountUpdateAsync<AppUser>(
    AccountUpdateRequest.New()
        .WithAuthToken(authToken),
    new { Firstname = "John", Lastname = "Doe" });
```

### Renew Auth Token

```csharp
// Same response as login. The old token (and its id) stop working.
var renewResponse = await _apilaneService.AccountRenewAuthTokenAsync<AppUser>(
    AccountRenewAuthTokenRequest.New()
        .WithAuthToken(authToken));

string newToken = renewResponse.Value.AuthToken;
long newTokenId = renewResponse.Value.AuthTokenID; // needed only if you sign requests
```

### Logout

```csharp
// false: only this token. true: every token of the user, on every client
var logoutResponse = await _apilaneService.AccountLogoutAsync(
    AccountLogoutRequest.New(logOutFromEverywhere: false)
        .WithAuthToken(authToken));

int tokensDeleted = logoutResponse.Value;
```

## Data Operations

Define your entity as a class extending `DataItem`:

```csharp
public class Product : Apilane.Net.Models.Data.DataItem
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public bool? InStock { get; set; }
}
```

`DataItem` provides the system properties `ID`, `Owner` (the id of the user who created the record, nullable) and `Created` (Unix time in milliseconds).

### Get Records (Paginated)

```csharp
var response = await _apilaneService.GetDataAsync<Product>(
    DataGetListRequest.New("Products")
        .WithAuthToken(authToken)
        .WithPageIndex(1)
        .WithPageSize(50)
        .WithProperties("ID", "Name", "Price"));

List<Product> products = response.Value.Data;
```

### Get Records with Total Count

```csharp
var response = await _apilaneService.GetDataTotalAsync<Product>(
    DataGetListRequest.New("Products")
        .WithAuthToken(authToken));

List<Product> products = response.Value.Data;
long total = response.Value.Total;
```

### Get All Records (No Paging)

```csharp
var allProducts = await _apilaneService.GetAllDataAsync<Product>(
    DataGetAllRequest.New("Products")
        .WithAuthToken(authToken));
```

### Get Record by ID

```csharp
var product = await _apilaneService.GetDataByIdAsync<Product>(
    DataGetByIdRequest.New("Products", id: 1)
        .WithAuthToken(authToken));
```

### Get Record History

Returns the change-tracking history for a single record (newest first), when the entity has change tracking enabled. Each entry holds the record's property values *before* the change, plus `History_Record_Created` (Unix ms) and `History_Record_Owner` (user id, nullable). Supply a model `T` that combines the entity's properties with those two columns. History is resolved through the live record — once the record is deleted the endpoint returns a `NOT_FOUND` error.

```csharp
var history = await _apilaneService.GetHistoryByIdAsync<ProductHistory>(
    DataGetHistoryByIdRequest.New("Products", id: 1)
        .WithPageSize(20)
        .WithPageIndex(1)
        .WithAuthToken(authToken));

var entries = history.Value.Data;   // each entry: Name, Price, ..., History_Record_Created, History_Record_Owner
int total = history.Value.Total;
```

### Create Records

```csharp
var response = await _apilaneService.PostDataAsync(
    DataPostRequest.New("Products")
        .WithAuthToken(authToken),
    new Product { Name = "Widget", Price = 9.99m, InStock = true });

long newId = response.Value.Single();
```

### Update Records

```csharp
var response = await _apilaneService.PutDataAsync(
    DataPutRequest.New("Products")
        .WithAuthToken(authToken),
    new { ID = 1, Price = 12.99 });

int affectedRecords = response.Value;
```

### Delete Records

```csharp
var response = await _apilaneService.DeleteDataAsync(
    DataDeleteRequest.New("Products")
        .WithAuthToken(authToken)
        .AddIdToDelete(1));

long[] deletedIds = response.Value;
```

## Filtering & Sorting

See [Filtering & Sorting](filtering_sorting.md) for the full filter/sort reference.

### Filter Builder

```csharp
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;

// Simple filter
var simpleFilter = new FilterItem("Price", FilterOperator.less, 100);

// Compound filter (AND/OR)
var compoundFilter = new FilterItem(FilterLogic.AND, new List<FilterItem>
{
    new FilterItem("Price", FilterOperator.greaterorequal, 10),
    new FilterItem("InStock", FilterOperator.equal, true)
});

// Nested filter
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

### Sort

```csharp
var sort = new SortItem { Property = "Price", Direction = "ASC" };
```

### Combined Example

```csharp
var response = await _apilaneService.GetDataAsync<Product>(
    DataGetListRequest.New("Products")
        .WithAuthToken(authToken)
        .WithFilter(new FilterItem("InStock", FilterOperator.equal, true))
        .WithSort(new SortItem { Property = "Price", Direction = "ASC" })
        .WithPageSize(25)
        .WithProperties("ID", "Name", "Price"));
```

## Transactions

See [Transactions](transactions.md) for the full reference.

### Grouped Transaction

```csharp
var data = new InTransactionData
{
    Post = new List<InTransactionData.InTransactionSet>
    {
        new() { Entity = "Orders", Data = new { CustomerName = "John" } }
    },
    Put = new List<InTransactionData.InTransactionSet>
    {
        new() { Entity = "Products", Data = new { ID = 5, Stock = 48 } }
    },
    Delete = new List<InTransactionData.InTransactionDelete>
    {
        new() { Entity = "TempRecords", Ids = "10,11" }
    }
};

var result = await _apilaneService.TransactionDataAsync(
    DataTransactionRequest.New().WithAuthToken(authToken), data);
```

### Ordered Transaction with Cross-Referencing

```csharp
using Apilane.Net.Models.Data;

var transaction = new TransactionBuilder()
    .Post("Orders", new { CustomerName = "John" }, out var orderRef)
    .Post("OrderItems", new { OrderId = orderRef.Id(), Product = "Widget" })
    .Put("Orders", new { ID = orderRef.Id(), Status = "Active" })
    .Build();

var result = await _apilaneService.TransactionOperationsAsync(
    DataTransactionOperationsRequest.New().WithAuthToken(authToken),
    transaction);
```

## Files

```csharp
using Apilane.Net.Models.Files;

// Upload (an upload cannot be signed: use WithAuthToken)
byte[] fileBytes = File.ReadAllBytes("photo.jpg");
var uploadResult = await _apilaneService.PostFileAsync(
    FilePostRequest.New()
        .WithAuthToken(authToken)
        .WithFileName("photo.jpg"),
    fileBytes);

long? fileId = uploadResult.Value;

// List file records (FileItem: ID, Owner, Created, Name, Size in MB, UID)
var files = await _apilaneService.GetFilesAsync<FileItem>(
    FileGetListRequest.New().WithAuthToken(authToken));

// Get file record by ID
var file = await _apilaneService.GetFileByIdAsync<FileItem>(
    FileGetByIdRequest.New(id: 1).WithAuthToken(authToken));

// Delete files
var deleted = await _apilaneService.DeleteFileAsync(
    FileDeleteRequest.New()
        .WithAuthToken(authToken)
        .AddIdToDelete(1));
```

The `UID` of a file is generated by the server: read it from the file record. The SDK has no call to download the file itself, see [Download File](../api_reference.md#download-file).

## Stats & Aggregation

### Aggregate

```csharp
using static Apilane.Net.Request.StatsAggregateRequest;

// One object per group; the columns are named {Property}_{function}
public class OrderTotals
{
    public string? Status { get; set; }
    public decimal Total_sum { get; set; }
    public decimal Total_avg { get; set; }
    public long ID_count { get; set; }
}

var result = await _apilaneService.GetStatsAggregateAsync<List<OrderTotals>>(
    StatsAggregateRequest.New("Orders")
        .WithAuthToken(authToken)
        .WithProperty("Total", DataAggregates.Sum)
        .WithProperty("Total", DataAggregates.Avg)
        .WithProperty("ID", DataAggregates.Count)
        .WithGroupBy("Status")
        .WithSort(ascending: false));
```

### Distinct

```csharp
// One object per distinct value, keyed by the property name
public class CategoryValue
{
    public string? Category { get; set; }
}

var result = await _apilaneService.GetStatsDistinctAsync<List<CategoryValue>>(
    StatsDistinctRequest.New("Products", "Category")
        .WithAuthToken(authToken));
```

## Schema

Retrieve the application schema at runtime:

```csharp
var schema = await _apilaneService.GetApplicationSchemaAsync(
    DataGetSchemaRequest.New().WithAuthToken(authToken));

var entities = schema.Value.Entities;
foreach (var entity in entities)
{
    Console.WriteLine($"{entity.Name}: {entity.Properties.Count} properties");
}
```

## Custom Endpoints

```csharp
// Raw JSON response
var json = await _apilaneService.GetCustomEndpointAsync(
    CustomEndpointRequest.New("MyEndpoint")
        .WithAuthToken(authToken)
        .WithParameter("UserID", 42));

// Typed response
var result = await _apilaneService.GetCustomEndpointAsync<List<UserSummary>>(
    CustomEndpointRequest.New("GetUserSummary")
        .WithAuthToken(authToken)
        .WithParameter("UserID", 42));

// Multi-result set (multiple SELECT statements): one type per result set, up to five
var response = await _apilaneService.GetCustomEndpointAsync<List<Order>, List<OrderItem>>(
    CustomEndpointRequest.New("GetOrderDetails")
        .WithAuthToken(authToken)
        .WithParameter("OrderID", 100));

var (orders, items) = response.Value;
```

A `List<>` type receives the whole result set. Any other type receives the first row of that result set (or its default when the set is empty). Parameters are whole numbers (`long?`); a parameter name can be given only once per request.

## URL Helpers

The SDK provides URL builders for Apilane pages:

```csharp
// Forgot password page URL
string forgotPasswordUrl = _apilaneService.UrlFor_Account_Manage_ForgotPassword();

// API endpoint: send forgot password email
string sendResetEmailUrl = _apilaneService.UrlFor_Email_ForgotPassword("user@example.com");

// API endpoint: send confirmation email
string confirmEmailUrl = _apilaneService.UrlFor_Email_RequestConfirmation("user@example.com");
```

## Health Check

Verify the API is running:

```csharp
var health = await _apilaneService.HealthCheckAsync();
```

## License

The Apilane SDKs are released under the [MIT License](https://github.com/raptisv/apilane/blob/main/sdk/LICENSE): Apilane.Js, and Apilane.Net after version 10.1.1 (versions up to 10.1.1 were published under AGPL-3.0-only and stay under it). The Apilane server (API and Portal) is licensed under [AGPL-3.0](https://github.com/raptisv/apilane/blob/main/LICENSE).
