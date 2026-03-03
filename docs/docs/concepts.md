# Concepts

## Architecture

An Apilane deployment consists of two services that work together:

| Service | Purpose |
|---|---|
| **Apilane Portal** | Web-based management UI for creating applications, defining entities, configuring security, and viewing reports. |
| **Apilane API** | HTTP API server that your client applications (web/mobile) call for data, authentication, and file operations. |

Both services share an **Installation Key** to authorize their internal communication.

## Core Terminology

- **Apilane Installation** — A deployment of one `Apilane Portal` and one or more `Apilane API` services, all sharing the same `InstallationKey`.
- **Server** — Represents a reference to an `Apilane API` deployment. An Installation supports multiple Servers[^1] for scenarios like separate testing and production environments.
- **Storage Provider** — The database system where application data is stored: SQLite, SQL Server, or MySQL.

## Applications

- **Application** — The backend of a client application. Each Installation supports unlimited Applications. An Application has its own entities, users, security rules, and storage provider.
- **Application Token** — A unique identifier (GUID) for each Application. Used in every API call to identify which application is being accessed.

## Entities & Properties

- **Entity** — A data object in your application (e.g., `Products`, `Orders`). Maps to a database table. See [Entities & Properties](developer_guide/entities_properties.md).
- **System Entity** — A built-in entity for platform features. `Users` stores application users; `Files` stores file metadata.
- **Property** — A field within an entity (e.g., `Name`, `Price`). Maps to a database column.
- **System Property** — A built-in property managed by Apilane. Every entity has `ID`, `Owner`, and `Created`.

### Property Types

| Type | Description | Examples |
|---|---|---|
| **String** | Text data | `"hello"`, `"user@example.com"` |
| **Number** | Integer or decimal values | `42`, `3.14` |
| **Boolean** | True/false values | `true`, `false` |
| **Date** | Date and time | `"2025-01-15 10:30:00.000"` |

### Constraints

| Type | Description |
|---|---|
| **Unique** | No two records can share the same value |
| **Foreign Key** | Links a property to the `ID` of another entity, with configurable delete behavior (No Action, Set Null, Cascade) |

## Users & Roles

- **Apilane User** — A user with access to the Portal who can create and manage applications.
- **Apilane Admin** — An Apilane User with admin rights and access to all applications and instance settings.
- **Application User** — A user registered to an Application through the client app's registration flow.

### Built-in Roles

Every Application has two built-in access levels used in [security rules](developer_guide/security.md):

| Role | Description |
|---|---|
| **ANONYMOUS** | Any request without an authentication token |
| **AUTHENTICATED** | Any request with a valid authentication token |

You can create custom roles (e.g., `admin`, `manager`, `editor`) and assign them to users. Security rules can target any combination of built-in and custom roles.

## Authentication

Apilane uses token-based authentication:

1. A user registers or logs in via the [Account endpoints](api_reference.md#authentication)
2. On successful login, an **AuthToken** (GUID) is returned
3. The client includes this token in subsequent requests via the `Authorization` header
4. Tokens expire after a configurable period of **inactivity** (default: 60 minutes) — each authenticated request resets the timer
5. Tokens can be renewed without re-authenticating

## API Access Pattern

Every API call requires an **Application Token** to identify the target application. It can be passed as:

- Query parameter: `?appToken={token}`
- Header: `x-application-token: {token}`

Authenticated endpoints additionally require the user's **AuthToken**.

[^1]: For example, an Installation might have a testing Server with limited resources and a production Server with increased resources. Both must be accessible from the Portal.
