# Apilane.Net — .NET SDK

The official .NET SDK for the [Apilane](https://github.com/raptisv/apilane) backend-as-a-service platform.

## Installation

```bash
dotnet add package Apilane.Net
```

## Quick Start

### 1. Register the service

```csharp
using Microsoft.Extensions.DependencyInjection;

services.UseApilane(
    applicationApiUrl: "https://my.api.server",
    applicationToken: "your-app-token"
);
```

### 2. Inject and use

```csharp
public class MyService
{
    private readonly IApilaneService _apilane;

    public MyService(IApilaneService apilane)
    {
        _apilane = apilane;
    }

    public async Task Example()
    {
        // Login
        var loginResult = await _apilane.AccountLoginAsync<MyUser>(
            new AccountLoginRequest("user@example.com", "password"));

        // Get data using the auth token from login
        var data = await _apilane.GetDataAsync<MyProduct>(
            new DataGetListRequest("Products")
                .WithAuthToken(loginResult.Result.AuthToken));
    }
}
```

Auth tokens can be provided per-request via `.WithAuthToken()` on any request builder,
or globally by implementing `IApilaneAuthTokenProvider` and registering it in DI.

## Features

- **Authentication** — Login, Register, UserData, ChangePassword, RenewAuthToken, Logout
- **Data CRUD** — Get, GetById, Post, Put, Delete with filtering, sorting, and paging
- **Transactions** — Grouped and ordered (TransactionOperations) with cross-reference support
- **Files** — Upload, Download, List, Delete
- **Stats** — Aggregate and Distinct queries
- **Custom Endpoints** — Execute custom SQL with typed responses
- **Schema** — Retrieve entity schema definitions

## Requirements

- .NET 8.0+
- An Apilane API instance

## Documentation

Full SDK documentation: [https://docs.apilane.com/developer_guide/sdk/](https://docs.apilane.com/developer_guide/sdk/)

## License

See the [Apilane repository](https://github.com/raptisv/apilane) for license information.
