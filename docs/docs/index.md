---
description: "Apilane is a backend platform with tools to develop and manage APIs for mobile and web apps, including database management, user authentication and file storage."
---

# Overview

## What Is Apilane?

Apilane is a backend platform that provides tools for developing and managing APIs for mobile and web client applications.
It offers features such as database management, user authentication, file storage and more, aiming to simplify backend development.

![Apilane](assets/overview.png)

## Key Features

| Feature | Description |
|---|---|
| **Data Management** | Create entities and properties via the Portal. Full CRUD with [filtering, sorting, and paging](developer_guide/filtering_sorting.md). |
| **User Authentication** | Built-in registration, login, token management, password reset, and email confirmation. |
| **File Storage** | [Upload, download, and manage files](developer_guide/files.md) with role-based access control. |
| **Role-Based Security** | Granular [access control](developer_guide/security.md) on entity, property, and custom endpoint level. |
| **Transactions** | [Atomic multi-operation transactions](developer_guide/transactions.md) with cross-referencing between operations. |
| **Custom Endpoints** | Run [custom SQL queries](developer_guide/custom_endpoints.md) as API endpoints with parameterized inputs. |
| **Aggregation & Stats** | Run aggregate functions (Count, Sum, Avg, Min, Max) and distinct queries on your data. |
| **Reports** | Build [visual reports](developer_guide/reports.md) (grids and pie, line, bar, radar and stacked bar charts) from the Portal. |
| **Email Templates** | Configurable [email templates](developer_guide/email_templates.md) for registration confirmation and password reset. |
| **Change Tracking** | Optional [entity history](developer_guide/entities_properties.md#change-tracking) to audit record changes over time. |
| **Schema API** | Retrieve your [application schema](api_reference.md#get-application-schema) programmatically at runtime. |
| **.NET SDK** | Type-safe [.NET SDK](developer_guide/sdk.md) with builder pattern for the account, data, transaction, file, stats, custom endpoint and schema calls. |
| **JavaScript SDK** | A single-file [JavaScript SDK](https://github.com/raptisv/apilane/tree/main/sdk/Apilane.Js) for browsers and Node.js 18+. |

## Why Apilane?

* **Rapid Development** — Pre-built backend services (authentication, data management, file storage) let you focus on your frontend.

* **Scalability** — Built on [Microsoft Orleans](https://github.com/dotnet/orleans) for distributed state management, ensuring your application remains responsive as it grows.

* **Simplicity** — Easy-to-use REST API and Portal UI. No backend expertise required.

* **Security** — Data encryption, role-based access control, IP allow/block lists, and sliding window rate limiting.

* **Storage Providers** — Out-of-the-box support for [SQLite](https://en.wikipedia.org/wiki/SQLite), [SQL Server](https://en.wikipedia.org/wiki/Microsoft_SQL_Server), [MySQL](https://en.wikipedia.org/wiki/MySQL) and [PostgreSQL](https://en.wikipedia.org/wiki/PostgreSQL).

## Quick Start

Execute the provided [docker-compose.yaml](assets/docker-compose.yaml) to spin up the Portal and API. The two share an **installation key**: the first command stores a random one in a `.env` file next to the compose file, only if it is not there yet. Keep that file for every later run.

```bash
grep -qs "^APILANE_INSTALLATION_KEY=." .env || echo "APILANE_INSTALLATION_KEY=$(openssl rand -hex 32)" >> .env
docker-compose -p apilane up -d
```

Access the Portal at [http://localhost:5000](http://localhost:5000) with the default credentials:

- **Email:** `admin@admin.com`
- **Password:** `admin`

For a complete walkthrough, including the addresses the two services use to reach each other, follow the [Getting Started](getting_started.md) guide.
