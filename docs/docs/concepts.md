---
description: "The core concepts of Apilane: the Portal and the API, applications, entities and properties, users and roles, authentication and how clients call the API."
---

# Concepts

## Architecture

An Apilane deployment consists of two services that work together:

| Service | Purpose |
|---|---|
| **Apilane Portal** | Web-based management UI for creating applications, defining entities, configuring security, and viewing reports. It manages applications through its management API (`/api/v1`), which scripts and AI agents can call too with an agent key. The records, files and report data it shows are read by your browser straight from the API server. |
| **Apilane API** | HTTP API server that your client applications (web/mobile) call for data, authentication, and file operations. |

Both services share an **Installation Key** to authorize their internal communication.

## Core Terminology

- **Apilane Installation** — A deployment of one `Apilane Portal` and one or more `Apilane API` services, all sharing the same `InstallationKey`.
- **Server** — Represents a reference to an `Apilane API` deployment. An Installation supports multiple Servers[^1] for scenarios like separate testing and production environments.
- **Storage Provider** — The database system where application data is stored: SQLite, SQL Server, MySQL, or PostgreSQL.

## Applications

- **Application** — The backend of a client application. Each Installation supports unlimited Applications. An Application has its own entities, users, security rules, and storage provider.
- **Application Token** — A unique identifier (GUID) for each Application. Used in every API call to identify which application is being accessed.

## Entities & Properties

- **Entity** — A data object in your application (e.g., `Products`, `Orders`). Maps to a database table. See [Entities & Properties](developer_guide/entities_properties.md).
- **System Entity** — A built-in entity for platform features: `Users` stores application users, `AuthTokens` their sign-in tokens and `Files` file metadata, plus the differentiation entity if the application has one.
- **Property** — A field within an entity (e.g., `Name`, `Price`). Maps to a database column.
- **System Property** — A built-in property managed by Apilane. Every entity you create has `ID`, `Owner`, and `Created`.

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
- **Apilane Admin** — An Apilane User with the Admin role. Besides their own applications, an admin has the **Instance** screens: servers, users and agents, settings, backup, the instance audit log, the list of all applications and the data browser of any of them. An admin cannot change the entities or security of an application that is not their own or shared with them.
- **Collaborator** — An Apilane User an application was shared with. A collaborator can do everything the owner can, except sharing.
- **Apilane Agent** — An account for a script or an AI agent: a named Portal account that has no password and calls the management API with a key (`Authorization: Bearer apl_...`). An administrator adds it under **Instance > Agents**, and the owner of an application shares the application with it. An agent works with the applications shared with it, but cannot delete anything, create, clone or rebuild an application, or read its encryption key. See [AI Agent Guidelines for the Portal](developer_guide/ai_agent_portal.md).
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
4. Tokens expire after a configurable period of **inactivity** (default: 60 minutes) — a request made after more than a tenth of that time has passed since the token was last extended extends it again, so a token expires after about this time of inactivity (at least 90% of it)
5. Tokens can be renewed without re-authenticating

## API Access Pattern

Every API call requires an **Application Token** to identify the target application. It can be passed as:

- Query parameter: `?appToken={token}`
- Header: `x-application-token: {token}`

Authenticated endpoints additionally require the user's **AuthToken**.

[^1]: For example, an Installation might have a testing Server with limited resources and a production Server with increased resources. Both must be accessible from the Portal.
