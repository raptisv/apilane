# Storage providers

Apilane provides out-of-the-box support for Sqlite, SqlServer and MySql. That means that each application data can be stored in any of the supported storage providers.

!!!info "Note"
    On storage provider level, Apilane handles creating, renaming or deleting entities (tables) and properties (columns). It also handles unique and foreign key constaints. It does not provide any tools further than that like index management.

### Sqlite

For Sqlite there is no further configuration required. A new Sqlite database file will be generated to store your application data.

!!!info "Note"
    Sqlite can support requirements for most applications. It is perfect for developing a quick PoC, a test application but is can also support production applications if the requirements are not very high. You can start with Sqlite and migrate to another storage provider at a later stage.

### SqlServer

For SqlServer you will need to provide a connection string to an existing empty database. Apilane API service should be able to access the SqlServer instance. It will attempt to connect and create the system entites/properties. Database management regarding resources and availability is a developer concern.

### MySql

For MySql you will need to provide a connection string to an existing empty database. Apilane API service should be able to access the MySql instance. It will attempt to connect and create the system entites/properties. Database management regarding resources and availability is a developer concern.