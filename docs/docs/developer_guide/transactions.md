# Transactions

Apilane supports executing multiple data operations (create, update, delete) within a single atomic transaction. If any operation fails, all changes are rolled back.

## When to Use Transactions

Transactions are useful when you need to:

- Create related records that must all succeed together (e.g., an order and its line items)
- Update multiple entities in a single atomic operation
- Ensure data consistency across related changes
- Create a record and immediately pass its ID to a [custom endpoint](custom_endpoints.md) for additional processing

## Transaction Types

Apilane offers two transaction endpoints:

| Endpoint | Description | Cross-referencing |
|---|---|---|
| **Transaction** | Group operations by type (all Posts, then all Puts, then all Deletes) | No |
| **TransactionOperations** | Ordered list of operations executed sequentially | Yes — reference results from earlier operations |

## Transaction (Grouped)

Groups operations into `Post`, `Put`, and `Delete` arrays. All operations execute in a single database transaction.

### Request

```
POST https://my.api.server/api/Data/Transaction
x-application-token: {appToken}
Content-Type: application/json
Authorization: Bearer {authToken}
```

```json
{
  "Post": [
    { "Entity": "Orders", "Data": { "CustomerName": "John", "Total": 99.90 } },
    { "Entity": "Orders", "Data": { "CustomerName": "Jane", "Total": 150.00 } }
  ],
  "Put": [
    { "Entity": "Products", "Data": { "ID": 5, "Stock": 48 } }
  ],
  "Delete": [
    { "Entity": "TempRecords", "Ids": "10,11,12" }
  ]
}
```

### Response

```json
{
  "Post": [1, 2],
  "Put": 1,
  "Delete": [10, 11, 12]
}
```

| Field | Description |
|---|---|
| `Post` | Array of newly created record IDs |
| `Put` | Count of updated records |
| `Delete` | Array of deleted record IDs |

## TransactionOperations (Ordered with Cross-Referencing)

Defines an ordered list of operations that execute sequentially. This is the more powerful option because operations can **reference results from earlier operations** using the `$ref:{OperationId}` syntax. In addition to Post, Put, and Delete, this endpoint also supports calling **Custom endpoints**.

### Request

```
POST https://my.api.server/api/Data/TransactionOperations
x-application-token: {appToken}
Content-Type: application/json
Authorization: Bearer {authToken}
```

```json
{
  "Operations": [
    {
      "Action": "Post",
      "Entity": "Orders",
      "Id": "createOrder",
      "Data": { "CustomerName": "John", "Status": "Pending" }
    },
    {
      "Action": "Post",
      "Entity": "OrderItems",
      "Data": { "OrderId": "$ref:createOrder", "Product": "Widget", "Qty": 3 }
    },
    {
      "Action": "Post",
      "Entity": "OrderItems",
      "Data": { "OrderId": "$ref:createOrder", "Product": "Gadget", "Qty": 1 }
    },
    {
      "Action": "Put",
      "Entity": "Orders",
      "Data": { "ID": "$ref:createOrder", "Status": "Active" }
    },
    {
      "Action": "Custom",
      "Entity": "NotifyOrderCreated",
      "Data": { "orderId": "$ref:createOrder" }
    }
  ]
}
```

In the example above:

1. An order is created and assigned the operation ID `createOrder`
2. Two order items are created, both referencing the order's ID via `$ref:createOrder`
3. The order's status is updated using the same reference
4. A custom endpoint `NotifyOrderCreated` is called, receiving the order's ID as a parameter

### Cross-Reference Syntax

| Syntax | Description |
|---|---|
| `$ref:{OperationId}` | Resolves to the **first ID** returned by the referenced operation |

- The `Id` field on an operation is optional — only needed if other operations will reference it
- `$ref` values are resolved server-side before the operation executes
- References can only point to operations declared **earlier** in the list

### Supported Actions

