# Entities & Properties

Entities and properties are the building blocks of your application's data model. An entity maps to a database table, and properties map to columns.

## Entities

An entity represents a conceptual object in your application — for example `Products`, `Orders`, or `Invoices`. When you create an entity, Apilane creates the underlying database table and automatically adds [system properties](#system-properties).

### System Entities

Every application is created with the following system entities:

| Entity | Purpose |
|---|---|
| **Users** | Stores application user accounts (email, username, password, roles, etc.) |
| **Files** | Stores file metadata (name, size, UID, public flag). Managed via the [Files](files.md) endpoints. |

System entities cannot be deleted. You can add custom properties to them just like any other entity.

### Change Tracking

Each entity can optionally have **change tracking** enabled. When enabled, Apilane stores a historical snapshot of every record before it is updated or deleted. This allows you to:

- View the full history of changes for any record
- Audit who changed what and when
- Retrieve previous versions of a record

Change tracking data can be accessed via the [GetHistoryByID](../api_reference.md#get-record-history) endpoint.

!!!info "Storage consideration"
    Change tracking increases storage usage since every update creates a history entry. Enable it only on entities where auditing is required.

## Properties

A property is a specific piece of data within an entity — like `Name`, `Price`, or `IsActive`. Each property has a type and optional validation rules.

### Property Types

| Type | Description | Database mapping | Example values |
|---|---|---|---|
| **String** | Text data | `NVARCHAR` / `TEXT` | `"hello"`, `"john@example.com"` |
| **Number** | Numeric data (integers and decimals) | `BIGINT` / `FLOAT` | `42`, `3.14`, `-100` |
| **Boolean** | True/false values | `BIT` / `TINYINT` | `true`, `false` |
| **Date** | Date and time values | `DATETIME` / `TIMESTAMP` | `"2025-01-15 10:30:00.000"` |

### System Properties

Every entity automatically includes these system properties:

| Property | Type | Description |
|---|---|---|
| **ID** | Number | Auto-incrementing primary key |
| **Owner** | Number | The user ID that created the record |
| **Created** | Number | Unix timestamp (milliseconds) when the record was created |

These properties are managed by Apilane and cannot be modified directly by application users.

### Property Validation

Each property supports the following validation options:

| Option | Applies to | Description |
|---|---|---|
| **Required** | All types | The property must have a value when creating a record |
| **Unique** | All types | No two records can have the same value |
| **Minimum** | String, Number | Minimum length (String) or minimum value (Number) |
| **Maximum** | String, Number | Maximum length (String) or maximum value (Number) |
| **Decimal Places** | Number | Number of decimal places to store |
| **Validation Regex** | String | A regular expression pattern the value must match |
| **Encrypted** | String | The value is encrypted at rest in the database |

!!!warning "Encrypted properties"
    Encrypted properties cannot be used in filters or sorting since the stored value is encrypted. Use encryption only for sensitive data like personal identifiers.

## Constraints

Constraints enforce data integrity rules between entities.

### Unique Constraint

Ensures that no two records in an entity share the same value for a given property. This is configured per-property via the **Unique** validation option.

### Foreign Key Constraint

Links a property to the `ID` column of another entity, enforcing referential integrity. When creating a foreign key, you choose the delete behavior:

| Behavior | Description |
|---|---|
| **No Action** | Prevents deleting a parent record if child records reference it |
| **Set Null** | Sets the foreign key property to `null` when the parent record is deleted |
| **Cascade** | Automatically deletes child records when the parent record is deleted |

!!!info "Example"
    If you have an `Orders` entity with a property `CustomerID` that is a foreign key to `Users.ID` with **Cascade** behavior — deleting a user automatically deletes all their orders.

## Differentiation Entity

A differentiation entity is a special system feature that partitions data across your application. See [Application > Differentiation Entity](application.md#differentiation-entity) for details.
