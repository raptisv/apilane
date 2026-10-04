# Portal migration: from the Razor pages to the new UI

For the owner. What was moved, what behaves differently on purpose, what still needs your answer,
and how to remove the Razor portal later. Written at the end of slice 20 (parity audit). The audit
compared code with code; nothing was deleted or rewired.

## 1. Where things stand

- Migrated: every page of the Razor portal has a screen in the Vue app (`src/Apilane.Portal.Ui`) on top of the management API (`/api/v1`, contract in `openapi/portal-v1.json`). No screen of the new UI links to a Razor page.
- Still running: the Razor views and MVC controllers work next to it as before, and `/` still opens the Razor portal.
- Open the new UI: `/ui/` on the Portal, for example `http://localhost:5000/ui/`. Its sign-in page is `/ui/account/login`.
- Run it locally: "Working on the UI" in [README.md](README.md) (start the Portal, then `npm run dev` in this folder).
- Left to do: your answers (section 4), the browser checks that were not done yet (listed in section 5), then the removal of the Razor portal (section 6).

## 2. Page by page

How to read the table:

- Views are under `src/Apilane.Portal/Views/`, actions in `src/Apilane.Portal/Controllers/`.
- Routes are relative to `/ui`. Endpoints are relative to `/api/v1`, and `{app}` stands for `/applications/{appToken}`.
- "API server:" marks a call the browser makes straight to the application's API server, as the Razor page does.
- Status: **covered** (same behaviour), **differences** (covered, with the deliberate differences of section 3), **not migrated** (by decision, reason given).

| Razor view or action | New UI route | API endpoints | Status |
|---|---|---|---|
| **Account** | | | |
| `Account/Login.cshtml` | `/account/login?returnUrl=` | `POST /session`, `GET /instance` | differences |
| `AccountController.LogOff` (no view) | User menu, 'Sign out' | `DELETE /session` | differences |
| `Account/Register.cshtml` | `/account/register` | `POST /account`, `GET /instance` | differences |
| `Account/ForgotPassword.cshtml`, `ForgotPasswordConfirmation.cshtml` | `/account/forgot-password` (the confirmation is its success state) | `POST /account/password-reset-requests` | differences |
| `Account/ResetPassword.cshtml`, `ResetPasswordConfirmation.cshtml` | `/account/reset-password?code=` (the confirmation is its success state) | `POST /account/password-resets` | differences |
| `Account/AppEmailConfirmed.cshtml` | `/account/email-confirmed` | none | covered |
| `Manage/ChangePassword.cshtml` | User menu, 'Change password'; `/account/password` opens the same dialog | `PUT /account/password` | differences |
| `Shared/_LayoutAccount.cshtml` | `AuthLayout` around every `/account/...` screen | `GET /instance` | differences |
| `Shared/Error.cshtml` | No error page of its own. Its one live use (a reset link without a code) is the 'This link is not valid' state of the reset page; every screen has its own error state | none | differences |
| **Shell and navigation** | | | |
| `Shared/_Layout.cshtml` (top bar, Applications menu, Admin menu, user menu) | `AppShell`: sidebar, application switcher, 'Instance' group, user menu | `GET /session`, `GET /applications` | differences |
| `Application/OptionsPartial.cshtml` (button bar of an application) | `AppLayout`: tabs under `/apps/:appToken`, Info and API buttons | `GET {app}` | differences |
| **Applications** | | | |
| `Applications/Index.cshtml` | `/apps` | `GET /applications`, `GET /session/api-token`. API server: `Application/GetStorageUsed`, `Application/Export`, `Health/Liveness` | differences |
| 'Application info' popup (`custom.js`, `ApplicationController.Info`) | `/apps?info=:appToken`, and the Info button of an application | `GET {app}/connection-info` | differences |
| Compare modal (`ApplicationsController.CompareApplications`) | `/apps?compare=:appToken&with=:otherToken` | `GET {app}/comparison?Target=` | differences |
| `Applications/Create.cshtml` | `/apps/new` | `GET /servers`, `POST /applications` | differences |
| `Applications/Import.cshtml` | `/apps/import` | `GET /servers`, `POST /applications/import` | differences |
| **One application** | | | |
| `Application/Entities.cshtml`, `Entity/IndexPartial.cshtml` | `/apps/:appToken/entities` | `GET {app}/entities`. API server: `Stats/CountDataAndHistory` | differences |
| `Application/EntityCreate.cshtml` | 'New entity' dialog on the entities screen | `POST {app}/entities` | differences |
| `Entity/Edit.cshtml` | Edit dialog on the entities screen | `PUT {app}/entities/{entity}` | differences |
| `Entity/Rename.cshtml` | Rename dialog on the entities screen | `POST {app}/entities/{entity}/rename` | differences |
| `Entity/Delete.cshtml` | Delete dialog on the entities screen | `DELETE {app}/entities/{entity}` | differences |
| `Entity/Properties.cshtml`, `Property/IndexPartial.cshtml` | `/apps/:appToken/entities/:entity` | `GET {app}/entities/{entity}` | differences |
| `Entity/PropertyCreate.cshtml` | 'New property' dialog on the properties screen | `POST {app}/entities/{entity}/properties` | differences |
| `Property/Edit.cshtml` | Edit dialog on the properties screen | `PUT {app}/entities/{entity}/properties/{property}` | differences |
| `Property/Rename.cshtml` | Rename dialog on the properties screen | `POST {app}/entities/{entity}/properties/{property}/rename` | differences |
| `Property/Delete.cshtml` | Delete dialog on the properties screen | `DELETE {app}/entities/{entity}/properties/{property}` | differences |
| `Entity/Constraints.cshtml` | `/apps/:appToken/entities/:entity/constraints` | `GET` and `PUT {app}/entities/{entity}/constraints` | differences |
| `Entity/DefaultOrder.cshtml` | `/apps/:appToken/entities/:entity/sorting` | `GET` and `PUT {app}/entities/{entity}/default-order` | differences |
| `Entity/Data.cshtml`, `Application/DataBrowser.cshtml`, `Shared/_LayoutData.cshtml` | `/apps/:appToken/data/:entity?` (`?page=`, `?pageSize=`, `?sort=`; an old `?entity=Name` is honoured) | `GET {app}/entities?IncludeProperties=true`, `GET /session/api-token`. API server: `Data/*`, `Files/*`, `EntityHistory/*`, `Account/Register` | differences |
| `Application/Security.cshtml` with `security-tree.js` and `security-matrix.js` | `/apps/:appToken/security` (`?item=`, `?view=tree`, `?view=matrix`) | `GET {app}/security`, `PUT {app}/security/settings`, `PUT {app}/security/rules` | differences |
| `CustomEndpoints/Index.cshtml` | `/apps/:appToken/endpoints` | `GET {app}/custom-endpoints` | differences |
| `CustomEndpoints/AddEdit.cshtml`, `CustomEndpointsController.GetUrl` | `/apps/:appToken/endpoints/new`, `/apps/:appToken/endpoints/:id` | `POST {app}/custom-endpoints`, `GET` and `PUT {app}/custom-endpoints/{id}`. API server: `Custom/TestQuery`. `POST {app}/custom-endpoints/preview` exists for API callers; the UI works the address out itself | differences |
| `CustomEndpoints/Delete.cshtml` | Delete dialog on the list | `DELETE {app}/custom-endpoints/{id}` | covered |
| `Application/Email.cshtml` | `/apps/:appToken/email` | `GET` and `PUT {app}/email-settings`. API server: `Email/GetEmails`, `Email/Update` | differences |
| `Reports/Index.cshtml`, `Shared/Report.cshtml`, `ReportsController.SaveLayout` | `/apps/:appToken/reports` | `GET {app}/reports`, `PUT {app}/reports/layout`. API server: `Stats/Aggregate` | differences |
| `Reports/AddEdit.cshtml`, `Shared/FilterBuilder.cshtml`, `EntityController.GetProperties`, `ApplicationController.UpdateFilterBuilder` | `/apps/:appToken/reports/new`, `/apps/:appToken/reports/:reportId/edit` (side sheet) | `POST {app}/reports`, `GET` and `PUT {app}/reports/{reportId}`, `GET {app}/entities/{entity}/report-fields?Type=` | differences |
| `Reports/Delete.cshtml` | Confirm on the dashboard | `DELETE {app}/reports/{reportId}` | differences |
| `Collaborate/Index.cshtml`, `Share.cshtml`, `Unshare.cshtml` | `/apps/:appToken/sharing` (owner only; share dialog, remove confirm) | `GET` and `POST {app}/collaborators`, `DELETE {app}/collaborators/{id}` | differences |
| `Application/Import.cshtml` (`ImportController`) | `/apps/:appToken/import` | `GET {app}/schema-import/diff?Source=`, `POST {app}/schema-import` | differences |
| `Application/AuditLog.cshtml`, `Shared/_AuditLogDetail.cshtml` | `/apps/:appToken/audit-log?page=` | `GET {app}/audit-log?Page=&PageSize=` | differences |
| `Application/Edit.cshtml` | `/apps/:appToken/settings`, General | `PUT {app}` | differences |
| `Application/SetStatus.cshtml` | Status dialog (settings screen and card menu) | `PUT {app}/status` | differences |
| `Application/Rebuild.cshtml` | Rebuild dialog (settings screen and card menu) | `POST {app}/rebuild` | differences |
| `Application/Delete.cshtml` | Delete dialog (settings screen and card menu) | `DELETE {app}` | differences |
| `Application/Clone.cshtml` | `/apps/:appToken/clone` | `GET /servers`, `GET {app}/entities`, `POST {app}/clones` | differences |
| `Application/CloneProgress.cshtml`, `ApplicationController.CloneStatus` | `/apps/:appToken/clone/:operationId` | `GET {app}/clones/{operationId}` | differences |
| **Administration** | | | |
| `Admin/Servers.cshtml` | `/admin/servers` | `GET /admin/servers` | differences |
| `Admin/ServerAddEdit.cshtml` | Add and Edit dialogs on the servers screen | `POST /admin/servers`, `PUT /admin/servers/{id}` | differences |
| `Admin/ServerDelete.cshtml` | Delete dialog on the servers screen | `DELETE /admin/servers/{id}` | differences |
| `Admin/Users.cshtml`, `AdminController.SetUserRole` | `/admin/users` | `GET /admin/users`, `PUT /admin/users/{userId}/role` | differences |
| `Admin/Settings.cshtml` | `/admin/settings` | `GET` and `PUT /admin/settings` | differences |
| `AdminController.BackupDatabase` (no view) | 'Backup database' card on `/admin/settings` | `GET /admin/backup` | differences |
| `Admin/AuditLog.cshtml`, `Shared/_AuditLogDetail.cshtml` | `/admin/audit-log?page=` | `GET /admin/audit-log?Page=&PageSize=` | differences |
| `Admin/UserApplications.cshtml`, with its 'Clear cache' link | `/admin/applications` | `GET /admin/applications`, `POST {app}/cache-reset` | differences |
| `Admin/ApplicationData.cshtml`, `AdminController.EntityData` | `/admin/applications/:appToken/data/:entity?` | `GET /admin/applications/{appToken}`, then the data browser's API server calls | differences |
| **Not migrated** | | | |
| `Application/Checkout.cshtml` | none | none | not migrated: dead view, no action reaches it (left from a removed payments feature) |
| `Entity/Clone.cshtml` | none | none | not migrated: dead view, `EntityController` has no Clone action |
| `AdminController.ApplicationLogs` | none | none | not migrated: the action has no view and nothing links to it |
| `PropertyController.Index` | none | `GET {app}/entities/{entity}/properties` exists for API callers | not migrated: the action has no view and fails today |
| `ApplicationController.DataBrowser(inFrame: true)`, the 'Open in a new tab' button, `Shared/_LayoutData.cshtml` as a bare layout | none | none | not migrated: the new UI has no iframes |
| Dead script: `generateApplicationKey` and js-cookie in `custom.js`; `?section=`, the tab click handler and `showhide()` in `Security.cshtml`; select-all and foreign-key filter left-overs in `Data.cshtml` | none | none | not migrated: none of it runs today |
| `AuthenticateController.InRole` | none | none | not migrated: stays as the MVC action, for external apps that share the login cookie |
| `InfoController.GetApplication`, `InfoController.UserOwnsApplication` | none | none | not migrated: stay as they are, every API server calls them |
| `_ViewImports.cshtml`, `_ViewStart.cshtml` | none | none | Razor plumbing, not pages |

