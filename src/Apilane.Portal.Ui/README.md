# Apilane Portal: UI and management API

The Portal is one ASP.NET Core process (`src/Apilane.Portal`) that serves:

- the **management API** under `/api/v1`: JSON in and out, described by the contract `openapi/portal-v1.json`;
- **MCP** under `/api/mcp`, with browser-approved connections acting as existing agents;
- the **UI**: this folder, a Vue 3 single-page app, built into static files that the Portal serves at
  the site root (`/apps`, `/apps/{token}/entities`, `/account/login`, `/admin/servers`, ...).

The UI manages applications through this API, so a script or an AI agent can do the same management
work with the same calls, using an agent key or an MCP connection (see [Agents](#agents) and
[MCP connections](#mcp-connections); a few calls are for people only). Records, files,
statistics, e-mail templates and the SQL test of a custom endpoint are served
by the API servers and need a person's token: an agent cannot reach them. The Portal renders no
pages on the server.

This is the one document for both. It has three parts:

1. [Using and deploying the Portal](#part-1-using-and-deploying-the-portal): addresses, screens, sign-in, behaviour, deployment, known limits, open decisions.
2. [Calling the management API](#part-2-calling-the-management-api): for people, scripts and AI agents.
3. [Working on the UI](#part-3-working-on-the-ui): commands, running it locally, layout, conventions, tests, dependencies.

---

# Part 1: Using and deploying the Portal

## Addresses

The Portal keeps four address prefixes for itself and explicitly maps two MCP discovery aliases
under `/.well-known`. Every other address belongs to the UI.

| Address | What answers | Sign-in |
|---|---|---|
| `/api/v1/...` | The management API. | Session cookie, or an agent key (`Authorization: Bearer`). A few account calls need neither. |
| `/api/internal/...` | The internal API the API servers call. See [The internal API](#the-internal-api). | The `x-installation-key` header. |
| `/api/mcp` | Portal-hosted MCP, using Streamable HTTP. See [MCP connections](#mcp-connections). | A connection-specific OAuth bearer token. Cookies and raw agent keys are refused. |
| `/api/mcp/oauth/...`, `/api/mcp/.well-known/...` | OAuth code flow, client registration and discovery. | Public protocol endpoints; approving access requires a signed-in, confirmed personal account. |
| `/.well-known/oauth-authorization-server/api/mcp`, `/.well-known/openid-configuration/api/mcp` | Standard discovery aliases for the MCP issuer. | None. These two addresses are explicit exceptions to the UI fallback. |
| `/swagger` | The API reference (Swagger UI) and the contract at `/swagger/v1/swagger.json`. | Signed-in users only. Signed out, the browser is sent to `/account/login?returnUrl=/swagger` and comes back after signing in. |
| `/health/liveness`, `/health/readiness` | Health checks. | None. |
| `/metrics` | Prometheus scrape endpoint. Answers 404 when `OpenTelemetry:Metrics:Enabled` is false. | None. |
| everything else | The UI. | The UI sends a visitor without a session to `/account/login`. |

How the rest is handed to the UI:

- A file of the built UI is served as a file: `/favicon.ico`, `/assets/index-abc123.js`.
- Any other GET or HEAD address whose last segment has no dot gets the UI's `index.html`, and the UI
  shows the screen for that address, or 'Not found'. So a UI route must never end in a segment with a dot.
- An address with a dot that is no file of the UI answers 404.
- An unknown address under one of the four prefixes answers 404, never the index page: a monitor or
  an API server must not get a page with status 200. Under `/api` the 404 has the JSON error body.
- UI addresses match whatever their letter case (`/Account/Login` opens the sign-in screen).
- Nothing else under `wwwroot` can be downloaded. `wwwroot/EmailTemplates/FORGOT_PASSWORD.html` is
  read from disk by the Portal when it mails a reset link.
- The Portal must sit at the root of its host name. A sub-path (`https://example.com/portal/`) is not supported.

The four prefixes are written down in three places that must stay the same: `_serverPaths` in
`src/Apilane.Portal/Extensions/PortalApiDependencyInjection.cs`, `portalPaths` in `vite.config.ts`
and `serverPaths` in `src/lib/returnUrl.ts`.

## Screens

Endpoints are relative to `/api/v1`, and `{app}` stands for `/applications/{appToken}`. "API server:"
marks calls the browser makes straight to the application's API server
(see [The browser and the API servers](#the-browser-and-the-api-servers)).

| Screen | Address | Endpoints |
|---|---|---|
| **Account** | | |
| First administrator setup | `/account/setup` | `GET` and `POST /bootstrap`, `GET /instance` |
| Sign in | `/account/login?returnUrl=` | `POST /session`, `GET /instance` |
| Sign out | User menu, 'Sign out' | `DELETE /session` |
| Sign up | `/account/register` | `POST /account`, `GET /instance` |
| Forgot password | `/account/forgot-password` | `POST /account/password-reset-requests` |
| Reset password | `/account/reset-password?code=` | `POST /account/password-resets` |
| E-mail confirmed (for end users of an application) | `/account/email-confirmed` | none |
| Change password | User menu, 'Change password'; `/account/password` opens the same dialog | `PUT /account/password` |
| MCP connections | `/mcp/connections` | `GET /mcp/connections`, `DELETE /mcp/connections/{id}`; MCP setup reads public MCP resource metadata |
| Authorize an MCP client | `/mcp/authorize?requestId=` | `GET /mcp/authorize`, `POST /mcp/authorize`, `POST /mcp/authorize/deny` |
| **Applications** | | |
| Applications (the home page) | `/apps` | `GET /applications`, `GET /session/api-token`. API server: `Application/GetStorageUsed`, `Application/Export`, `Health/Liveness` |
| Application info dialog | `/apps?info={appToken}`, and the Info button of an application | `GET {app}/connection-info` |
| Compare two applications | `/apps?compare={appToken}&with={otherToken}` | `GET {app}/comparison?Target=` |
| New application | `/apps/new` | `GET /servers`, `POST /applications` |
| Import application | `/apps/import` | `GET /servers`, `POST /applications/import` |
| **One application** | | |
| Entities (with the create, edit, rename and delete dialogs) | `/apps/{appToken}/entities` | `GET` and `POST {app}/entities`, `PUT` and `DELETE {app}/entities/{entity}`, `POST {app}/entities/{entity}/rename`. API server: `Stats/CountDataAndHistory` |
| Properties of an entity (with the dialogs) | `/apps/{appToken}/entities/{entity}` | `GET {app}/entities/{entity}`, `POST {app}/entities/{entity}/properties`, `PUT` and `DELETE .../properties/{property}`, `POST .../properties/{property}/rename` |
| Constraints of an entity | `/apps/{appToken}/entities/{entity}/constraints` | `GET` and `PUT {app}/entities/{entity}/constraints` |
| Default sorting of an entity | `/apps/{appToken}/entities/{entity}/sorting` | `GET` and `PUT {app}/entities/{entity}/default-order` |
| Data browser | `/apps/{appToken}/data/{entity}?page=&pageSize=&sort=` | `GET {app}/entities?IncludeProperties=true`, `GET /session/api-token`. API server: `Data/*`, `Files/*`, `EntityHistory/*`, `Account/Register` |
| Security | `/apps/{appToken}/security` (`?item=`, `?view=tree`, `?view=matrix`) | `GET {app}/security`, `PUT {app}/security/settings`, `PUT {app}/security/rules` |
| Custom endpoints | `/apps/{appToken}/endpoints` | `GET {app}/custom-endpoints`, `DELETE {app}/custom-endpoints/{id}` |
| Custom endpoint editor | `/apps/{appToken}/endpoints/new`, `/apps/{appToken}/endpoints/{id}` | `POST {app}/custom-endpoints`, `GET` and `PUT {app}/custom-endpoints/{id}`, `GET {app}/entities?IncludeProperties=true`. API server: `Custom/TestQuery` |
| Email | `/apps/{appToken}/email` | `GET` and `PUT {app}/email-settings`. API server: `Email/GetEmails`, `Email/Update` |
| Reports dashboard | `/apps/{appToken}/reports` | `GET {app}/reports`, `PUT {app}/reports/layout`, `DELETE {app}/reports/{reportId}`. API server: `Stats/Aggregate` |
| Report editor (a side sheet) | `/apps/{appToken}/reports/new`, `/apps/{appToken}/reports/{reportId}/edit` | `POST {app}/reports`, `PUT {app}/reports/{reportId}`, `GET {app}/entities?IncludeProperties=true`, `GET {app}/entities/{entity}/report-fields?Type=` |
| Sharing (owner only) | `/apps/{appToken}/sharing` | `GET` and `POST {app}/collaborators`, `GET {app}/collaborators/available-agents`, `GET {app}/permissions`, `PUT {app}/collaborators/{id}/permissions`, `DELETE {app}/collaborators/{id}` |
| Import schema | `/apps/{appToken}/import` | `GET {app}/schema-import/diff?Source=`, `POST {app}/schema-import` |
| Audit log | `/apps/{appToken}/audit-log?page=` | `GET {app}/audit-log?Page=&PageSize=` |
| Settings (general, status, rebuild, delete) | `/apps/{appToken}/settings` | `PUT {app}`, `PUT {app}/status`, `POST {app}/rebuild`, `DELETE {app}` |
| Clone | `/apps/{appToken}/clone` | `GET /servers`, `GET {app}/entities`, `POST {app}/clones` |
| Clone progress | `/apps/{appToken}/clone/{operationId}` | `GET {app}/clones/{operationId}` |
| **Instance (administrators)** | | |
| Servers | `/admin/servers` | `GET` and `POST /admin/servers`, `PUT` and `DELETE /admin/servers/{id}` |
| Users (people and their roles) | `/admin/users` | `GET /admin/users`, `PUT /admin/users/{userId}/role` |
| Agents (with 'Add agent', the key shown once, and deletion) | `/admin/agents` | `GET /admin/users`, `POST /admin/agents`, `DELETE /admin/agents/{userId}` |
| Applications of the instance | `/admin/applications` | `GET /admin/applications`, `POST {app}/cache-reset` |
| Data browser of any application | `/admin/applications/{appToken}/data/{entity}` | `GET /admin/applications/{appToken}`, then the data browser's API server calls |
| Settings, with 'Backup database' | `/admin/settings` | `GET` and `PUT /admin/settings`, `GET /admin/backup` |
| Audit log | `/admin/audit-log?page=` | `GET /admin/audit-log?Page=&PageSize=` |

Five endpoints exist for API callers only; the UI does not use them:
`GET {app}/entities/{entity}/properties`, `GET .../properties/{property}`, `GET {app}/reports/{reportId}`,
`GET /admin/servers/{id}` and `POST {app}/custom-endpoints/preview`.

The user menu also has 'API reference', a link to `/swagger`.

## Signing in, sessions and roles

**The session**

- Signing in sets the cookie `Apilane.Portal.Identity`. The setting `AuthCookieDomain` gives it a
  domain (`.example.com`), so other sites under that domain receive it too; `null` means the current host.
- A user has one session at a time: signing in somewhere else ends the earlier one.
- A session that was ended (signed out, or replaced by a newer sign-in) stays ended. Signing out with
  an old cookie only removes that cookie; it does not end the newer session.
- The cookie is checked against the account every 30 minutes. That is when a role change takes
  effect, unless the user signs in again first.
- 'Last sign-in' of a user moves only on a sign-in, a registration or a password change. For an
  agent it is the time the agent was added.
- A first start creates a pending administrator with a random temporary password printed in the server's startup output.
  Every UI route opens `/account/setup` until the administrator supplies that temporary password and chooses
  an email address and a new password. The API also blocks other operations (including sign-in, registration and internal access)
  until setup is complete. A pending setup gets a new temporary password on each server restart; completed
  setups never regenerate it. Existing installations without a bootstrap record are marked complete directly,
  without inspecting or changing their accounts or passwords.

**Sign-in, sign-up and passwords**

- `returnUrl` is followed after signing in only when it is a path on this site. A full address or
  anything a browser could read as another site is ignored and the applications page opens.
- A signed-in user who opens the sign-in or sign-up screen is sent on: to `returnUrl` when it is a
  path on this site, otherwise to the applications page.
- Administrator setup, sign in, sign up and the password-reset request share one rate limit: 30 calls per 60 seconds per
  IP address, then 429 (`AccountRateLimit__PermitLimit`, `AccountRateLimit__WindowSeconds`). Behind a
  reverse proxy read [Deploying](#deploying).
- When registration is switched off (Instance > Settings) the API answers 403 and the sign-up
  screen shows 'Registration is switched off'.
- A password has 8 to 100 characters.
- The reset request answers the same whether or not the address has an account. Resetting with an
  address that has no account fails with 'Invalid token.', like a wrong code.
- A successful password reset ends every session of that user.
- After changing the password you stay signed in; other browsers are signed out. With a session that
  was ended elsewhere the change is refused (401).
- A reset link without a code shows 'This link is not valid' with 'Ask for a new link'.
- The confirmations of forgot password and reset password are states of their screen, not addresses.

**Agents**

- An agent is an account for a script or an AI agent. The Portal shows its name; the internal
  `@agent.local` address is kept in API payloads and stored records only. It has no password and
  calls the API with a key. How to call, and what an agent is refused: [Agents](#agents).
- An administrator adds one on Instance > Agents with 'Add agent' and a name (3 to 40 characters:
  lower-case letters, digits and dashes). The key is shown once; the Portal stores only a hash of it.
- The owner opens 'Share with agent' on the Sharing screen and selects an available agent by name.
  The dialog lists the selected agent's other applications that the owner can also see (owned or
  shared with them). Agents start read-only. The owner chooses their rights when sharing,
  or later with 'Edit rights'; each application has its own policy. No mail is sent to an agent.
- Deleting the agent on the Agents screen stops its key at once and removes the agent from every
  application shared with it. The Users screen lists people separately. A lost key cannot be
  shown or replaced: delete the agent, add it again and share again.
- Nobody can sign up with an address at `@agent.local`, a password-reset request for one does
  nothing, and an agent cannot be made an administrator.

**Who may do what**

| Who | What |
|---|---|
| Any signed-in user | The own account, the list of servers, creating applications, and every application the user owns or that is shared with the user. |
| Owner of an application | Everything about it, sharing included. |
| Human collaborator | Everything the owner can do (delete, rebuild, clone, import schema, edit security, read the encryption key), except sharing. |
| Administrator (role Admin) | The 'Instance' screens and `/api/v1/admin/...`: servers, users and agents, instance settings, backup, the instance audit log, the list of all applications and the data browser of any of them. |
| Agent (calling with its key) | The shared applications, with the read/write/delete rights their owners grant per area. Defaults to read-only. Rebuild is a separate grant; deleting an application, sharing, creating/importing/cloning an application, encryption keys, API-server tokens and `/api/v1/admin` remain unavailable. See [Agents](#agents). |

- Under `/api/v1/applications/{appToken}` the Admin role opens nothing: an application the caller
  neither owns nor collaborates on is 404, administrator or not. The one exception is
  `POST {app}/cache-reset`, which works for an administrator on any application.
- Only an administrator may change the constraints of a system entity (Users, Files, the
  differentiation entity), in an application the administrator owns or collaborates on. For anyone
  else, an agent always, it is 403.
- An unknown or inaccessible application, entity, property or id is 404. An owner-only or
  administrator-only call is 403. The UI shows a 'not found' or 'no access' state for them.
- The API never answers with a redirect: no session is 401 with a JSON error, and an ended session is
  401 even on an administrator's address.

## Secrets

- Connection strings, the mail passwords of applications and of the instance, and the installation
  key are write-only. No answer carries them; an answer says whether a value is stored. In the UI an
  empty box keeps the stored value.
- A mail password is removed with 'Remove the stored value'. The installation key cannot be cleared.
- Three answers carry a secret, each from one endpoint only: the encryption key of an application
  (`GET {app}/connection-info`, shown masked in the Info dialog and dropped when it closes), the
  caller's own API-server token (`GET /session/api-token`) and the key of a new agent
  (`POST /admin/agents`, shown once in a dialog; only a hash of it is stored).
- The separate OAuth token endpoint (`POST /api/mcp/oauth/token`) returns connection-specific
  access/refresh tokens to the client. Only their hashes are stored; browser connection lists
  and approval summaries contain no tokens or agent keys.
- The administrator's list of applications shows each secret as 'Set', 'Not set' or 'Not needed'.
- A save that changes only a secret writes an audit entry showing `***` to `***`.
- The database backup (`GET /admin/backup`) holds everything, secrets included. Every download writes
  an audit entry ('Database backup', 'Apilane.db', 'Downloaded').

## The browser and the API servers

The Portal knows applications, servers, users and settings. Records, files, record history,
statistics, e-mail templates, storage size and the export of an application live on the API server
that hosts the application, and the browser calls that server directly:

- The UI asks the Portal for the user's token (`GET /api/v1/session/api-token`) and sends it to the
  API server as `Authorization: Bearer {token}` together with `x-application-token: {appToken}` and
  `x-client-id: portal`. Without the last header the API server does not take the token for a
  Portal user's.
- The token is kept in memory only: never in an address, never in storage. It changes at every
  sign-in; on a 401 from an API server the UI fetches it once more, and a second 401 is shown as an error.
- The API server asks the Portal whether that user may manage the application
  (`/api/internal/applications/{appToken}/access`) on every authorization check. Grants are not cached,
  so a revoked token or removed collaborator is refused on the next check; Portal connectivity is required.
- So every API server must be reachable from the browser at the address stored under
  Instance > Servers, and its open CORS policy is required (the calls carry an Authorization header).
- The health dot next to a server name is such a call too: any successful answer of
  `{server}/Health/Liveness` counts as online, it gives up after 4 seconds, repeats every 5 seconds,
  pauses while the tab is hidden and is grey until the first answer.

## Behaviour worth knowing

**Every screen**

- The API server's cache is reset only after a save succeeded, and the request waits for it. If the
  reset fails the save stands and the answer carries a `Warning` header, which the UI shows as a warning toast.
- A refusal by the API server shows with its own message at the field it is about. Any other failure
  of the API server is a 502 with a fixed text.
- Dialogs that can be linked to keep their state in the address: `?info=`, `?compare=`, `?item=`, `?view=`.
- Create, edit, rename and delete are dialogs on the list screen. Deleting a server, entity,
  property, custom endpoint, application or agent asks you to type its name.
- A screen that collects several edits before one Save (constraints, default sorting, security
  rules, the custom endpoint editor) asks before you leave with unsaved changes.
- Every screen works from 360px wide. Dark is the only theme.

**Applications page, new application, import application**

- A new application gets its token and its encryption key from the Portal.
- An unknown server is 404, a missing one 400. When the API server refuses the database, its message
  shows under the connection string. Any other failure of the API server is 502, and nothing is created.
- When the application was created but the API server could not be refreshed, you land on the
  application with a warning.
- When the API server created the application and the Portal then could not save it, the answer says
  exactly that, and the application stays on the API server.
- 'Import application' takes the `application.json` from the zip that 'Export' downloads. The file is
  checked completely before anything is created, an existing token is 409, and the report-series IDs of
  the file are reset. The application keeps the token and the encryption key of the file.
- No server is preselected unless the instance has exactly one. Without any server both forms show
  'No API server yet'.
- The search box appears from 7 applications up. Server groups are ordered by server name.

**Entities and properties**

- Renaming or deleting a system entity, and changing a system property, is 409.
- Renaming to a name that exists, in any letter case, is 409. So is a duplicate name on create.
- Renaming or deleting a property that is part of a unique or foreign key constraint is 409: remove
  the constraint first.
- Adding a property to an entity that takes none (Files) is 409.
- The differentiation switch is refused when the application has no differentiation entity. Change
  tracking cannot be turned on for an entity whose records cannot be updated.
- Property checks: decimal places 0 to 8; the type is one of the four names; the minimum length of a
  String is not negative and its maximum at least 1; Minimum and Maximum lie between
  -9,007,199,254,740,991 and 9,007,199,254,740,991, the range a browser can show exactly.
- An entity name has 4 to 30 characters, a property name 4 to 120; both are letters and underscore
  only, and a property name must not end in '_Data'.
- A description or regex that is only spaces is stored as empty.
- Stored constraints that cannot be read are left out of the answers instead of breaking them.
- The link from an entity to its security rules is `/apps/{appToken}/security?item=Entity-{name}`.

**Constraints and default sorting**

- The server checks every constraint (names exist and are allowed, no duplicates) and keeps the
  system constraints itself. The same unique properties in another order, and the same foreign key
  with another on-delete action, count as duplicates.
- On a system entity a user who is not an administrator sees the constraints read-only, and a save is 403.
- The on-delete action of a stored foreign key changes only by removing it, saving, adding it again
  and saving.
- A foreign key stored without an on-delete action is rewritten with 'no action' on the next save,
  and a stored constraint that cannot be read is removed by the next save.
- Default sorting: each property must exist, appear once and have the direction asc or desc. A stored
  direction that is not 'asc' shows as descending, a property stored twice shows once, and stored
  data that cannot be read counts as no sorting.

**Application settings, status, rebuild, delete**

- The stored database type decides whether a connection string is written.
- The status call takes an explicit Online value, so repeating it changes nothing.
- The typed name and the 'I understand' box of rebuild and delete are guards of the browser only:
  the API has no confirm field.
- When the API server deleted the application and the Portal then could not save, the answer says exactly that.

**Sharing**

- 'Share' accepts a person's e-mail address as free text. Sharing with your own address is 409.
- 'Share with agent' opens a separate dialog with an explicit agent picker. Only agents not
  already shared with this application are offered (`GET {app}/collaborators/available-agents`).
  The selected agent's other applications are shown by name, limited on the server to applications
  the caller owns or collaborates on. Hidden applications are not included, even in counts.
  Names are displayed; the original agent address is sent to the API. A failed agent list can be
  retried, and an agent must be selected before sharing.
- The collaborator gets a mail when the instance can send one; the toast says whether it was sent.
  An agent is never mailed.
- An agent starts read-only. For each area choose None, Read or Read and write; deletion is a
  separate choice for entities/properties, custom endpoints and reports. Application settings and
  rebuild are separate permissions with no read choice. The application summary stays visible.
- 'Edit rights' beside an agent changes its policy for this application, effective on its next
  request. Existing agent collaborators without a saved policy also start read-only.
- Email settings support Read only. Entity/property renames and all mail-setting changes are
  permanently unavailable to agents, regardless of their other rights.
- A collaborator who opens the Sharing address gets the 'no access' state (403).

**Email**

- The SMTP settings and the confirmation landing page are saved to the Portal with one 'Save settings'.
  The e-mail templates live on the API server and are read and saved there.
- Only people may change these settings. Agents with email-settings Read may inspect them without
  the password; no grant permits an agent to change the transport, sender, credentials or redirect.
- A template preview is drawn in a sandboxed frame in which no script runs.
- Without a Redirect URL, a user who confirmed their e-mail lands on `/account/email-confirmed` of the Portal.

**Audit log (instance and application)**

- A page below 1 or a page size outside 1 to 200 is 400. The UI asks for 50 and reads a bad `?page=` as page 1.
- Entries with the same timestamp are ordered by ID, so pages are stable.
- Stored changes that cannot be read are shown with empty fields.

**Security**

- Settings and rules are saved separately, each with its own Save and endpoint.
- The rules API refuses: an unknown type or rate window, an empty role, Schema rules that are not
  'get' on 'Schema', duplicate rules, actions the item does not offer, property names the action
  does not offer, properties on rules that are not about an entity, and a missing record scope.
- Stored rules that can never apply (deleted item, unknown type or action, blank role, a second rule
  for the same cell) are left out when reading, and so dropped by the next save.
- IP addresses are plain dotted IPv4 (no leading zeros, signs or spaces inside), because the API
  server compares them exactly. The logic is Block or Allow. With Allow and a non-empty list the
  screen warns about locking yourself out.
- The action of a rule is stored in lower case.
- A switched-on cell with no properties means 'only the ID is returned' (get) or 'the call is
  refused' (post, put). The editor, the tree and the matrix all say so.
- The tree and the matrix show access as the API server enforces it: inherited access (Anonymous and
  Authenticated apply to every role), properties added up, the rate limit of the item and action as a whole.
- When the API server cannot be asked for the users' roles, the screen still opens, with a warning
  and the roles already used in rules.
- The file size limit is 1 to 25600 KB.
- A cell switched off and on again gets its properties and rate limit back, until the next save or discard.

**Custom endpoints**

- An endpoint is addressed by its numeric ID, so a rename keeps the address of its editor. An unknown ID is 404.
- The name is trimmed, letters only, at most 80.
- Renaming changes the address of the endpoint on the API server, and the security rules written for
  the old name no longer apply. The editor warns about both.
- SQL that contains `{appToken}` works.
- Test sends the parameter values URL-encoded and exactly as typed, so long whole numbers are not
  rounded. An empty parameter is sent as SQL null.
- In the SQL editor Tab leaves the editor (it does not indent), so the keyboard can always get out.

**Data browser**

- Editing a record sends only the fields that changed, plus the primary key. If nothing changed,
  nothing is sent.
- A file is downloaded with the token in a request header and saved under the record's Name.
- 'Export page as CSV' exports the current page only; a null is an empty cell.
- 'Delete selected' deletes several records in one call. Clearing the history of a record asks first.
- Filters are sent 400 ms after typing stops, and a newer request cancels the one still running.
- Page, page size and sort are in the address; filters are not, so switching entity starts fresh.
- Custom entities are listed first, then system entities. Without an entity in the address the first
  custom entity opens.
- The dates of a new record are pre-filled with the browser's local time, while the API server reads
  the text as UTC. The help line of the field says so.

**Reports**

- The labels and values of a Grid report are written as text, never as HTML.
- The report API refuses: an unknown report type, a time range that is not 1 to 1000 followed by h, d,
  m or y, a Property or Group-by the editor does not offer for that entity and type, a grouping listed
  twice, and an entity name in the wrong letter case.
- The layout API checks bounds (X 0 to 11, Y 0 to 10000, Width 1 to 12, Height 1 to 100, X + Width at
  most 12) and refuses unknown or repeated report IDs. A refused layout saves nothing.
- A series whose entity or property is gone, or whose call fails, shows its own error inside the
  panel; the other series still draw.
- A stored filter the flat filter builder cannot show (OR, nested groups) is shown as raw text and
  kept unless it is replaced.
- An unknown report ID is 404. A new report starts with 'Max points per series' = 20.
- Refresh and 'View API endpoint' work out the time window from now.
- From 768px wide the panels sit on a 12-column grid and can be dragged and resized; the layout is
  saved 600 ms after the last change. Below that the dashboard is one column and nothing moves.
- If the grid library cannot be downloaded the dashboard falls back to one column; if the chart
  library cannot, a chart falls back to a table.

**Import schema and compare**

- Everything is checked before anything is applied. A failure is an HTTP error that names the place
  in the payload (`Entities[0].Properties[2].TypeID`).
- Constraint references are validated against the complete imported schema before any writes.
  Unique properties must be unencrypted; a foreign key needs a custom Number property with 0 decimal
  places and cannot point to Files. Names are trimmed and normalized to schema spelling; invalid
  identifiers, missing references and SQL fragments are refused. Equivalent existing constraints
  are skipped, including whitespace, unique-property order and default/numeric foreign-key actions.
- The import is not atomic: the steps before a failed one stay applied. Sending the same payload again
  is safe, since what exists and does not differ is skipped with a warning. The screen asks before it runs.
- In the payload an entity must be listed before the entities whose foreign keys point to it.
- TypeID, Record and TimeWindowType are numbers in the payload, the values that are stored.
- Only an administrator may add constraints to a system entity through an import (403 otherwise).
- Stored security rules that cannot be read answer 409 before the first write.
- When the API server cannot be refreshed after an import, the result is success with a warning.
- 'Load diff' needs the exact token of the source application, which the user must be able to see.
- Compare answers 404 for an application the user cannot see and 409 for stored rules that cannot be read.

**Clone**

- The clone is a new application named '{name} - Clone', owned by the caller, with a new token and
  the same encryption key. Reports, collaborators and files are not copied.
- Starting answers at once (202) with an operation id. The progress can be read only by the user who
  started the clone, and only under the application that is cloned.
- With 'Clone data' on, entity names that are not entities of the application are refused, and the
  form refuses an empty selection.
- A wrong connection string shows as a failed clone, not as an error of the form: the API server is
  called after the answer.
- A failed clone leaves the new application listed; delete it then.
- The progress screen asks every 2 seconds and pauses in a hidden tab. An operation the Portal no
  longer knows shows 'This clone can no longer be tracked'.

**Administration**

- A server address must be an http or https URL. A server that hosts applications cannot be deleted (409).
- Users: changing your own role is 409, an unknown user 404, and asking for the role a user already
  has succeeds without change. A role change takes effect within 30 minutes or at the next sign-in.
- Applications: 'Clear cache' goes through the Portal, so a failure reflects the Portal's connection
  to the API server. 'Open application' appears only for applications the administrator owns or
  collaborates on.
- The administrator's data browser works for every application. For someone else's application the
  links to its own screens are left out.
- Backup: the file is made with SQLite's online backup into a uniquely named temporary file and streamed.
- The key stored in the Portal database (Instance > Settings) is the one in force in both directions: the Portal
  expects it from the API servers (`/api/internal`) and sends it to them when it creates, imports or clones an
  application (`POST /api/ApplicationNew/Generate`). After a change, set the same value as `InstallationKey` of every
  API server and restart the API servers; the Portal needs no restart. Until then the two refuse each other, and
  create, import and clone fail with a 502.

## Mails the Portal sends

| Mail | Link in it |
|---|---|
| Password reset | `/account/reset-password?code=...` (only the code, no user id) |
| Password changed (sent to the address stored on the account) | `/account/forgot-password` |
| An application was shared with you | `/apps` |

The links use the configured `PublicUrl`, never the request's host or forwarded headers. Without an
explicit value, a concrete listening `Url` is used for local development. A wildcard listener needs
`PublicUrl` before sending Portal email. Reset requests answer 409 when that origin or the mail settings
under Instance > Settings are missing. Set `AllowedHosts` as an additional restriction on incoming hosts.

The mails of an application (confirmation, password reset for its end users) are sent by the API
server, not by the Portal. After a confirmation the API server sends the end user to the
application's Redirect URL, or without one to `{PortalUrl}/account/email-confirmed`.

## Deploying

**The image.** `src/Apilane.Portal/Dockerfile` builds the UI in a Node stage and copies the result
into the final image; the final image has no Node. Build it from the repository root:

```bash
docker build -f src/Apilane.Portal/Dockerfile --build-arg VERSION=10.2.0 -t apilane-portal .
```

`VERSION` is required: without it the .NET build stage fails. It becomes the version shown in the footer.

**Outside Docker.** `dotnet build` and `dotnet publish` do not build the UI. A publish includes
whatever is in `src/Apilane.Portal/wwwroot/ui` at that moment (the last local build, or nothing):
run `npm ci` and `npm run build` in this folder first. Without a built UI every UI address answers
404 with a text that says how to build it. Start the Portal from its own folder (the one that holds
`wwwroot` and `appsettings.json`): started from another folder it finds no UI and answers the same 404.

**Settings.** `appsettings.json` holds defaults; override them with `appsettings.{Environment}.json`
or environment variables (`__` for nesting).

| Setting | Meaning |
|---|---|
| `Url` | The address the Portal listens on. In Docker `http://0.0.0.0:5000`. |
| `ApiUrl` | The first API server, as the Portal and browsers reach it. It seeds Instance > Servers on first start. |
| `FilesPath` | The folder of the Portal's SQLite database (`Apilane.db`) and its data-protection keys. A mounted volume in Docker. |
| `InstallationKey` | The secret shared with the API servers. It seeds the key stored in the database on first start and is read again only for the startup warning. The key in force, in both directions, is the stored one, changed under Instance > Settings (no restart of the Portal). |
| `PublicUrl` | Trusted browser-facing origin for Portal email links and MCP discovery/audience, without credentials, a path, query or fragment. Falls back to a concrete `Url`; wildcard listeners need an explicit value. MCP requires HTTPS except loopback development. |
| `InstanceTitle` | The name shown in the UI. It seeds the database on first start only; later it is changed under Instance > Settings. |
| `AuthCookieDomain` | The domain of the login cookie; `null` for the current host. |
| `AccountRateLimit__PermitLimit`, `AccountRateLimit__WindowSeconds` | The shared rate limit of administrator setup, sign in, sign up and reset request. Default 30 per 60 seconds per IP address. Both must be greater than 0, or the Portal does not start. |
| `McpOAuthRateLimit__PermitLimit`, `McpOAuthRateLimit__WindowSeconds` | The shared rate limit of MCP OAuth registration, authorization and token exchange. Default 60 per 60 seconds per IP address. Both must be greater than 0. |
| `AllowedHosts` | The host names the Portal answers for. Set it to the public host name, plus the host in the API servers' `PortalUrl`. |
| `OpenTelemetry`, `Serilog`, `MinThreads` | Metrics at `/metrics`, tracing, logging, thread pool. |

**Behind a reverse proxy.** The rate limit counts by the connection's address. Behind a proxy that
is not on loopback that is the proxy's address, so all visitors share one budget. Either raise
`AccountRateLimit__PermitLimit`, or set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, but only when the
proxy is the only way to reach the Portal and it overwrites `X-Forwarded-For`: the header is otherwise
trusted from any sender.

**Caching.** Files under `/assets` carry a content hash in their name and are served as
`public, max-age=31536000, immutable`. Every other file of the UI and the index page are `no-cache`,
so a new version is picked up on the next page load. Every answer under `/api` is `no-store`.
A tab that was open during a deploy and asks for a file that no longer exists reloads itself once.

**Versions.** A Portal and its API servers must run the same version. The API servers call the
Portal's internal API (`/api/internal`), whose addresses and answers are not versioned, and they
send end users to a UI address (`{PortalUrl}/account/email-confirmed`). Upgrade them together.

**What must be reachable.** The API servers must reach the Portal (`PortalUrl`), and the Portal and
every browser must reach each API server at its address under Instance > Servers. `/api/internal`
is only for the API servers: when the Portal is public, a proxy may block that prefix from outside.

**Changing the installation key.** The key stored in the database (Instance > Settings) is the one in force. Save
the new key there, set the same value as `InstallationKey` of every API server and restart the API servers; the
Portal needs no restart, and its own `InstallationKey` setting stays the first-start seed. Until the API servers run
with the new key, the Portal and the API servers refuse each other.

## Known limits

Sign-in and mail:

- A person has one session at a time: a script that signs in with a person's account ends that
  person's session. A script should use an agent key instead ([Agents](#agents)).
- An agent has one key, without an expiry date, and wrong keys are not counted or slowed down
  (see [Open decisions](#open-decisions)).
- An account that a person registered at `@agent.local` before agents existed keeps its password,
  but counts as an agent: it cannot be made an administrator and it can be deleted on the Agents screen.
- Portal email links use `PublicUrl`; request headers cannot change their destination.
- The shared mail service (`Apilane.Common/Services/EmailService.cs`) logs the full mail body, reset
  link included, at Information level, and swallows SMTP failures. So a failed send looks like a
  success, and 'mail sent' means handed to the sender, not delivered.
- The reset request answers the same for a known and an unknown address, but not equally fast: the
  mail is sent inside the request.
- Collaborators are matched by the exact letter case of their e-mail address. 'Bob@x.com' for the
  account 'bob@x.com' is accepted and gives no access.
- A role change takes effect within 30 minutes or at the user's next sign-in.
- The API server builds the e-mail-confirmation landing address from its `PortalUrl`, the
  server-to-server address. When that is not the public address of the Portal, set the application's Redirect URL.
- The Content-Security-Policy header does not restrict script and style sources yet.

Applications and schema:

- Renaming an entity or property does not update security rules, reports, custom endpoint SQL or the
  default sorting. Deleting does not remove them. Security rules for the old name are not listed any
  more, nor are property names that are gone from a rule, and the next save of the rules drops
  them: write the rules again under the new name.
- A case-only rename of a property changes the name in the Portal but not the column, which breaks
  PostgreSQL applications. The cause is in the API server.
- When the API server accepted a create, import or clone and the Portal step then failed, the
  application stays on the API server.
- Clone: the bar reaches 100% after the schema phase and drops to 50% when data starts; reports are
  not cloned; the clone's name can pass 100 characters; operations are kept in memory only, so a
  restart of the Portal loses the progress screen.
- Entity names are matched case-sensitively in Compare and case-insensitively in the schema import diff.
- 'Load diff' rounds whole numbers above 9,007,199,254,740,991 before they reach the payload box.
- A security rule stored with an unknown rate window (written by an older version) makes Compare
  and a schema import answer 500 until the Security screen is saved once.
- If the stored security rules are not readable JSON, the Security screen shows an empty grid and the
  next save overwrites them. This needs a hand-edited database to happen.
- Saves that replace a whole list (security rules, constraints, default sorting, the series of a
  report) and the layout save do not check whether someone else changed it in the meantime.
- After a rules save the role rows are not read again from the API server; a reload shows new or vanished roles.

Reports:

- A series with a time range still gets at most 'Max points per series' groups, so 'Last 90 days'
  by day with the default 20 shows the newest 20 days.
- Panels can be moved and resized only with a pointer or touch.
- A report saved by an older version may need one correction: a panel stored with a height above
  100 or X + Width above 12 makes every layout save answer 400 until that panel is made smaller, and
  a report whose Property or Group-by its type does not offer must be corrected before it saves.
- After a panel was deleted in another tab, a layout save fails until the dashboard is reloaded.

Smaller things:

- Backup: the temporary file goes to the system's temporary folder and is not removed when the
  process is killed during a download. A failed download opens the JSON error in the browser.
- The Info dialog opened from an application's own screen cannot be linked to; `/apps?info={appToken}` can.
- The tab title does not name the application (the Data screen names the entity).
- One screen has two names on purpose: the tab reads 'Sorting', the heading 'Default sorting'.
- The contract lists a 401 answer on the anonymous account endpoints, because the base class
  declares it for every action. Only `POST /session` can answer it.
- The e-mail placeholder list (`src/lib/emailTemplates.ts`) is a hand copy of `EmailEvent.cs`: update
  it when an event is added.
- The list of the Portal's own address prefixes is kept in three files by hand (see [Addresses](#addresses)).

Not checked in a browser yet (do these first, see [Release check](#release-check)):

- The Portal serving the built UI at the site root, the dev server with its proxy, and `/swagger`
  signed out through the sign-in screen and back.
- The delivery of a mail through a real mail server.
- The administrator's data browser on an application the administrator does not own.
- Constraints of a system entity as a user who is not an administrator, and a real 403 or 502 on a save.
- Widths from 640 to 1000px, where the application tabs wrap, and keyboard focus after dialogs opened from a menu.
- The Agents screen: 'Add agent', the dialog that shows the key, the agent list and deletion.
- The separate agent-sharing dialog: choosing an agent, its other visible applications, and the toast after sharing.

## Open decisions

For the owner. The default is what the code does today; say nothing and it stays.

| Decision | Default now in place | Alternative |
|---|---|---|
| Data plane | The browser calls the API server directly with the token from `GET /session/api-token`. Script in the page can read that token. | Send every data call through the Portal: about 15 more endpoints and an extra hop. |
| Protection against cross-site requests | The header `X-Apilane-Portal: 1` on every write, plus the SameSite cookie. | ASP.NET antiforgery token endpoint and header. |
| Secrets | Write-only; only the encryption key can be read, in the Info dialog. | An administrator-only 'reveal', for example for the installation key. |
| Administrator's data browser | Works for any application of the instance, with no audit entry when it opens someone else's. | Keep it to applications the administrator owns or collaborates on, or write an audit entry. |
| `AuthCookieDomain` and `returnUrl` | The setting stays, and `returnUrl` accepts only paths on this site. | Remove the setting, or accept return addresses on hosts under that domain for other sites that share the cookie. |
| Cache reset after a save | Only after success, awaited; a failure is a warning and the save stands. | Fail the request with 502. |
| Cache reset after 'Import application' | Not done. | Reset it, so an API server that cached an error for that token recovers at once. |
| Security screen | Separate saves for settings and rules. | One atomic save of both. |
| Server address | http or https only. | Any scheme (`ServerService.ValidateUrl`). |
| Rebuild and delete confirmation | Guards of the browser only. | Require the application name in the request. |
| Collaborator rights | A human collaborator can delete, rebuild, clone, import schema, edit security and read the encryption key; only Sharing is owner-only. Agents have per-application read/write/delete policies and permanent restrictions described below. | Narrow them for people too. |
| Collaborator e-mail letter case | Saved as typed; access needs an exact match. | Save the exact address of the matching account when sharing (`CollaboratorService.AddAsync`), or match without case everywhere. |
| Rate limit | 30 calls per 60 seconds per IP address. | Other numbers. |
| Agents | One key per agent, with per-application permissions starting read-only. Left out on purpose: an expiry date for raw keys, several keys per agent, re-issuing a key without deleting the agent, a rate limit on wrong keys. MCP connections have their own expiry and revocation. | Add the ones that turn out to be needed. |
| Schema import: numbers in the payload | TypeID, Record and TimeWindowType are the stored numbers. | Names, like the rest of the API. |
| Schema import: foreign-key order | A referenced entity must be listed before the entities that point to it. | Order them in `InForeignKeyOrder`. |
| Stored sort direction that is neither asc nor desc | Shown as descending, although the API server sorts it ascending. | Show it as ascending. |
| Dates of a new record | Pre-filled with the browser's local time, read as UTC by the API server. | Pre-fill in UTC. |
| `{appToken}` as a parameter name of a custom endpoint | Accepted and works. | Refuse it with a 400. |
| Security rules the API still accepts | 'Owned records only' where it has no effect, and a property listed twice in one rule; the schema import accepts them too. | Refuse both in `SecurityRuleChecks`, which the rules editor and the import share. |
| Differentiation entity of a new application | Checked for length only; a bad name comes back as an API server error under the connection string. | Add the letters-only rule. |

---

# Part 2: Calling the management API

For a person with `curl`, a script or an AI agent. The contract is the file `openapi/portal-v1.json`
in the repository. A running Portal serves the same document at `/swagger/v1/swagger.json` and a
browser for it at `/swagger`, both for users signed in with the cookie: an agent key does not open
them, so an agent reads the file, of its own version: `Version` in the answer of `GET /api/v1/session`
is the release tag (`openapi/portal-v1.json` at that tag; `main` for a build of your own). The
summaries in the contract say what each call checks and answers. The contract does not describe the
Bearer header as a security scheme: send it yourself.

## Basics

- **Base address**: `{Portal}/api/v1`.
- **Two ways to authenticate**:
  - An agent key, for scripts and AI agents: send `Authorization: Bearer {key}` on every call. There
    is no sign-in call and no cookie. See [Agents](#agents).
  - The session cookie, for browsers and for a person with `curl`: `POST /api/v1/session` with
    `{"Email": "...", "Password": "..."}` sets it; send it on every later call. A user has one
    session at a time, so a script that signs in as a person ends that person's session.

  A request that sends a Bearer value is judged by that value alone: a cookie sent with it is ignored.
- **Writes need a header**: every POST, PUT, PATCH and DELETE must send `X-Apilane-Portal: 1`, the
  sign-in call included. Without it the answer is 403 FORBIDDEN. A page on another site cannot add
  the header to a request that carries the user's cookie, which is what protects writes against
  cross-site request forgery. A request with an agent key needs no such header: a browser never
  sends a key on its own.
- **Without a session**: only `GET /bootstrap`, `POST /bootstrap`, `GET /instance`, `POST /session`,
  `DELETE /session`, `POST /account`, `POST /account/password-reset-requests` and
  `POST /account/password-resets` work. Until administrator setup is complete, only the bootstrap
  calls and `GET /instance` are available. Setup, sign in, sign up and the reset request share the
  rate limit (429 with a `Retry-After` header).
- **JSON**: property names are PascalCase. Types and choices are names, not numbers (`SQLLite`,
  `Grid`, `Owned`). The one exception is the schema import payload, which uses the stored numbers.
- **Lists**: every list answer is `{ "Data": [...], "Total": n }`. Only the audit logs are paged:
  `?Page=` (from 1) and `?PageSize=` (1 to 200, default 50).
- **Secrets are never returned**; an answer says whether one is stored (`HasConnectionString`).
  In a request, `null` or a left-out secret keeps the stored value. See [Secrets](#secrets).
- **Answers are never cached**: every answer under `/api` carries `Cache-Control: no-store`.
- **Never a redirect**: no session is 401, not a redirect to the sign-in page.
- **A `Warning` response header** on a successful write means: saved, but the API server could not
  be refreshed. Its value is a text for the user. `POST {app}/cache-reset` tries the refresh again
  (204).
- **202** means the work goes on after the answer: a clone answers with an `OperationId` and a
  `Location` header to poll.
- **Who may call what**: [Signing in, sessions and roles](#signing-in-sessions-and-roles).

## Agents

An agent is an account for a script or an AI agent: a normal user at `@agent.local` with no
password, and one key. The documentation page `docs/docs/developer_guide/ai_agent_portal.md` has a
block to append to the `AGENTS.md` of the project that holds the agent.

1. **Create it.** An administrator opens Instance > Agents, chooses 'Add agent' and types a name:
   3 to 40 characters, lower-case letters, digits and dashes. (The call is `POST /api/v1/admin/agents`
   with `{"Name": "deploy-bot"}`.) The answer gives the agent's address, `deploy-bot@agent.local`,
   and its key, `apl_{KeyId}_{Secret}`. The key is shown this once: the Portal stores only a SHA-256
   hash of the secret.
2. **Give it access.** The owner opens 'Share with agent' on the Sharing screen and selects its name.
   `GET {app}/collaborators/available-agents` lists the available names and original addresses,
   plus `Applications` summaries (`Token`, `Name`) for other applications visible to the caller.
   The UI sends the selected address unchanged, so exact account matching is preserved. The
   agent is then a collaborator of that application with read-only access by default. Set its
   rights in the share dialog or choose 'Edit rights' beside it later. No mail is sent to an agent.
3. **Call the API.** Send `Authorization: Bearer {key}` on every call. No sign-in, no cookie, no
   `X-Apilane-Portal` header.

For an MCP client, use [MCP connections](#mcp-connections) instead of handing it the agent key.

```bash
KEY='apl_0123456789ab_...'

# Who am I, and which applications are shared with me? Take the Token of one.
curl -H "Authorization: Bearer $KEY" http://localhost:5000/api/v1/session
curl -H "Authorization: Bearer $KEY" http://localhost:5000/api/v1/applications

# Discover the rights and operation requirements for this application first.
curl -H "Authorization: Bearer $KEY" http://localhost:5000/api/v1/applications/{appToken}/permissions

# Only when entities.Write is granted: create an entity in it.
curl -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"Name":"Products","Description":"The catalogue"}' \
  http://localhost:5000/api/v1/applications/{appToken}/entities
```

A key that is wrong, unknown or malformed answers 401 UNAUTHORIZED, with the same body in every case.

**Per-application rights.** Both new and existing agent collaborators start read-only unless the
owner saves a policy. The policy is stored separately from the application and collaborator
records. A policy is checked on every request, so changing it takes effect on the next request.

| Resource | Read | Write | Delete |
|---|---|---|---|
| `application` | The common summary is always visible | General settings, status and cache reset | Never |
| `entities` | Entities, properties, constraints, sorting and report fields | Create, edit, constraints and sorting; never rename entities or properties | Entities and properties |
| `security` | Settings, roles and rules | Settings and rules, including replacing or removing rules | — |
| `custom-endpoints` | Definitions and preview | Create and edit SQL definitions | Custom endpoints |
| `reports` | Definitions and layout | Create, edit and arrange reports | Reports |
| `email-settings` | SMTP settings and confirmation redirect, excluding secrets | Never | — |
| `audit-log` | The application's audit log | — | — |
| `schema` | Schema import diff and comparison | Schema import | — |
| `rebuild` | — | Rebuild, which removes all application data | — |

Write and Delete require Read for areas that support reading; Delete does not require Write.
Application settings and rebuild are write-only capabilities and default to denied. All areas
that support Read default to readable. Write access can have wide effects even without Delete:
security can replace rules, entities can replace constraints, and custom endpoints can save SQL.
Entity/property renames and all application mail-setting changes remain unavailable regardless
of grants. Custom endpoint renames keep their existing custom-endpoint permissions. The former
email-settings Write grant is retired: it is ignored when reading saved policies while unrelated
grants remain in effect, and new policies cannot grant it.

**Discovering rights.** `GET {app}/permissions` remains available even if all areas are denied.
It answers `IsAgent`, effective `Permissions` (`Resource`, `Read`, `Write`, `Delete`), the resource
catalogue in `Resources` (`Resource`, `Name`, `Description`, `CanRead`, `CanWrite`, `CanDelete`),
and operation requirements and permanent restrictions. An agent should read it before planning
work, call only allowed operations, and refresh it after a 403 instead of trying another route.
`Operations` contains `Method`, `Path`, `AllowedForThisApplication`, `Requirements` (`Resource`,
`Access`: Read, Write or Delete) and `AdditionalRequirements`. `Restrictions` lists permanent limits.
The boolean covers the current application's fixed requirements; the additional text explains
checks of another application, a selected entity or the request body.

Comparison and schema diff need schema, entities, security and custom-endpoint Read on both
applications. Schema import needs schema Write plus Write for each non-empty affected area
(`Entities`, `Security`, `CustomEndpoints`); all permission checks finish before any changes begin.
Audit-log Read exposes historical changes across every area, including SQL and security rules,
even if current reads of those areas are denied. Security and report Read also reveal the names
and types of referenced entities, properties and endpoints; these implications are described in
the resource catalogue shown to the owner.

**Saving rights.** The owner may supply `Permissions` with `POST {app}/collaborators` when the
address is an agent. Omit it for the default read-only policy. `PUT {app}/collaborators/{id}/permissions`
with `{"Permissions":[{"Resource":"security","Read":true,"Write":false,"Delete":false}]}`
replaces the agent's policy; every area omitted from an explicit policy is denied. An empty array
denies all areas, while the common application summary and permission discovery remain visible.
Collaborator responses carry effective `Permissions` for agents and `null` for people. Only the
owner can change rights, and people keep their existing collaborator access.

**What an agent is refused.** These answer 403 FORBIDDEN with the message 'An agent cannot do this.
A person has to do it in the Portal.':

- deleting an application or collaborator, and signing out through `DELETE /session`;
- `GET {app}/connection-info`, the encryption key;
- `POST {app}/entities/{entity}/rename` and `POST {app}/entities/{entity}/properties/{property}/rename`;
- `PUT {app}/email-settings`, including every SMTP field and the confirmation redirect;
- `POST /applications`, `POST /applications/import` and `POST {app}/clones`: a new application;
- everything under `/api/v1/admin`. An agent cannot be made an administrator either;
- `GET /session/api-token`, so an agent cannot call the API servers (records, files);
- `POST /session`, `POST /account`, `PUT /account/password` and `POST /account/password-resets`.

Other operations require the corresponding grants above. Missing rights answer 403 FORBIDDEN.
An application that is not shared with the agent is 404, as for any user.

Two things are not open to an agent although the list above does not name them, and answer an
ordinary 403 FORBIDDEN: everything under `{app}/collaborators` (sharing is for the owner, and an
agent is a collaborator), and `PUT .../constraints` or a schema import with constraints for a system
entity (only an administrator may, and an agent is never one).

An agent cannot get a token for the API servers (`GET /session/api-token` is refused; the Portal
keeps one for its own calls on the agent's behalf), so it cannot read or write
records, files, record history or statistics, edit e-mail templates, or run the Test of a custom
endpoint (`POST {ServerUrl}/api/Custom/TestQuery`). The Portal does not check the query of a custom endpoint:
it is stored as it is sent.

These permissions govern management API operations. A custom endpoint is SQL that the API server
runs and commits against the application's database when called. An agent with custom endpoint
and security write access may create one and allow it to run, so those grants carry broad effects
beyond the management endpoint itself. Give an agent only the rights needed for its work.

**Revoking.** Delete the agent on Instance > Agents (`DELETE /api/v1/admin/agents/{userId}`). Its
key stops working at once and the agent is removed from every application shared with it. A key
cannot be replaced: delete the agent, add it again and share again.

## MCP connections

The Portal hosts MCP in the same process at `/api/mcp`; no sidecar or client-specific plugin is needed.
The implementation is the `Apilane.Portal.MCP` class library: transport, OAuth, database models,
connection actions/contracts and the operation gateway. It references Common rather than the
Portal host. The Portal registers it through `AddPortalMcpHost`, supplies database/identity and
operation-policy adapters, and attaches its usual guards through a thin connection controller.
The five MCP tables stay in the Portal database, configured by `ConfigureMcpModel`. This is a
project boundary within the current deployment; the Portal remains the host and authorization
authority.
Set `PublicUrl` to the canonical HTTPS Portal origin (loopback HTTP is allowed for local
development). Discovery and credentials are bound to this configured address, never an incoming
Host header. The two tools are `apilane_operations` (operation catalogue, or the detailed contract
for one `operationId`) and `apilane_call` (operation ID, `path`, `query` and JSON `body`). The latter
returns `status`, `data` and any `warning`, and marks failed API calls as tool errors. Use
`PortalApplications_List` to discover the connection's applications, then
`ApplicationPermissions_Get` to discover the agent's current grants. The catalogue omits
permanently refused calls and does not imply that a particular grant is enabled.

**Connect a client.** In **MCP connections > MCP setup**, copy the server address and suggested
unique server name. Add a remote Streamable HTTP server with OAuth browser authentication in
your client's settings. Supported clients must implement OAuth discovery, dynamic registration,
authorization-code flow with S256 PKCE and a loopback callback. Each client has its own
configuration syntax and sign-in controls; the Portal does not generate a client-specific file.
For separate project connections, use a distinct server name per project, such as `apilane_shop`
or `apilane_billing`, in project configuration when supported by the client.
Authenticate the server in the client, then sign into the Portal in the browser. Pick an existing
agent, review the listed applications, name the connection and approve it. No agent key,
profile name or chat ID is needed in the client's configuration.
The Portal's approval page returns only to the registered loopback callback, with PKCE protecting
the authorization code.

Chats using the same authenticated server share its connection. Clients may store credentials
outside project configuration or reuse them across projects; follow the client's instructions
for separate authentication entries. Names and directories are configuration scopes and do not
isolate credentials from people/processes with access to the same credential store.
For a client in a container or on a remote host, the browser must reach its loopback OAuth callback:
forward the callback port to the browser's machine before authenticating. See the client's
documentation for callback settings. Separate concurrent callback listeners need separate ports.

**Delegated access.** Any confirmed personal account can approve an agent already shared into
at least one application it owns. Administrators have the same ownership boundary when approving.
A connection snapshots only the application IDs reviewed on the approval page; approval refuses
applications no longer owned/shared and cannot silently include a new share. Each request intersects that snapshot
with the authorizer's current ownership and the agent's current collaborations, then applies the
agent's current resource grants. Future applications/shares cannot enlarge the connection. Lists,
primary application calls and secondary application lookups (comparison/schema import) all
respect this boundary. The existing agent restrictions, including entity/property renames,
administration, sharing, application lifecycle, secrets and mail-setting writes, also apply.
MCP runs the existing management actions through MVC's validation and agent/session filters;
its transport is stateless so no transport session retains an old identity or permission set.

**Review and revoke.** `/mcp/connections` lists the caller's connections; administrators can see
and revoke all. Each row shows its agent, effective applications, authorizer (for administrators),
creation/last use, expiry and status. Revoking one stops its access and refresh tokens without
affecting the agent's other connections or raw key. Deleting the agent, replacing its key,
deleting/locking its authorizer or changing the author's security stamp invalidates access.
Removing an application's share or transferring its ownership removes that application from
the connection. Connections last 30 days, access tokens 15 minutes, browser approval requests
10 minutes and authorization codes 2 minutes. Refresh tokens rotate; replay revokes the whole
connection. Codes and tokens are stored only as hashes. Browser approval/revocation endpoints
are cookie-only, retain the CSRF header requirement and are marked `[NoAgent]`.

OAuth registration, authorization and token endpoints have their own per-IP limit, configured
by `McpOAuthRateLimit:PermitLimit` and `McpOAuthRateLimit:WindowSeconds` (60 requests per 60
seconds by default). Dynamic registration accepts only public clients and exact HTTP loopback
callbacks. OAuth is authorization-code flow with mandatory S256 PKCE, exact callbacks and a
matching resource/audience. It does not authenticate a chat ID.
Unused client registrations expire after one hour when they have no live pending approval;
the 10,000-client capacity limit applies to registrations without a connection, so unused
registrations cannot permanently block new clients. Management tool requests are limited to
1 MiB and operation responses to 8 MiB.

## Errors

Every answer that is not 2xx has this body:

```json
{
  "Code": "VALIDATION",
  "Message": "The request is not valid.",
  "Entity": null,
  "Property": null,
  "Errors": [{ "Property": "Name", "Message": "Required" }],
  "TraceId": "0af7651916cd43dd8448eb211c80319c"
}
```

Branch on `Code` and the status, never on `Message`. `Entity` and `Property` name what the error is
about when there is one thing. `Errors` lists the problems of a VALIDATION error, one per property;
an item of a list is named by its place (`Rules[3].Action`). `TraceId` finds the request in the Portal's logs.

| Status | Code | Meaning |
|---|---|---|
| 400 | `VALIDATION` | The request is not valid, or the API server refused it with a message of its own. |
| 401 | `UNAUTHORIZED` | No session, or the session was ended. Also a failed sign-in, and an agent key that is not valid. |
| 403 | `FORBIDDEN` | The write header is missing; or the call is for the owner or an administrator only; or registration is switched off; or an agent lacks a permission or called something agents are always refused. |
| 404 | `NOT_FOUND` | Unknown, or not the caller's. Also an unknown API address or a wrong method. |
| 409 | `CONFLICT` | The state does not allow it: a duplicate name, a system entity, a property that is part of a constraint, mail not set up. |
| 409 | `SETUP_REQUIRED` | First-administrator setup is pending. Complete `/bootstrap` before calling other management or internal endpoints. |
| 415 | `ERROR` | The request body is not sent as `application/json`. |
| 429 | `TOO_MANY_REQUESTS` | The rate limit of the anonymous account calls. |
| 502 | `UPSTREAM_ERROR` | The API server could not be reached or failed. |
| 500 | `ERROR` | Anything else. The detail is in the Portal's logs. 'The change was made on the API server but could not be saved in the Portal' means the API server has the change and the Portal does not: look at the state before you try again. |

## Endpoints

The contract is the reference. In short, under `/api/v1`:

| Group | Paths |
|---|---|
| Session and account | `/session`, `/session/api-token`, `/account`, `/account/password`, `/account/password-reset-requests`, `/account/password-resets`, `/instance` |
| Servers (read) | `/servers` |
| Applications | `/applications`, `/applications/import`, `/applications/{appToken}` with `/connection-info`, `/status`, `/rebuild`, `/cache-reset`, `/audit-log` |
| Entities and properties | `{app}/entities`, `{app}/entities/{entity}` with `/rename`, `/properties`, `/properties/{property}`, `/properties/{property}/rename`, `/constraints`, `/default-order`, `/report-fields` |
| Security | `{app}/security`, `{app}/security/settings`, `{app}/security/rules` |
| Custom endpoints | `{app}/custom-endpoints`, `{app}/custom-endpoints/{id}`, `{app}/custom-endpoints/preview` |
| Reports | `{app}/reports`, `{app}/reports/{reportId}`, `{app}/reports/layout` |
| Email settings | `{app}/email-settings` |
| Sharing and agent rights | `{app}/collaborators`, `{app}/collaborators/available-agents`, `{app}/collaborators/{id}`, `{app}/collaborators/{id}/permissions`, `{app}/permissions` |
| Schema import and comparison | `{app}/schema-import`, `{app}/schema-import/diff`, `{app}/comparison` |
| Clones | `{app}/clones`, `{app}/clones/{operationId}` |
| Administration | `/admin/servers`, `/admin/users`, `/admin/users/{userId}/role`, `/admin/agents`, `/admin/agents/{userId}`, `/admin/applications`, `/admin/applications/{appToken}`, `/admin/settings`, `/admin/backup`, `/admin/audit-log` |

Things an agent should know before it writes:

- Deletes and rebuild have no confirmation step in the API and cannot be undone. Agents need a
  separate Delete grant for entities/properties, custom endpoints or reports, or the rebuild Write
  grant. Deleting an application remains unavailable to agents.
- `PUT {app}/security/rules`, `PUT .../constraints` and `PUT .../default-order` replace the whole
  list: read it first, change it, send all of it. `PUT {app}/reports/layout` moves only the panels
  it lists (reports left out stay where they are), and `PUT {app}/reports/{reportId}` replaces the
  series of that report.
- Every PUT under `{app}` writes all the values it carries: an optional value that is left out or
  null is cleared (a `PUT .../properties/{property}` with only a Description clears the Minimum and
  the ValidationRegex). The exception is a secret (`ConnectionString`, `MailPassword`): null keeps
  the stored one. A property that the request does not know is ignored, so a misspelled name counts
  as left out. GET the resource, change it, send it back whole.
- `POST {app}/schema-import` is not atomic. `GET {app}/schema-import/diff?Source=` answers in the
  shape the import accepts.
- Entity and property names are case-sensitive in addresses.

## What is not in this API

Records, files, record history, statistics, e-mail templates, the storage size and the export of an
application are served by the API server that hosts the application, not by the Portal
(reference: `docs/docs/api_reference.md`). To call it as the signed-in Portal user:

1. `GET /api/v1/session/api-token` gives `{ "Token": "..." }`. It changes at every sign-in.
2. `GET /api/v1/applications/{appToken}` gives `Server.ServerUrl`.
3. Call `{ServerUrl}/api/...` with the headers `Authorization: Bearer {Token}`, `x-application-token: {appToken}` and `x-client-id: portal`. Without the last one the API server reads the Bearer value as an application user's token.

An agent cannot do this: step 1 is refused for an agent key.

## A worked example

With the session cookie, as a person. The same with an agent key is under [Agents](#agents).

```bash
# Sign in. The cookie jar keeps the session.
curl -c cookies.txt -H "Content-Type: application/json" -H "X-Apilane-Portal: 1" \
  -d '{"Email":"your-email@example.com","Password":"your-chosen-password"}' \
  http://localhost:5000/api/v1/session

# The applications of the user. Take the Token of one.
curl -b cookies.txt http://localhost:5000/api/v1/applications

# Create an entity in it.
curl -b cookies.txt -H "Content-Type: application/json" -H "X-Apilane-Portal: 1" \
  -d '{"Name":"Products","Description":"The catalogue"}' \
  http://localhost:5000/api/v1/applications/{appToken}/entities

# Add a property to it.
curl -b cookies.txt -H "Content-Type: application/json" -H "X-Apilane-Portal: 1" \
  -d '{"Name":"Title","Type":"String","Required":true,"Maximum":200}' \
  http://localhost:5000/api/v1/applications/{appToken}/entities/Products/properties
```

## The internal API

Two endpoints that only the API servers call (`PortalInfoService` in `Apilane.Api.Core`). They are
not part of `/api/v1`, not in the contract and not for browsers.

| Call | Answer |
|---|---|
| `GET /api/internal/applications/{appToken}` | The application as it is stored, with its server, entities, properties, custom endpoints and collaborators, secrets included. `null` for an unknown token. |
| `GET /api/internal/applications/{appToken}/access` | `true` or `false`: whether the Portal user whose token is in `Authorization: Bearer {token}` may manage the application (owner, collaborator, or an administrator for any application). |

- The caller proves itself with the header `x-installation-key`, compared in constant time. A missing
  or wrong key is 401 with no body. The key compared is the one stored in the Portal database (Instance >
  Settings), read on every call; the Portal sends the same stored key in `POST /api/ApplicationNew/Generate`
  (create, import, clone).
- The bodies are the stored records with property names as in the models and enums as numbers,
  because that is how the API server reads them. Do not add a naming policy or an enum converter to
  the Portal's global JSON options (`Program.cs`).
- Nothing is versioned here: a change needs the matching change in `PortalInfoService`, and a Portal
  and its API servers must run the same version.
- `tests/Apilane.Portal.Tests/InternalApiTests.cs` pins the Portal's side and
  `tests/Apilane.UnitTests/PortalInfoServiceTests.cs` the API server's side.

---

# Part 3: Working on the UI

Node is needed only to work on this folder. `dotnet build`, `dotnet test` and the Portal itself
never call it, and the Docker image builds the UI in its own stage.

## Prerequisites

Node 22.12 or newer (the Docker image builds with Node 24, see `src/Apilane.Portal/Dockerfile`).

On Windows: `winget install OpenJS.NodeJS.LTS`. If PowerShell refuses to run `npm` ("running
scripts is disabled on this system"), run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
once, or call `npm.cmd` instead.

## Commands

Run them in this folder. `npm ci` once after cloning (and after pulling dependency changes).

| Command | What it does |
|---|---|
| `npm run dev` | Dev server with hot reload at <http://localhost:5173/>. |
| `npm run build` | Type-checks, then builds into `../Apilane.Portal/wwwroot/ui`. |
| `npm run api:types` | Regenerates `src/lib/api-types.ts` from `openapi/portal-v1.json`. |
| `npm test` | Runs the unit tests (`src/**/*.test.ts`) once. |

## Running it locally

1. Start the Portal: in Visual Studio (project `Apilane.Portal`, F5 for the debugger), or
   `dotnet run --project src/Apilane.Portal` from the repository root. It listens on the `Url`
   setting, <http://localhost:5000> by default.
2. Most screens also need an API server: `dotnet run --project src/Apilane.Api`
   (<http://localhost:5001>), with the same installation key as the Portal: on a fresh database its
   `InstallationKey` setting, otherwise the key under Instance > Settings.
3. `npm run dev` here, and browse <http://localhost:5173/>. On a fresh database, complete the setup
   screen using the temporary password from the Portal startup output, then choose your administrator email and a new password.

Local settings go into `src/Apilane.Portal/appsettings.Development.json` (git-ignored). Set at least
`FilesPath` there: it is the folder of the Portal's database, and the value in `appsettings.json` is
only a sample.

The dev server answers every address of the UI itself and forwards only the Portal's own addresses
(`/api`, `/swagger`, `/health`, `/metrics`) to the Portal. The browser talks to one site, so the
login cookie and the API work as in production. One difference: the dev server matches those four
prefixes in lower case only. If the Portal runs on another address, set `PORTAL_URL` when starting
the dev server:

```powershell
$env:PORTAL_URL='http://localhost:5010'; npm run dev
```

```bash
PORTAL_URL=http://localhost:5010 npm run dev
```

Debugging: use the browser's developer tools; breakpoints land in the `.vue` and `.ts` source
files. The [Vue Devtools](https://devtools.vuejs.org/) browser extension shows components and
their state. In VS Code install the recommended "Vue - Official" extension for type-checking in
templates. Breakpoints in the API are set in Visual Studio as in any ASP.NET project.

`npm run dev` does not type-check. To check types, and to see the built UI exactly as the Portal
serves it, run `npm run build` and browse <http://localhost:5000/>. The Portal picks up a new build
without a restart. Before the first build it answers UI addresses with a 404 text that says what to run.

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

Commit `openapi/portal-v1.json` and `src/lib/api-types.ts` together. The XML summaries of the
actions and of the classes in `Api/V1/Contracts` are the texts of the contract, so rewording one
changes the contract too.

## Adding a screen

1. Add the API endpoints in `src/Apilane.Portal/Api/V1` (request and response classes in `Api/V1/Contracts`, the service and its interface in `Services` and `Abstractions`) and their tests in `tests/Apilane.Portal.Tests`.
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
   - The first segment of a route must not be `api`, `swagger`, `health` or `metrics`, and no route
     may end in a segment with a dot: the Portal answers those addresses itself (`src/router.test.ts` checks the first).
5. Add its navigation entry in `src/layouts/SidebarNav.vue` when it has one. For a section of an
   application, the entry is its line in `sections` of `src/layouts/AppLayout.vue`.
6. Add its line to the [Screens](#screens) table above.
7. Write its Help in `src/components/help/` and put it in the `PageHeader` (see Conventions).

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

`src/main.ts` loads the session, then starts the app. `src/router.ts` holds every route (each page
is loaded lazily), the guard that decides which screens open without a session, and `afterSignIn`.

### Pages (`src/pages`)

One component per screen. A part only that screen uses sits next to it (`pages/apps/ApplicationCard.vue`).

**`pages/account`**: the public screens: administrator setup, sign in, sign up, forgot password, reset password, e-mail confirmed.

**`pages/apps`**

| File | Contents |
|---|---|
| `ApplicationsPage` | `/apps`, also the home page: the user's applications as cards grouped by server. A card's menu opens the status, rebuild and delete dialogs, which the page holds. |
| `CreateApplicationPage`, `ImportApplicationPage` | `/apps/new` and `/apps/import`: the two pages that make an application. Each has a Help; the questions that used to sit under the form are in `CreateApplicationHelp`. |
| `CompareApplicationsDialog` | `/apps?compare=<application token>&with=<application token>`, opened by 'Compare with' of a card's menu: the other application is picked in the dialog, then Entities, Properties, Constraints, Custom endpoints and Security each list what was added, removed and changed, or 'Applications are identical'. `ComparedProperty` is its part. |

**`pages/application`**: the screens of one application, inside `AppLayout`.

| File | Contents |
|---|---|
| `EntitiesPage` | `/apps/:appToken/entities`: the custom and system entities with the dialogs to create, edit, rename and delete one. `EntityTable` and `EntityRow` are its parts (a row loads its own record counts from the API server). |
| `PropertiesPage` | `/apps/:appToken/entities/:entity`, inside `EntityLayout`: the custom and system properties of one entity with the dialogs to create, edit, rename and delete one. `PropertyTable` is its part. |
| `ConstraintsPage` | `.../entities/:entity/constraints`: the unique and foreign key constraints of one entity, added (`AddConstraintDialog`, a two-step dialog: kind, then its fields) and removed in the list and saved together; read-only for a system entity unless the user is an administrator. |
| `SortingPage` | `.../entities/:entity/sorting`: the default sorting of one entity as an ordered list. |
| `DataPage` | `/apps/:appToken/data/:entity?page=&pageSize=&sort=`: the data browser of the application: the entities with properties loaded once, then `ApplicationDataBrowser`. |
| `SecurityPage` | `/apps/:appToken/security`, with a Help for the screen (`SecurityHelp`) and one for the rules (`AccessRulesHelp`, in the header of `SecurityRulesEditor`): `SecuritySettingsForm` (sign-in, register, files, IP access with its own Save, 'Not saved yet' next to it and its own question before leaving; the forgot-password links read-only) and `SecurityRulesEditor` (the rules of every item edited locally and saved together: `SecurityItemPicker` lists the items, a searchable list or a select on a phone, the selected one in `?item=Entity-<name>`, `CustomEndpoint-<name>` or `Schema-Schema`; a role x action grid of `SecurityRuleCell`; `SecurityTreeDialog` and `SecurityMatrixDialog`, the saved rules read-only at `?view=tree` and `?view=matrix`, with `SecurityAccessBadge`). |
| `CustomEndpointsPage` | `/apps/:appToken/endpoints`: the custom endpoints, one line each with the SQL cut to one line, the 'Call endpoint' link (opens `CallUrl` in a new tab), Edit and the delete confirm. `CustomEndpointSearchDialog` finds any text of the names, descriptions and whole SQL and marks it, with Edit opening the editor in a new tab. |
| `CustomEndpointEditorPage` | `/apps/:appToken/endpoints/new` and `/apps/:appToken/endpoints/<ID>`: name, description and the SQL in `SqlEditor`, saved with `UnsavedChangesBar`, the rename warning and its Help (`CustomEndpointEditorHelp`, which shows the SQL Server item only for a SQL Server application). `CustomEndpointTestPanel` is its right-hand part (the address and a box per parameter, worked out in the browser, and Test, which runs the SQL on the API server and shows the JSON or the database's error). |
| `EmailPage` | `/apps/:appToken/email`: the SMTP settings and the confirmation landing page, one form saved to the Portal, and the e-mail templates read from and saved to the API server. `EmailTemplateDialog` edits one template (enabled, subject, HTML body with a live `HtmlPreview`, the placeholders). |
| `ReportsPage`, `Report*.vue`, `TimeRangePicker` | `/apps/:appToken/reports`: the dashboard. On a wide screen (768px and up) the reports are panels on a 12-column grid (`ReportsGrid`, gridstack), dragged by their title and resized by their edges; the layout is saved 600 ms after the last change (`PUT reports/layout`, every panel), and a save that fails shows above the grid with 'Reload the dashboard'. On a phone the panels are one column in the same order and nothing moves. The page also holds the delete confirm, `ReportEndpointDialog` ('View API endpoint': the address of each series' call, with a copy button) and the editor. `ReportPanel`: one report: title, the time range or Top N badge, Refresh (which moves the time window to now), the menu, and the body: `ReportTable` for a Grid, `ReportChart` (Chart.js) for the other types. It loads each series itself from the API server (`/api/Stats/Aggregate`); a series that cannot run or whose call fails is named inside the panel and the others still show. `ReportEditorSheet` (`/apps/:appToken/reports/new` and `/apps/:appToken/reports/<ID>/edit`, a side sheet over the dashboard; the three routes share `ReportsPage`): title, visualization, time range (`TimeRangePicker`: the quick ranges or a custom number and unit), Top N and the series. `ReportSeriesEditor` is one series (label, entity, group-by, property, filter), with its choices from `GET entities/{entity}/report-fields?Type=` and the filter edited in a dialog with `FilterBuilder`. |
| `SharingPage`, `AgentPermissionsEditor` | `/apps/:appToken/sharing`, owner only: collaborators and permission summaries, a free-text email sharing dialog for people, a separate agent picker with other visible applications, per-area rights when adding or editing an agent, and the remove confirm. People keep full collaborator access. |
| `SchemaImportPage` | `/apps/:appToken/import`, the 'Import' tab (route `app-schema-import`): adds entities, properties, constraints, security rules and custom endpoints from a JSON payload. 'Load diff' asks the API what another application has that this one lacks, puts it into the payload box and shows its counts; the box stays editable. Import checks the text in the browser (`parsePayload`), asks first (not atomic, cannot be undone), then shows 'Imported' with the skipped items, or the failed step with its place in the payload. Its Help (`SchemaImportHelp`) holds how the import works, the payload reference and the example payload (`examplePayload`). |
| `AuditLogPage` | `/apps/:appToken/audit-log?page=1`: the application's audit log, with `AuditLogTable` and `AppPagination`. |
| `SettingsPage` | `/apps/:appToken/settings`: General (name, connection string; server and database type read-only), Status (online / offline) and Danger zone (rebuild, delete). |
| `CloneApplicationPage`, `CloneProgressPage` | Opened from the card menu of the applications page (no tab in `AppLayout`). `/apps/:appToken/clone`: the server (it starts on the application's own), `DatabaseTypeFields`, 'Clone data' and, when it is on, the entities whose records are copied (all ticked, 'Select all' / 'Deselect all', Files left out; an empty selection is refused). Starting answers at once and leads to `/apps/:appToken/clone/:operationId`: the phase, the bar (`components/ui/progress`), the counters of the phase, the entity being worked on and the time remaining, asked every 2 seconds with `usePolling`; then 'Clone completed' with the link to the new application, or 'Clone failed' with the message and 'Back to clone'; an operation the Portal no longer knows (404) shows 'This clone can no longer be tracked'. |

**`pages/admin`**: the screens of the 'Instance' group, for administrators (`meta.requiresAdmin`).

| File | Contents |
|---|---|
| `ServersPage`, `UsersPage`, `AuditLogPage` | `/admin/servers`, `/admin/users`, `/admin/audit-log`. |
| `AgentsPage` | `/admin/agents`: agents only, shown by name, with creation, the key shown once, and deletion confirmed by typing the agent's name. UsersPage lists people and manages their roles. Both pages filter the existing `GET /admin/users` response. |
| `SettingsPage` | `/admin/settings`, with the 'Backup database' card. |
| `ApplicationsPage` | `/admin/applications`: every application of the instance in creation order, with a search by name, token or owner e-mail. The table shows name and token, owner, server, database and status; a Details button opens the other settings under the row (a secret only as 'Set' / 'Not set'); the row menu has 'Open data browser', 'Open application' (only for an application the administrator owns or collaborates on) and 'Clear cache'. |
| `ApplicationDataPage` | `/admin/applications/:appToken/data/:entity?`: the data browser of any application, loaded from `GET /api/v1/admin/applications/{appToken}` and drawn by `ApplicationDataBrowser`, under the breadcrumb Instance / Applications / name / Data. |

### Layouts (`src/layouts`)

| File | Contents |
|---|---|
| `AppShell` | The shell around every screen for a signed-in user: sidebar, phone drawer, user menu, the change-password dialog. |
| `SidebarNav` | The navigation inside it. A link of the 'Instance' group stays lit on every address below its own (`/admin/applications/<token>/data`). |
| `AppSwitcher` | The menu of the user's applications under 'Applications' in the sidebar; it marks the application being worked on. |
| `UserMenu` | Change password, 'API reference' (`/swagger`), sign out. |
| `AppLayout` | The frame around every screen of one application: loads it, breadcrumb, section tabs, Info and API buttons, the 'not found' state, and the Forbidden state for a `meta.requiresOwner` screen opened by a collaborator. |
| `EntityLayout` | The frame around every screen of one entity, inside `AppLayout`: loads it, back link, name, description, tabs, the 'not found' state. |
| `AuthLayout` | The centred card around the public screens, with the instance name. |

### Components (`src/components`)

| File | Contents |
|---|---|
| `PageHeader`, `StateMessage`, `LoadingState`, `ErrorState` | The heading and the loading, empty and error states of a screen. |
| `HelpSheet`, `HelpItem` | The Help of a screen: a button for the header (`PageHeader` actions) that opens a side sheet with a lead, which says what the screen is for, and a list of `HelpItem` accordions. `size="sm"` for a smaller header than a screen's. |
| `help/` | The words of each screen, one component per screen (`EntitiesHelp`, `SecurityHelp`, `AccessRulesHelp`, ...), each wrapping `HelpSheet`. A page only places it: `<EntitiesHelp />`. |
| `ForbiddenState` | A screen the user may not open; `description` says who may. |
| `FormDialog`, `FormField`, `SwitchField` | A form in a dialog (`wide` for a large field with something next to it); a labelled field with its error message; the same for an on/off choice. |
| `AuthForm` | A form that is a whole public screen, the counterpart of `FormDialog` inside `AuthLayout`. |
| `ConfirmDialog` | `destructive` for a red button; `opener-removed` when a success removes the button that opened it, such as the row of a deleted record. |
| `ConfirmByNameDialog` | Type the name to confirm. `acknowledge` adds an 'I understand' box that must be ticked too, and its slot takes more about the consequences. |
| `SecretInput` | A password or key the API never sends back. |
| `CopyField` | A read-only value with a copy button, masked when it is a secret. |
| `AppPagination` | Page links bound to `?page=`. |
| `AuditLogTable`, `AuditLogDetail` | An audit log page with expandable rows, for the instance and the application audit log. |
| `ChangePasswordDialog` | The dialog of the user menu and of `/account/password`. |
| `ServerStatusDot` | Whether an API server answers. |
| `ApplicationInfoDialog` | Server address, token and encryption key of one application. |
| `ApplicationSelect` | Picks one of the user's applications, grouped by server: for a screen that needs a second application (the source of a schema import, the other side of a comparison). |
| `DatabaseTypeFields` | Database type, connection string and the notes per database type, for every form that puts an application on a database. `existing` for the edit form: the type disabled, the connection string a `SecretInput`. |
| `ApplicationStatusDialog`, `RebuildApplicationDialog`, `DeleteApplicationDialog`, `ConsequenceList` | Ask, run the write and show the toast, for the settings screen and the cards of the applications page alike; the bulleted consequences of a rebuild or delete. |
| `ServerSelect`, `NoServersState` | The API server of a new application; what is shown instead of such a form when the instance has no server. |
| `FileInput` | A file picker for one file. |
| `InlineAsync` | A value one row or card loads on its own: skeleton, short error with retry, then the value. |
| `ConstraintBadge` | A unique or foreign key constraint of an entity. |
| `UnsavedChangesBar` | Save / Discard at the bottom of a screen that collects several edits before one Save, with the leave guard. |
| `HtmlPreview` | HTML someone typed (an e-mail body), drawn in a sandboxed frame where no script runs. |
| `FullScreenDialog` | A read-only view that needs the whole window, such as the security tree and matrix. |
| `TextListInput` | A list of short values, one box each, with Add, Remove and an error per entry from `List[2]` messages; pasting a comma-separated list splits it. |
| `SqlEditor` | SQL in CodeMirror 6 with the application's entity and property names offered while typing. CodeMirror is downloaded the first time an editor is shown, and a plain text box takes over if it cannot be. |
| `FilterBuilder` | A filter of the data API as a list of conditions (property, operator, value) that must all hold, for every place a stored filter is edited. It edits `FilterRow`s; `src/lib/filterBuilder.ts` reads and writes the stored text. |
| `data/` | The data browser, for every screen that shows the records of an entity (the application's, and the administrator's of any application). `ApplicationDataBrowser` is what such a screen mounts once it has the entities: it takes `application`, `entities` (with their `Properties`) and `to` (the route of one entity on that screen, whose path ends in `:entity?`), shows `EntitySwitcher` and the `EntityDataBrowser` of the entity in the address, and without an entity opens the first custom one; the screen keeps its own loading, error and 'no entities' states. `foreign` says the application is someone else's (an administrator in an application they neither own nor collaborate on): links to its screens under `/apps` are left out. `EntityDataBrowser` takes `application` (token, API server, largest file: `DataApplication` of `lib/records.ts`) and `entity` (with its `Properties`) and is given `:key` of the entity name: toolbar (Refresh, New record / Register user / Upload, Delete selected, Export page as CSV, Clear history), the grid (a sortable header with an info tooltip per property, a filter box per column, cells cut to one line that open on a click, row actions by the entity's flags), the paging bar; page, size and sort live in the address (`?page=`, `?pageSize=`, `?sort=Name` or `-Name`), the filters do not. Its parts: `RecordFilterInput` (the filter box of one column, by type), `RecordPaging` (size, first / previous / page x of y / next / last, total), `RecordFormSheet` (create and edit in a side sheet, one box per property a caller may set; Users registers), `RecordHistorySheet` (the newest 100 history entries of a record, changed values marked, 'Clear history'), `FileUploadDialog` (one file, size checked before it is sent). `EntitySwitcher`: the entities as links (a select on a phone), each a route given by `to`. |
| `diff/` | What differs between two sides, for every screen that shows a comparison. `DiffSection`: one section (a title with its counts, then the Added, Removed and Changed lists; the `item` slot draws an added or removed thing, the `changed` slot one that differs; nothing is drawn when all three are empty). `FieldChange`: one value before and after (`code` for SQL: two blocks that wrap). `DiffCount`: a count as a badge ('2 added'), also used alone for a summary line. |
| `ui` (a folder) | shadcn-vue components. They are source files owned by this project: add one with `npx shadcn-vue@2.8.2 add <name>` and edit it freely. |

After `shadcn-vue add`, check `git diff`: the CLI can add packages to `package.json` (note them
under Dependencies) and tokens to `src/style.css`. When moving to a newer CLI version, refresh
`src/styles/shadcn.css` too.

### Composables (`src/composables`)

| File | Contents |
|---|---|
| `useAsync`, `useMutation` | Every read gets the same loading / error / data states; `useMutation` is the counterpart for writes. |
| `usePageQuery` | Reads the page number of a paged list from `?page=`. |
| `useInstance` | What the Portal tells a visitor, for the public screens: the instance name and whether registration is open. |
| `useApplications` | The one shared list of the user's applications, read by the applications page and the sidebar switcher. |
| `useApplication`, `useEntity` | Give a screen under `AppLayout` (or `EntityLayout`) the application (or entity) it belongs to, already loaded, and a `reload()`. |
| `useServerHealth` | Asks an API server whether it is up, every 5 seconds. |
| `useServerChoice` | Loads the servers an application can be put on and holds the one picked (the only one is picked automatically). |
| `useLeaveGuard` | Asks before leaving a screen with unsaved changes (`UnsavedChangesBar` already uses it). |
| `usePolling` | `usePolling(load, { intervalMs, done, giveUp })`: the state of something that runs on the server, asked at once and again after every answer until `done` says the answer is final. A failed attempt keeps the last answer and is tried again; `giveUp` names the failures that end it (a 404). It pauses while the tab is hidden and stops when the screen is left. For the screen that follows a long-running operation; a value that is simply refreshed now and then (`useServerHealth`) does not need it. Its test (`usePolling.test.ts`) runs the mount and unmount hooks by hand and moves a fake clock, since there is no component to mount. |

### Modules (`src/lib`)

Rules that need neither Vue nor the browser, as pure functions, each with a `*.test.ts` next to it.

| File | Contents |
|---|---|
| `api.ts` | The typed API client, `unwrap`, `unwrapAnonymous` (for calls made without a session), `ApiError`, and `loginUrl` / `redirectToLogin`. It adds the write header and shows the `Warning` header of any answer as a warning toast. `api-types.ts` is generated. |
| `apiServer.ts` | `apiServer(serverUrl, appToken)`: the client for calls that go straight from the browser to an API server (storage used, export, record counts, e-mail templates, the SQL test of a custom endpoint, records, files, history and the series of a report). `get` and `getBlob` read (`get` takes `{ signal }` of an `AbortController`, for a read a newer one replaces; `isAbort(error)` tells that failure apart); `put` and `post` send a JSON body; `delete` deletes; `postFile(path, field, file)` uploads one file as multipart/form-data. A query value that is `undefined` is left out. It fetches the user's API token from the Portal, keeps it in memory only, and throws the same `ApiError` as `api` (with the `Property` the API server names, so `formErrors` marks that field). |
| `agents.ts` | The rules of agents on the Users, Agents and Sharing screens: `isAgent` (an address at `@agent.local`), `accountDisplayName` (display-only name, original identity retained), and `isAgentName` with `agentNameRule` (a copy of the name rule of `POST /admin/agents`). |
| `agentPermissions.ts` | Read-only defaults, copies of saved policies, access-level transitions and collaborator permission summaries. Resource capabilities come from the API catalogue. |
| `session.ts` | The signed-in user: loaded before the app starts, `useSession()` inside `AppShell`, `setSession` after signing in. |
| `bootstrap.ts` | Loads whether administrator setup is required before the session or first route; the router keeps every screen on setup until completion. |
| `returnUrl.ts` | `safeReturnUrl`: the check that a `returnUrl` is a path on this site before it is followed after signing in. `isServerPath`: whether the Portal answers an address itself (`/swagger`), so it needs a full page load. |
| `forms.ts` | What a form shows for a failed write: `formErrors` (a message per field and one above the form) and `listErrors` (a message per item of a list sent as a whole, `Constraints[1]...`, and one above the list). |
| `formData.ts` | `toFormData`: the `bodySerializer` of an `api` call that uploads a file (multipart/form-data). |
| `download.ts` | `saveBlob`: saves a fetched file under a file name. |
| `toast.ts` | Toast helpers that report the result of a write: `success`, `error`, `warning`. |
| `format.ts` | Date formats: `formatDateTime` (local time) and `formatUtc` (`yyyy-MM-dd HH:mm:ss` in UTC, for logs). |
| `applications.ts` | How the applications list is grouped, ordered, filtered and labelled (`groupByServer`, `filterByName`, `formatStorage`, `databaseTypeLabel`), plus the database types a form offers (`databaseTypes`, `needsConnectionString`). |
| `applicationActions.ts` | The texts and rules shared by the application settings screen and the card dialogs: `statusChange` (what the status button offers and says), `offlineWarning`, `rebuildConsequences`, `deleteConsequences`, `understandText`, and `connectionStringToSend` (the edit request's ConnectionString: an empty box is null, which keeps the stored value). |
| `adminApplications.ts` | The rules of the administrator's applications list: `searchApplications` (name, token or owner e-mail), `countText` ('3 of 12 applications') and `applicationDetails` (the settings a row opens, a secret as `stored` only). |
| `entities.ts` | How entities are filtered and described: `filterEntities`, the texts of a constraint (`constraintTypeText`, `constraintLabel`, `constraintDescription`, `onDeleteText`), `formatCount`, and the order of the data browser's entity list (`dataEntityOrder`). |
| `properties.ts` | How properties are described and which fields a property form shows: `propertyRules` (the rules of a property in plain words), `typeFields` (the fields a type uses), `limitNoun`, `decimalPlacesText`, `maxLengthNote` (the string limits of MySQL and SQL Server), `numberOrNull` (a number box as the API takes it), `propertyTypes`. |
| `constraints.ts` | The editing rules of the constraints screen: `constraintRows` (saved, added and removed constraints and their place in the request), `addConstraint`, `constraintRequests` (the body of the save), `isDuplicate`, `changesText`, `onDeleteHelp`. |
| `defaultOrder.ts` | The editing rules of the default sorting screen: `moveItem`, `toggleDirection`, `remainingCandidates`, `sameOrder`, `directionText`. |
| `records.ts` | The rules of the data browser: the shapes of the API server's answers (`RecordPage`, `HistoryEntry`), `recordActions` (what the entity's flags allow), paging (`pageSizes`, `pageSizeFrom`, `pageCount`), sorting (`sortFrom`, `sortQuery`, `nextSort`, `sortParam`), filtering (`filterOperators`, `emptyFilters`, `hasFilters`, `filterParam`), values (`formatRecordDate`, `cellText`: system UTC dates in local time, other dates in UTC, `yyyy-MM-dd HH:mm:ss.SSS`), the record form (`formProperties`, `initialForm`, `createBody`, `updateBody`: only the changed boxes, `recordErrors`: an API server Property may name several, comma-separated), `fileTooLarge`, `historyRows`. |
| `csv.ts` | `toCsv` (every field quoted, quotes doubled, CRLF) and `csvFileName`, for a CSV export. |
| `security.ts` | The editing rules of the security screen: `toEditable`, `newRule` (a switched-on cell), `restoreRule` (a cell switched off and on again gets its properties and rate limit back), `setRule`, `rulesRequest` (the body of the save), `changedItems` (which items hold unsaved changes), `changesText`. |
| `securityAccess.ts` | What a security rule grants, worked out as the API server enforces it: the items' actions and properties (`itemActions`, `offeredProperties`), `ownedApplies`, `inheritance` (the editor's 'Inherits from' notes), `propertyAccess` (an empty list is 'no properties', never full access), `endpointRateLimit`, `effectiveAccess` (Anonymous and Authenticated inherited by every role), `describeAccess`, `rateLimitShort` / `rateLimitText`. The editor, the tree and the matrix all read access through it. |
| `customEndpoints.ts` | The rules of the custom endpoints screens: `endpointParameters` and `endpointAddress` (a copy of `DBWS_CustomEndpoint.GetParameters` and `GetUrl`, so the editor follows every key press without a request), `testParameters` and `testQueryBody` (the SQL test call), `searchEndpoints` and `highlightMatches` (the search dialog), `renameLosesRules`. |
| `sqlEditor.ts` | `createSqlEditor`: CodeMirror 6 set up for SQL (dialect of the database type, completion, colours from the design tokens). Only `SqlEditor.vue` loads it, through a dynamic import, so it is a chunk of its own. |
| `emailTemplates.ts` | The e-mail templates as the API server sends them (`EmailTemplate`, `EmailTemplateUpdate`), `visibleTemplates`, `templatePlaceholders` (a copy of the placeholders in `EmailEvent.cs`) and `templateProblems` (the empty fields checked before a save). |
| `reports.ts` | The rules of the report editor: the choices (`reportTypes`, `quickTimeRanges`, `timeUnits`), the form (`newReportForm`, `reportForm`, `newSeries`), the body of the save (`reportRequest`: empty series rows are left out), where the problems of a failed save belong (`reportErrors`: `Series[1].GroupBy` back to the row of the form) and the group-by picker (`fieldNames`, `splitGroupBy`, `toggleGroupBy`, `notOffered`). |
| `reportData.ts` | Everything a report panel works out: the time range (`parseTimeRange`, `timeRangeLabel`, `timeWindow`), the call of a series (`seriesFilter`, `seriesQuery`, `aggregateUrl`), badges and legend labels (`panelScope`), the shared x-axis (`combineSeries`, `rowValue`, `hasNoRows`), the cells of a Grid (`gridText`) and the Chart.js configuration (`chartConfig`, `toNumberOrNull`, `withAlpha`). The answer of `Stats/Aggregate` is described there (`AggregateRow`). |
| `reportChart.ts` | `createReportChart` and `reportPalette`: Chart.js with only the chart kinds a report has, and the texts, lines and tooltips in the colours of the design tokens. Only `ReportChart.vue` loads it, through a dynamic import, so it is a chunk of its own. |
| `filterBuilder.ts` | The rules of `FilterBuilder`: every operator of the data API (`allFilterOperators`), `parseFilter` (a stored filter as conditions, or as `raw` text when it has groups, OR or anything else a flat list cannot show), `filterText` (the JSON to store), `filterSummary`, `newFilterRow`. |
| `schemaImport.ts` | The rules of the schema import screen: `parsePayload` (the text of the payload box: JSON, an object, its three lists are lists), `payloadSummary` (the counts shown after 'Load diff'), `importFailure` (the message, the places in the payload and the trace of a failed import), `examplePayload`. |
| `comparison.ts` | How the answer of `GET /applications/{appToken}/comparison` is laid out for the compare dialog: `comparisonView` (the five sections; the properties and constraints of a changed entity move to their own sections, named 'Entity.Property'), `isIdentical`, `ruleText`, `propertyFacts`, `constraintTypeName`. |
| `clone.ts` | The rules of the clone screens: `cloneableEntities`, `entitiesToSend`, `isFinished`, `phaseText`, `clonePercent`, `cloneCounters`, `currentEntities`, `formatEta` / `etaText`, `failureText`. |
| `auditDiff.ts` | The display rules of an audit log entry's changes. `AuditLogDetail` draws the result. |

### Styles

| File | Contents |
|---|---|
| `src/style.css` | Design tokens (colours, radius). Dark is the only theme; the one light colour is `preview` (`bg-preview`), the white page behind an e-mail preview. Also the colours of chart series (`--chart-1` to `--chart-10`) and the drop target of the reports grid. |
| `src/styles/shadcn.css` | Utilities the shadcn-vue components need, copied from shadcn-vue 2.8.2. |

## Conventions

- `<script setup lang="ts">` in every component; no Options API.
- Reads go through `useAsync(() => unwrap(api.GET(...)))`, and every screen shows its loading, error and empty states.
- Every route needs a session unless it carries `meta.public`. The guard in `src/router.ts` is the one place that decides; a 401 from any call made with `unwrap` leads to the login page (`/account/login?returnUrl=...`) with a full page load, and signing in returns to that address.
- A `returnUrl` is followed only through `afterSignIn` (`src/router.ts`), which accepts paths on this site and nothing else: an address the Portal answers itself (`/swagger`) is a full page load, every other path opens in this app.
- Signing out is a `DELETE /api/v1/session` followed by a full page load of the login page, so nothing of the session stays in memory.
- A password box is an `Input` with `type="password"` and the right `autocomplete` (`current-password`, `new-password`; the e-mail box of the same form has `username`), so password managers work. `SecretInput` is for stored secrets, not for the user's own password.
- `api` or `apiServer`? Everything the Portal knows (applications, servers, users, settings, the encryption key) is a call to the Portal: `unwrap(api.GET('/api/v1/...'))`, typed from the contract. What lives on an API server (storage used, export, record counts, records, files, history, the statistics of a report) is called straight from the browser: `apiServer(app.Server.ServerUrl, app.Token).get(...)` or `.getBlob(...)`, and for a write `.put(path, body)` or `.post(path, body)` inside `useMutation`. Those calls are not in the contract, so say what they return (`get<number>(...)`) and describe their bodies in a module under `src/lib/` (`emailTemplates.ts`). Never build such a request by hand: `apiServer` adds the token and the headers, and the token must not end up in an address or in storage.
- HTML that is not this UI's own (an e-mail template) is shown only through `HtmlPreview`, never with `v-html`: its frame is sandboxed with every permission off, so a script in it cannot run.
- A failed call to an API server inside a list (one card, one row) shows a short inline error with a retry there; it never replaces the screen. `InlineAsync` draws that.
- A file fetched with headers is saved with `saveBlob` (`src/lib/download.ts`). A file of an API server is downloaded that way too, never through a link with the token in its address.
- A read that follows typing (the filters of the data browser) waits until typing pauses and cancels the read still running (`AbortController`, the `signal` of `apiServer(...).get`), so an old answer never replaces a newer one.
- SQL is edited in `SqlEditor`, never in a plain text box, and CodeMirror is imported nowhere else: that keeps it out of every other screen's download.
- A chart is drawn by `ReportChart`, and Chart.js is imported only by `src/lib/reportChart.ts`; gridstack is imported only by `ReportsGrid.vue`, which the reports page loads on a wide screen. Both are loaded with a dynamic import and have a fallback when the download fails (a table instead of the chart, one column instead of the grid).
- A canvas cannot use the Tailwind classes: `src/lib/reportChart.ts` reads the design tokens with `getComputedStyle`. The colours of the series are the tokens `--chart-1` to `--chart-10` in `src/style.css`.
- Data of an application (a record value, a label or value of a report) is written with `{{ }}`, never with `v-html`.
- A component that hands its elements to a library that moves them (the gridstack grid) sets what the library reads (the `gs-*` attributes) itself, once, when it is mounted, not in the template: a later render would write an old place over the one the library keeps. Its parent gives it a `key` that changes when the set of items does, so a new grid is built instead of the old one being patched.
- Dragging is for the reports dashboard only, and only on a wide screen; the layout is saved on its own a moment after the last change, one save at a time. Everything a panel offers is also in its menu, so nothing needs a drag.
- An editor that is a side sheet with an address of its own (`reports/new`, `reports/<ID>/edit`) is declared in `src/router.ts` as routes that show the same page as the list; the page opens the sheet from `route.name`, and closing it goes back to the list's route.
- A dialog that can be linked to keeps its state in the query (`/apps?info=<application token>`); closing it removes the parameter.
- A dialog that works on two things keeps both in the query (`/apps?compare=<application token>&with=<application token>`): the first opens it, the second is picked inside it, and closing it removes both. A value that is not one of the choices counts as not picked.
- A comparison is drawn with the components of `src/components/diff/`, and its layout is worked out in a module under `src/lib/` (`comparison.ts`). Added is green, removed red, changed amber, each also named in words.
- A write that is not atomic says so before it runs (`ConfirmDialog`) and shows its outcome on the screen, not in a toast: what was skipped, or the step that failed and that the earlier steps stay applied (the schema import).
- The applications list is loaded once, by `useApplications`. Call its `reload()` after a change that adds, removes or renames an application, so the page and the sidebar switcher both follow.
- A screen of one application reads it with `useApplication()` and never loads it again. Its API paths take `application.value.Token`.
- Writes go through `useMutation`. The API rejects a POST, PUT or DELETE without the header `X-Apilane-Portal: 1`; the client in `src/lib/api.ts` adds it automatically, so screens never set it. Anything else that calls the API with the cookie (a person with `curl`) has to send it; a call with an agent key does not.
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
- Logic that does not need Vue or the browser (ordering, formatting, parsing) goes into a module under `src/lib/` as pure functions, with a `*.test.ts` next to it.
- Every screen explains itself in a Help (`components/help/<Screen>Help.vue`), placed first in the actions of its `PageHeader` so that the primary button stays last. The lead says what the screen is for, the first item (`What you can do here`, open) lists what it offers, and the other items answer what a person asks next. Write it in plain words, check every statement against the code, and put what must be seen without opening anything (a warning before something irreversible, a note that is the point of the field) on the screen itself, not in the Help.
- Visible texts say 'email'; comments, this document and the API's summaries write 'e-mail'.
- Validation messages are short and the same everywhere ('Required', 'Not a valid email address', 'Must be 8 to 100 characters'), one per field.

## Tests

| Where | What it covers | Run |
|---|---|---|
| `src/**/*.test.ts` (this folder) | The pure modules under `src/lib`, `usePolling`, and the route table (`src/router.test.ts`: which address opens which screen, that no route sits under a server prefix, and `afterSignIn`). Sharing and agent-rights component tests mount the real page, dialogs and controls using Vue Test Utils and jsdom, with mocked API responses; they cover permission choices, saving/cancelling, failures, and delayed catalogue loading. Other tests keep Vitest's default Node environment. `npm run build` type-checks the tests but does not bundle them. | `npm test` |
| `tests/Apilane.Portal.Tests` | The management API, endpoint by endpoint (`*ApiTests.cs`); the rules every endpoint must keep and the contract file (`ApiContractTests`); the error body, the write header and the cache headers (`ApiPipelineTests`); how the UI is served at the site root next to the Portal's own addresses (`UiServingTests`); the internal API (`InternalApiTests`). xUnit: the real Portal in memory (`Infrastructure/PortalFactory.cs`) on a throw-away SQLite database, with a scripted stand-in for the API servers (`FakeApiServer`). No Docker, no Node. | `dotnet test tests/Apilane.Portal.Tests` |
| `tests/Apilane.UnitTests` | MSTest unit tests of the shared code, among them `PortalInfoServiceTests`: the addresses and headers the API server sends to `/api/internal`. | `dotnet test tests/Apilane.UnitTests` |
| `tests/Apilane.Api.Component.Tests` | The API server against SQLite, SQL Server, MySQL and PostgreSQL. It fakes the Portal, so it does not exercise this API. Needs Docker. | `dotnet test tests/Apilane.Api.Component.Tests` |

Run just the Sharing and permission-control interactions with
`npm test -- SharingPage.test.ts AgentPermissionsEditor.test.ts`. These exercise the actual dialogs,
keyboard-operated select menus, and deletion checkboxes. jsdom does not render layout, so responsive
appearance and full-stack browser behavior still need a browser smoke test.

`ApiContractTests` fails when the API no longer matches `openapi/portal-v1.json`: follow
[When the API changes](#when-the-api-changes). An endpoint that works without a session must be on
its allow-list.

### Release check

Nothing automated runs the UI in a browser. Before a release:

- `dotnet build Apilane.sln`, `dotnet test tests/Apilane.Portal.Tests`, `npm test`, `npm run build`.
- With a real API server: its `/health/readiness` is healthy (it calls the Portal's
  `/health/liveness`); create an application (the API server reads it from `/api/internal`); open its
  data browser (this exercises `/api/internal/applications/{appToken}/access`); register an
  application user and follow the confirmation mail to `/account/email-confirmed`.
- `/swagger` signed out: the sign-in screen, then back on `/swagger`.
- A password-reset mail, from the request to signing in with the new password.
- An agent: add it, select it by name when sharing, check its other visible applications, call the API with the key, delete it and see the key answer 401.
- The Docker image builds, `/` loads, the favicon shows, a file under `/assets` is served as immutable.
- One pass over the [Screens](#screens) table on desktop and at phone width.

## Dependencies

Adding one is a deliberate decision: note here what it is for, and add its license text to
`licenses/` at the repository root.

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
| `gridstack` | The grid of the reports dashboard (drag and resize on 12 columns); a layout is stored in its 12-column units. Imported only by `ReportsGrid.vue` and downloaded only when a dashboard is shown on a wide screen. It has no dependencies. Its Vue wrapper (`gridstack/dist/vue`) is not used. |
| `chart.js` | The charts of the report panels. Imported only by `src/lib/reportChart.ts`, which registers just the chart kinds a report can be, and downloaded when the first chart is shown. It brings `@kurkle/color` along. |
| `tailwindcss`, `@tailwindcss/vite` | Styling. |
| `vite`, `@vitejs/plugin-vue`, `typescript`, `vue-tsc`, `@vue/tsconfig`, `@types/node` | Build and type-checking. TypeScript stays on 6.0.x until `vue-tsc` supports 7. |
| `vitest` | Runs the unit tests (`npm test`). Development only; it reads `vite.config.ts` and needs no configuration of its own. The Docker image does not run it. |
| `@vue/test-utils`, `jsdom` | Mount the Sharing page and agent-rights editor for component interaction tests. Only tests with `@vitest-environment jsdom` opt into the DOM environment. The test utilities are pinned to a release compatible with the development Node runtime. Development only. |