| Action | Entity field | Data field | Ids field |
|---|---|---|---|
| `Post` | Entity name | Record data | — |
| `Put` | Entity name | Record data (must include ID) | — |
| `Delete` | Entity name | — | Comma-separated IDs |
| `Custom` | Custom endpoint name | Key-value parameters for the endpoint | — |

### Response

```json
{
  "Results": [
    { "Action": "Post", "Entity": "Orders", "Created": [42] },
    { "Action": "Post", "Entity": "OrderItems", "Created": [101] },
    { "Action": "Post", "Entity": "OrderItems", "Created": [102] },
    { "Action": "Put", "Entity": "Orders", "Affected": 1 },
    { "Action": "Custom", "Entity": "NotifyOrderCreated", "CustomResult": [[{ "Status": "sent" }]] }
  ]
}
```

Each result includes:

| Field | Appears on | Description |
|---|---|---|
| `Created` | Post | Array of newly created IDs |
| `Affected` | Put | Count of updated records |
| `Deleted` | Delete | Array of deleted IDs |
| `CustomResult` | Custom | Nested array of result sets from the custom endpoint (same format as [custom endpoint responses](custom_endpoints.md#multiple-result-sets)) |

## SDK Usage

### Grouped Transaction

```csharp
var transactionData = new InTransactionData
{
    Post = new List<InTransactionData.InTransactionSet>
    {
        new() { Entity = "Orders", Data = new { CustomerName = "John", Total = 99.90 } }
    },
    Put = new List<InTransactionData.InTransactionSet>
    {
        new() { Entity = "Products", Data = new { ID = 5, Stock = 48 } }
    },
    Delete = new List<InTransactionData.InTransactionDelete>
    {
        new() { Entity = "TempRecords", Ids = "10,11,12" }
    }
};

var result = await _apilaneService.TransactionDataAsync(
    DataTransactionRequest.New().WithAuthToken(authToken),
    transactionData);
```

### Ordered TransactionOperations (with builder)

The SDK provides a fluent `TransactionBuilder` for the ordered transaction endpoint:

```csharp
using Apilane.Net.Models.Data;

var transaction = new TransactionBuilder()
    .Post("Orders", new { CustomerName = "John", Status = "Pending" }, out var orderRef)
    .Post("OrderItems", new { OrderId = orderRef.Id(), Product = "Widget", Qty = 3 })
    .Post("OrderItems", new { OrderId = orderRef.Id(), Product = "Gadget", Qty = 1 })
    .Put("Orders", new { ID = orderRef.Id(), Status = "Active" })
    .Delete("TempRecords", "10,11,12")
    .Custom("NotifyOrderCreated", new { orderId = orderRef.Id() })
    .Build();

var result = await _apilaneService.TransactionOperationsAsync(
    DataTransactionOperationsRequest.New().WithAuthToken(authToken),
    transaction);

// Access custom endpoint result
var customResult = result.Match(
    r => r.Results.Last().CustomResult,
    e => throw new Exception(e.Message));
```

The `out var orderRef` captures a `TransactionRef` object. Calling `orderRef.Id()` returns the `$ref:auto_0` placeholder that the server resolves to the actual created ID.

### Custom Endpoints in Transactions

The `.Custom(endpointName, data)` method calls a [custom endpoint](custom_endpoints.md) as part of the transaction. This is useful for triggering server-side logic (e.g., computed queries, notifications, or aggregations) that depends on records created or modified earlier in the same transaction.

- The `endpointName` must match a custom endpoint configured in the Portal
- The `data` object provides parameter values — use `orderRef.Id()` to pass IDs from prior operations via `$ref` resolution
- The custom endpoint's security rules still apply — the calling user must have `get` access to the endpoint
- Custom endpoint parameters follow the same [type restriction](custom_endpoints.md#parameters) (big integer / long values only)

!!!info "Atomicity"
    Both transaction types are fully atomic. If any individual operation fails validation or encounters an error, the entire transaction is rolled back and no changes are persisted.