## 3. What behaves differently

Deliberate differences from the Razor portal. In each group the ones that change security or how
data is handled come first, then how it looks and reads.

### Shared code: these also changed the Razor pages

The Razor views were not rewritten, but two Razor files were edited (`AccountController.cs`, where
the sign-in claims factory lives, and `_Layout.cshtml`, which got a 'Servers (new UI)' link) and
some shared services changed. So these apply to both portals today:

- The session token is no longer re-issued on the 30-minute cookie refresh; it lives for the whole sign-in.
- A session ended by signing out, or by signing in elsewhere, is no longer brought back by that refresh, and two browsers on one account no longer take the session from each other.
- Last sign-in moves only on a real sign-in (sign in, register, change password), so Admin > Users can show a value that is days old for an active user.
- A save that changes only a secret (installation key, a mail password, a connection string) now writes an audit entry showing `***` to `***`; before, it wrote none.
- `/swagger` sends a visitor whose session was ended to the login page instead of showing the contract.
- The clone routine writes the error message before the status 'Failed', so a status read never sees a failure without its message.
- A foreign-key cycle no longer crashes schema import, clone or the API server's rebuild (the shared ordering helper in `Apilane.Common` now stops at a node it is already visiting).

### Every screen

Security and data:

- Every write of the API needs the header `X-Apilane-Portal: 1`, which the UI adds; Razor checks an antiforgery token on some forms and nothing on others.
- An API call is never answered with a redirect to the login page: it gets 401 or 403 with a JSON error, and a session that was ended gets 401 even on an administrator's address.
- Secrets are write-only: connection strings, the application and instance mail passwords and the installation key are never sent to the browser; a form says whether a value is stored, and an empty box keeps it.
- The encryption key of an application is the one secret that can still be read, on demand, in the Info dialog.
- The token for calls from the browser to an API server comes from `GET /api/v1/session/api-token` and is kept in memory only; Razor writes it into the page source.
- The API server's cache is reset only after a save succeeded, and the request waits for it; if the reset fails, the save stands and a warning toast says so. Razor fires it in the background after every post, failed ones included, and never reports a failure.
- A refusal by the API server shows with its own message at the field it is about; any other failure of the API server is a 502 with a fixed text instead of the raw exception.
- An unknown or inaccessible application, entity, property or id shows a 'not found' state (API 404) instead of an error page, and an owner-only or administrator-only screen shows a 'no access' state (API 403).
- A non-administrator who opens an administrator's address sees that state without the API being called.
- The API validates what Razor took from the browser unchecked; the stricter rules are listed per area below.
- Validation messages are short and uniform ('Required', 'Not a valid email address', 'Must be 8 to 100 characters'), one per field.

Look and feel:

- Navigation is a left sidebar (a drawer on phones) with an application switcher instead of a top bar; the administrator's group is named 'Instance' instead of 'Admin'.
- Create, edit, rename and delete are dialogs on the list screen instead of pages of their own; the result is a toast and you stay where you are.
- Deleting a server, entity, property, custom endpoint or application asks you to type its name in the same dialog. For the first four the dialog reads `Delete <kind> <name>?` and 'This cannot be undone.'; an application keeps its list of consequences.
- A screen that collects several edits (constraints, default sorting, security rules, the custom endpoint editor) has a Save / Discard bar and asks before you leave with unsaved changes.
- There are no iframes and no popups: page number, sort, selected item and linkable dialogs (`?info=`, `?compare=`, `?item=`, `?view=`) are in the address.
- The browser tab title is the page, then the instance name (`Entities · Apilane`); Razor put the instance name first.
- Every screen works from 360px wide; wide tables move columns under the name, and phones can zoom the page.
- The server health dot sends a plain request, counts any successful answer as online, gives up after 4 seconds, pauses while the tab is hidden, checks only the servers shown and is grey until the first answer.
- Help and question texts of the Razor pages are kept, with typos fixed and plainer wording; inputs have visible labels instead of placeholders only.
- The user menu has a new 'API reference' link to the Portal's own `/swagger`; the footer shows the version and apilane.com without the copyright year.

### Sign-in and account

Security and data:

