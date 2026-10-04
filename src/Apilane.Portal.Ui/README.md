# Apilane Portal UI

The Portal's user interface: a Vue 3 single-page app. It is built into static files that the
Portal (ASP.NET, `src/Apilane.Portal`) serves under `/ui/`, and it talks to the Portal's
management API under `/api/v1`.

Every page of the Portal has a screen here. The older Razor pages still run next to this UI until
they are removed: [MIGRATION.md](MIGRATION.md) lists every page with its screen and endpoints, what
behaves differently on purpose, the decisions still open and the steps of that removal.

Node is needed only to work on this folder. `dotnet build`, `dotnet test` and the Portal itself
never call it; the Docker image builds the UI in its own stage. A `dotnet publish` made outside
Docker includes whatever is in `src/Apilane.Portal/wwwroot/ui` at that moment (the last local
build, or nothing): run `npm run build` first.

## Prerequisites

Node 22.12 or newer (the Docker image builds with Node 24, see `src/Apilane.Portal/Dockerfile`).

On Windows: `winget install OpenJS.NodeJS.LTS`. If PowerShell refuses to run `npm` ("running
scripts is disabled on this system"), run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
once, or call `npm.cmd` instead.

## Commands

Run them in this folder. `npm ci` once after cloning (and after pulling dependency changes).

| Command | What it does |
|---|---|
| `npm run dev` | Dev server with hot reload at <http://localhost:5173/ui/>. |
| `npm run build` | Type-checks, then builds into `../Apilane.Portal/wwwroot/ui`. |
| `npm run api:types` | Regenerates `src/lib/api-types.ts` from `openapi/portal-v1.json`. |
| `npm test` | Runs the unit tests (`src/**/*.test.ts`) once. |

## Working on the UI

1. Start the Portal as usual (Visual Studio, or `dotnet run --project src/Apilane.Portal` from the repository root). It listens on <http://localhost:5000>.
2. `npm run dev` here, and browse <http://localhost:5173/ui/>.

The dev server answers `/ui/...` itself (the login page included) and forwards everything else (the
API, `/swagger`, and the Razor pages until they are removed) to the Portal, so the browser sees one site and the login cookie works
as in production. If the Portal runs on another address, set `PORTAL_URL` when starting the dev server:

```powershell
$env:PORTAL_URL='http://localhost:5010'; npm run dev
```

```bash
PORTAL_URL=http://localhost:5010 npm run dev
```

Debugging: use the browser's developer tools; breakpoints land in the `.vue` and `.ts` source
files. The [Vue Devtools](https://devtools.vuejs.org/) browser extension shows components and
their state. In VS Code install the recommended "Vue - Official" extension for type-checking in
templates.

`npm run dev` does not type-check. To check types, and to see the built UI exactly as the Portal
serves it, run `npm run build` and browse <http://localhost:5000/ui/>.

## When the API changes

The API contract lives in `openapi/portal-v1.json` and is written by the Portal's tests, never by
hand:

1. Rewrite the contract. Run this one from the repository root.

   ```powershell
   $env:UPDATE_OPENAPI='1'; dotnet test tests/Apilane.Portal.Tests --filter "FullyQualifiedName~OpenApi_Document_Should_Match"; Remove-Item Env:UPDATE_OPENAPI
   ```

   ```bash
   UPDATE_OPENAPI=1 dotnet test tests/Apilane.Portal.Tests --filter "FullyQualifiedName~OpenApi_Document_Should_Match"
   ```

   While the variable is set, the contract test rewrites the file instead of checking it, so do
   not leave it set in a terminal.
2. `npm run api:types` (in this folder).
3. `npm run build`: the compiler points at every screen the change affects.

Commit `openapi/portal-v1.json` and `src/lib/api-types.ts` together.

## Adding a screen

1. Add the API endpoints in `src/Apilane.Portal/Api/V1` (request and response classes in `Api/V1/Contracts`) and their tests in `tests/Apilane.Portal.Tests`.
2. Rewrite the contract with `UPDATE_OPENAPI` (step 1 of "When the API changes").
3. `npm run api:types`.
4. Add the page under `src/pages/` and its route in `src/router.ts`:
   - A screen for a signed-in user is a child of the `AppShell` route. Nothing else is needed: the
     router sends a visitor without a session to the login page and brings them back afterwards.
     Add `meta.requiresAdmin` for an administrator screen.
   - A screen that must work without a session (sign in, password reset) is a child of an
     `AuthLayout` route, which carries `meta.public`. Add `meta.guestOnly` when a signed-in user has
     no use for it. Such a screen calls the API through `unwrapAnonymous` and never calls `useSession()`.
   - A screen of one application (`/apps/:appToken/...`) goes under `src/pages/application/` and is a
     child of the `apps/:appToken` route, whose component is `AppLayout`. Name the route `app-<segment>`
     (`app-entities` for `entities`). The screen gets the application from `useApplication()`: it is
     already loaded, and an unknown application never reaches the screen. See "Screens of one
     application" below.
   - A screen of one entity (`/apps/:appToken/entities/:entity/...`) is a child of the
     `entities/:entity` route, whose component is `EntityLayout`. Name the route `app-entity-<tab>`
     and give it a line in `tabs` of `src/layouts/EntityLayout.vue`. The screen gets the entity from
     `useEntity()`. See "Screens of one entity" below.
5. Add its navigation entry in `src/layouts/SidebarNav.vue` when it has one. For a section of an
   application, the entry is its line in `sections` of `src/layouts/AppLayout.vue`.

### Screens of one application

`AppLayout` loads `GET /api/v1/applications/{appToken}` once for all screens under
`/apps/:appToken` and shows the breadcrumb (Applications / application, with its server / section)
and the section tabs. The section name in the breadcrumb is the route's `meta.title`.

```ts
const { application, reload } = useApplication()

// AppLayout gives every application a fresh screen, so the token never changes under a screen.
const appToken = application.value.Token
```

- `application.value` is the `ApplicationResponse`: `Name`, `Token`, `Online`, `DatabaseType`,
  `HasConnectionString`, `Server.ServerUrl` (for `apiServer`), `IsOwner`, `DifferentiationEntity`,
  `CustomEndpointCount`.
- Call `reload()` after a write that changes what that answer carries (the name, online/offline,
  the number of custom endpoints). After a rename, status change, delete or sharing change of the
  application also call `useApplications().reload()`, for the sidebar switcher and the cards ('Shared with N')
  (`ApplicationStatusDialog` and `DeleteApplicationDialog` do that themselves).
- A tab is the current one when the address continues with its segment
  (`/apps/<token>/entities/...`), so the sub-screens of a section keep its tab lit.
- One route is not named after its segment: the Import tab (`/apps/:appToken/import`) is `app-schema-import`, because
  `app-import` is the page that creates an application from an export (`/apps/import`). Its line in `sections` carries
  that `name`.
- A screen only the owner may open (sharing) carries `meta.requiresOwner` on its route and
  `ownerOnly` on its line in `sections`: the tab is hidden from collaborators, and a collaborator
  who opens the address gets the Forbidden state from `AppLayout` instead of the screen.

### Screens of one entity

`EntityLayout` sits inside `AppLayout`. It loads `GET /api/v1/applications/{appToken}/entities/{entity}`
once for all screens under `/apps/:appToken/entities/:entity` and shows the back link to the entities
list, the entity's name and description, and its tabs (Properties, Constraints, Sorting, Data,
Security). An unknown entity (names are case-sensitive) shows 'Entity not found' instead of the screen.

```ts
const { entity, reload } = useEntity()

// EntityLayout gives every entity a fresh screen, so the name never changes under a screen.
const entityName = entity.value.Name
```

- `entity.value` is the `EntityResponse`: `Name`, `Description`, `IsSystem`, `RequireChangeTracking`,
  `AllowAddProperties`, `Constraints`, `Properties`.
- Call `reload()` after a write that changes what that answer carries (the constraints, the properties).

## Layout

| Path | Contents |
|---|---|
| `src/pages/` | One component per screen, loaded lazily by `src/router.ts`. A part only that screen uses sits next to it (`pages/apps/ApplicationCard.vue`). |
| `src/pages/apps/` | The applications page (`/apps`, also the home page): the user's applications as cards grouped by server; a card's menu opens the status, rebuild and delete dialogs, which the page holds. Also the two pages that make an application: `CreateApplicationPage` (`/apps/new`) and `ImportApplicationPage` (`/apps/import`). |
| `src/pages/application/` | The screens of one application (`/apps/:appToken/...`), inside `AppLayout`. `EntitiesPage` (`/apps/:appToken/entities`): the custom and system entities with the dialogs to create, edit, rename and delete one; `EntityTable` and `EntityRow` are its parts (a row loads its own record counts from the API server). `PropertiesPage` (`/apps/:appToken/entities/:entity`, inside `EntityLayout`): the custom and system properties of one entity with the dialogs to create, edit, rename and delete one; `PropertyTable` is its part. `ConstraintsPage` (`.../entities/:entity/constraints`): the unique and foreign key constraints of one entity, added (`AddConstraintDialog`, a two-step dialog: kind, then its fields) and removed in the list and saved together; read-only for a system entity unless the user is an administrator. `SortingPage` (`.../entities/:entity/sorting`): the default sorting of one entity as an ordered list. `SharingPage` (`/apps/:appToken/sharing`, owner only): the users the application is shared with, the share dialog, the remove confirm and what a collaborator can do. `SettingsPage` (`/apps/:appToken/settings`): General (name, connection string; server and database type read-only), Status (online / offline) and Danger zone (rebuild, delete). `EmailPage` (`/apps/:appToken/email`): the SMTP settings and the confirmation landing page, one form saved to the Portal, and the e-mail templates read from and saved to the API server; `EmailTemplateDialog` edits one template (enabled, subject, HTML body with a live `HtmlPreview`, the placeholders). `AuditLogPage` (`/apps/:appToken/audit-log?page=1`): the application's audit log, with `AuditLogTable` and `AppPagination`. `SecurityPage` (`/apps/:appToken/security`): `SecuritySettingsForm` (sign-in, register, files, IP access with its own Save, 'Not saved yet' next to it and its own question before leaving; the forgot-password links read-only) and `SecurityRulesEditor` (the rules of every item edited locally and saved together: `SecurityItemPicker` lists the items, a searchable list or a select on a phone, the selected one in `?item=Entity-<name>`, `CustomEndpoint-<name>` or `Schema-Schema`; a role x action grid of `SecurityRuleCell`; `SecurityTreeDialog` and `SecurityMatrixDialog`, the saved rules read-only at `?view=tree` and `?view=matrix`, with `SecurityAccessBadge`). `CustomEndpointsPage` (`/apps/:appToken/endpoints`): the custom endpoints, one line each with the SQL cut to one line, the 'Call endpoint' link (opens `CallUrl` in a new tab), Edit and the delete confirm; `CustomEndpointSearchDialog` finds any text of the names, descriptions and whole SQL and marks it, with Edit opening the editor in a new tab. `CustomEndpointEditorPage` (`/apps/:appToken/endpoints/new` and `/apps/:appToken/endpoints/<ID>`): name, description and the SQL in `SqlEditor`, saved with `UnsavedChangesBar`, the rename warning, the help and the questions; `CustomEndpointTestPanel` is its right-hand part (the address and a box per parameter, worked out in the browser, and Test, which runs the SQL on the API server and shows the JSON or the database's error). `DataPage` (`/apps/:appToken/data/:entity?page=&pageSize=&sort=`): the data browser of the application: the entities with properties loaded once, then `ApplicationDataBrowser`. |
| `src/pages/admin/` | The screens of the 'Instance' group, for administrators (`meta.requiresAdmin`): `ServersPage`, `UsersPage`, `SettingsPage` (with the 'Backup database' card), `AuditLogPage`. `ApplicationsPage` (`/admin/applications`): every application of the instance in creation order, with a search by name, token or owner e-mail; the table shows name and token, owner, server, database and status, a Details button opens the other settings under the row (a secret only as 'Set' / 'Not set'), and the row menu has 'Open data browser', 'Open application' (only for an application the administrator owns or collaborates on) and 'Clear cache'. `ApplicationDataPage` (`/admin/applications/:appToken/data/:entity?`): the data browser of any application, loaded from `GET /api/v1/admin/applications/{appToken}` and drawn by `ApplicationDataBrowser`, under the breadcrumb Instance / Applications / name / Data. |
| `src/layouts/` | `AppShell`: the shell around every screen for a signed-in user (sidebar, phone drawer, user menu, the change-password dialog). `SidebarNav`: the navigation inside it; a link of the 'Instance' group stays lit on every address below its own (`/admin/applications/<token>/data`). `AppSwitcher`: the menu of the user's applications under 'Applications' in the sidebar; it marks the application being worked on. `AppLayout`: the frame around every screen of one application (loads it, breadcrumb, section tabs, Info and API buttons, the 'not found' state, and the Forbidden state for a `meta.requiresOwner` screen opened by a collaborator). `EntityLayout`: the frame around every screen of one entity, inside `AppLayout` (loads it, back link, name, description, tabs, the 'not found' state). `AuthLayout`: the centred card around the public screens, with the instance name. |
| `src/pages/account/` | The public account screens: sign in, sign up, forgot password, reset password, e-mail confirmed. |
| `src/components/` | Building blocks shared by screens: `PageHeader`, `StateMessage`, `ErrorState`, `ForbiddenState` (a screen the user may not open; `description` says who may), `FormDialog` (a form in a dialog; `wide` for a large field with something next to it), `FormField` (a labelled field with its error message), `ConfirmDialog` (`destructive` for a red button, `opener-removed` when a success removes the button that opened it, such as the row of a deleted record), `ConfirmByNameDialog` (type the name to confirm; `acknowledge` adds an 'I understand' box that must be ticked too, and its slot takes more about the consequences), `SecretInput` (a password or key the API never sends back), `AppPagination` (page links bound to `?page=`), `AuditLogTable` and `AuditLogDetail` (an audit log page with expandable rows, for the instance and the application audit log), `AuthForm` (a form that is a whole public screen, the counterpart of `FormDialog` inside `AuthLayout`), `ChangePasswordDialog`, `ServerStatusDot` (whether an API server answers), `CopyField` (a read-only value with a copy button, masked when it is a secret), `ApplicationInfoDialog` (server address, token and encryption key of one application), `DatabaseTypeFields` (database type, connection string and the notes per database type, for every form that puts an application on a database; `existing` for the edit form: the type disabled, the connection string a `SecretInput`), `ApplicationStatusDialog`, `RebuildApplicationDialog` and `DeleteApplicationDialog` (ask, run the write and show the toast, for the settings screen and the cards of the applications page alike), `ConsequenceList` (the bulleted consequences of a rebuild or delete), `ServerSelect` (the API server of a new application) and `NoServersState` (shown instead of such a form when the instance has no server), `FileInput` (a file picker for one file), `SwitchField` (an on/off choice of a form with its label, help and error: `FormField` for a boolean), `InlineAsync` (a value one row or card loads on its own: skeleton, short error with retry, then the value), `ConstraintBadge` (a unique or foreign key constraint of an entity), `UnsavedChangesBar` (Save / Discard at the bottom of a screen that collects several edits before one Save, with the leave guard), `HtmlPreview` (HTML someone typed, an e-mail body, drawn in a sandboxed frame where no script runs), `FullScreenDialog` (a read-only view that needs the whole window, such as the security tree and matrix), `TextListInput` (a list of short values, one box each, with Add, Remove and an error per entry from `List[2]` messages; pasting a comma-separated list splits it), `SqlEditor` (SQL in CodeMirror 6 with the application's entity and property names offered while typing; CodeMirror is downloaded the first time an editor is shown, and a plain text box takes over if it cannot be). |
| `src/components/data/` | The data browser, for every screen that shows the records of an entity (the application's data browser, and the administrator's of any application). `ApplicationDataBrowser` is what such a screen mounts once it has the entities: it takes `application`, `entities` (with their `Properties`) and `to` (the route of one entity on that screen, whose path ends in `:entity?`), shows `EntitySwitcher` and the `EntityDataBrowser` of the entity in the address, and without an entity opens the one of `?entity=<name>` (the address of the classic data browser) or the first custom one; the screen keeps its own loading, error and 'no entities' states. `foreign` says the application is someone else's (an administrator in an application they neither own nor collaborate on): links to its screens under `/apps` are left out. `EntityDataBrowser` takes `application` (token, API server, largest file: `DataApplication` of `lib/records.ts`) and `entity` (with its `Properties`) and is given `:key` of the entity name: toolbar (Refresh, New record / Register user / Upload, Delete selected, Export page as CSV, Clear history), the grid (a sortable header with an info tooltip per property, a filter box per column, cells cut to one line that open on a click, row actions by the entity's flags), the paging bar; page, size and sort live in the address (`?page=`, `?pageSize=`, `?sort=Name` or `-Name`), the filters do not. Its parts: `RecordFilterInput` (the filter box of one column, by type), `RecordPaging` (size, first / previous / page x of y / next / last, total), `RecordFormSheet` (create and edit in a side sheet, one box per property a caller may set; Users registers), `RecordHistorySheet` (the newest 100 history entries of a record, changed values marked, 'Clear history'), `FileUploadDialog` (one file, size checked before it is sent). `EntitySwitcher`: the entities as links (a select on a phone), each a route given by `to`. |
| `src/components/diff/` | What differs between two sides, for every screen that shows a comparison. `DiffSection`: one section (a title with its counts, then the Added, Removed and Changed lists; the `item` slot draws an added or removed thing, the `changed` slot one that differs; nothing is drawn when all three are empty). `FieldChange`: one value before and after (`code` for SQL: two blocks that wrap). `DiffCount`: a count as a badge ('2 added'), also used alone for a summary line. |
| `src/components/ApplicationSelect.vue` | Picks one of the user's applications, grouped by server: for a screen that needs a second application (the source of a schema import, the other side of a comparison). |
| `src/pages/application/SchemaImportPage.vue` | `/apps/:appToken/import`, the 'Import' tab (route `app-schema-import`): adds entities, properties, constraints, security rules and custom endpoints from a JSON payload. 'Load diff' asks the API what another application has that this one lacks, puts it into the payload box and shows its counts; the box stays editable. Import checks the text in the browser (`parsePayload`), asks first (not atomic, cannot be undone), then shows 'Imported' with the skipped items, or the failed step with its place in the payload. The notes and the example payload of the classic page are kept, collapsible. |
| `src/pages/apps/CompareApplicationsDialog.vue` | The compare dialog of the applications page (`/apps?compare=<application token>&with=<application token>`), opened by the 'Compare with' item of a card's menu: the other application is picked in the dialog, then Entities, Properties, Constraints, Custom endpoints and Security each list what was added, removed and changed, or 'Applications are identical'. `ComparedProperty` is its part. |
| `src/lib/schemaImport.ts` | The rules of the schema import screen: `parsePayload` (the text of the payload box: JSON, an object, its three lists are lists), `payloadSummary` (the counts shown after 'Load diff'), `importFailure` (the message, the places in the payload and the trace of a failed import), `examplePayload`. Pure functions. |
| `src/lib/comparison.ts` | How the answer of `GET /applications/{appToken}/comparison` is laid out for the compare dialog: `comparisonView` (the five sections; the properties and constraints of a changed entity move to their own sections, named 'Entity.Property'), `isIdentical`, `ruleText`, `propertyFacts`, `constraintTypeName`. Pure functions. |
| `src/components/ui/` | shadcn-vue components. They are source files owned by this project: add one with `npx shadcn-vue@2.8.2 add <name>` and edit it freely. |
| `src/composables/` | Shared logic. `useAsync` gives every read the same loading / error / data states; `useMutation` is its counterpart for writes. `usePageQuery` reads the page number of a paged list from `?page=`. `useInstance` gives the public screens what the Portal tells a visitor: the instance name and whether registration is open. `useApplications` is the one shared list of the user's applications, read by the applications page and the sidebar switcher. `useServerHealth` asks an API server whether it is up, every 5 seconds. `useServerChoice` loads the servers an application can be put on and holds the one picked (the only one is picked automatically). `useApplication` gives a screen under `AppLayout` the application it belongs to, already loaded, and a `reload()`. `useEntity` does the same for the entity of a screen under `EntityLayout`. `useLeaveGuard` asks before leaving a screen with unsaved changes (`UnsavedChangesBar` already uses it). |
| `src/pages/application/Clone*Page.vue` | The two clone screens, opened from the card menu of the applications page (no tab in `AppLayout`). `CloneApplicationPage` (`/apps/:appToken/clone`): the server (it starts on the application's own), `DatabaseTypeFields`, 'Clone data' and, when it is on, the entities whose records are copied (all ticked, 'Select all' / 'Deselect all', Files left out; an empty selection is refused), with the notes of the classic page. Starting answers at once and leads to `CloneProgressPage` (`/apps/:appToken/clone/:operationId`): the phase, the bar (`components/ui/progress`), the counters of the phase, the entity being worked on and the time remaining, asked every 2 seconds with `usePolling`; then 'Clone completed' with the link to the new application, or 'Clone failed' with the message and 'Back to clone'; an operation the Portal no longer knows (404) shows 'This clone can no longer be tracked'. |
| `src/composables/usePolling.ts` | `usePolling(load, { intervalMs, done, giveUp })`: the state of something that runs on the server, asked at once and again after every answer until `done` says the answer is final. A failed attempt keeps the last answer and is tried again; `giveUp` names the failures that end it (a 404). It pauses while the tab is hidden and stops when the screen is left. For the screen that follows a long-running operation; a value that is simply refreshed now and then (`useServerHealth`) does not need it. Its test (`usePolling.test.ts`) runs the mount and unmount hooks by hand and moves a fake clock, since there is no component to mount. |
| `src/lib/clone.ts` | The rules of the clone screens, from the classic `Clone.cshtml` and `CloneProgress.cshtml`: `cloneableEntities`, `entitiesToSend`, `isFinished`, `phaseText`, `clonePercent`, `cloneCounters`, `currentEntities`, `formatEta` / `etaText`, `failureText`. Pure functions. |
| `src/lib/api.ts` | The typed API client, `unwrap`, `unwrapAnonymous` (for calls made without a session), `ApiError`, and `loginUrl` / `redirectToLogin`. It also shows the `Warning` header of any answer as a warning toast. `api-types.ts` is generated. |
| `src/lib/formData.ts` | `toFormData`: the `bodySerializer` of an `api` call that uploads a file (multipart/form-data). |
| `src/lib/apiServer.ts` | `apiServer(serverUrl, appToken)`: the client for calls that go straight from the browser to an API server (storage used, export, record counts, e-mail templates, the SQL test of a custom endpoint, records, files, history and the series of a report). `get` and `getBlob` read (`get` takes `{ signal }` of an `AbortController`, for a read a newer one replaces; `isAbort(error)` tells that failure apart); `put` and `post` send a JSON body; `delete` deletes; `postFile(path, field, file)` uploads one file as multipart/form-data. A query value that is `undefined` is left out. It fetches the user's API token from the Portal, keeps it in memory only, and throws the same `ApiError` as `api` (with the `Property` the API server names, so `formErrors` marks that field). |
| `src/lib/customEndpoints.ts` | The rules of the custom endpoints screens: `endpointParameters` and `endpointAddress` (a copy of `DBWS_CustomEndpoint.GetParameters` and `GetUrl`, so the editor follows every key press without a request), `testParameters` and `testQueryBody` (the SQL test call), `searchEndpoints` and `highlightMatches` (the search dialog), `renameLosesRules`. Pure functions. |
| `src/lib/sqlEditor.ts` | `createSqlEditor`: CodeMirror 6 set up for SQL (dialect of the database type, completion, colours from the design tokens). Only `SqlEditor.vue` loads it, through a dynamic import, so it is a chunk of its own. |
| `src/lib/emailTemplates.ts` | The e-mail templates as the API server sends them (`EmailTemplate`, `EmailTemplateUpdate`), `visibleTemplates`, `templatePlaceholders` (a copy of the placeholders in `EmailEvent.cs`) and `templateProblems` (the empty fields checked before a save). Pure functions. |
| `src/lib/download.ts` | `saveBlob`: saves a fetched file under a file name. |
| `src/lib/records.ts` | The rules of the data browser, copied from the classic `Views/Entity/Data.cshtml`: the shapes of the API server's answers (`RecordPage`, `HistoryEntry`), `recordActions` (what the entity's flags allow), paging (`pageSizes`, `pageSizeFrom`, `pageCount`), sorting (`sortFrom`, `sortQuery`, `nextSort`, `sortParam`), filtering (`filterOperators`, `emptyFilters`, `hasFilters`, `filterParam`), values (`formatRecordDate`, `cellText`: system UTC dates in local time, other dates in UTC, `yyyy-MM-dd HH:mm:ss.SSS`), the record form (`formProperties`, `initialForm`, `createBody`, `updateBody`: only the changed boxes, `recordErrors`: an API server Property may name several, comma-separated), `fileTooLarge`, `historyRows`. Pure functions. |
| `src/lib/csv.ts` | `toCsv` (every field quoted, quotes doubled, CRLF) and `csvFileName`, for a CSV export. Pure functions. |
| `src/lib/applicationActions.ts` | The texts and rules shared by the application settings screen and the card dialogs: `statusChange` (what the status button offers and says), `offlineWarning`, `rebuildConsequences`, `deleteConsequences`, `understandText`, and `connectionStringToSend` (the edit request's ConnectionString: an empty box is null, which keeps the stored value). Pure functions. |
| `src/lib/applications.ts` | How the applications list is grouped, ordered, filtered and labelled (`groupByServer`, `filterByName`, `formatStorage`, `databaseTypeLabel`), plus the database types a form offers (`databaseTypes`, `needsConnectionString`). Pure functions. |
| `src/lib/adminApplications.ts` | The rules of the administrator's applications list: `searchApplications` (name, token or owner e-mail), `countText` ('3 of 12 applications') and `applicationDetails` (the settings a row opens, a secret as `stored` only). Pure functions. |
| `src/lib/forms.ts` | What a form shows for a failed write: `formErrors` (a message per field and one above the form) and `listErrors` (a message per item of a list sent as a whole, `Constraints[1]...`, and one above the list). |
| `src/lib/constraints.ts` | The editing rules of the constraints screen: `constraintRows` (saved, added and removed constraints and their place in the request), `addConstraint`, `constraintRequests` (the body of the save), `isDuplicate`, `changesText`, `onDeleteHelp`. Pure functions. |
| `src/lib/defaultOrder.ts` | The editing rules of the default sorting screen: `moveItem`, `toggleDirection`, `remainingCandidates`, `sameOrder`, `directionText`. Pure functions. |
| `src/lib/entities.ts` | How entities are filtered and described: `filterEntities`, the texts of a constraint (`constraintTypeText`, `constraintLabel`, `constraintDescription`, `onDeleteText`), `formatCount`, and which entity the data browser lists first and opens (`dataEntityOrder`, `initialDataEntity`). Pure functions. |
| `src/lib/properties.ts` | How properties are described and which fields a property form shows: `propertyRules` (the rules of a property in plain words), `typeFields` (the fields a type uses), `limitNoun`, `decimalPlacesText`, `maxLengthNote` (the string limits of MySQL and SQL Server), `numberOrNull` (a number box as the API takes it), `propertyTypes`. Pure functions. |
| `src/lib/securityAccess.ts` | What a security rule grants, worked out as the API server enforces it: the items' actions and properties (`itemActions`, `offeredProperties`), `ownedApplies`, `inheritance` (the editor's 'Inherits from' notes), `propertyAccess` (an empty list is 'no properties', never full access), `endpointRateLimit`, `effectiveAccess` (Anonymous and Authenticated inherited by every role), `describeAccess`, `rateLimitShort` / `rateLimitText`. The editor, the tree and the matrix all read access through it. Pure functions. |
| `src/lib/security.ts` | The editing rules of the security screen: `toEditable`, `newRule` (a switched-on cell), `restoreRule` (a cell switched off and on again gets its properties and rate limit back), `setRule`, `rulesRequest` (the body of the save), `changedItems` (which items hold unsaved changes), `changesText`. Pure functions. |
| `src/pages/application/Report*.vue`, `TimeRangePicker.vue` | The reports of one application. `ReportsPage` (`/apps/:appToken/reports`): the dashboard. On a wide screen (768px and up) the reports are panels on a 12-column grid (`ReportsGrid`, gridstack with the settings of the classic page), dragged by their title and resized by their edges; the layout is saved 600 ms after the last change (`PUT reports/layout`, every panel), and a save that fails shows above the grid with 'Reload the dashboard'. On a phone the panels are one column in the same order and nothing moves. The page also holds the delete confirm, `ReportEndpointDialog` ('View API endpoint': the address of each series' call, with a copy button) and the editor. `ReportPanel`: one report - title, the time range or Top N badge, Refresh (which moves the time window to now), the menu, and the body: `ReportTable` for a Grid, `ReportChart` (Chart.js) for the other types. It loads each series itself from the API server (`/api/Stats/Aggregate`); a series that cannot run or whose call fails is named inside the panel and the others still show. `ReportEditorSheet` (`/apps/:appToken/reports/new` and `/apps/:appToken/reports/<ID>/edit`, a side sheet over the dashboard; the three routes share `ReportsPage`): title, visualization, time range (`TimeRangePicker`: the quick ranges or a custom number and unit), Top N and the series; `ReportSeriesEditor` is one series (label, entity, group-by, property, filter), with its choices from `GET entities/{entity}/report-fields?Type=` and the filter edited in a dialog with `FilterBuilder`. |
| `src/components/FilterBuilder.vue` | A filter of the data API as a list of conditions (property, operator, value) that must all hold, for every place a stored filter is edited. It edits `FilterRow`s; `src/lib/filterBuilder.ts` reads and writes the stored text. |
| `src/lib/reportData.ts` | Everything a report panel works out, copied from the classic `Views/Shared/Report.cshtml` and the report functions of `custom.js`: the time range (`parseTimeRange`, `timeRangeLabel`, `timeWindow`), the call of a series (`seriesFilter`, `seriesQuery`, `aggregateUrl`), badges and legend labels (`panelScope`), the shared x-axis (`combineSeries`, `rowValue`, `hasNoRows`), the cells of a Grid (`gridText`) and the Chart.js configuration (`chartConfig`, `toNumberOrNull`, `withAlpha`). The answer of `Stats/Aggregate` is described there (`AggregateRow`). Pure functions. |
| `src/lib/reports.ts` | The rules of the report editor: the choices (`reportTypes`, `quickTimeRanges`, `timeUnits`), the form (`newReportForm`, `reportForm`, `newSeries`), the body of the save (`reportRequest`: empty series rows are left out), where the problems of a failed save belong (`reportErrors`: `Series[1].GroupBy` back to the row of the form) and the group-by picker (`fieldNames`, `splitGroupBy`, `toggleGroupBy`, `notOffered`). Pure functions. |
| `src/lib/filterBuilder.ts` | The rules of `FilterBuilder`: every operator of the data API (`allFilterOperators`), `parseFilter` (a stored filter as conditions, or as `raw` text when it has groups, OR or anything else a flat list cannot show), `filterText` (the JSON to store, the same as the classic builder writes), `filterSummary`, `newFilterRow`. Pure functions. |
| `src/lib/reportChart.ts` | `createReportChart` and `reportPalette`: Chart.js with only the chart kinds a report has, and the texts, lines and tooltips in the colours of the design tokens. Only `ReportChart.vue` loads it, through a dynamic import, so it is a chunk of its own. |
| `src/lib/session.ts` | The signed-in user: loaded before the app starts, `useSession()` inside `AppShell`, `setSession` after signing in. |
| `src/lib/returnUrl.ts` | `safeReturnUrl`: the check that a `returnUrl` is a path on this site before it is followed after signing in. Pure functions. |
| `src/lib/toast.ts` | Toast helpers that report the result of a write: `success`, `error`, `warning`. |
| `src/lib/format.ts` | Date formats: `formatDateTime` (local time) and `formatUtc` (`yyyy-MM-dd HH:mm:ss` in UTC, for logs). |
| `src/lib/auditDiff.ts` | The display rules of an audit log entry's changes, as pure functions without Vue, so they can be unit-tested. `AuditLogDetail` draws the result. |
| `src/**/*.test.ts` | Unit tests, next to the module they test. |
| `src/style.css` | Design tokens (colours, radius). Dark is the only theme; the one light colour is `preview` (`bg-preview`), the white page behind an e-mail preview. Also the colours of chart series (`--chart-1` to `--chart-10`) and the drop target of the reports grid. |
| `src/styles/shadcn.css` | Utilities the shadcn-vue components need, copied from shadcn-vue 2.8.2. |

After `shadcn-vue add`, check `git diff`: the CLI can add packages to `package.json` (note them
under Dependencies) and tokens to `src/style.css`. When moving to a newer CLI version, refresh
`src/styles/shadcn.css` too.

## Conventions

- `<script setup lang="ts">` in every component; no Options API.
- Reads go through `useAsync(() => unwrap(api.GET(...)))`, and every screen shows its loading, error and empty states.
- Every route needs a session unless it carries `meta.public`. The guard in `src/router.ts` is the one place that decides; a 401 from any call made with `unwrap` leads to the login page (`/ui/account/login?returnUrl=...`) with a full page load, and signing in returns to that address.
- A `returnUrl` is followed only through `afterSignIn` (`src/router.ts`), which accepts paths on this site and nothing else: a `/ui/...` path opens in this app, any other path on this site (`/swagger`) is a full page load.
- Signing out is a `DELETE /api/v1/session` followed by a full page load of the login page, so nothing of the session stays in memory.
- A password box is an `Input` with `type="password"` and the right `autocomplete` (`current-password`, `new-password`; the e-mail box of the same form has `username`), so password managers work. `SecretInput` is for stored secrets, not for the user's own password.
- `api` or `apiServer`? Everything the Portal knows (applications, servers, users, settings, the encryption key) is a call to the Portal: `unwrap(api.GET('/api/v1/...'))`, typed from the contract. What lives on an API server (storage used, export, record counts, records, files, history, the statistics of a report) is called straight from the browser, as the classic pages do: `apiServer(app.Server.ServerUrl, app.Token).get(...)` or `.getBlob(...)`, and for a write `.put(path, body)` or `.post(path, body)` inside `useMutation`. Those calls are not in the contract, so say what they return (`get<number>(...)`) and describe their bodies in a module under `src/lib/` (`emailTemplates.ts`). Never build such a request by hand: `apiServer` adds the token and the headers, and the token must not end up in an address or in storage.
- HTML that is not this UI's own (an e-mail template) is shown only through `HtmlPreview`, never with `v-html`: its frame is sandboxed with every permission off, so a script in it cannot run.
- A failed call to an API server inside a list (one card, one row) shows a short inline error with a retry there; it never replaces the screen. `InlineAsync` draws that.
- A file fetched with headers is saved with `saveBlob` (`src/lib/download.ts`). A file of an API server is downloaded that way too, never through a link with the token in its address.
- A read that follows typing (the filters of the data browser) waits until typing pauses and cancels the read still running (`AbortController`, the `signal` of `apiServer(...).get`), so an old answer never replaces a newer one.
- SQL is edited in `SqlEditor`, never in a plain text box, and CodeMirror is imported nowhere else: that keeps it out of every other screen's download.
- A chart is drawn by `ReportChart`, and Chart.js is imported only by `src/lib/reportChart.ts`; gridstack is imported only by `ReportsGrid.vue`, which the reports page loads on a wide screen. Both are loaded with a dynamic import and have a fallback when the download fails (a table instead of the chart, one column instead of the grid).
- A canvas cannot use the Tailwind classes: `src/lib/reportChart.ts` reads the design tokens with `getComputedStyle`. The colours of the series are the tokens `--chart-1` to `--chart-10` in `src/style.css`.
- Data of an application (a record value, a label or value of a report) is written with `{{ }}`, never with `v-html`. The Grid report of the classic portal builds HTML from such values; here they are text.
- A component that hands its elements to a library that moves them (the gridstack grid) sets what the library reads (the `gs-*` attributes) itself, once, when it is mounted, not in the template: a later render would write an old place over the one the library keeps. Its parent gives it a `key` that changes when the set of items does, so a new grid is built instead of the old one being patched.
- Dragging is for the reports dashboard only, and only on a wide screen; the layout is saved on its own a moment after the last change, one save at a time. Everything a panel offers is also in its menu, so nothing needs a drag.
- An editor that is a side sheet with an address of its own (`reports/new`, `reports/<ID>/edit`) is declared in `src/router.ts` as routes that show the same page as the list; the page opens the sheet from `route.name`, and closing it goes back to the list's route.
- A dialog that can be linked to keeps its state in the query (`/apps?info=<application token>`); closing it removes the parameter.
- A dialog that works on two things keeps both in the query (`/apps?compare=<application token>&with=<application token>`): the first opens it, the second is picked inside it, and closing it removes both. A value that is not one of the choices counts as not picked.
- A comparison is drawn with the components of `src/components/diff/`, and its layout is worked out in a module under `src/lib/` (`comparison.ts`). Added is green, removed red, changed amber, each also named in words.
- A write that is not atomic says so before it runs (`ConfirmDialog`) and shows its outcome on the screen, not in a toast: what was skipped, or the step that failed and that the earlier steps stay applied (the schema import).
- The applications list is loaded once, by `useApplications`. Call its `reload()` after a change that adds, removes or renames an application, so the page and the sidebar switcher both follow.
- A screen of one application reads it with `useApplication()` and never loads it again. Its API paths take `application.value.Token`.
- Writes go through `useMutation`. The API rejects a POST, PUT or DELETE without the header `X-Apilane-Portal: 1`; the client in `src/lib/api.ts` adds it automatically, so screens never set it. Anything else that calls the API (a script, `curl`) has to send it.
- A form opens in `FormDialog` and builds its inputs with `FormField` (`SwitchField` for an on/off choice). The dialog puts the focus on its first control: keep links (a warning with a link) below the first input. A destructive action asks first: `ConfirmDialog`, or `ConfirmByNameDialog` where the user has to type the name (with `acknowledge` for an action on a whole application: rebuild, delete). Report the result with the helpers in `src/lib/toast.ts`.
- A screen that edits a list and saves it as a whole (constraints, default sorting) keeps the edits in its own state until Save, marks what is new or about to be removed, and puts `UnsavedChangesBar` at its end, always mounted: it shows Save / Discard while something is unsaved and asks before the user leaves. Problems the API reports per item (`Items[2].Property`) show at that item through `listErrors`; any edit clears them, because the places in the list move. The list is locked while Save runs, because a successful save resets it. Reordering is done with up / down buttons that keep the focus on the moved item, not with drag and drop; Remove and Undo move the focus to the button now in that place.
- A screen with a plain form next to an `UnsavedChangesBar` (security: the settings form and the rules) gives the form its own `useLeaveGuard`, which stands back while the bar's part is unsaved: two guards that both ask would open each other's question again and again.
- A screen that edits a list too large to show at once (the security rules) shows one part of it at a time, keeps the part in the query (`?item=`), keeps edits of every part until the one Save, and marks the parts with unsaved changes. A problem the API reports at one item (`Rules[3].Action`) opens that part and that place.
- Changing only the query (a dialog, `?item=`) does not scroll the page; a new screen or a new `?page=` starts at the top (`scrollBehavior` in `src/router.ts`).
- A form that is a whole screen (instance settings, new application, import application) is a plain `<form>` built from the same `FormField` and `formErrors` pieces, with its own Save button. On a public screen that form is `AuthForm`.
- An error that is not about one field (a failed sign-in, 'mail is not set up', the 429 of the rate limit) shows above the form: `formErrors(...).message`.
- A secret (password, key, connection string) is never shown. `SecretInput` says whether one is stored; an empty box sends `null`, which keeps the stored value.
- A connection string that is being entered for a new application or in the settings of one (`DatabaseTypeFields`) stays in the page's own state: never in the address, in storage or in a shared module, so it is gone when the page is left. The settings form empties the box after a save.
- An action offered both on a screen and from a menu on another screen (the status, rebuild and delete of an application) is one dialog component in `src/components` that runs its own write and toast; the screens only open it and react to its event (`done`, `deleted`). A list page holds such dialogs itself rather than inside each card, so a deleted card can go while its dialog closes.
- A write that was saved but left something undone answers with a `Warning` header (today: 'Saved, but the API server could not be refreshed.'). `src/lib/api.ts` shows it as a warning toast for every call, so screens do nothing for it.
- A write that starts work the Portal goes on with (a clone) answers 202 with an `OperationId`. The form then opens a screen with that id in its address (`/apps/:appToken/clone/:operationId`), which follows the work with `usePolling` and shows four states: running, completed, failed, and 'can no longer be tracked' for a 404 (the Portal keeps operations in memory only). A failed attempt to ask is not a failed operation: the last answer stays on screen with a 'trying again' line.
- A file upload is a normal `api.POST` with `bodySerializer: toFormData`. The contract types an uploaded file as `string`, so the `File` is cast where it is put into the body.
- A routed page with a fixed segment (`apps/new`, `apps/import`) is declared in `src/router.ts` before any `apps/:appToken` route.
- A paged list keeps its page number in the address: `usePageQuery` for the read, `AppPagination` under the list.
- Dates: `formatDateTime` (local time) inside a `<time>` whose `title` is `formatUtc(...)`; log timestamps use `formatUtc` alone.
- A button that navigates is `<Button as-child>` around an `<a>` or a `<RouterLink>`, so it stays a real link.
- Colours come from the tokens in `src/style.css` (`bg-card`, `text-muted-foreground`, ...), not from raw palette classes.
- Every screen works from 360px wide to desktop. A wide table keeps the name and the row menu and moves the other columns under the name on a phone (`ServersPage`, `EntityRow`). A table with more values than fit as columns (the administrator's applications list, the audit log) shows the ones people look for and opens the rest under the row, with a button that carries `aria-expanded`, rather than scrolling sideways.
- An administrator's screen of someone else's application lives under `/admin` and reads it from `/api/v1/admin/...`. The screens under `/apps/:appToken` and their API stay for the owner and the collaborators: there the Admin role gives nothing (the one exception is `POST .../cache-reset`). So a link from an administrator's screen to `/apps/:appToken/...` is shown only when the application is in `useApplications()`.
- A number box is an `Input` with `type="number"`; its value is a number, or `''` while it is empty. Send it with `numberOrNull` (`src/lib/properties.ts`): an empty box is `null`. A whole number that can be larger than 9007199254740991 (a 64-bit ID, the parameter of a custom endpoint) is typed into a text box with `inputmode="numeric"` and sent as text: a number box would round it.
- A form whose fields depend on a choice (the type of a new property) puts that choice first, shows only the fields it uses, and sends `null` for the hidden ones.
- A value that cannot be changed after creation says so in the help line of its field on the create form; the edit form shows it disabled with the reason.
- A row menu item that is not available stays in the menu, disabled, with the reason as a second line under its label (`EntityRow`); a tooltip would not show on a disabled item. A row where no item is available gets no menu at all (system properties in `PropertyTable`).
- A row that scrolls sideways (`overflow-x-auto`) is also `relative`: the `sr-only` texts inside it are absolutely positioned and would otherwise widen the page on a phone.
- Logic that does not need Vue or the browser (ordering, formatting, parsing) goes into a module under `src/lib/` as pure functions, with a `*.test.ts` next to it. Tests import `describe`, `it` and `expect` from `vitest`; there is no browser environment (no jsdom), so components are not unit-tested. `npm run build` type-checks the tests but does not bundle them: nothing imports them.
- Nothing in this UI links to a Razor page, and a new screen never does. Where a text here says 'the classic page' or 'the classic portal', it names the Razor page a rule was copied from. The Razor views and MVC controllers stay untouched until they are removed in one go; [MIGRATION.md](MIGRATION.md) has the plan.

## Dependencies

Adding one is a deliberate decision: note here what it is for.

| Package | Why |
|---|---|
| `vue`, `vue-router` | The framework and its router. |
| `openapi-fetch` | Small typed `fetch` wrapper driven by the generated API types. |
| `openapi-typescript` (run through `npx`, pinned in the `api:types` script) | Generates `src/lib/api-types.ts`. Not installed, because its peer range is TypeScript 5 and this project is on 6.0; move it to `devDependencies` once it accepts 6. |
| `reka-ui` | Accessible, unstyled primitives (menus, dialogs) under the shadcn-vue components. |
| `class-variance-authority`, `clsx`, `tailwind-merge`, `tw-animate-css`, `@vueuse/core` | Helpers the shadcn-vue components import. |
| `vue-sonner` | Toasts, under the shadcn-vue `sonner` component and `src/lib/toast.ts`. |
| `@lucide/vue` | Icons, imported one by one. |
| `@codemirror/state`, `@codemirror/view`, `@codemirror/commands`, `@codemirror/language`, `@codemirror/lang-sql`, `@codemirror/autocomplete` | The SQL editor (`SqlEditor`, `src/lib/sqlEditor.ts`): editing, history, SQL parsing and colours, completion of entity and property names. Loaded only when an editor is shown. They bring `@lezer/*` along, which this project does not import. |
| `gridstack` | The grid of the reports dashboard (drag and resize on 12 columns): the same library as the classic portal (10.3.1 there, vendored), so a stored layout looks the same in both. Imported only by `ReportsGrid.vue` and downloaded only when a dashboard is shown on a wide screen. It has no dependencies. Its Vue wrapper (`gridstack/dist/vue`) is not used. |
| `chart.js` | The charts of the report panels: the same library as the classic portal (4.4.4 there, vendored). Imported only by `src/lib/reportChart.ts`, which registers just the chart kinds a report can be, and downloaded when the first chart is shown. It brings `@kurkle/color` along. |
| `tailwindcss`, `@tailwindcss/vite` | Styling. |
| `vite`, `@vitejs/plugin-vue`, `typescript`, `vue-tsc`, `@vue/tsconfig`, `@types/node` | Build and type-checking. TypeScript stays on 6.0.x until `vue-tsc` supports 7. |
| `vitest` | Runs the unit tests (`npm test`). Development only; it reads `vite.config.ts` and needs no configuration of its own. The Docker image does not run it. |
