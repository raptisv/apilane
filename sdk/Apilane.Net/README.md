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

Auth tokens are provided per-request via `.WithAuthToken()` on any request builder.

### Signed requests (don't send the token over the wire)

Instead of sending the auth token on every request, you can **sign** requests. The token is used
only to compute an HMAC signature locally and is never transmitted, which protects it from request
logs / proxies and limits a captured request to a short time window.

Login returns the token's id (`AuthTokenID`) — use it as the public key id and the token as the
signing secret. Call `.WithSigning(keyId, secret)` on any request builder (it takes precedence over
`.WithAuthToken`):

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