- After signing in, `returnUrl` is followed only when it is a path on this site; the Razor login redirects anywhere (an open redirect, still there in the Razor page).
- Because of that, an external app that shares the login cookie and sends users to the login page with its own full address is not sent back after signing in.
- Sign in, sign up and the password-reset request are limited to 30 calls per minute per IP address, together (`AccountRateLimit`); Razor has no limit.
- Sign-out is a `DELETE` call instead of a GET link, and signing out with an old cookie only removes that cookie instead of ending the newer session.
- Resetting a password with an email that has no account fails with the same 'Invalid token.' as a bad code; Razor shows 'Your password has been reset.' although nothing was reset.
- A successful password reset ends every session of that user; in Razor an open session survived it.
- Changing the password with a session that was ended elsewhere is refused (401); the Razor page still allowed it.
- Mails of the new API (password reset, password changed, share notice) link to `/ui` pages; the reset link carries only the code, not the user id.
- The 'Password changed' mail goes to the email stored on the account, not the one in the cookie, and its link is properly quoted.
- When registration is switched off the API answers 403 and the sign-up address shows a 'Registration is switched off' card instead of redirecting to the login page.
- Field validation runs before the 'registration is off' and 'mail settings not set' checks; Razor checks those first.

Look and feel:

- A signed-in user who opens the sign-in or sign-up page is sent on to the applications page.
- After changing the password you stay signed in and see a toast; Razor shows the login form.
- Change password is a dialog over the current screen and says that other browsers are signed out.
- The forgot-password and reset-password confirmations are states of their page, not addresses; a refresh shows the form again.
- A reset link without a code shows 'This link is not valid' with 'Ask for a new link' instead of the '500 Internal Server Error' card.
- Cancel on forgot password goes to the login page instead of the applications page.
- Sign-up shows the first problem under each field; Razor shows only the last problem of all.
- The account screens show the instance title and logo instead of the fixed word 'Apilane'; the button reads 'Sign in' instead of 'Log In'; there is no 50-character input limit.

### Applications page, new application, import application

Security and data:

- Info dialog: the encryption key is fetched when the dialog opens, masked until you press show, and dropped when it closes; a failed load says so with 'Try again' instead of silently leaving the key out.
- New application: the encryption key is generated with a cryptographic random generator (same length and alphabet).
- New application and import: an unknown server is a 404, a missing one a 400, a refusal of the API server shows under the connection string, and any other failure is a 502; Razor showed a raw exception or a 500.
- New application: when the application was created but the API server could not be refreshed, you land on it with a warning toast; Razor showed an error although the application existed.
- Import application: the file is checked before anything is created (name, encryption key, entities, properties, custom endpoints and reports must be complete), an existing token is a 409, and report-series IDs from the file are reset.
- Import application: PostgreSQL is offered as a database type.
- When the API server created the application and the Portal then could not save it, the answer says exactly that; the application stays on the API server, as in Razor.

Look and feel:

- Server groups are ordered by server name; Razor showed them in the order the servers were added.
- Each card has one actions menu for everything Razor spread over three icons and two menus; Status, Rebuild and Delete open as dialogs, and 'Edit' is the application's Settings screen.
- The Online / Offline label on a card is a badge, not a link; the status is changed from the menu ('Take offline', 'Bring online').
- Clicking a server name opens its health address in a new tab instead of the 'Checking server...' popup.
- A failed 'size on disk' shows the API server's message with 'Try again', and a failed export shows an error toast; Razor showed nothing when an export failed.
- New: an empty state for a user without applications, and a search box from 7 applications up.
- 'New application' and 'Import application' are two buttons instead of a split button.
- No server is preselected unless the instance has exactly one; with no server at all both pages show 'No API server yet' instead of an unusable form.
- The save button is labelled 'Save' with a Cancel link next to it; the questions are a collapsible list.
- Import application keeps what you entered after a failure and shows the error in the form; Razor showed a popup and reloaded the page.
- After creating or importing, the new application's entities screen opens with a toast.
- Database types read 'SQLite', 'SQL Server', 'MySQL' and 'PostgreSQL' (Razor: 'My SQL'), and the Swagger / API link no longer lower-cases the server address.

### Application frame, entities and properties

Security and data:

- Renaming or deleting a system entity, and changing a system property, answer 409 with the reason; Razor silently redirects.
- Renaming an entity or property to a name that exists (in any letter case) is refused by the Portal with 409 before the API server is called.
- Renaming or deleting a property that is part of a unique or foreign key constraint is refused (409); the constraint has to be removed first. Razor left the old name in the constraint.
- Only the values of the form can be posted: a new entity takes its four values, a new property cannot set its ID or the primary-key flag, and an edit uses the stored type instead of a hidden field.
- The differentiation switch is refused when the application has no differentiation entity, and change tracking cannot be turned on for an entity whose records cannot be updated.
- New checks on a property: decimal places 0 to 8, the type one of the four names, a String's minimum not negative and its maximum at least 1, and on edit a String's minimum is compared with its stored maximum.
- Minimum and Maximum must be between -9,007,199,254,740,991 and 9,007,199,254,740,991, the range a browser can show exactly; Razor accepts any 64-bit number.
- Adding a property to an entity that takes none (Files) answers 409; Razor silently redirects.
- Stored constraints that cannot be read are left out instead of breaking the entities list, rename and delete.
- Deleting an entity that a foreign key points to says 'Cannot delete entity...' (Razor said 'rename') and names only the first entity that points to it.
- A property's regex is shown as text; Razor wrote it into the page as raw HTML.
- A description or regex that is only spaces is stored as empty.
- A duplicate name on create is a 409 shown above the form instead of under the Name box.

Look and feel:

- The button bar became tabs, shown on every screen of the application, with two new ones: Entities and Settings.
- The breadcrumb reads Applications / application (server name with health dot) / section instead of starting with the server; an Offline badge is new.
- Entity screens have a back link and tabs: Properties, Constraints, Sorting, Data, Security.
- Entities: the description is text under the name, constraint badges sit under the name, and the row menu adds Properties and Data.
- Entities: a menu item that is not available shows the reason as a second line instead of a tooltip.
- Entities: Constraints is offered for system entities too, read-only unless you are an administrator; Razor disables the link for everyone.
- Entities: counts have thousands separators, a failed count shows its message with a retry button, a search box appears from 10 entities, and an empty custom list offers 'New entity'.
- Properties: the list says the rules in plain words ('Length 2 to 50', 'Integer') and shows Primary key, Required and Encrypted as badges.
- Properties: the create form asks for the type first and shows only the fields that type uses; Minimum and Maximum are labelled as length or value; the type reads 'Number' where Razor's dropdown says 'Numeric'.
- Properties: the forms say which values cannot be changed later, and the edit form shows them read-only.
- Properties: the rename box allows 120 characters, the real limit; Razor's box stops at 30.
- The name-rule message has its typo fixed, and a name ending in '_Data' gets a message of its own.
- The link from an entity to its security rules uses `?item=Entity-Name` instead of `?entity=Entity-Name`.

### Constraints and default sorting

Security and data:

- The server checks every constraint (names exist and are allowed, no duplicates) and keeps the system constraints itself; Razor stored what the browser posted, hidden fields included.
- On a system entity a non-administrator sees the constraints read-only and a save answers 403; Razor redirects them away.
- Duplicates are refused with a message instead of being dropped silently; the same unique properties in another order, and the same foreign key with another on-delete action, count as duplicates.
- The on-delete action of a stored foreign key can only be changed by removing it, saving, adding it again and saving; Razor accepted the edit but never applied it to the table.
- A foreign key stored in the old two-part format is rewritten with 'no action' on the next save, and a stored constraint the API cannot read is removed by the next save.
- Default sorting: the server checks that each property exists, appears once and has the direction asc or desc; Razor stored any text.
- Default sorting: a stored direction that is not 'asc' shows as descending (as the Razor label does), a property stored twice shows once, and unreadable stored data reads as no sorting.

Look and feel:

- Several constraints can be added and removed before one Save; a removed one stays in the list marked 'Removed on save' with Undo.
- There is no Edit button for a constraint: remove it and add the new one.
- The Foreign key choice is disabled with the reason when the entity has no property that can be one; each on-delete action has a line of explanation.
- Default sorting is one row per property with a direction button and up / down buttons, instead of a checkbox list with drag and drop; a property can no longer be listed in both directions.
- After saving the default sorting you stay on the screen; Razor goes back to the entities list.

### Application settings, status, rebuild, delete

Security and data:

