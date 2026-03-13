# Apilane.Js — JavaScript SDK

The official JavaScript SDK for the [Apilane](https://github.com/raptisv/apilane) backend-as-a-service platform.

## Installation

Copy `apilane.js` into your project, or reference it directly:

```javascript
import { createApilaneService } from './apilane.js';
```

> Works in browsers and Node.js 18+ (requires native `fetch` and `FormData`).

## Quick Start

### 1. Create the service

```javascript
import { createApilaneService, AccountLoginRequest, DataGetListRequest } from './apilane.js';

const apilane = createApilaneService({
    apiUrl: 'https://my.api.server',
    appToken: 'your-app-token',
});
```

### 2. Use it

```javascript
// Login
const loginResult = await apilane.accountLogin(
    AccountLoginRequest.new({ Email: 'user@example.com', Password: 'password' })
);

if (loginResult.isError) {
    console.error(loginResult.error.buildErrorMessage());
} else {
    const { AuthToken, User } = loginResult.value;
    console.log('Logged in as', User.Email);

    // Get data using the auth token from login
    const dataResult = await apilane.getData(
        DataGetListRequest.new('Products').withAuthToken(AuthToken)
    );

    if (dataResult.isSuccess) {
        console.log('Products:', dataResult.value.Data);
    }
}
```

### 3. Or use a global auth token provider

```javascript
const apilane = createApilaneService({
    apiUrl: 'https://my.api.server',
    appToken: 'your-app-token',
    authTokenProvider: async () => localStorage.getItem('authToken'),
});

// No need to pass auth token per request — it's resolved automatically
const data = await apilane.getData(DataGetListRequest.new('Products'));
```

## Features

### Authentication

```javascript
import {
    AccountLoginRequest,
    AccountRegisterRequest,
    AccountLogoutRequest,
    AccountRenewAuthTokenRequest,
    AccountUserDataRequest,
    AccountUpdateRequest,
} from './apilane.js';

// Login
const login = await apilane.accountLogin(
    AccountLoginRequest.new({ Email: 'user@example.com', Password: 'pass123' })
);

// Register
const register = await apilane.accountRegister(
    AccountRegisterRequest.new({ Email: 'new@user.com', Username: 'newuser', Password: 'pass123' })
);

// Get user data
const userData = await apilane.getAccountUserData(
    AccountUserDataRequest.new().withAuthToken(authToken)
);

// Update account
const updated = await apilane.accountUpdate(
    AccountUpdateRequest.new().withAuthToken(authToken),
    { Username: 'newname' }
);

// Renew token
const newToken = await apilane.accountRenewAuthToken(
    AccountRenewAuthTokenRequest.new().withAuthToken(authToken)
);

// Logout
const logout = await apilane.accountLogout(
    AccountLogoutRequest.new(false).withAuthToken(authToken)
);
```

### Data CRUD

```javascript
import {
    DataGetListRequest,
    DataGetByIdRequest,
    DataGetAllRequest,
    DataPostRequest,
    DataPutRequest,
    DataDeleteRequest,
    FilterItem,
    FilterOperator,
    FilterLogic,
    SortItem,
} from './apilane.js';

// Get list with filtering, sorting, and paging
const products = await apilane.getData(
    DataGetListRequest.new('Products')
        .withPageIndex(1)
        .withPageSize(50)
        .withFilter(
            FilterItem.and(
                FilterItem.condition('Price', FilterOperator.greaterorequal, 10),
                FilterItem.condition('Category', FilterOperator.equal, 'Electronics')
            )
        )
        .withSort(new SortItem('Price', 'desc'))
        .withProperties('ID', 'Name', 'Price')
        .withAuthToken(authToken)
);

// Get with total count
const withTotal = await apilane.getDataTotal(
    DataGetListRequest.new('Products')
        .withPageSize(10)
        .withAuthToken(authToken)
);
// withTotal.value => { Data: [...], Total: 150 }

// Get by ID
const product = await apilane.getDataById(
    DataGetByIdRequest.new('Products', 42).withAuthToken(authToken)
);

// Get ALL records (auto-paginates)
const allProducts = await apilane.getAllData(
    DataGetAllRequest.new('Products').withAuthToken(authToken)
);

// Create
const created = await apilane.postData(
    DataPostRequest.new('Products').withAuthToken(authToken),
    { Name: 'Widget', Price: 9.99 }
);
// created.value => [123] (new IDs)

// Update
const updated = await apilane.putData(
    DataPutRequest.new('Products').withAuthToken(authToken),
    { ID: 123, Price: 12.99 }
);
// updated.value => 1 (rows affected)

// Delete
const deleted = await apilane.deleteData(
    DataDeleteRequest.new('Products', [123, 124]).withAuthToken(authToken)
);
// deleted.value => [123, 124] (deleted IDs)
```

### Transactions

```javascript
import {
    DataTransactionRequest,
    DataTransactionOperationsRequest,
    TransactionBuilder,
} from './apilane.js';

// Simple transaction (grouped Post/Put/Delete)
const txResult = await apilane.transactionData(
    DataTransactionRequest.new().withAuthToken(authToken),
    {
        Post: [{ Entity: 'Orders', Data: { Name: 'New Order' } }],
        Put: [{ Entity: 'Products', Data: { ID: 1, Stock: 99 } }],
        Delete: [{ Entity: 'OldOrders', Ids: '10,11,12' }],
    }
);

// Ordered operations with cross-references
const builder = new TransactionBuilder();
const orderRef = builder.postWithRef('Orders', { Name: 'Test Order' });
builder
    .post('OrderItems', { OrderId: orderRef.id(), Product: 'Widget' })
    .put('Orders', { ID: orderRef.id(), Status: 'Active' })
    .delete('OldOrders', '1,2,3')
    .custom('ProcessOrder', { orderId: orderRef.id() });

const opsResult = await apilane.transactionOperations(
    DataTransactionOperationsRequest.new().withAuthToken(authToken),
    builder.build()
);
```

### Files

```javascript
import {
    FileGetListRequest,
    FileGetByIdRequest,
    FilePostRequest,
    FileDeleteRequest,
} from './apilane.js';

// List files
const files = await apilane.getFiles(
    FileGetListRequest.new().withAuthToken(authToken)
);

// Get file metadata by ID
const file = await apilane.getFileById(
    FileGetByIdRequest.new(42).withAuthToken(authToken)
);

// Upload a file (browser)
const input = document.querySelector('input[type="file"]');
const uploadResult = await apilane.postFile(
    FilePostRequest.new()
        .withFileName('photo.jpg')
        .withPublicFlag(true)
        .withAuthToken(authToken),
    input.files[0]
);

// Upload a file (Node.js)
import { readFile } from 'node:fs/promises';
const buffer = await readFile('photo.jpg');
const uploadResult = await apilane.postFile(
    FilePostRequest.new().withFileName('photo.jpg').withAuthToken(authToken),
    buffer
);

// Delete files
const deleteResult = await apilane.deleteFile(
    FileDeleteRequest.new([42, 43]).withAuthToken(authToken)
);
```

### Stats

```javascript
import {
    StatsAggregateRequest,
    StatsDistinctRequest,
    DataAggregates,
} from './apilane.js';

// Aggregate query
const stats = await apilane.getStatsAggregate(
    StatsAggregateRequest.new('Products')
        .withProperty('Price', DataAggregates.Avg)
        .withProperty('Price', DataAggregates.Max)
        .withGroupBy('Category')
        .withSort(true)
        .withAuthToken(authToken)
);

// Distinct values
const distinct = await apilane.getStatsDistinct(
    StatsDistinctRequest.new('Products', 'Category')
        .withAuthToken(authToken)
);
```

### Custom Endpoints

```javascript
import { CustomEndpointRequest } from './apilane.js';

const result = await apilane.getCustomEndpoint(
    CustomEndpointRequest.new('MyEndpoint')
        .withParameter('userId', 42)
        .withAuthToken(authToken)
);
```

### Schema

```javascript
import { DataGetSchemaRequest } from './apilane.js';

const schema = await apilane.getApplicationSchema(
    DataGetSchemaRequest.new().withAuthToken(authToken)
);
// schema.value => { AuthTokenExpireMinutes, Entities: [...], ... }
```

### Error Handling

The SDK uses `ApilaneResult` — a discriminated result type:

```javascript
const result = await apilane.getData(DataGetListRequest.new('Products'));

// Option 1: Check isSuccess/isError
if (result.isSuccess) {
    console.log(result.value.Data);
} else {
    console.error(result.error.buildErrorMessage());
}

// Option 2: Pattern match
result.match(
    (data) => console.log('Success:', data.Data),
    (error) => console.error('Failed:', error.Code, error.Message)
);

// Option 3: Throw on error (configured per-request)
const result = await apilane.getData(
    DataGetListRequest.new('Products').onErrorThrowException()
);
// Throws an Error if the API returns an error response
```

### Request Cancellation

Use the standard `AbortController` to cancel in-flight requests:

```javascript
const controller = new AbortController();

// Cancel after 5 seconds
setTimeout(() => controller.abort(), 5000);

const result = await apilane.getData(
    DataGetListRequest.new('Products'),
    controller.signal
);
```

### Utility Functions

```javascript
import { unixTimestampToDate, dateToUnixTimestampSeconds } from './apilane.js';

// Convert API timestamps to Date objects
const date = unixTimestampToDate(1700000000);     // seconds (10 digits)
const date2 = unixTimestampToDate(1700000000000); // milliseconds (13 digits)

// Convert Date to Unix timestamp
const ts = dateToUnixTimestampSeconds(new Date());
```

## API Parity with .NET SDK

| .NET SDK | JavaScript SDK |
|---|---|
| `AccountLoginAsync<T>()` | `accountLogin()` |
| `AccountRegisterAsync()` | `accountRegister()` |
| `AccountLogoutAsync()` | `accountLogout()` |
| `AccountRenewAuthTokenAsync()` | `accountRenewAuthToken()` |
| `GetAccountUserDataAsync<T>()` | `getAccountUserData()` |
| `AccountUpdateAsync<T>()` | `accountUpdate()` |
| `GetDataAsync<T>()` | `getData()` |
| `GetDataByIdAsync<T>()` | `getDataById()` |
| `GetAllDataAsync<T>()` | `getAllData()` |
| `GetDataTotalAsync<T>()` | `getDataTotal()` |
| `PostDataAsync()` | `postData()` |
| `PutDataAsync()` | `putData()` |
| `DeleteDataAsync()` | `deleteData()` |
| `TransactionDataAsync()` | `transactionData()` |
| `TransactionOperationsAsync()` | `transactionOperations()` |
| `GetFilesAsync<T>()` | `getFiles()` |
| `GetFileByIdAsync<T>()` | `getFileById()` |
| `PostFileAsync()` | `postFile()` |
| `DeleteFileAsync()` | `deleteFile()` |
| `GetStatsAggregateAsync()` | `getStatsAggregate()` |
| `GetStatsDistinctAsync()` | `getStatsDistinct()` |
| `GetCustomEndpointAsync()` | `getCustomEndpoint()` |
| `GetApplicationSchemaAsync()` | `getApplicationSchema()` |
| `HealthCheckAsync()` | `healthCheck()` |
| `UrlFor_Account_Manage_ForgotPassword()` | `urlForAccountManageForgotPassword()` |
| `UrlFor_Email_RequestConfirmation()` | `urlForEmailRequestConfirmation()` |
| `UrlFor_Email_ForgotPassword()` | `urlForEmailForgotPassword()` |

## Requirements

- Browsers: Any modern browser with `fetch` and `FormData` support
- Node.js: 18.0+ (native `fetch`)
- An Apilane API instance

## Documentation

Full SDK documentation: [https://docs.apilane.com/developer_guide/sdk/](https://docs.apilane.com/developer_guide/sdk/)

## License

See the [Apilane repository](https://github.com/raptisv/apilane) for license information.
