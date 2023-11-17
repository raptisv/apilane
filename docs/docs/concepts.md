# Concepts

## Core

- **Apilane Portal** - A service that acts as the web portal, offering visual tools for managing APIs served from `Apilane API`.
- **Apilane API** - A service that exposes http API endpoints that can be used from web and/or mobile applications for storing users and entity data records.
- **Apilane Installation** - A deployment of one `Apilane Portal` and (at least) one `Apilane API` services, where all share the same `InstallationKey`.
- **Storage provider** - The database system of choice where application data are persisted e.g. SqlServer, MySql or Sqlite.
- **Server** - Represents a reference to an `Apilane API` deployment. Each `Apilane Installation` supports multiple `Servers` meaning multiple `Apilane API` deployments [^1].

## Applications

- **Application** - The backend of a client application. Each `Apilane Installation` supports creating and managing unlimited Applications. Each Application has users, entites and relevant data that are persisted to one of the supported `storage providers`.
- **Entity** - An application entity that represents a conceptual entity of the client application built by the developer. An entity is backed up by a database table.
- **System Entity** - A predefined application entity used to support Apilane specific features. For example, the system entity `Users` holds information regarding `Application Users`.
- **Property** - An entity property that represents a specific piece of information that makes sense the the underlying client application. A property is backed up by a database table column.
- **System Property** - A predefined application entity property used to support Apilane specific features. For example, every `Entity` contains the property `Created` that holds the date that a record was persisted to the storage provider.

## Users

- **Apilane User** - A user that has access to the `Apilane Portal` and owns applications. A user can create, manage and delete owned applications. A user can share and/or unshare owned applications with other users which in turn acquire rights to the application.
- **Apilane Admin** - An `Apilane User` with admin rights that has access to all instance applications, servers and instance settings.
- **Application User** - A user that has registered to an Apilane `Application` through the developer web/mobile client application.


[^1]: For example, an `Apilane Installation` might consist of 1 `Apilane Portal` and 2 `Apilane API (servers)`. One `Server` migh be internal for testing applications with limited resources and the other `Server` acting as production server with increased resources. All `Servers` should be accesible from `Apilane Portal`.