- The stored database type decides whether a connection string is written; Razor trusts a hidden form field.
- No connection string or encryption key is put into the page for a rebuild or delete; Razor's pages carry them in hidden fields.
- The status call takes an explicit Online value instead of toggling, so repeating it changes nothing.
- The typed name and the 'I understand' box stay browser-only guards, as in Razor; the API has no confirm field.
- When the API server delete succeeded and the Portal could not save, the answer says exactly that instead of the raw exception.

Look and feel:

- One Settings screen (General, Status, Danger zone) replaces four Razor pages; the same dialogs open from the card menu on the applications page.
- The server and the database type are shown disabled with 'cannot be changed'; Razor shows neither.
- After saving you stay on the settings screen; Razor goes to the applications list.
- Rebuild and delete show the consequences, the 'I understand' box and the typed name in one dialog; a name containing `'` or `&` can be confirmed (it cannot in Razor).
- The delete dialog has one more consequence line saying what is removed with the application.

### Sharing

Security and data:

- Sharing with your own address is refused (409).
- The notification mail HTML-encodes the application name and the owner's address (Razor put them in raw) and links to `/ui/apps`.
- Only the email is accepted from the browser; Razor bound the whole record, so an ID could be posted too.
- A collaborator who opens the Sharing address gets a 'no access' state (API 403) instead of an error page; an unknown id is a 404.

Look and feel:

- Share and remove are a dialog and a confirm on the list; the action is called 'Stop sharing' everywhere.
- The toast says whether a notification mail was sent (a warning when none was), and the dialog no longer promises one when mail is not set up.
- The share dialog warns that the user gets full access, even to delete, and says to type the address exactly as in the user's account.
- The two help questions are one always-visible section.

### Email

Security and data:

- The mail password is removed with a 'Remove the stored value' box; in Razor a blank post clears it.
- The template preview is drawn in a stricter sandbox (no script, no same-origin access).

Look and feel:

- One 'Save settings' button for both cards and a toast; Razor has a button per card and no feedback.
- Incomplete SMTP settings show a warning sentence; the port is checked to be a whole number before sending.
- The template preview is live inside the edit dialog instead of a separate popup.
- An empty subject or body is caught in the browser, and API server errors show in the dialog instead of alert popups.
- The 'default landing page' link opens `/ui/account/email-confirmed` instead of `/Account/AppEmailConfirmed`.

### Audit log (instance and application)

- The API rejects a page below 1 or a page size outside 1 to 200 (400); the UI always asks for 50 and treats a bad `?page=` as page 1.
- Entries with the same timestamp are ordered by ID, so pages are stable; Razor orders by timestamp only.
- Stored changes that make the Razor page fail (a rule type that is not a number, a list item that is not an object) are shown with empty fields instead.
- An empty page past the last one offers 'Go to the first page'; on phones the columns move under the action; the heading is 'Audit log' instead of 'Admin Audit Log'.

### Security

Security and data:

- Settings and rules are saved separately, each with its own Save and endpoint; Razor saved both in one post.
- The rules API refuses what Razor stored unchecked: an unknown type or rate window, an empty role, Schema rules that are not 'get' on 'Schema', duplicate rules, actions the item does not offer, property names the action does not offer, properties on rules that are not about an entity, and a missing record scope.
- Stored rules that can never apply (deleted item, unknown type or action, blank role, second rule for the same cell) are left out when reading and so dropped by the next save, as Razor's next save did.
- IP addresses must be plain dotted IPv4 (no leading zeros, signs or spaces inside), because the API server could never match the others; the logic must be Block or Allow; a missing settings value is refused instead of stored as off.
- The action of a rule is always stored in lower case.
- A switched-on cell with no properties shows as 'No properties: only the ID is returned' or 'the call is refused' in the editor, the tree and the matrix; Razor's tree and matrix showed it as full access.
- The tree and the matrix show access as the API server enforces it (inherited access, properties added up, the rate limit of the item and action as a whole) instead of each stored rule on its own.
- When the API server cannot be asked for the users' roles, the page still opens with a warning and the roles already used in rules; Razor showed its error page.

Look and feel:

- The settings are always-visible cards with one 'Save settings' button, a 'Not saved yet' note and their own question before leaving, instead of a collapsed accordion with a button per section.
- IP addresses are a list with one box and one error each (a pasted comma-separated list is split); with 'Allowed' and a non-empty list a lock-out warning is shown.
- The file size box shows the real limit of 1 to 25600 KB; Razor's box allowed any number while the server enforced 25600.
- 'Owned records only' stored in a cell where it has no effect stays visible with a note; Razor silently saved it as 'All records'.
- The details of an allowed cell sit behind a 'Details' toggle with a one-line summary; the property list has 'Select all' and 'Deselect all'.
- A cell switched off and on again gets its properties and rate limit back, until the next save or discard.
- A 'How rules add up' block explains inheritance, properties, owned records and rate limits; Razor explained none of it.
- The tree is an expandable list (item, role, action) without d3, zoom, pan or a CDN; the matrix has items as rows and roles as columns; both say so when the editor holds unsaved changes they do not show.
- Items are picked from a searchable grouped list (a select on a phone) instead of a row of pills; role rows show the plain role name, and a role no user has is badged.
- Saving rules shows a toast; an error names the item, role and action and opens that cell. Razor showed nothing on success and one message under the grid on failure.
- The two forgot-password addresses are copy fields, and only the page address can be opened.
- Labels read 'Allow only one sign-in at a time', 'Allow users with an unconfirmed email to sign in' and 'Owned records only'.

### Custom endpoints

Security and data:

- Custom endpoints are addressed by their numeric ID; an unknown or foreign ID shows 'not found' (404) instead of the error page.
- The name is trimmed before the letters-only check and capped at 80 letters on the server; in Razor the cap was only in the input box.
- A new endpoint gets its modified date set to now; Razor leaves 0001-01-01.
- An endpoint whose SQL contains `{appToken}` works; the Razor list crashes on it.
- Test sends the parameter values URL-encoded and exactly as typed, so long whole numbers are not rounded.

Look and feel:

