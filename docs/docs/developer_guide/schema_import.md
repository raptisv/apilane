---
description: "Bulk-import entities, properties, constraints, security rules and custom endpoints into an Apilane application from one JSON document, written for AI agents."
---

# Schema Import (for AI agents)

Apilane can bulk-import **entities, properties, constraints, security rules and custom endpoints** into an existing application from a single JSON document. This page is a precise specification of that JSON so an AI agent (or any automation) can generate a valid payload from scratch.

Open the **Import** tab of the application in the Portal, paste the JSON into the *JSON payload* box, and click **Import**. (The same screen can also pre-fill the payload by diffing another application — pick it under *Load from another application* and click **Load diff** — which is a good way to see a real payload.) The **Help** button in the header of the tab holds a reference of the payload and an example.

**Load diff** computes what the other application has and this one lacks: the custom entities that are missing, the properties and constraints missing from entities this application already has, the security rules for entities and custom endpoints that this application has no rule for (rules that exist with other values are left out, and Schema rules are never listed; a rule is offered as the import accepts it, so a property the application would lack, such as a custom property of `Users`, is left out of its `Properties`, and a rule whose item does not offer its action is left out), and the custom endpoints that are missing. Entities in it carry `"IsNew"`, which the import ignores. Nothing is listed when nothing is missing.

The same payload can be sent to the Portal's management API (see [Calling the management API](#calling-the-management-api)): `POST /api/v1/applications/{appToken}/schema-import`, and `GET /api/v1/applications/{appToken}/schema-import/diff?Source={otherAppToken}` returns the diff in this shape.

---

## How the import behaves

