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
using Apilane.Net.Abstractions;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;

public class MyUser : ApiUser { }

public class MyProduct : DataItem
{
    public string? Name { get; set; }
}

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
            AccountLoginRequest.New(new LoginItem { Email = "user@example.com", Password = "password" }));

        if (loginResult.HasError(out var error))
        {
            Console.WriteLine($"{error.Code}: {error.Message}");
            return;
        }

        // Get data using the auth token from login
        var data = await _apilane.GetDataAsync<MyProduct>(
            DataGetListRequest.New("Products")
                .WithAuthToken(loginResult.Value.AuthToken));
    }
}
```

Auth tokens are provided per-request via `.WithAuthToken()` on any request builder.

### Errors

Every call returns an `Either<T, ApilaneError>`: check `HasError(out var error)` before you read `.Value`.
To throw instead, build the request with `.OnErrorThrowException(true)`: the call then throws an
`ApilaneErrorException`, which derives from `Exception` and carries the `Error` (`Code`, `Message`,
`Property`, `Entity`) and the HTTP `StatusCode`:

```csharp
try
{
    var products = (await _apilane.GetDataAsync<MyProduct>(
        DataGetListRequest.New("Products").OnErrorThrowException(true))).Value;
}
catch (ApilaneErrorException ex) when (ex.Error.Code == ValidationError.UNAUTHORIZED)
{
    // Sign in again
}
```

`ValidationError` is in `Apilane.Net.Models.Enums`.

### Signed requests (don't send the token over the wire)

Instead of sending the auth token on every request, you can **sign** requests. The token is used
only to compute an HMAC signature locally and is never transmitted, which protects it from request
logs / proxies and limits a captured request to a short time window.

Login returns the token's id (`AuthTokenID`) — use it as the public key id and the token as the
signing secret. Call `.WithSigning(keyId, secret)` on any request builder instead of `.WithAuthToken`
(a request uses one of the two: calling both throws `InvalidOperationException`):

```csharp
var login = (await _apilane.AccountLoginAsync<MyUser>(
    AccountLoginRequest.New(new LoginItem { Email = "user@example.com", Password = "password" }))).Value;

var data = await _apilane.GetDataAsync<MyProduct>(
    DataGetListRequest.New("Products")
        .WithSigning(login.AuthTokenID, login.AuthToken));
```

How it works: the SDK sends `x-auth-keyid`, `x-auth-timestamp`, and `x-auth-signature`, where the
signature is `HMAC-SHA256(token, canonical)` and the canonical string is the newline-joined
`keyId, METHOD, path+query, timestamp, base64(SHA-256(body))`. The server resolves the secret by
key id, recomputes the signature, and rejects requests outside a small clock-skew window
(default ±2 min). Keep the client clock roughly in sync. File uploads cannot be signed — use
`WithAuthToken` for `PostFileAsync` (calling `WithSigning` on a file upload throws).

## Features

- **Authentication** — Login, Register, UserData, Update, RenewAuthToken, Logout
- **Email tokens** — ConfirmEmail and ResetPassword with the token of the email (`{confirmation_token}`, `{reset_password_token}`), to complete both flows without the hosted pages
- **Data CRUD** — Get, GetById, Post, Put, Delete with filtering, sorting, and paging
- **Transactions** — Grouped and ordered (TransactionOperations) with cross-reference support
- **Files** — Upload, List, Get by ID, Delete
- **Stats** — Aggregate and Distinct queries
- **Custom Endpoints** — Execute custom SQL with typed responses
- **Schema** — Retrieve entity schema definitions

Changing a password and downloading a file have no method: call `PUT /api/Account/ChangePassword`
and `GET /api/Files/Download` over HTTP (see the [REST API reference](https://docs.apilane.com/api_reference/)).

## Requirements

- A target that supports .NET Standard 2.0 (.NET Core 2.0+, .NET 5+, .NET Framework 4.6.1+)
- An Apilane API instance

## Documentation

Full SDK documentation: [https://docs.apilane.com/developer_guide/sdk/](https://docs.apilane.com/developer_guide/sdk/)

## License

Apilane.Net is released under the [MIT License](https://github.com/raptisv/apilane/blob/main/sdk/LICENSE); a copy is inside the package.
Versions up to 10.1.1 were published under AGPL-3.0-only and stay under that license.

The Apilane server (API and Portal) is a separate program under AGPL-3.0.
