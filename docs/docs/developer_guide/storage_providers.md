# Storage Providers

Apilane supports four storage providers out of the box. Each application can use a different provider, and the choice is made when creating the application.

!!!info "What Apilane manages"
    On the storage provider level, Apilane handles creating, renaming, and deleting entities (tables) and properties (columns). It also manages unique and foreign key constraints. It does not provide tools beyond that, such as index management or manual query optimization.

## SQLite

**Best for:** Getting started, prototyping, small-to-medium applications.

No additional configuration is required. Apilane creates a SQLite database file automatically in the API's configured `FilesPath`.

| Pros | Cons |
|---|---|
| Zero configuration | Not ideal for high-concurrency workloads |
| Included with the API server | Single-file storage may limit scalability |
| Perfect for PoC and development | |
| Can support production for moderate workloads | |

!!!info "Migration path"
    You can start with SQLite and migrate to SQL Server, MySQL, or PostgreSQL later as your application grows.

## SQL Server

**Best for:** Enterprise applications, high-concurrency workloads.

You need to provide a connection string to an **existing empty database**. The Apilane API service must be able to reach the SQL Server instance. On first use, Apilane creates all required system tables and columns.

**Example connection string:**

```
Server=myserver.database.windows.net;Database=myapp_db;User Id=myuser;Password=mypassword;
```

!!!warning "Your responsibility"
    Database management (backups, scaling, availability, index optimization) is a developer concern. Apilane handles schema management only.

## MySQL

**Best for:** Open-source stacks, Linux-based deployments, cost-sensitive projects.

Same setup as SQL Server — provide a connection string to an existing empty database. Apilane creates all system tables on first use.

**Example connection string:**

```
Server=myserver;Database=myapp_db;User=myuser;Password=mypassword;
```

!!!warning "Your responsibility"
    Database management (backups, scaling, availability, index optimization) is a developer concern. Apilane handles schema management only.

## PostgreSQL

**Best for:** Open-source stacks, cloud-native deployments, applications requiring advanced SQL features.

Same setup as SQL Server and MySQL — provide a connection string to an existing empty database. Apilane creates all system tables on first use.

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

