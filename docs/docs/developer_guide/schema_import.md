# Schema Import (for AI agents)

Apilane can bulk-import **entities, properties, constraints, security rules and custom endpoints** into an existing application from a single JSON document. This page is a precise specification of that JSON so an AI agent (or any automation) can generate a valid payload from scratch.

Open it in the Portal at **Application → Import**, paste the JSON into the *JSON payload* box, and click **Import**. (The same page can also pre-fill the payload by diffing another application — the *Load from another application* panel — which is a good way to see a real payload.)

---

## How the import behaves

- **Additive and idempotent.** The import only ever *creates* what is missing. Existing entities, properties, constraints, security rules and custom endpoints are never modified or deleted.
- **Existing items are validated, not overwritten.** If an entity/property/security rule already exists, its metadata is compared against the payload. If it is identical, it is skipped (a warning is returned). If it differs, **the whole import aborts with an error** — nothing is half-applied for that mismatch.
- **FK-dependency ordering is automatic.** Entities are created in foreign-key dependency order, so you may list them in any order — a child entity may appear before the parent it references, as long as both are in the payload (or the parent already exists).
- **System columns are added for you.** When a new entity is created, Apilane automatically adds its system properties (`ID`, `Owner`, `Created`, …). **Never** include them in `Properties`.

---

## Top-level shape

```json
{
  "Entities": [],
  "Security": [],
  "CustomEndpoints": []
}
```

All three arrays are optional — include only what you want to import.

| Field | Type | Description |
|---|---|---|
| `Entities` | array | Entities to create, each with its properties and constraints |
| `Security` | array | Access-control rules (entity / custom endpoint / schema) |
| `CustomEndpoints` | array | Custom SQL endpoints |

---

## Entities

