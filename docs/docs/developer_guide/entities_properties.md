---
description: "Define the data model of an Apilane application: entities map to tables and properties to columns, with types and constraints."
---

# Entities & Properties

Entities and properties are the building blocks of your application's data model. An entity maps to a database table, and properties map to columns.

## Entities

An entity represents a conceptual object in your application — for example `Products`, `Orders`, or `Invoices`. When you create an entity (the **Entities** tab of the application in the Portal, **New entity**), Apilane creates the underlying database table and automatically adds [system properties](#system-properties).

A new entity is private: until you add a [security rule](security.md) for it, only the people who manage the application (its owner, its collaborators and the administrators of the instance), working in the Portal, can read or change its records.

### System Entities

Every application is created with the following system entities:

| Entity | Purpose |
|---|---|
| **Users** | Stores application user accounts (email, username, password, roles, etc.) |
| **AuthTokens** | Stores the sign-in tokens. Read-only through the API |
| **Files** | Stores file metadata: name, UID, size (in MB), owner and creation time. Managed via the [Files](files.md) endpoints. |

If the application has a [differentiation entity](application.md#differentiation-entity), it is a system entity too.

System entities cannot be renamed or deleted. You can add custom properties to `Users` and to the differentiation entity just like to any other entity; `Files` and `AuthTokens` take none. Only an administrator can change the constraints of a system entity.

### Renaming and deleting

Rename and Delete are in the entity's menu (properties have the same actions on their page); deleting asks you to type the name and removes all its data. Neither is possible for a system entity or property, for an entity that a foreign key of any entity points to, or for a property that is part of a constraint: remove the constraint first. Security rules, reports, custom endpoint SQL and the default sorting that name the old name are not updated, and deleting does not remove them, so change them yourself. On PostgreSQL do not rename a property only by changing its letter case.

### Change Tracking

Each entity can optionally have **record change tracking** enabled when you create or edit it (it is on by default for `Users`). When enabled, Apilane stores a snapshot of the record, as it was, every time it is updated or deleted through the Data API, and when a user updates their own profile. This allows you to:

- View the history of changes for a record
- Audit who changed what and when
- Retrieve previous versions of a record

Changes made by [custom endpoint](custom_endpoints.md) SQL are not tracked.

The history of a record is returned by the [GetHistoryByID](../api_reference.md#get-record-history) endpoint, which needs read access to the entity, and the record must still exist. Each entry has `History_Record_Created`, `History_Record_Owner` (the user who made the change) and the properties as they were. The **History** button of the Portal [data browser](#data-browser) shows the same, and **Clear history** (in **More actions**) deletes it. The snapshots of a deleted record stay in the table `H_Entity_Change_Tracking`, but the API cannot return them once the record is gone. Encrypted properties show their encrypted value in the history.

!!!info "Storage consideration"
    Change tracking increases storage usage and slows updates down, since every update or delete writes a history entry. Enable it only on entities where auditing is required.

### Default sorting

Open the **Sorting** tab of an entity (**Default sorting** in its menu) to set the order in which `Data/Get` returns its records when the request has no `sort` parameter. Add properties, switch each between ascending and descending, move them up or down and save. Records are ordered by the first property; the next ones order the records that tie. Without a default sorting, records come back by ID, ascending. The default does not apply to `Files/Get`.

!!!warning "Sort only by readable properties"
    The default sorting is checked against the caller's property access like any `sort` parameter: if a role may not read a property that is in it, `Data/Get` fails for that role.

### Data browser

The **Data** tab of the application (and **Data** in an entity's menu) opens the records of an entity as a grid. Sort by a column, filter under the headings, add, edit and delete records, export the rows on screen as CSV, upload files, register users, and open the **History** of a record when the entity tracks changes. The browser calls the same API as your client app, but the people who manage the application are held back by neither the [security rules](security.md) nor the rate limits that apply to users. The checks on the data itself (required values, limits, unique values and foreign keys) still run.

## Properties

A property is a specific piece of data within an entity — like `Name`, `Price`, or `IsActive`. Each property has a type and optional validation rules. In the Portal, open an entity to see its properties and add one with **New property**. The type, **Required**, **Encrypted**, the decimal places and the maximum length of a String cannot be changed after the property is created.

### Property Types

| Type | Description | Example values |
|---|---|---|
| **String** | Text data | `"hello"`, `"john@example.com"` |
| **Number** | Numeric data (integers and decimals) | `42`, `3.14`, `-100` |
| **Boolean** | True/false values | `true`, `false` |
| **Date** | Date and time values | `1736937000000`, `"2025-01-15 10:30:00.000"` |

The columns are created as follows:

| Type | SQLite | SQL Server | MySQL | PostgreSQL |
|---|---|---|---|---|
| **String** | `TEXT` | `NVARCHAR(n)` (up to 4,000), else `NVARCHAR(MAX)` | `VARCHAR(n)` (up to 16,383), else `TEXT` | `VARCHAR(n)` (up to 10,485,760), else `TEXT` |
| **Number** | `INTEGER`, or `NUMERIC(18,n)` | `BIGINT`, or `DECIMAL(18,n)` | `BIGINT`, or `DECIMAL(18,n)` | `BIGINT`, or `NUMERIC(18,n)` |
| **Boolean** | `BOOLEAN` | `BIT` | `BOOL` | `BOOLEAN` |
| **Date** | `BIGINT` | `BIGINT` | `BIGINT` | `BIGINT` |

`n` is the maximum length you set when you create a String property (leave it empty for no limit), or the decimal places of a Number. A Date is stored as a Unix timestamp in milliseconds (UTC). To write or filter one, send a Unix timestamp (10 digits for seconds, 13 for milliseconds) or text as `yyyy-MM-dd`, `yyyy-MM-dd HH:mm`, `yyyy-MM-dd HH:mm:ss` or `yyyy-MM-dd HH:mm:ss.fff`, read as UTC. Reading returns the number of milliseconds.

### System Properties

Every entity automatically includes these system properties:

| Property | Type | Description |
|---|---|---|
| **ID** | Number | Auto-incrementing primary key |
| **Owner** | Number | The ID of the user that created the record (`null` for an anonymous caller). It is a foreign key to `Users.ID` (no action), so a user who still owns records cannot be deleted |
| **Created** | Date | When the record was created, set by Apilane. Returned as a Unix timestamp in milliseconds (UTC) |

If the application has a differentiation entity, an entity created with the differentiation option also gets the system property `{Entity}_ID`.

These properties are managed by Apilane and cannot be modified directly by application users.

### Property Validation

Each property supports the following validation options:

| Option | Applies to | Description |
|---|---|---|
| **Required** | All types | The property must have a value when creating a record |
| **Unique** | All types except encrypted Strings | No two records can have the same value. Set as a [constraint](#constraints) of the entity, not on the property. A String with no maximum length (`NVARCHAR(MAX)` on SQL Server, `TEXT` on MySQL) cannot be unique |
| **Minimum** | String, Number | Minimum length (String) or minimum value (Number) |
| **Maximum** | String, Number | Maximum length (String) or maximum value (Number) |
| **Decimal Places** | Number | Number of decimal places to store |
| **Validation Regex** | String | A .NET regular expression. The value is accepted when the pattern is found anywhere in it, so anchor it (`^...$`) to match the whole value |
| **Encrypted** | String | The value is encrypted at rest in the database |

!!!warning "Encrypted properties"
    The API does not refuse a filter or a sort on an encrypted property, but it compares and orders the stored, encrypted text: a filter on the readable value never matches. Encrypted properties also cannot be part of a unique constraint, and record history shows their encrypted value. Use encryption only for sensitive data like personal identifiers.

### Names and limits

- An entity name has 4 to 30 characters, a property name 4 to 120. Both take letters (a–z, A–Z) and underscore only, and a property name cannot end in `_Data`. Names are unique ignoring letter case.
- **Decimal Places** (0 to 8) is set when the property is created. Extra digits in a value are cut off, not rounded; the column holds at most 18 digits in total.
- **Minimum** and **Maximum** lie between -9,007,199,254,740,991 and 9,007,199,254,740,991. The maximum length of a String is the size of the column: up to 4,000 on SQL Server, 16,383 on MySQL and 10,485,760 on PostgreSQL; leave it empty for no limit. An encrypted String is checked against its maximum but stored without a limit.
- A **Minimum**, and the **Maximum** of a Number, can be changed later; the **Maximum** of a String cannot.

## Constraints

Constraints enforce data integrity rules between entities. They are managed on the **Constraints** tab of an entity in the Portal: add or remove constraints in the list, then save them together.

### Unique Constraint

Ensures that no two records in an entity share the same value for a given property, or the same combination of values for several properties.

### Foreign Key Constraint

Links a property to the `ID` column of another entity, enforcing referential integrity. The property must be a custom **Number** property with 0 decimal places, and the other entity can be any entity except `Files`. (An entity you create also has a system foreign key from `Owner` to `Users`.) When creating a foreign key, you choose the **On delete behavior**. To change the delete behavior of an existing foreign key, remove it, save, add it again and save.

| Behavior | Description |
|---|---|
| **On delete no action** | Prevents deleting a parent record if child records reference it |
| **On delete set null** | Sets the foreign key property to `null` when the parent record is deleted |
| **On delete cascade** | Automatically deletes child records when the parent record is deleted |

!!!info "Example"
    If you have an `Orders` entity with a property `CustomerID` that is a foreign key to `Users.ID` with **Cascade** behavior — deleting a user automatically deletes all their orders.

## Differentiation Entity

A differentiation entity is a special system feature that partitions data across your application. See [Application > Differentiation Entity](application.md#differentiation-entity) for details.
