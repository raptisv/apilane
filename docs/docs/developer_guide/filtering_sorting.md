# Filtering & Sorting

Apilane provides a powerful JSON-based filtering and sorting system for querying data. These parameters are available on all `Get` endpoints for Data, Files, and Stats.

## Filtering

The `filter` query parameter accepts a JSON object that defines one or more conditions.

### Simple Filter

A single condition compares a property to a value:

```json
{
  "Property": "Email",
  "Operator": "equal",
  "Value": "john@example.com"
}
```

### Compound Filter

Combine multiple conditions with `AND` or `OR` logic:

```json
{
  "Logic": "AND",
  "Filters": [
    { "Property": "Age", "Operator": "greaterorequal", "Value": 18 },
    { "Property": "IsActive", "Operator": "equal", "Value": true }
  ]
}
```

### Nested Filters

Filters can be nested to create complex expressions:

```json
{
  "Logic": "AND",
  "Filters": [
    { "Property": "Country", "Operator": "equal", "Value": "US" },
    {
      "Logic": "OR",
      "Filters": [
        { "Property": "Role", "Operator": "equal", "Value": "admin" },
        { "Property": "Role", "Operator": "equal", "Value": "manager" }
      ]
    }
  ]
}
```

This translates to: `Country = 'US' AND (Role = 'admin' OR Role = 'manager')`

### Filter Operators

| Operator | Aliases | Description | Applicable types |
|---|---|---|---|
| `equal` | `eq`, `==`, `=` | Exact match | All |
| `notequal` | `neq`, `!=`, `<>` | Not equal | All |
| `greater` | `g`, `>` | Greater than | Number, Date |
| `greaterorequal` | `ge`, `>=` | Greater than or equal | Number, Date |
| `less` | `l`, `<` | Less than | Number, Date |
| `lessorequal` | `le`, `<=` | Less than or equal | Number, Date |
| `startswith` | `sw` | Starts with string | String |
| `endswith` | `ew` | Ends with string | String |
| `contains` | `like` | Contains substring | String |
| `notcontains` | `nc` | Does not contain substring | String |

### Filter Logic

| Logic | Description |
|---|---|
| `AND` | All conditions must be true |
| `OR` | At least one condition must be true |

### URL Encoding

Since filters are passed as a query parameter, the JSON must be URL-encoded:

```
GET https://my.api.server/api/Data/Get?entity=Products&filter=%7B%22Property%22%3A%22Price%22%2C%22Operator%22%3A%22less%22%2C%22Value%22%3A100%7D
x-application-token: {appToken}
```

!!!info "Tip"
    Use the [.NET SDK](sdk.md) to build filters with a type-safe builder pattern instead of constructing JSON manually.

## Sorting

The `sort` query parameter accepts a JSON object that specifies the property and direction:

```json
{
  "Property": "Created",
  "Direction": "DESC"
}
```

### Sort Directions

| Direction | Description |
|---|---|
| `ASC` | Ascending (smallest first) |
| `DESC` | Descending (largest first) |

### Sorting Example

Get products sorted by price ascending:

```
GET https://my.api.server/api/Data/Get?entity=Products&sort={"Property":"Price","Direction":"ASC"}
x-application-token: {appToken}
```

## Paging

All list endpoints support paging with two parameters:

| Parameter | Default | Range | Description |
|---|---|---|---|
| `pageIndex` | `1` | 1+ | The page number to retrieve |
| `pageSize` | `20` | 0-1000 | Number of records per page |

### Getting Total Count

Set `getTotal=true` to include the total record count in the response. This is useful for building pagination UIs.

**Without total:**
```json
{
  "Data": [ { "ID": 1, "Name": "Widget" }, ... ]
}
```

**With total:**
```json
{
  "Data": [ { "ID": 1, "Name": "Widget" }, ... ],
  "Total": 142
}
```

!!!warning "Performance"
    Getting the total count adds an extra database query. Only request it when needed (e.g., for the first page load or when building a pager).

## Property Selection

Use the `properties` parameter to fetch only specific properties. This reduces bandwidth and improves performance.

```
GET https://my.api.server/api/Data/Get?entity=Products&properties=ID,Name,Price
x-application-token: {appToken}
```

When omitted, all properties accessible to the user are returned.

## SDK Filter Builder

The .NET SDK provides a type-safe way to build filters:

```csharp
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;

// Simple filter
var filter = new FilterItem("Price", FilterOperator.less, 100);

// Compound filter
var filter = new FilterItem(FilterLogic.AND, new List<FilterItem>
{
    new FilterItem("Age", FilterOperator.greaterorequal, 18),
    new FilterItem("IsActive", FilterOperator.equal, true)
});

// Use in a request
var response = await _apilaneService.GetDataAsync<Product>(
    DataGetListRequest.New("Products")
        .WithAuthToken(authToken)
        .WithFilter(filter)
        .WithSort(new SortItem { Property = "Price", Direction = "ASC" })
        .WithPageSize(50)
        .WithProperties("ID", "Name", "Price"));
```
