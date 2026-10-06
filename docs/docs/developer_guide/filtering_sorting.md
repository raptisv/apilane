---
description: "Filter and sort data in Apilane with JSON filters and sorting parameters, available on the Data, Files and Stats endpoints."
---

# Filtering & Sorting

Apilane provides a powerful JSON-based filtering and sorting system for querying data. The `filter` parameter is accepted by `Data/Get`, `Files/Get`, `Stats/Aggregate` and `Stats/Distinct`. The `sort`, `properties` and `getTotal` parameters are accepted by `Data/Get` and `Files/Get`; the `GetByID` endpoints take `properties` only. `Stats/Aggregate` has its own `pageIndex`, `pageSize` and `orderDirection` (`ASC` or `DESC`, default `DESC`), which orders the groups by their aggregated value.

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

| Operator | Description | Applicable types |
|---|---|---|
| `equal` | Equal (text ignores case, see below) | All |
| `notequal` | Not equal | All |
| `greater` | Greater than | Number, Date, String |
| `greaterorequal` | Greater than or equal | Number, Date, String |
| `less` | Less than | Number, Date, String |
| `lessorequal` | Less than or equal | Number, Date, String |
| `startswith` | Starts with | String |
| `endswith` | Ends with | String |
| `contains` | Contains the text. On a number: is one of the listed numbers (see below) | String, Number |
| `notcontains` | Does not contain the text. On a number: is none of the listed numbers | String, Number |

Write the operator in full, as in the table. Letter case does not matter (`Equal` works), but short forms such as `eq`, `=` or `>=` are refused with `INVALID_FILTER_PARAMETER`. A Boolean property accepts only `equal` and `notequal`.

String values are matched literally: `%`, `_`, `[` and `\` are ordinary characters, not wildcards or escapes, so send values unescaped. For example, `equal` `cust_1` does not match `custA1`, and `contains` `%` matches only values that contain a percent sign. Spaces at the start and end of a value are removed before it is compared.

`equal`, `notequal`, `startswith`, `endswith`, `contains` and `notcontains` ignore case (on SQLite only for A–Z). On SQL Server and MySQL, case, accent and similar rules follow the database collation, which is case-insensitive by default. `greater`, `greaterorequal`, `less` and `lessorequal` on text use the ordering of the database, which on SQLite is binary: capitals sort before lower-case letters.

### Values

How a value is written depends on the type of the property:

| Property type | Value |
|---|---|
| String | Text. Spaces at the start and end are removed. |
| Number | A number such as `3` or `3.5`. With `contains` and `notcontains`, a comma-separated list of numbers (see below). |
| Boolean | `true` or `false` (`1`, `0`, `on` and `off` also work). Only `equal` and `notequal`. |
| Date | Unix time in seconds (10 digits) or milliseconds (13 digits), or `yyyy-MM-dd`, `yyyy-MM-dd HH:mm`, `yyyy-MM-dd HH:mm:ss` or `yyyy-MM-dd HH:mm:ss.fff`, all in UTC. Text with a `T` or a `Z` (ISO 8601) is refused. |
| any | `null`: `equal` finds the records where the property is empty, `notequal` the ones where it is not. No other operator accepts `null`. |

A filter on a property that does not exist, and a filter or sort on a property the caller has no read access to, is refused (`INVALID_FILTER_PARAMETER`, `INVALID_SORT_PARAMETER`). A group inside another group must not have an empty `Filters` list: the query fails.

!!!warning "Encrypted properties"
    The value of an encrypted property is stored encrypted and a filter compares it as stored, so a filter with the plain value finds nothing, and sorting orders the encrypted text. Do not filter or sort by an encrypted property.

### Matching a set of values (`IN`)

Apilane has no dedicated `IN` operator. On a **numeric** property, `contains` and `notcontains` compile to SQL `IN (...)` / `NOT IN (...)` when the value is a comma-separated list of numbers. This is the established Apilane idiom for "match any of these IDs":

```json
{ "Property": "ID", "Operator": "contains", "Value": "3,7,42" }
```

translates to `ID IN (3, 7, 42)`.

!!!warning "Numeric only"
    On a **string** property, `contains` means *substring* `LIKE` — not `IN`. The set-membership behaviour applies only to numeric properties.

With the SDK, build the value with `string.Join(",", ids)`:

```csharp
// .NET — "ID IN (...)"
var filter = new FilterItem("ID", FilterOperator.contains, string.Join(",", ids));
```
```javascript
// JavaScript — "ID IN (...)"
const filter = FilterItem.condition('ID', FilterOperator.contains, ids.join(','));
```

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

The direction is not case sensitive, and anything other than `DESC` sorts ascending.

### Several properties

Send an array to sort by several properties; the next one decides between records that are equal in the ones before it:

```json
[
  { "Property": "Created", "Direction": "DESC" },
  { "Property": "Title", "Direction": "ASC" }
]
```

A property that does not exist is ignored; one the caller cannot read is refused (`INVALID_SORT_PARAMETER`). Without `sort`, `Data/Get` returns the records in the default sorting of the entity (the **Sorting** tab of the entity in the Portal), and without that by `ID`, ascending. The .NET SDK `WithSort` takes one property.

### Sorting Example

Get products sorted by price ascending:

```
GET https://my.api.server/api/Data/Get?entity=Products&sort={"Property":"Price","Direction":"ASC"}
x-application-token: {appToken}
```

## Paging

`Data/Get`, `Files/Get` and `Stats/Aggregate` support paging with two parameters (`Data/GetHistoryByID` has them too, with a default page size of 10):

| Parameter | Default | Range | Description |
|---|---|---|---|
| `pageIndex` | `1` | 1+ | The page number to retrieve. A smaller value is read as 1 |
| `pageSize` | `20` | 1-1000 | Number of records per page. A value above 1000 or below 0 is read as 1000 |

!!!warning "pageSize 0"
    `pageSize=0` does not return an empty page: `Data/Get` and `Files/Get` then return every matching record, without the 1000 limit. Always send a page size from 1 to 1000.

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
