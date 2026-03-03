# SDK (.NET)

Apilane offers a .NET SDK that simplifies integration with the Apilane API. It provides a type-safe, builder-pattern interface for all API operations.

[![NuGet](https://img.shields.io/nuget/v/Apilane.Net.svg?style=flat&label=Apilane.Net)](https://www.nuget.org/packages/Apilane.Net)

## Installation

Add the latest [Apilane.Net NuGet package](https://www.nuget.org/packages/Apilane.Net) to your project:

```bash
dotnet add package Apilane.Net
```

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

The `UseApilane` method accepts optional parameters:

```csharp
builder.Services.UseApilane(
    serverUrl,
    applicationToken,
    httpClient: customHttpClient,           // Custom HttpClient instance
    apilaneAuthTokenProvider: myProvider,    // Global auth token provider
    serviceKey: "app1"                      // Keyed registration for multi-app scenarios
);
```

### Global Auth Token Provider

Instead of passing an auth token on every request, implement `IApilaneAuthTokenProvider` to provide it automatically (e.g., from the current HTTP context):

```csharp
public class HttpContextAuthTokenProvider : IApilaneAuthTokenProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAuthTokenProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<string?> GetAuthTokenAsync()
    {
        var token = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"]
            .ToString()?.Replace("Bearer ", "");
        return Task.FromResult(token);
    }
}
```

Register it before calling `UseApilane`:

```csharp
builder.Services.AddSingleton<IApilaneAuthTokenProvider, HttpContextAuthTokenProvider>();
builder.Services.UseApilane(serverUrl, applicationToken);
```

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
var loginResponse = await _apilaneService.AccountLoginAsync<AppUser>(
    AccountLoginRequest.New()
        .WithEmail("user@example.com")
        .WithPassword("secret"));

if (!loginResponse.HasError(out var error))
{
    string authToken = loginResponse.Value.AuthToken;
    AppUser user = loginResponse.Value.User;
}
```

### Register

```csharp
var registerResponse = await _apilaneService.AccountRegisterAsync(
    AccountRegisterRequest.New()
        .WithAuthToken(authToken),
    new { Email = "new@example.com", Username = "newuser", Password = "Pass123!" });
```

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
var renewResponse = await _apilaneService.AccountRenewAuthTokenAsync(
    AccountRenewAuthTokenRequest.New()
        .WithAuthToken(authToken));

string newToken = renewResponse.Value;
```

### Logout

```csharp
var logoutResponse = await _apilaneService.AccountLogoutAsync(
    AccountLogoutRequest.New()
        .WithAuthToken(authToken));
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

`DataItem` provides the system properties: `ID`, `Owner`, `Created`, and `Created_Date`.

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
    DataGetByIdRequest.New("Products", recordId: 1)
        .WithAuthToken(authToken));
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
var filter = new FilterItem("Price", FilterOperator.less, 100);

// Compound filter (AND/OR)
var filter = new FilterItem(FilterLogic.AND, new List<FilterItem>
{
    new FilterItem("Price", FilterOperator.greaterorequal, 10),
    new FilterItem("InStock", FilterOperator.equal, true)
});

// Nested filter
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
// Upload
byte[] fileBytes = File.ReadAllBytes("photo.jpg");
var uploadResult = await _apilaneService.PostFileAsync(
    FilePostRequest.New()
        .WithAuthToken(authToken)
        .WithFileName("photo.jpg")
        .WithPublicFlag(false)
        .WithFileUID("user-avatar-123"),
    fileBytes);

// List files
var files = await _apilaneService.GetFilesAsync<MyFile>(
    FileGetListRequest.New().WithAuthToken(authToken));

// Get file by ID
var file = await _apilaneService.GetFileByIdAsync<MyFile>(
    FileGetByIdRequest.New(fileId: 1).WithAuthToken(authToken));

// Delete files
var deleted = await _apilaneService.DeleteFileAsync(
    FileDeleteRequest.New()
        .WithAuthToken(authToken)
        .AddIdToDelete(1));
```

## Stats & Aggregation

### Aggregate

```csharp
using static Apilane.Net.Request.StatsAggregateRequest;

var result = await _apilaneService.GetStatsAggregateAsync<MyAggResult>(
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
var result = await _apilaneService.GetStatsDistinctAsync<string[]>(
    StatsDistinctRequest.New("Products")
        .WithAuthToken(authToken)
        .WithProperty("Category"));
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

// Multi-result set (multiple SELECT statements)
var (orders, items) = await _apilaneService.GetCustomEndpointAsync<List<Order>, List<OrderItem>>(
    CustomEndpointRequest.New("GetOrderDetails")
        .WithAuthToken(authToken)
        .WithParameter("OrderID", 100));
```

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