- The list shows the description as a second line, and the row buttons are ordered Call, Edit, Delete.
- The search also looks in the description, uses the text as typed and marks what it found.
- The Name box no longer blocks key presses (Razor's filter also blocked capital Z by mistake); the server rule decides.
- A rename warning appears in the editor: the address changes and the security rules written for the old name no longer apply.
- The address and the parameter boxes are worked out in the browser instead of one request per key press; a typed value survives when its placeholder leaves the SQL and comes back; the boxes have labels and a note that empty is sent as SQL null.
- The SQL editor is CodeMirror 6: Tab leaves the editor instead of indenting, lines wrap, it does not take focus on load, and entity and property names are offered while typing.
- The test result is a read-only code block; on a phone the result comes right after the form.
- The answer to 'Can I change the name later?' now says access is given by name; the 'rebuild' link goes to the Settings screen.

### Data browser

Security and data:

- Editing a record sends only the fields you changed plus the primary key; Razor sends every editable field and so overwrites changes someone else made meanwhile.
- A file is downloaded with the token in a request header and saved under the record's Name; Razor opens a URL that carries the token in the query string.
- An API server error shows as text in the grid with 'Try again'; Razor inserts the message as unescaped HTML.
- A 401 from the API server shows as an error after one token refresh; Razor silently ignores it.
- Clearing the history of one record asks first; Razor clears it at once.
- The CSV export quotes correctly, is not cut off at `#`, and writes a null as an empty cell instead of the text 'null'; it still exports the current page only.

Look and feel:

- Rows have checkboxes and 'Delete selected (N)' deletes several records in one call.
- Filters are sent 400 ms after typing stops, trimmed, and a newer request cancels the one still running; Razor sends one request per key press.
- A plain text filter can be switched between 'contains' and 'equal'.
- Record history opens in a side sheet, shows the total and marks the values that changed.
- The entity switcher lists custom entities first, then system entities, each by name; without an entity in the address the first custom entity opens. Razor used creation order, which starts with a system entity.
- Page, page size and sort are in the address and filters are not, so switching entity starts fresh.
- If nothing was changed, the edit sheet says 'Nothing has changed.' and sends nothing.
- The heading is 'Data:' followed by the entity name, and the tab title names the entity.
- 'Clear filters' goes back to page 1, 'Export page as CSV' is disabled on an empty page, and the paging bar stays visible on a page past the end.

### Reports

Security and data:

- Grid reports write labels and values as text, not HTML, which closes a stored-XSS hole of the Razor page.
- The report API refuses an unknown report type, a time range that is not 1 to 1000 followed by h, d, m or y, a Property or Group-by the editor does not offer for that entity and type, a grouping listed twice, and an entity name in the wrong letter case; Razor stores whatever is posted.
- The layout API checks bounds (X 0 to 11, Y 0 to 10000, Width 1 to 12, Height 1 to 100, X + Width at most 12) and refuses unknown or repeated report IDs, saving nothing; Razor accepts anything and skips unknown IDs.
- A series whose entity or property is gone, or whose call fails, shows its own error inside the panel and the other series still draw; Razor breaks the page or the whole panel.
- A stored filter the flat builder cannot show (OR, nested groups) is shown as raw text and kept unless replaced; Razor's builder silently flattens it to AND.
- The statistics call is URL-encoded and keeps spaces inside filter values; Razor strips every space.
- An unknown report ID is a 404; its edit address shows a toast and the dashboard instead of an error page.
- A new report gets its modified date set to now.

Look and feel:

- The editor is a side sheet over the dashboard; Property is a select, Group by a checkbox menu that shows what is ticked, Time range a select with 'Custom'.
- A new report starts with Max points per series = 20; the Razor box starts empty and fails its own check.
- After the visualization changes, a Property or Group-by the new type does not offer is flagged before saving.
- Validation names every problem at its field instead of one message for the form.
- Refresh and 'View API endpoint' work out the time window from now; Razor keeps the window of the moment the page was rendered.
- 'View API endpoint' shows one copyable field per series.
- Below 768px the dashboard is a single column without drag or resize.
- The layout is saved 600 ms after the last change; a failed save shows an alert with 'Reload the dashboard'. Razor posts on every change and ignores failures.
- Deleting a report is a confirm on the dashboard that adds 'The data it shows is not touched.'
- gridstack 14 and Chart.js 4.5 from npm replace the vendored 10.3 and 4.4; if either cannot be downloaded the dashboard falls back to one column or a table.

### Import schema and compare

Security and data:

- Import failures are HTTP errors that name the place in the payload; Razor answered 200 with an error text. Warnings are shown as text, not injected as HTML.
- Names and types of new entities, properties and custom endpoints, constraint types, the IsSystem flag, on-delete values and rate-limit values are checked before anything is applied; Razor stored or forwarded them unchecked.
- Only an administrator may add constraints to a system entity through an import; Razor allowed any member.
- Stored security rules that cannot be read answer 409 before the first write.
- When the API server cannot be refreshed after a successful import, the result is success with a warning; Razor reported an error although everything was applied.
- 'Load diff' needs the exact source token; custom endpoint names are trimmed before they are matched and stored.
- Compare: an application you cannot see is a 404 instead of a 400, and unreadable stored rules answer 409 instead of an error page.

Look and feel:

- A confirm asks before the import runs and says it cannot be undone and is not atomic.
- Two more summary badges (properties, constraints); notes and example payload are collapsible; three new notes; the browser refuses a payload that is not a JSON object.
- Compare is a full-screen dialog with the pair in the address, so it can be linked to and the other application changed in place.
- Compare lists properties and constraints in their own sections as 'Entity.Property'; an added or removed property is one line of badges instead of a nine-column table.
- A changed security rule shows only Record, Rate limit and Properties; a value that is not set reads 'not set'.
- Both application pickers order applications as the applications page does.

### Clone

Security and data:

- Clone progress can be read only by the user who started the clone, and only under the application it clones; Razor lets any member read any operation.
- With 'Clone data' on, the API refuses entity names that are not entities of the application; it also refuses a database type that is not one of the four names.
- With 'Clone data' on and no entity ticked, the form refuses to submit; in Razor that combination copies every entity.
- Starting a clone no longer resets the API server cache of the source application.
- PostgreSQL is offered as a target.

Look and feel:

- The server select starts on the server of the application being cloned.
- The connection-string note says it is checked when the clone starts and a wrong value shows as a failed clone.
- An operation the Portal no longer knows shows 'This clone can no longer be tracked' instead of a silent redirect.
- The failed state adds that the new application is already listed and can be deleted, with a 'Go to applications' button.
- Polling waits 2 seconds after each answer, pauses in a hidden tab and shows 'Trying again' when the Portal does not answer.
- The remaining time is rounded up before it is split, and the percentage stays between 0 and 100.
- The button reads 'Clone' and has a Cancel link.

### Administration

Security and data:

- Settings: the installation key cannot be cleared, and the mail password is removed with a 'Remove the stored value' box.
- Applications: the connection string and mail password of each application show as 'Set', 'Not set' or 'Not needed' instead of clear text.
- Backup: the file is made with SQLite's online backup into a uniquely named temp file and streamed; Razor copies the live file to a fixed name and loads it into memory.
- Backup: every download writes an audit entry ('Database backup', 'Apilane.db', 'Downloaded').
- Servers: the address must be an http or https URL; Razor accepts any scheme.
- Users: changing your own role answers 409, an unknown user 404, and asking for the role a user already has succeeds without change; Razor throws in all three cases.
- Applications: 'Clear cache' goes through the Portal instead of from the browser to the API server, so a failure now reflects the Portal's connection to it.
- Admin data browser: it works for every application; in Razor the tabs fail for applications the administrator neither owns nor collaborates on.
- An unknown server id answers 404 instead of an exception.

Look and feel:

- Servers: a table instead of cards, 'Add server' instead of 'New server', help lines under the fields, a reworded edit warning, a 'No servers yet' state; an empty address reports only 'Required'.
- Users: a role change asks first and ends with a toast; the page says it takes effect within 30 minutes or at the next sign-in.
- Users: last sign-in is local time with the UTC value as a tooltip; buttons read 'Make admin' and 'Make user'; the role is a badge; your own row carries a 'You' badge.
- Settings: one Save button and a toast; the sidebar and tab title pick up a new instance name at once; a failed save keeps what was typed.
- Settings: a 'Not complete' note while the mail settings cannot send mail; the mail port accepts digits only; validation messages are reworded.
- Backup: a card with a sensitivity warning on the Settings screen, not an entry of the menu.
- Applications: a compact table with a Details row replaces the 18-column table; search by name, token or owner email; a count line.
- Applications: 'Clear cache' shows 'Clearing…' and reports with a toast; 'Open application' appears only for applications the administrator owns or collaborates on.
- Admin data browser: a normal page with an entity switcher instead of an iframe popup with tabs; clear states for an unknown token, no entities and an unknown entity.
- Admin data browser: for someone else's application the link to its Email settings is left out.

## 4. Decisions still open

The default is what the code does today. Say nothing and it stays; the alternative is what would
change.

### Planned defaults, built as recommended (please confirm)

| Decision | Default now in place | Alternative |
|---|---|---|
| Data plane | The browser calls the API server directly with a token from `GET /session/api-token` (memory only). The token is readable by script in the page, as today | Proxy every data call through the Portal: about 15 more endpoints and an extra hop |
| CSRF protection | Required header `X-Apilane-Portal: 1` on every write, plus the SameSite cookie | ASP.NET antiforgery token endpoint and header |
| Secrets | Write-only; only the encryption key can be read, in the Info dialog | An administrator-only 'reveal' action, for example for the installation key |
| Admin data browser | Works for any application of the instance | Keep it to applications the administrator owns or collaborates on, as Razor in effect does |
| Unreachable views and actions | `Checkout.cshtml`, `Entity/Clone.cshtml`, `AdminController.ApplicationLogs`, `PropertyController.Index` are dropped | Name the one you still want and it gets a screen |
| Sign-in | Same-site `returnUrl` only; sign-out is a DELETE; you stay signed in after a password change | For external apps behind `/Authenticate/InRole`: allow return addresses on hosts under `AuthCookieDomain`, checked by the server |
| Mail links | Mails of the new API link to `/ui` pages; mails sent from Razor pages link to Razor pages until those are removed | None worth having |
| Cache reset after a save | Only after success, awaited; a failure is a warning and the save stands | Fail the request with 502 |
| Security page | Separate saves for settings and rules; 'no properties' shown honestly; stricter rule checks | One atomic save of both |
| Stricter validation | The API checks what Razor trusted the browser with (see section 3) | Strict parity for single rules, for example any URL scheme for a server address (three lines in `ServerService.ValidateUrl`) |
| Rebuild and delete confirmation | Browser-only guards, no confirm field in the API | Require the application name in the request |
| Collaborator rights | Unchanged: a collaborator can delete, rebuild, clone, import schema, edit security and read the encryption key; only Sharing is owner-only | Narrow them before agents use the API |
| Small additions | PostgreSQL in import and clone, confirm before a role change and before clearing one record's history, several constraints per save, bulk record delete | Remove any you do not want |
| Database backup | Online backup, streamed, with an audit entry per download | Keep the plain file copy |
| Rate limit | 30 calls per 60 seconds per IP on sign in, sign up and reset request (`AccountRateLimit__PermitLimit`, `AccountRateLimit__WindowSeconds`) | Other numbers. Behind a reverse proxy see section 5 |
| Automation access | The API is cookie-only and one sign-in ends the user's other sessions | An API-key or token scheme, planned after the migration |

### Raised during the work

| Decision | Default now in place | Alternative |
|---|---|---|
| Session token on the cookie refresh (also changes Razor) | Not re-issued every 30 minutes; an ended session stays ended; last sign-in moves only on a real sign-in | Go back to re-issuing on every refresh |
| Collaborator email letter case | Saved as typed; access needs an exact match, so 'Bob@x.com' for the account 'bob@x.com' is accepted but gives no access (as in Razor) | Save the exact address of the matching account when sharing (small change in `CollaboratorService.AddAsync`), or match without case everywhere |
| Server preselection on new and import application | None unless there is exactly one server | Preselect the first server (one line in `useServerChoice.ts`); note that the list is ordered by name, so 'first' is not Razor's first |
| Cache reset after 'Import application' | Not done, as in Razor | Reset it, so an API server that cached an error for that token recovers at once |
| Import of the exported zip | Import takes the `application.json` inside the zip, as in Razor | Read the zip directly |
| Schema import: order of steps | Security rules before custom endpoints, as in Razor | Swap them (two lines in `SchemaImportService.ImportAsync`); that would also allow checking imported rules with the rules service |
| Schema import: numbers in the payload | TypeID, Record and TimeWindowType stay numbers, so a payload written for Razor works | Enum names, like the rest of the API |
| Schema import: foreign-key order | A referenced entity must be listed before the entities that point to it (Razor's defect, reproduced and documented) | Fix the ordering in `InForeignKeyOrder`; the order then differs from Razor in some cases |
| Stored sort direction that is neither asc nor desc | Shown as descending, as the Razor label does, although the API server sorts it ascending | Show it as ascending |
| Dates of a new record | Pre-filled with the browser's local time while the API server reads the text as UTC (copied from Razor; the help line says so) | Pre-fill in UTC |
| Administrator reads another user's data | No audit entry, as in Razor | Write one when the admin data browser opens a foreign application |
| 'Backup database' in the sidebar | No entry; it is a card on Settings | Add the entry |
| `{appToken}` as a parameter name in a custom endpoint | Accepted and works | Refuse it with a 400 |
| 'This action is not reversible!' | Kept in the rebuild and delete consequence lists of an application (the plan said to keep those texts) | 'This cannot be undone.', as every other delete now says |
| Boolean choice in the record form | '-- select --', the Razor text | 'No value (null)' |
| npm packages `chart.js` and `gridstack` | Added: the libraries the Razor dashboard already uses, in newer versions | None; they need your nod because the plan did not name them |
| Change in `Apilane.Common` | `ObjectTreeExtensions` stops on a foreign-key cycle (also used by the API server's rebuild) | Revert it and guard only inside the schema import |
| `Apilane.sln` | x64 platform rows removed; the Portal test project uses the SDK-style project type id | Revert those lines |
| Route name of the Import tab | `app-schema-import`, because `app-import` is the 'Import application' page | Rename the other route instead |
| Security rules the API still accepts | 'Owned records only' where it has no effect, and a property listed twice in one rule, as Razor accepts them | Refuse both in `SecurityRulesService` |
| Differentiation entity of a new application | Checked for length only, as in Razor; a bad name comes back as an API server error under the connection string | Add the letters-only rule |

### To decide when the Razor portal is removed

| Decision | Recommended | Alternative |
|---|---|---|
| Redirect of `/` to `/ui/` | Temporary (302): browsers cache a 301 of `/` and it cannot be taken back | Permanent, as the plan first said |
| Old bookmarks (`/Admin/...`, `/Applications/...`, `/App/{token}/...`) | Redirect them with the table in section 6 | Let them answer 404 |
| External apps that send users to the login page with a full return address | Accept that they land on the applications page | Allow hosts under `AuthCookieDomain` (see 'Sign-in' above) |
| The four unreachable views and actions | Delete them with the rest | Rebuild one first |

## 5. Known limits

### Left as they are in Razor too

- The password-reset link is built from the request's host. Set `AllowedHosts` per deployment (the public host and the host in the API's `PortalUrl`), or a reset mail can carry a link to a host the caller chose.
- The shared mail service (`Apilane.Common/Services/EmailService.cs`) logs the full mail body, reset link included, at Information level, and swallows SMTP failures; so a failed send still looks like a success, and 'notification sent' means handed to the sender, not delivered.
- The reset request answers the same for a known and an unknown email, but not equally fast: the mail is sent inside the request.
- Collaborators are matched by the exact letter case of their email (see section 4).
- The API is cookie-only, with one session per user: signing in somewhere else ends the earlier session.
- A role change takes effect within 30 minutes or at the user's next sign-in.
- `/Authenticate/InRole` does not check whether the session was ended, as the rest now does.
- The API server builds the email-confirmation landing address from its `PortalUrl`, which is the server-to-server address.
- Renaming an entity or property does not update security rules, reports, custom endpoint SQL, the default sorting or the differentiation entity; deleting does not remove them.
- A case-only rename of a property changes the name in the Portal but not the column (it breaks PostgreSQL applications); the cause is in the API server.
- When the API server accepted a create, import or clone and the Portal step then failed, the application stays on the API server; a failed clone leaves its new application listed.
- Clone: the bar reaches 100% after the schema phase and drops to 50% when data starts; reports are not cloned; the clone's name can pass 100 characters; operations are kept in memory only, so a Portal restart loses the progress screen.
- The schema import is not atomic: the steps before a failed one stay applied.
- Entity names are matched case-sensitively in Compare and case-insensitively in the schema import diff.
- 'Load diff' rounds whole numbers above 9,007,199,254,740,991 before they reach the payload box.
- A security rule already stored with an unknown rate window (written by an old Razor import) makes Compare and a re-import answer 500 until the Security screen is saved once.
- If the stored security rules are not readable JSON, the Security screen shows an empty grid and the next save overwrites them; this needs a hand-edited database to happen.
- Full-replacement saves (security rules, constraints, layout) have no check for a change made by someone else in the meantime.
- Reports: a series with a time range still gets at most 'Max points' groups, so 'Last 90 days by day' with the default 20 shows the newest 20 days.
- Reports: panels can be moved and resized only with a pointer or touch.

### New limits to know about

- Until the Razor portal is removed, its pages still show the installation key, mail passwords and connection strings in clear text, its login still redirects anywhere, its Grid report still builds HTML from data, and `/Admin/BackupDatabase` still uses the fixed temp file. The 'secrets are write-only' decision takes full effect only at cut-over.
- Rate limit behind a reverse proxy: the limit counts by the connection's address, so behind a proxy that is not on loopback all visitors share one budget. Raise `AccountRateLimit__PermitLimit`, or set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` only when the proxy is the only way in and overwrites `X-Forwarded-For`. The same text is in `docs/docs/deployment.md` under 'Production considerations'.
- Reports saved in Razor may need one correction: a panel stored with a height above 100 or X + Width above 12 makes every layout save of the new UI answer 400 until that panel is made smaller, and a report whose Property or Group-by its type no longer offers must be corrected before it saves.
- Reports: after a panel was deleted in another tab, a layout save fails until the dashboard is reloaded.
- The email placeholder list (`src/lib/emailTemplates.ts`) is a hand copy of `EmailEvent.cs`; update it when an event is added.
- Backup: the temp file goes to the system temp folder (not checked in the Docker image) and is not swept when the process is killed mid-download; a failed download opens the JSON error in the browser instead of a message on the page.
- After a rules save the role rows are not re-read from the API server; a reload shows new or vanished roles.
- The Info dialog opened from an application's own screen cannot be linked to; `/apps?info=<token>` can.
- The tab title does not name the application (the Data screen names the entity).
- One screen has two names on purpose: the tab reads 'Sorting', the menu item and heading 'Default sorting'.

### Gaps the fix pass did not close

- **What was checked in a browser, and what was not.** The audit itself was code against code. Separately, each slice was opened against a running Portal and a real API server (a throw-away instance on SQLite), and after the last slice every route was loaded once at desktop width with no script error.
  - Checked by hand, end to end: sign in with a return address, sign out, sign up, change password, the forgot and reset password states; servers (add, edit, delete); users (role change, own row); instance settings and the backup response; both audit logs; the applications page (health dot, size on disk); create application; entities (create in the UI; edit, rename, delete through the API calls); properties (create in the UI; edit, rename, delete through the API calls); constraints (the two-step dialog, save) and default sorting (save, the leave question); application settings (rename, take offline, bring online, delete); sharing, as owner and as collaborator; email settings and a template save, with the preview refusing to run a script; security (a rule saved, tree and matrix); custom endpoints (editor completion, Test, save, search); the data browser (create, edit sending only the changed field, a filter, deleting one record and several selected ones, the CSV export with quotes and commas, the history panel and its 'Clear history'); files (the size check, a real upload, a download that sends the token in a header and not in the address, delete); admin applications, the admin data browser and Clear cache; reports (a panel with real data, dragging and resizing a panel with the layout saved, a report created in the editor with a filter); schema import and the compare dialog; importing an application from a real exported `application.json` (a new token imports, an existing one is refused); Rebuild; a clone with data from the form to 'Clone completed'; a password reset with a real code, from the request to signing in with the new password.
  - Checked at 360px for sideways scrolling: the screens of every slice, including the data grid, the security grid, the compare dialog and the reports dashboard.
  - The Docker image was built with every screen (`docker build -f src/Apilane.Portal/Dockerfile --build-arg VERSION=1.0.0 .`; without `VERSION` the .NET build stage fails, as before this work) and run: `/ui/` is served with `no-cache`, its assets as immutable, the API signs in, and the Razor login still answers.
  - NOT checked in a browser, look at these first: the delivery of a mail through a real mail server (the reset link was taken from the Portal log; the mail code is unchanged and needs a TLS server); the admin data browser on an application the administrator does not own; constraints of a system entity as a non-administrator, and a real 403 or 502 on a save; 640 to 1000px, where the application tabs wrap; keyboard focus after dialogs opened from a menu.
- `dotnet test Apilane.sln` was run once at the end with Docker running: 600 unit tests, 452 component tests (SQLite, SQL Server, MySQL, PostgreSQL) and 1,374 Portal tests pass.
- Found by the Rebuild check, fixed in the API server (not in the Portal): `ApplicationAPI.RebuildAsync` dropped every table and recreated only the entity tables, so the record history and email template tables were missing until the API server restarted (record counts, history and email templates failed; the Razor Rebuild had the same effect). Rebuild now calls `EnsureSystemTablesAsync`, as application creation does, and the rebuild component test checks it on all four databases.
- Docs outside this folder: `docs/docs/deployment.md` now has the rate limit behind a proxy and `AllowedHosts`. Still to do at cut-over: `docs/docs/developer_guide/security.md` lines 9, 12 and 124 (old security labels), and the screenshots under `docs/docs/assets`, which show Razor screens.
- The API's 409 texts 'Cannot rename system Entities' and 'Cannot delete system Entities' keep the old wording; the UI shows its own text, and two tests pin them.
- 'e-mail' is still written in code comments, the README and the API's XML summaries; only the visible texts were changed to 'email'.
- The contract lists a 401 answer on the anonymous account endpoints, because the base class declares it for every action; only `POST /session` can answer it.
- The close-focus handler exists in six copies, and the row menu buttons have no tooltip; both were optional tidy-ups.
- The start-up check that refuses a rate limit of 0 or less was written but never run.

## 6. Removing the Razor portal

One change set, in this order. Each numbered step can be its own commit; the build and the Portal
tests must be green after each.

### 1. What must survive, unchanged

- `GET /Info/GetApplication` and `GET /Info/UserOwnsApplication` (`InfoController.cs`) with `IPortalSettingsService` and the `x-installation-key` check. Every API server calls them, and the new data browser depends on the second one. Do not add `[ApiController]` (a missing `appToken` would become a 400).
- `/Authenticate/InRole` (`AuthController.cs`): any HTTP method, 200 or 401, never a redirect.
- `AddControllers().AddJsonOptions(PropertyNamingPolicy = null)` in `Program.cs`, and no global enum-as-string converter: the API server reads the `/Info` answers case-sensitively, with numeric enums.
- `/health/liveness`, `/health/readiness`, `/metrics`, `/swagger`, `/api/v1`, `/ui`, the cookie name `Apilane.Portal.Identity` and `AuthCookieDomain`.
- `wwwroot/EmailTemplates/FORGOT_PASSWORD.html` (read by `PortalAccountService`) and `wwwroot/favicon.ico`.
- Services: `ApiHttpService` / `IApiHttpService` (the clone routine sends through it), `PortalSettingsService`, `CloneService` / `ICloneService`.
- Models: `ApplicationDbContext`, `CloneProgressInfo`, `IdentityExtensions`, `PortalAuditLog`, `PortalConfiguration`, and the model methods the API uses (`DBWS_Entity.AllowPut` / `AllowAddProperties`, `DBWS_EntityProperty.Allow*`, `EntityConstraintExtensions`, `SortData.ParseList`).
- On the API servers: the open CORS policy (the UI calls them from the browser with an Authorization header, as the Razor pages do).

### 2. Prepare (before deleting anything)

1. Keep `tests/Apilane.Portal.Tests/LegacyServiceEndpointsTests.cs` green from here on. It covers the three endpoints above (14 tests, added by the fix pass) and is the guard for everything below.
2. Freeze the parity tests. Many Portal tests run the Razor action and compare the API with what it does (names ending in `..._The_Razor_Page_...`, `..._Razor_Portal_...`, `..._Razor_Dialog_...`, `..._Razor_Form_...`). Replace each Razor half with the literal values it produces today (API server requests, stored rows, audit rows), or that knowledge is lost. Files: `ApplicationAuditLog`, `ApplicationClone`, `ApplicationComparison`, `ApplicationEmailSettings`, `ApplicationProvisioning`, `ApplicationSecurity`, `ApplicationSettings`, `CustomEndpoints`, `Entities`, `EntityConstraints`, `EntityDefaultOrder`, `Properties`, `Reports` and `SchemaImport` `ApiTests.cs`, plus `Infrastructure/EntityScene.cs` (`AddTwinsAsync`).
3. Move `AppClaimsPrincipalFactory` out of `Controllers/AccountController.cs` (it is a nested class) into `Services/AppClaimsPrincipalFactory.cs`; update `Extensions/ServiceDependencyInjection.cs` and the `cref` in `Services/PortalSecurityStampValidator.cs`. The build fails if this is forgotten.
4. `Infrastructure/PortalFactory.cs`: make `SignInAsync` sign in with `POST /api/v1/session` (today it posts the Razor login form). Keep the method name; drop `SignInWithApiAsync`, `_antiforgeryField` and `GetAntiforgeryTokenAsync`.

### 3. `Program.cs`

- Cookie options, all three in the same change:
  - `LoginPath = "/ui/account/login"`;
  - `ReturnUrlParameter = "returnUrl"` (the UI reads that exact spelling; the framework default is `ReturnUrl`, which the login page would ignore);
  - `AccessDeniedPath = "/ui/"`. Not the login page: it is for guests only and would send a signed-in user straight back, in a loop.
- Routes: delete the four `MapControllerRoute` calls and add `app.MapControllers()`. Those calls are what maps the attribute-routed `/api/v1` controllers today; `MapPortalApiAndUi` maps only the two fallbacks. Give `InfoController` `[Route("Info/[action]")]` and `AuthenticateController` `[Route("Authenticate/[action]")]`. (With no controller edits at all: keep one `MapControllerRoute("default", "{controller}/{action}")`.)
- Remove: `AddMvc()` (keep `AddControllers()`; if `LegacyServiceEndpointsTests` then fail, use `AddControllersWithViews()`), `.AddAssets()`, `UseWebOptimizer()`, `UseExceptionHandler("/Home/Error")` (it points at a controller that never existed), the `FormOptions.ValueCountLimit` block (it was for the Razor security form) and the comment about inline scripts in views above the Content-Security-Policy header.
- Update the two comments that mention `AddMvc` in `Extensions/PortalApiDependencyInjection.cs`.

### 4. Redirects

One small file (for example `Extensions/LegacyRedirects.cs`), GET and HEAD only, no sign-in needed. Old POST addresses may answer 404.

Required:

| Old address | New address | Why |
|---|---|---|
| `/Account/AppEmailConfirmed` | `/ui/account/email-confirmed` | Every released API server sends end users here after they confirm their email (`AccountAPI.cs`). Permanent; keep it for good |
| `/Account/ResetPassword?userId=&code=` | `/ui/account/reset-password` with the query string passed through as it is | Reset mails already sent (links live one day). The new page reads `code` and ignores `userId` |
| `/Account/ForgotPassword` | `/ui/account/forgot-password` | The link in old 'Password changed' mails never expires |
| `/Account/Login` | `/ui/account/login`, without the query string | Old links; an old `returnUrl` points at a Razor page |
| `/` | `/ui/` | Today `/` is the Razor home through the default route. Temporary redirect recommended (section 4) |

For bookmarks (optional, permanent):

| Old address | New address |
|---|---|
| `/Account/Register` | `/ui/account/register` |
| `/Account/ForgotPasswordConfirmation` | `/ui/account/forgot-password` |
| `/Account/ResetPasswordConfirmation` | `/ui/account/login` |
| `/Manage/ChangePassword` | `/ui/account/password` |
| `/Applications`, `/Applications/Index` | `/ui/apps` |
| `/Applications/Create`, `/Applications/Import` | `/ui/apps/new`, `/ui/apps/import` |
| `/Admin/Servers`, `ServerCreate`, `ServerEdit`, `ServerDelete` | `/ui/admin/servers` |
| `/Admin/Users` | `/ui/admin/users` |
| `/Admin/UserApplications` | `/ui/admin/applications` |
| `/Admin/ApplicationData?AppToken=`, `/Admin/EntityData?AppToken=&EntityName=` | `/ui/admin/applications/{token}/data`, `/ui/admin/applications/{token}/data/{entity}` |
| `/Admin/Settings`, `/Admin/BackupDatabase` | `/ui/admin/settings` |
| `/Admin/AuditLog?page=` | `/ui/admin/audit-log?page=` |
| `/App/{t}/Application/Entities`, `EntityCreate` | `/ui/apps/{t}/entities` |
| `/App/{t}/Application/Edit`, `SetStatus`, `Rebuild`, `Delete` | `/ui/apps/{t}/settings` |
| `/App/{t}/Application/Security?entity=X` | `/ui/apps/{t}/security?item=X` (same value format) |
| `/App/{t}/Application/Email` | `/ui/apps/{t}/email` |
| `/App/{t}/Application/AuditLog?page=` | `/ui/apps/{t}/audit-log?page=` |
| `/App/{t}/Application/DataBrowser?entity=X` | `/ui/apps/{t}/data?entity=X` |
| `/App/{t}/Application/Clone`, `CloneProgress` | `/ui/apps/{t}/clone` |
| `/App/{t}/Collaborate/*` | `/ui/apps/{t}/sharing` |
| `/App/{t}/CustomEndpoints/Index`, `Delete`; `Create`; `Edit?id=` | `/ui/apps/{t}/endpoints`; `/endpoints/new`; `/endpoints/{id}` |
| `/App/{t}/Reports/Index`, `Delete`; `Create`; `Edit?ID=` | `/ui/apps/{t}/reports`; `/reports/new`; `/reports/{ID}/edit` |
| `/App/{t}/Import/Index` | `/ui/apps/{t}/import` |
| `/App/{t}/Ent/{e}/Entity/Properties`, `PropertyCreate`, `Edit`, `Rename`, `Delete`, and `/App/{t}/Ent/{e}/Prop/{p}/Property/*` | `/ui/apps/{t}/entities/{e}` |
| `/App/{t}/Ent/{e}/Entity/Constraints` | `/ui/apps/{t}/entities/{e}/constraints` |
| `/App/{t}/Ent/{e}/Entity/DefaultOrder` | `/ui/apps/{t}/entities/{e}/sorting` |
| `/App/{t}/Ent/{e}/Entity/Data` | `/ui/apps/{t}/data/{e}` |

Leave `/Account/LogOff` out: a GET can no longer sign anyone out.

### 5. Delete

- Controllers: `Account`, `Admin`, `Application`, `Applications`, the four `BaseWeb*`, `Collaborate`, `CustomEndpoints`, `Entity`, `Import`, `Manage`, `Property`, `Reports`. Keep `InfoController.cs` and `AuthController.cs`.
- `Views/`: all 66 files, the two dead ones included.
- Models: `AccountViewModels`, `ApplicationClone_DTO`, `EnumHelper`, `ErrorViewModel`, `HtmlRequestHelper`, `ImportSchema`, `SecurityItemInput`, `SecurityItem_DTO`. In `Apilane.Common`: `Models/Dto/DBWS_ApplicationNew_Dto.cs` (only the Razor Create used it); `DBWS_EntityProperty.Descr()` becomes dead code.
- `Extensions/AssetsDependencyInjection.cs`, `wwwroot/assets` (12 MB, `custom.js`, `security-tree.js`, `security-matrix.js` and the vendored libraries with it), `tailwind/input.css`, `build-tools/README.md` and its two lines in `.gitignore`.
- `Apilane.Portal.csproj`: the roughly 2,230 `<None Include="wwwroot\assets\vendor\...">` lines.
- NuGet packages: `LigerShark.WebOptimizer.Core`, `Microsoft.VisualStudio.Web.CodeGeneration.Design`, `Microsoft.EntityFrameworkCore.SqlServer` (no Portal code uses it even today).
- `licenses/`: `LigerShark.WebOptimizer.Core`, `Alpine.js`, `d3.txt`. Keep `Chart.js`, `GridStack` and `Tailwind`: the new UI uses them.

### 6. Tests

- Delete: the three `Mvc_...` tests in `ApiPipelineTests.cs`, `Mvc_Forgot_Password_Should_Still_Mail_A_Link_To_The_Razor_Page` (`AccountApiTests.cs`), `Razor_Clone_Should_Still_Work_...` (`ApplicationCloneApiTests.cs`).
- Change: the two `/swagger` redirect tests in `ApiContractTests.cs` expect `/ui/account/login` and `returnUrl=`; the `SessionApiTests.cs` test that signs out with `GET /Account/LogOff` uses `DELETE /api/v1/session`, and the assert on `GET /Account/Login` there goes; the `/assets/custom.js` row in `ApiPipelineTests.cs` needs another static file.
- Add: one theory with a row per redirect, `/` included.
- `openapi/portal-v1.json` must come out unchanged (`ApiContractTests`).

### 7. UI and docs

- UI: reword the comments in `src/router.ts` (`afterSignIn`) and `src/pages/account/LoginPage.vue` that mention classic pages (the full page load stays, for `/swagger`), and the comment in `vite.config.ts`.
- `README.md` of this folder: the sentences that say the Razor pages are still there (the paragraph under the title, the dev server paragraph, the last convention). Then delete or archive this file.
- `AGENTS.md`: 'ASP.NET MVC Portal' in the overview, and the Razor bullet of 'Portal API and UI'.
- `docs/`: retake the screenshots under `docs/docs/assets`, check the button names in `docs/docs/getting_started.md`, add the two deployment notes of section 5, fix the security labels in `developer_guide/security.md`. A CHANGELOG entry that points to section 3.
- Optional: change the API server's default landing address (`AccountAPI.cs`) to `/ui/account/email-confirmed`; the redirect stays anyway for servers already released.

### 8. Check afterwards

- `dotnet build`, `dotnet test tests/Apilane.Portal.Tests`, `npm run build`. A search of `src` finds no `.cshtml`, `WebOptimizer` or `/Account/` outside the redirects.
- With a real API server: its `/health/readiness` is healthy (it calls the Portal's `/health/liveness`); create an application; open its data browser (this exercises `/Info/UserOwnsApplication`); register an application user and follow the confirmation mail to `/ui/account/email-confirmed`.
- `curl` each old address without a cookie: the expected `Location`.
- `/swagger` signed out: the login page, then back on `/swagger`. `/Authenticate/InRole?role=...`: 200 as an administrator, 401 otherwise.
- A password-reset mail, from request to new password.
- The Docker image builds, `/ui/` loads, `/assets/...` answers 404, the favicon shows.
- One pass over the route list of section 2 on desktop and at phone width.
