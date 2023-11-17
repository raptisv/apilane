# SDK .NET

Apilane offers a Software Development Kit (SDK) for .NET that is designed to simplify the integration of Apilane platform into your .NET applications. With this SDK, you can easily interact with your applications through our robust API, allowing you to focus on building features rather than handling complex API calls.

[![NuGet](https://img.shields.io/nuget/v/Apilane.Net.svg?style=flat&label=Apilane.Net)](https://www.nuget.org/packages/Apilane.Net)

Key Benefits of Using the SDK

- Ease of Use: The SDK provides a straightforward interface for accessing all API functionalities, making it easy for developers of all skill levels to get started.
- Streamlined Integration: Quickly integrate our platform into your .NET applications with minimal setup and configuration required.
- Intuitive Use of the SDK: Designed with user experience in mind, the SDK allows for intuitive interactions, helping you implement features quickly and efficiently.
- Builder Pattern for API Calls: The SDK utilizes the builder pattern to construct API calls, making it flexible and easy to customize requests while maintaining readability and maintainability.

## Get started

- Add the latest [Apilane.Net nuget](https://www.nuget.org/packages/Apilane.Net) to your application.
- Use DI to register the required Apilane services.


``` csharp
string serverUrl = "https://my.api.server";
string applicationToken = "23d4444f-b56e-4c5a-98ed-ef251796a238";
builder.Services.UseApilane(serverUrl, applicationToken);
```

- Inject `IApilaneService` to your code and start interacting with your Apilane server instance.


``` csharp
private readonly IApilaneService _apilaneService;

public AccountController(IApilaneService apilaneService)
{
    _apilaneService = apilaneService;
}
```

## Account

### Login 

``` csharp
public async Task<IActionResult> Login(string email, string password)
{
    // Login

    var loginResponse = await _apilaneService.AccountLoginAsync<AppUserExt>(new LoginItem
    {
        Email = email,
        Password = password
    });

    // Check for errors

    if (loginResponse.HasError(out var error))
    {
        throw new Exception("Something went wrong");
    }

    // Get and return the user authorization token

    var authorizationToken = loginResponse.Value.AuthToken;

    return Json(authorizationToken);
}
```

## Data

Assuming the following entity

``` csharp
public class MyEntity : Apilane.Net.Models.Data.DataItem
{
    public string? Property_A { get; set; }
    public string? Property_B { get; set; }
    public string? Property_C { get; set; }
}
```

### Get 

``` csharp
public async Task<IActionResult> GetData()
{
    string authToken = "...";

    // Get Data

    var getDataResponse = await _apilaneService.GetDataAsync<MyEntity>(DataGetListRequest.New("MyEntity")
        .WithAuthToken(authToken));

    // Check for errors

    if (getDataResponse.HasError(out var error))
    {
        throw new Exception("Something went wrong");
    }

    // Return data

    List<MyEntity> listOfData = getDataResponse.Value.Data;

    return Json(listOfData);
}
```

### Post 

``` csharp
public async Task<IActionResult> PostData()
{
    string authToken = "...";

    // Post Data

    var postDataResponse = await _apilaneService.PostDataAsync(DataPostRequest.New("MyEntity")
        .WithAuthToken(authToken),
        new MyEntity()
        {
            Property_A = "a",
            Property_B = "b",
            Property_C = "c",
        });

    // Check for errors

    if (postDataResponse.HasError(out var error))
    {
        throw new Exception("Something went wrong");
    }

    // Return new created id

    long newId = postDataResponse.Value.Single();

    return Json(newId);
}
```

### Put 

``` csharp
public async Task<IActionResult> PutData()
{
    string authToken = "...";

    // Put Data

    var putDataResponse = await _apilaneService.PutDataAsync(DataPutRequest.New("MyEntity")
        .WithAuthToken(authToken),
        new
        {
            ID = 1,
            Property_A = "a"
        });

    // Check for errors

    if (putDataResponse.HasError(out var error))
    {
        throw new Exception("Something went wrong");
    }

    // Return the count of affected records

    int affectedRecords = putDataResponse.Value;

    return Json(affectedRecords);
}
```

### Delete 

``` csharp
public async Task<IActionResult> DeleteData()
{
    string authToken = "...";
    long idToDelete = 1;

    // Delete Data

    var deleteDataResponse = await _apilaneService.DeleteDataAsync(DataDeleteRequest.New("MyEntity")
        .WithAuthToken(authToken)
        .AddIdToDelete(idToDelete));

    // Check for errors

    if (deleteDataResponse.HasError(out var error))
    {
        throw new Exception("Something went wrong");
    }

    // Return the deleted ids

    long[] deletedIds = deleteDataResponse.Value;

    return Json(deletedIds);
}
```

## Urls

SDK offers also urls for Apilane related pages or endpoints.

### Forgot password page

``` csharp
string url = _apilaneService.UrlFor_Account_Manage_ForgotPassword();
```

### Forgot password email API endpoint

``` csharp
string url = _apilaneService.UrlFor_Email_ForgotPassword("email@email.com");
```

### Request confirmation email API endpoint

``` csharp
string url = _apilaneService.UrlFor_Email_RequestConfirmation("email@email.com");
```
                    