- **Additive and idempotent.** The import only ever *creates* what is missing. Existing entities, properties, constraints, security rules and custom endpoints are never modified or deleted.
- **Existing items are validated, not overwritten.** If an entity/property/security rule already exists, its metadata is compared against the payload. If it is identical, it is skipped (a warning is returned). If it differs, **the import stops with an error** at that item.
- **Not atomic.** The items are applied one by one, each as its own transaction on the API server, which has **5 seconds**: a step that takes longer fails, for example adding a property to an entity with very many records. When one fails, the import stops there, everything applied before it stays applied, and the error names the place in the payload (e.g. `Entities[0].Properties[2].TypeID`). Sending the same payload again is safe: what was already applied is skipped.
- **List referenced entities first.** Entities without a foreign key are created first, then the ones whose foreign keys lead to `Users`; the others are created in the order they are listed. So list an entity before the entities whose foreign keys point to it (or make sure it already exists in the application).
- **System columns are added for you.** When a new entity is created, Apilane automatically adds its system properties (`ID`, `Owner`, `Created`, and `{DifferentiationEntity}_ID` when `HasDifferentiationProperty` is `true`). **Never** include them in `Properties`: a listed one is compared with the real one and the import stops if it differs.
- **Names of new items follow the rules of the Portal.** An entity name has 4 to 30 letters and underscores. A property name has 4 to 120 letters and underscores and must not end in `_Data`. A custom endpoint name has letters a-z and A-Z only, at most 80. A name that is refused stops the import before anything is applied, and so does a security rule that the Security tab would refuse (see [Security rules](#security-rules)). Names are matched ignoring case against what the application has already, so `product` finds `Product`.
- **Constraint references are checked before any changes.** Every property and referenced entity must belong to the application or the schema being imported and be a valid identifier. Invalid references, SQL fragments and unsupported constraint fields stop the entire request before any database or API-server changes. Errors name the constraint's place, such as `Entities[0].Constraints[1].Properties`.

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
| `Minimum` | long \| null | **String:** minimum length. **Number:** minimum value. Other types: `null` (the limits of a Date are stored but not checked when records are written). |
| `Maximum` | long \| null | **String:** maximum length. **Number:** maximum value. Other types: `null`. |
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
| `2` | Foreign key | `"LocalColumn,ReferencedEntity"` or `"LocalColumn,ReferencedEntity,OnDelete"` | `"Product_ID,Product"` · `"Product_ID,Product,ON_DELETE_CASCADE"` |

Unique constraints may use only existing or imported, unencrypted properties. List each property once; empty comma-separated elements are rejected. A null, empty or whitespace-only unique constraint is ignored.

For a foreign key, first add a custom `Number` property with `DecimalPlaces: 0` to hold the reference (e.g. `Product_ID`), then add the FK constraint pointing at the **referenced entity's** name. The FK targets that entity's `ID` primary key. The referenced entity must already exist or appear in the same payload; `Files` is not a valid target. System properties cannot be the local foreign-key property.

Property and entity names are matched ignoring case, trimmed around commas and stored with their actual schema spelling. Only identifiers are accepted, never SQL expressions or fragments.

**On delete** (the optional 3rd element; without it, no action) is written with its name, as the Portal stores it and **Load diff** returns it:

| Value | Behaviour |
|---|---|
| `ON_DELETE_NO_ACTION` | A record cannot be deleted while other records point to it |
| `ON_DELETE_SET_NULL` | The property of the records that point to it is set to null |
| `ON_DELETE_CASCADE` | The records that point to it are deleted too |

A foreign key of the same property that exists with another target or on-delete action stops the import. Equivalent constraints are skipped even when whitespace, name casing, unique-property order or the spelling of the default foreign-key action differs. Numeric on-delete values `0`, `1` and `2` are also accepted for the actions above; other values are rejected.

!!!info "IsSystem"
    A constraint may carry `"IsSystem": false`, which is what **Load diff** returns, or leave it out. `true` is refused: the system constraints come with a new entity and cannot be imported.

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
| `Name` | string | The target: the **entity name** (for `TypeID` 0), the **custom endpoint name** (for `TypeID` 1) or `Schema` (for `TypeID` 2). Required. |
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
| `2` | Schema | `Schema` | `get` |

**`Record` values** (`EndpointRecordAuthorization`):

| Value | Meaning |
|---|---|
| `0` | All records |
| `1` | Owned only — restricted to rows whose `Owner` equals the current user's ID |

**Roles.** Apilane always evaluates a user's custom roles plus the two built-ins:

| `RoleID` | Applies to |
|---|---|
| `ANONYMOUS` | Any request without a valid auth token |
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
| `0` | No limit |
| `1` | Per second |
| `2` | Per minute |
| `3` | Per hour |

Every rule is checked before anything is applied, with the checks of the Security tab (`PUT /api/v1/applications/{appToken}/security/rules`), against the entities, properties and custom endpoints the application has **and** the ones the payload creates (a rule for a new entity may list the properties the payload gives it, `Owner` and `Created` included). The import answers 400 `VALIDATION`, one error for each rule that is refused, naming its place (`Security[2].Action`), and applies nothing. A rule is refused when:

- `TypeID` is not 0, 1 or 2.
- `Name` is not an entity (`TypeID` 0) or a custom endpoint (`TypeID` 1) that the application or the payload has, or is not `Schema` (`TypeID` 2). The letter case of the name does not matter, and the rule is stored under the name the entity or custom endpoint has (`Schema` for a Schema rule): the Security tab lists only rules that spell it so, and its next save would delete the others.
- `Action` is not `get`, `post`, `put` or `delete`, or the item does not offer it: a Schema rule and a custom endpoint have only `get`, `Users` has no `post`, `Files` has no `put`, and a read-only entity has only `get`.
- `Record` is not 0 or 1.
- `Properties` lists a name that is not a property of the entity for that action. Property names are compared exactly, upper and lower case included (`name` is not `Name`), as the Security tab does, and a space after a comma counts. The primary key can never be listed, `post` and `put` can list only the properties a record can write (not `Owner`, `Created` and the other system properties), a `post` to `Files` and a `delete` list none, and only an entity rule has properties.
- The rule has the same `TypeID`, `Name`, `RoleID` and `Action` as an earlier rule of the payload but other values. An identical one is skipped with a warning, as a rule the application has already.

A rule with several problems is reported for the first one, checked in this order: `TypeID`, `Name`, `Action`, `Record`, `Properties`. Apart from the name, the rule that is stored is the one you sent, not a cleaned-up copy. The role is not checked: any text is a role, as on the Security tab, so a role no user holds is accepted and a misspelled one gives a rule that never applies. Write `ANONYMOUS` and `AUTHENTICATED` in capitals and custom roles in lower-case letters as they are in the `Roles` property of the users. `MaxRequests` is 1 or more.

!!!warning "Record"
    `Record: 1` (owned records only) has no effect on `ANONYMOUS`, on `post` or on `Users`: an anonymous caller has no owner and sees every record. See [Security](security.md#how-several-rules-combine).

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
| `Query` | string | The SQL. Write it for your storage provider (SQLite / SQL Server / MySQL / PostgreSQL). Parameters are `{ParamName}` placeholders and are **big-integer (long) only**. Multiple `;`-separated statements return multiple result sets. |

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
        { "Name": "Quantity",   "TypeID": 2, "Required": true, "Minimum": 1,    "Maximum": null, "DecimalPlaces": 0, "Encrypted": false, "ValidationRegex": null, "Description": null }
      ],
      "Constraints": [
        { "TypeID": 2, "Properties": "Product_ID,Product,ON_DELETE_CASCADE" }
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

## Calling the management API

Send the payload with `POST {Portal}/api/v1/applications/{appToken}/schema-import`. An AI agent authenticates with its key, `Authorization: Bearer apl_...`, and the application must be shared with it (see [Managing an instance with an agent key](ai_agent_guidelines.md#managing-an-instance-with-an-agent-key)). A script that signs in as a person sends the session cookie and the header `X-Apilane-Portal: 1` on the write. `GET .../schema-import/diff?Source={otherAppToken}` answers in the same shape; `Source` is the token of another application that the caller can open.

A success answers `{ "Warnings": [ ... ] }`, one text for each item that was already there and skipped. A failure answers the error body of the management API: `Code`, `Message` and, for a validation error, `Errors`, each naming the place in the payload (`Entities[0].Properties[2].TypeID`).

| Status | When |
|---|---|
| 400 `VALIDATION` | The payload is not valid, an existing item differs, or the API server refused a step |
| 403 `FORBIDDEN` | A caller who is not an administrator lists constraints for `Users` or `Files` |
| 404 `NOT_FOUND` | The application, or the `Source` of a diff, is not one the caller can see |
| 409 `CONFLICT` | The stored security rules cannot be read |
| 502 `UPSTREAM_ERROR` | The API server failed or could not be reached |

A `Warning` response header means the import went through but the API server could not be refreshed afterwards.

---

## Checklist for generating a payload

- [ ] Use the correct integer enums: property `TypeID` (String=1, Number=2, Boolean=3, Date=4); constraint `TypeID` (Unique=1, FK=2); security `TypeID` (Entity=0, CustomEndpoint=1, Schema=2).
- [ ] **Do not** include system properties (`ID`, `Owner`, `Created`) — they are added automatically.
- [ ] Foreign keys: add a `Number` property to hold the reference, then a constraint `"LocalColumn,ReferencedEntity[,OnDelete]"` with `OnDelete` written as `ON_DELETE_NO_ACTION`, `ON_DELETE_SET_NULL` or `ON_DELETE_CASCADE`.
- [ ] `DecimalPlaces` only for `Number`; `Encrypted`/`ValidationRegex` only for `String`; `Minimum`/`Maximum` mean *length* for strings and *value* for numbers, and stay `null` for Boolean and Date.
- [ ] Every entity referenced by an FK must already exist in the target app, or be in the payload before the entities that reference it.
- [ ] To expose a custom endpoint, add a `Security` rule with `TypeID: 1` and `Action: "get"`.
- [ ] Grant columns explicitly in each security rule's `Properties` — a `null` grants no non-PK columns.
- [ ] Make every security rule name an item the application or the payload has, an action that item offers, and properties that exist for that action: the import refuses the whole payload otherwise.
- [ ] Spell `ANONYMOUS`, `AUTHENTICATED` and custom role names exactly: the role is not checked, and a misspelled role never applies.
- [ ] For existing items, keep metadata identical to what is already in the app, or the import aborts.

