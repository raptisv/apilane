---
description: "Choose a storage provider for an Apilane application: SQLite, SQL Server, MySQL or PostgreSQL, and what Apilane manages for each."
---

# Storage Providers

Apilane supports four storage providers out of the box. Each application can use a different provider, and the choice is made when creating the application.

!!!info "What Apilane manages"
    On the storage provider level, Apilane handles creating, renaming, and deleting entities (tables) and properties (columns). It also manages unique and foreign key constraints. It does not provide tools beyond that, such as index management or manual query optimization.

    When you save a new application, Apilane creates its tables: those of the system entities (`Users`, `AuthTokens`, `Files` and the differentiation entity, if any) and four helper tables prefixed `H_` (`H_Entity_Change_Tracking`, `H_Auth_Email_Confirmation_Tokens`, `H_Auth_Password_Reset_Tokens` and `H_Email_Templates`).

!!!warning "Give the application its own database"
    On SQL Server, MySQL and PostgreSQL, **Rebuild** and **Delete** drop **every** table of the connected database (of the current schema on PostgreSQL), not only the ones Apilane created. Delete leaves the empty database in place.

## SQLite

**Best for:** Getting started, prototyping, small-to-medium applications.

No additional configuration is required. Apilane creates the database file `{FilesPath}/{applicationToken}/{applicationToken}.db` automatically in the API's configured `FilesPath`. Back up that folder: it also holds the uploaded files when `LocalFileSystem` [file storage](file_storage_providers.md) is used.

| Pros | Cons |
|---|---|
| Zero configuration | Not ideal for high-concurrency workloads |
| Included with the API server | Single-file storage may limit scalability |
| Perfect for PoC and development | |
| Can support production for moderate workloads | |

!!!info "Migration path"
    The database type cannot be changed once an application exists. To move an application from SQLite to SQL Server, MySQL or PostgreSQL, **clone** it (menu of its card on the Applications page), choose the new database type and connection string, and switch on **Clone data** to copy the records. The copy has a new application token, and the databases differ, so review it before you switch your client over. Uploaded files are never cloned.

## SQL Server

**Best for:** Enterprise applications, high-concurrency workloads.

You need to provide a connection string to an **existing empty database**. The Apilane API service must be able to reach the SQL Server instance. When you save the new application, Apilane connects with the connection string (a wrong one is refused there) and creates all required system tables and columns.

**Example connection string:**

```
Server=myserver.database.windows.net;Database=myapp_db;User Id=myuser;Password=mypassword;
```

!!!tip "Certificate"
    Add `TrustServerCertificate=true;` to the connection string when the server has no certificate that the API's host trusts (a self-signed one, for example): the SQL Server driver encrypts the connection and checks the certificate by default.

!!!warning "Your responsibility"
    Database management (backups, scaling, availability, index optimization) is a developer concern. Apilane handles schema management only.

## MySQL

**Best for:** Open-source stacks, Linux-based deployments, cost-sensitive projects.

Same setup as SQL Server — provide a connection string to an existing empty database. Apilane creates all system tables when you save the new application.

**Example connection string:**

```
Server=myserver;Database=myapp_db;User=myuser;Password=mypassword;UseXaTransactions=false;
```

!!!warning "Keep `UseXaTransactions=false;`"
    Apilane runs schema changes, custom endpoints, batches and other requests inside a transaction. With the MySQL driver's default (XA transactions) MySQL refuses a schema change made inside a transaction, so changing an entity, importing or rebuilding an application fails. MySQL also commits a schema change (create, alter, drop, rename) at once, so a schema change that fails halfway is not rolled back.

!!!info "Character set"
    Create the database with the `utf8mb4` character set (the MySQL 8 default), for example `CREATE DATABASE myapp_db CHARACTER SET utf8mb4;`. Apilane's tables inherit the database default, and `utf8mb3`/`latin1` databases cannot store or filter characters such as emoji.

!!!warning "Your responsibility"
    Database management (backups, scaling, availability, index optimization) is a developer concern. Apilane handles schema management only.

## PostgreSQL

**Best for:** Open-source stacks, cloud-native deployments, applications requiring advanced SQL features.

Same setup as SQL Server and MySQL — provide a connection string to an existing empty database. Apilane creates all system tables when you save the new application. The database user needs the `CREATE TABLE` privilege there, and Apilane works in the user's current schema (usually `public`).

**Example connection string:**

```
Host=myserver;Database=myapp_db;Username=myuser;Password=mypassword;
```

!!!warning "Your responsibility"
    Database management (backups, scaling, availability, index optimization) is a developer concern. Apilane handles schema management only.

## Choosing a Provider

| Criteria | SQLite | SQL Server | MySQL | PostgreSQL |
|---|---|---|---|---|
| Setup complexity | None | Moderate | Moderate | Moderate |
| Cost | Free | Licensed / Cloud | Free / Cloud | Free / Cloud |
| Concurrent users | Low-Medium | High | High | High |
| Hosting | Bundled with API | Separate server | Separate server | Separate server |
| Best for | Dev / Small apps | Enterprise | Open-source stacks | Open-source / Cloud-native |