```json
{
  "Name": "Product",
  "Description": "Product catalog",
  "RequireChangeTracking": false,
  "HasDifferentiationProperty": false,
  "Properties": [],
  "Constraints": []
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | ✅ | Entity name. Becomes the table name and the `entity` used in the API. |
| `Description` | string \| null | | Free-text description. |
| `RequireChangeTracking` | bool | ✅ | When `true`, Apilane keeps a history snapshot of a record's previous values on every update, plus a final snapshot on delete. |
| `HasDifferentiationProperty` | bool | ✅ | When `true`, the entity participates in the application's [differentiation](application.md) (multi-tenant) partitioning. Must match the target app's differentiation setup. |
| `Properties` | array | ✅ | User-defined properties (see below). Do **not** include system properties. |
| `Constraints` | array | | Unique and foreign-key constraints (see below). |

!!!warning "Existing entity metadata must match"
    If an entity with the same name already exists, `RequireChangeTracking` and `HasDifferentiationProperty` must be identical to the payload or the import aborts. Its new properties/constraints are still processed and added.

### Properties

```json
{
  "Name": "Price",
  "TypeID": 2,
  "Required": true,
  "Minimum": 0,
  "Maximum": null,
  "DecimalPlaces": 2,
  "Encrypted": false,
  "ValidationRegex": null,
  "Description": null
}
```

| Field | Type | Description |
|---|---|---|
| `Name` | string | Property (column) name. |
| `TypeID` | int | Data type — see the enum below. |
| `Required` | bool | `true` = `NOT NULL`. |
| `Minimum` | long \| null | **String:** minimum length. **Number:** minimum value. **Date:** minimum (unix ms). **Boolean:** `null`. |
| `Maximum` | long \| null | **String:** maximum length. **Number:** maximum value. **Date:** maximum (unix ms). **Boolean:** `null`. |
| `DecimalPlaces` | int \| null | **Number only.** `0` = integer, `2` = two decimals, etc. `null` for non-numeric types. |
| `Encrypted` | bool | **String only.** Stores the value encrypted at rest. |
| `ValidationRegex` | string \| null | **String only.** Server-side validation pattern. |
| `Description` | string \| null | Free-text description. |

**`TypeID` values** (`PropertyType`):

| `TypeID` | Type |
|---|---|
| `1` | String |
| `2` | Number |
| `3` | Boolean |
| `4` | Date (stored as unix-milliseconds) |

### Constraints

A constraint is `{ "TypeID": <int>, "Properties": "<string>" }`. The `Properties` string is encoded differently per type.

| `TypeID` | Constraint | `Properties` format | Example |
|---|---|---|---|
| `1` | Unique | Comma-separated column name(s). One column, or several for a composite unique key. | `"Email"` · `"FirstName,LastName"` |
| `2` | Foreign key | `"LocalColumn,ReferencedEntity"` or `"LocalColumn,ReferencedEntity,OnDeleteLogic"` | `"Product_ID,Product"` · `"Product_ID,Product,2"` |

For a foreign key, first add a `Number` property to hold the reference (e.g. `Product_ID`), then add the FK constraint pointing at the **referenced entity's** name. The FK targets that entity's `ID` primary key.

**On-delete logic** (`ForeignKeyLogic`, the optional 3rd element; defaults to `0`):

| Value | Behaviour |
|---|---|
| `0` | On delete no action |
| `1` | On delete set null |
| `2` | On delete cascade |

!!!info "Include `IsSystem` if you copy constraints verbatim"
    `EntityConstraint` also has an `IsSystem` flag. For hand-written imports leave it out (defaults to `false`) — you only ever create non-system constraints.

---

## Security rules

Each entry is a `DBWS_Security` rule granting a **role** permission to perform an **action** on an entity, custom endpoint, or the schema.

```json
{
  "Name": "Product",
  "TypeID": 0,
  "RoleID": "ANONYMOUS",
  "Action": "get",
  "Record": 0,
  "Properties": null,
  "RateLimit": null
}
```

| Field | Type | Description |
|---|---|---|
| `Name` | string | The target: the **entity name** (for `TypeID` 0) or **custom endpoint name** (for `TypeID` 1). Empty for schema rules. |
| `TypeID` | int | What the rule targets — `SecurityTypes` below. |
| `RoleID` | string | The role this rule grants. `ANONYMOUS`, `AUTHENTICATED`, or a custom role name. |
| `Action` | string | `get`, `post`, `put`, or `delete` (lower-case). |
| `Record` | int | Record scope — `All` (`0`) or `Owned` (`1`). Applies to `get`/`put`/`delete`. |
| `Properties` | string \| null | Comma-separated columns this rule grants for the action. The primary key is always included. `null`/empty grants **no** non-PK columns. |
| `RateLimit` | object \| null | Optional rate limit (see below). |

**`TypeID` values** (`SecurityTypes`):

| `TypeID` | Target | `Name` holds | Typical `Action` |
|---|---|---|---|
| `0` | Entity | Entity name | `get` / `post` / `put` / `delete` |
| `1` | Custom endpoint | Endpoint name | `get` |
| `2` | Schema | *(empty)* | `get` |

**`Record` values** (`EndpointRecordAuthorization`):

| Value | Meaning |
|---|---|
| `0` | All records |
| `1` | Owned only — restricted to rows whose `Owner` equals the current user's ID |

**Roles.** Apilane always evaluates a user's custom roles plus the two built-ins:

| `RoleID` | Applies to |
|---|---|
| `ANONYMOUS` | Any request with no auth token |
| `AUTHENTICATED` | Any request with a valid auth token |
| *(custom)* | Users whose `Roles` property contains that role |

### Rate limit (optional)

```json
"RateLimit": { "MaxRequests": 100, "TimeWindowType": 2 }
```

| Field | Type | Description |
|---|---|---|
| `MaxRequests` | int | Max requests allowed within the window. |
| `TimeWindowType` | int | Window — `EndpointRateLimit` below. |

| `TimeWindowType` | Window |
|---|---|
| `1` | Per second |
| `2` | Per minute |
| `3` | Per hour |

!!!info "Property-level access"
    To let a role read some columns but write only others, add two rules with different `Action` and `Properties` — e.g. a `get` rule listing all readable columns and a `put` rule listing only the writable ones.

---

## Custom endpoints

```json
{
  "Name": "GetAllProduct",
  "Description": "Retrieves all products.",
  "Query": "SELECT * FROM [Product];"
}
```

| Field | Type | Description |
|---|---|---|
| `Name` | string | Endpoint name (used in the API path). |
| `Description` | string \| null | Free-text description. |
| `Query` | string | The SQL. Write it for your storage provider (SQLite / SQL Server / MySQL). Parameters are `{ParamName}` placeholders and are **big-integer (long) only**. Multiple `;`-separated statements return multiple result sets. |

See [Custom Endpoints](custom_endpoints.md) for query authoring details. Importing a custom endpoint does **not** create a security rule for it — add a matching `Security` entry with `TypeID: 1` to expose it.

---

## Complete example

Creates a `Product` and `OrderItem` (with a foreign key to `Product`), makes `Product` readable by anonymous users, and adds a custom endpoint:

```json
{
  "Entities": [
    {
      "Name": "Product",
      "Description": "Product catalog",
      "RequireChangeTracking": false,
      "HasDifferentiationProperty": false,
      "Properties": [
        { "Name": "Title", "TypeID": 1, "Required": true,  "Minimum": null, "Maximum": 200,  "DecimalPlaces": null, "Encrypted": false, "ValidationRegex": null, "Description": null },
        { "Name": "Price", "TypeID": 2, "Required": true,  "Minimum": 0,    "Maximum": null, "DecimalPlaces": 2,    "Encrypted": false, "ValidationRegex": null, "Description": null }
      ],
      "Constraints": [
        { "TypeID": 1, "Properties": "Title" }
      ]
    },
    {
      "Name": "OrderItem",
      "Description": "Line item in an order",
      "RequireChangeTracking": false,
      "HasDifferentiationProperty": false,
      "Properties": [
        { "Name": "Product_ID", "TypeID": 2, "Required": true, "Minimum": null, "Maximum": null, "DecimalPlaces": 0, "Encrypted": false, "ValidationRegex": null, "Description": null },
        { "Name": "Qty",        "TypeID": 2, "Required": true, "Minimum": 1,    "Maximum": null, "DecimalPlaces": 0, "Encrypted": false, "ValidationRegex": null, "Description": null }
      ],
      "Constraints": [
        { "TypeID": 2, "Properties": "Product_ID,Product,2" }
      ]
    }
  ],
  "Security": [
    { "Name": "Product", "TypeID": 0, "RoleID": "ANONYMOUS", "Action": "get", "Record": 0, "Properties": "Title,Price", "RateLimit": { "MaxRequests": 100, "TimeWindowType": 2 } }
  ],
  "CustomEndpoints": [
    { "Name": "GetAllProduct", "Description": "Retrieves all products.", "Query": "SELECT * FROM [Product];" }
  ]
}
```

---

## Checklist for generating a payload

- [ ] Use the correct integer enums: property `TypeID` (String=1, Number=2, Boolean=3, Date=4); constraint `TypeID` (Unique=1, FK=2); security `TypeID` (Entity=0, CustomEndpoint=1, Schema=2).
- [ ] **Do not** include system properties (`ID`, `Owner`, `Created`) — they are added automatically.
- [ ] Foreign keys: add a `Number` property to hold the reference, then a constraint `"LocalColumn,ReferencedEntity[,OnDeleteLogic]"`.
- [ ] `DecimalPlaces` only for `Number`; `Encrypted`/`ValidationRegex` only for `String`; `Minimum`/`Maximum` mean *length* for strings and *value* for numbers/dates.
- [ ] Every entity referenced by an FK must be in the payload or already exist in the target app.
- [ ] To expose a custom endpoint, add a `Security` rule with `TypeID: 1` and `Action: "get"`.
- [ ] Grant columns explicitly in each security rule's `Properties` — a `null` grants no non-PK columns.
- [ ] For existing items, keep metadata identical to what is already in the app, or the import aborts.
```

