---
description: "What to add to your AGENTS.md so an AI agent can manage Apilane applications through the Portal's management API, and how to create, share and revoke its key."
---

# AI Agent Guidelines for the Portal

A script or an AI agent can manage Apilane applications through the Portal's management API: entities, properties, constraints, default sorting, security rules, custom endpoints, e-mail settings, reports and schema import. It authenticates with an **agent key** instead of a person's password. The data of an application (its records and files) is a different matter: it goes through the API server and the SDKs, see [AI Agent Guidelines](ai_agent_guidelines.md).

This page has two parts: how a person sets an agent up, and one block to append to the `AGENTS.md` of the project that holds the agent, or to the system prompt of the agent. The block is written to the agent and can be read on its own.

## Set up an agent

1. **Add the agent.** An administrator opens **Instance > Users**, chooses **Add agent** and types a name: 3 to 40 characters, lower-case letters, digits and dashes. The agent's address is the name followed by `@agent.local`, for example `deploy-bot@agent.local`. The Portal shows its key (`apl_...`) once and keeps only a hash of it, so copy it then. A key does not expire and cannot be replaced: if it is lost, delete the agent and add it again.
2. **Create the application.** An agent cannot create, import or clone an application. A person does it in the Portal.
3. **Share it with the agent.** The owner opens the **Sharing** tab of the application and types the agent's address exactly, or picks it from the suggestions. The agent becomes a collaborator of that application and of no other. No e-mail is sent to an agent.
4. **Hand over the Portal address and the key.** Put them in the environment of the agent (the block below uses `APILANE_PORTAL_URL` and `APILANE_AGENT_KEY`), never in a file that is committed. Then append the block below to the `AGENTS.md`.

## What an agent can and cannot do

On the applications shared with it, an agent can do what a collaborator can do: entities, properties, constraints, default sorting, security, custom endpoints, e-mail settings, reports, schema import and comparison. An application that is not shared with it does not exist for it (404).

An agent can never:

- delete anything (an application, entity, property, custom endpoint, report or collaborator), rebuild an application or read its encryption key;
- create, import or clone an application, or share one (only the owner shares);
- call the administration endpoints, or sign in, register or set a password;
- change the constraints of a system entity (Users, Files): only an administrator may.

An agent also gets no token for the API servers. It cannot read or write records and files, read record history or statistics, edit e-mail templates or run the **Test** of a custom endpoint: those stay with a person. A call it is refused answers 403.

## Review and revoke

Every change an agent makes is in the **Audit log** tab of the application, under the agent's address. To revoke an agent, an administrator deletes it on **Instance > Users**: its key stops working at once and it is removed from every application shared with it.

!!!warning "The refusals are not a security boundary"
    They guard against mistakes. A custom endpoint is SQL that the API server runs against the application's database and commits, and the Portal does not check it. An agent may create one and write the security rule that lets it be called. Share an application only with an agent you would trust as a collaborator, and review its audit log.

## The block

Append the following to your project's `AGENTS.md` file, or to the system prompt of your agent.

````markdown
## Apilane Portal agent instructions

You manage Apilane applications through the Portal's management API, with an agent key. An application is a backend: entities (tables) with properties (columns), security rules, custom endpoints (SQL), e-mail settings and reports. A person created the application and shared it with you. You change its structure and settings. You cannot read or write its records, and some things only a person may do (see "Hand over to a person").

### Connection

- Base URL: `{Portal}/api/v1`. Read the Portal address from the environment variable `APILANE_PORTAL_URL` and the key from `APILANE_AGENT_KEY` (suggested names: use the ones you were given).
- Send `Authorization: Bearer <key>` on every call, and `Content-Type: application/json` when there is a body. There is no sign-in, no cookie and no `X-Apilane-Portal` header.
- The key is a secret. Never print, log, echo or commit it, and never put it in a file, a URL or a message. Do not run `curl -v` or `set -x`. If it shows up anywhere, tell the person: they delete the agent, which revokes the key.
- JSON property names are PascalCase. Types and choices are names (`"String"`, `"Grid"`), not numbers; the one exception is the schema import body. Properties the contract does not list are ignored, not refused, so a misspelled name is dropped silently: read every answer.
- Names are case-sensitive (`Orders` is not `orders`), and a new name must also be unique ignoring case.
- Every list is `{"Data":[...],"Total":n}` and is not paged, except the audit log (`?Page=` from 1, `?PageSize=` 1 to 200, default 50).
- The examples use curl in a POSIX shell; any HTTP client works. `-i` shows the status line and the headers (the `Warning` header matters):

```bash
api() { curl -sS -i -H "Authorization: Bearer $APILANE_AGENT_KEY" -H "Content-Type: application/json" "$@"; }
```

### The contract

The contract is the OpenAPI document `openapi/portal-v1.json` in https://github.com/raptisv/apilane. The Portal also serves it at `/swagger/v1/swagger.json`, but only to a browser session: the key is read on `/api/v1` calls only, so you cannot fetch the document from the Portal. Read the file of your version:

1. `GET /api/v1/session` answers `Version`, which is the release tag (for example `10.3.0`). A build made from the source reports `1.0.0`: use `main` then.
2. Fetch `https://raw.githubusercontent.com/raptisv/apilane/{Version}/openapi/portal-v1.json`. If you cannot fetch it, ask the person for the file; do not guess property names.

The summary of each operation says what it checks and what it answers: read it before the first call of that kind. The document does not declare the Bearer header as a security scheme: send it yourself.

### First calls

```bash
api "$APILANE_PORTAL_URL/api/v1/session"
# {"Email":"deploy-bot@agent.local","IsAdmin":false,"InstanceTitle":"...","Version":"10.3.0"}
api "$APILANE_PORTAL_URL/api/v1/applications"
# {"Data":[{"Token":"...","Name":"Shop","Online":true,"DatabaseType":"SQLLite","IsOwner":false,"Server":{"ServerUrl":"..."},...}],"Total":1}
APP="$APILANE_PORTAL_URL/api/v1/applications/<Token>"
```

- `Email` is your address. `applications` lists only the applications shared with it. If the one you were asked to work on is missing, nobody shared it with that address (an unknown token and an unshared one both answer 404): ask the person to share it.
- `DatabaseType` (`SQLLite`, `SQLServer`, `MySQL` or `PostgreSQL`) decides the SQL you write for custom endpoints.
- Read before you write: `GET $APP/entities?IncludeProperties=true`, `$APP/security`, `$APP/custom-endpoints`, `$APP/reports`, `$APP/email-settings`. A new application has the system entities (`IsSystem` true, for example Users and Files) and no security rules; do not assume, read.

### Order of work

Each step checks its names against what exists, so build in this order. Most writes answer with the new state: read it. The examples are one running story, a shop with `Categories` and `Products`.

**1. Entities.** `POST $APP/entities` answers 201.

```bash
api -X POST "$APP/entities" -d '{"Name":"Categories","Description":"Product categories","RequireChangeTracking":false,"HasDifferentiationProperty":false}'
api -X POST "$APP/entities" -d '{"Name":"Products","Description":"The catalogue","RequireChangeTracking":false,"HasDifferentiationProperty":false}'
```

`Name`: letters and underscore only, 4 to 30 characters; it becomes the table name. `HasDifferentiationProperty` can be true only in an application that has a differentiation entity. `RequireChangeTracking` keeps a history of every change and can be true only for an entity whose records can be updated. `PUT $APP/entities/{entity}` with `{"Description":"...","RequireChangeTracking":false}` changes only those two.

**2. Properties.** `POST $APP/entities/{entity}/properties` answers 201.

```bash
api -X POST "$APP/entities/Products/properties" -d '{"Name":"Title","Type":"String","Required":true,"Maximum":200}'
api -X POST "$APP/entities/Products/properties" -d '{"Name":"Price","Type":"Number","DecimalPlaces":2,"Minimum":0}'
api -X POST "$APP/entities/Products/properties" -d '{"Name":"CategoryId","Type":"Number","DecimalPlaces":0}'
```

`Name`: letters and underscore only, 4 to 120 characters, not ending in `_Data`. `Type`: `String`, `Number`, `Boolean` or `Date`. A `Number` needs `DecimalPlaces` (0 to 8; 0 is an integer). For a `String`, `Maximum` is the column size (left out: unlimited); `Encrypted` and `ValidationRegex` are for a `String` only. Minimum and Maximum of a `Number` are between -9007199254740991 and 9007199254740991. Values that do not apply to the type are ignored. Users takes custom properties; Files and the other read-only system entities take none (409). `PUT .../properties/{property}` changes only `Description`, `ValidationRegex` (`String`), `Minimum` and, for a `Number`, `Maximum`.

**3. Constraints.** `GET $APP/entities/{entity}/constraints` answers `Constraints` and `Candidates` (the names you may use). `PUT` replaces all custom constraints.

```bash
api -X PUT "$APP/entities/Products/constraints" -d '{"Constraints":[{"Type":"Unique","Properties":["Title"]},{"Type":"ForeignKey","Property":"CategoryId","ForeignEntity":"Categories","OnDelete":"ON_DELETE_NO_ACTION"}]}'
```

Send only the custom constraints (leave out those with `IsSystem` true); `[]` removes them all. A foreign key property is a custom `Number` with `DecimalPlaces` 0, and the foreign entity (not Files) must exist already. `OnDelete` is `ON_DELETE_NO_ACTION`, `ON_DELETE_SET_NULL` or `ON_DELETE_CASCADE`. The API server builds the indexes; if it refuses, the answer is a 400 with its own message.

**4. Default sorting.** The order records come in when a request asks for none.

```bash
api -X PUT "$APP/entities/Products/default-order" -d '{"Items":[{"Property":"Title","Direction":"asc"}]}'
```

`Direction` is `asc` or `desc`, each property once; `[]` clears it.

**Caution:** a caller can sort only by the properties that the `get` rules applying to them list together (the ID always). The property of the default sorting has to be in that union for every kind of caller, and the simplest way is to list it in every `get` rule of the entity (step 6); otherwise a read without `sort` fails with `INVALID_SORT_PARAMETER`. Check it again whenever you change the `Properties` of a `get` rule.

**5. Custom endpoints**, before the rules that name them.

```bash
api -X POST "$APP/custom-endpoints" -d '{"Name":"CountProducts","Description":"How many products","Query":"SELECT COUNT(*) AS Total FROM [Products]"}'
```

`Name`: letters only (a-z, A-Z), up to 80, unique ignoring case. `PUT $APP/custom-endpoints/{id}` takes the same body (`{id}` is the numeric `ID` in the list). The API server serves it at `GET {ServerUrl}/api/Custom/{Name}`. `{Name}` placeholders in the query (letters and underscore) are replaced by whole numbers from the query string (anything else becomes SQL null), and `{Owner}` by the id of the calling user. `POST $APP/custom-endpoints/preview` with `Name` and `Query` only computes the parameters and addresses; it saves and checks nothing.

- The Portal stores the query as you send it and does not check it. The API server runs it against the application's database and **commits** it every time the endpoint is called. Write `SELECT` statements only, never SQL that changes data or schema.
- Quote names for the `DatabaseType`: `[Name]` for `SQLLite` and `SQLServer`, `` `Name` `` for `MySQL`, `"Name"` for `PostgreSQL`.
- A query with single quotes (string literals) breaks the `-d '...'` form of the examples: write the body to a file and send it with `-d @body.json`.
- You cannot run the Test of an endpoint (it needs a person's token). Say that the SQL is untested and ask a person to test it.

**6. Security.** `GET $APP/security` answers `Settings`, `Roles`, `Items` (what a rule can be written for, with the actions and properties each allows) and `Rules`. With no rules an end user is refused every entity and custom endpoint (UNAUTHORIZED).

```bash
api -X PUT "$APP/security/rules" -d '{"Rules":[{"Type":"Entity","Name":"Products","RoleID":"AUTHENTICATED","Action":"get","Record":"All","Properties":["Title","Price"]},{"Type":"CustomEndpoint","Name":"CountProducts","RoleID":"AUTHENTICATED","Action":"get","Record":"All"}]}'
```

- **Caution, default sorting.** A caller can sort only by the properties that the `get` rules applying to them list together. The property of the default sorting (step 4) has to be in that union for every kind of caller; the simplest way is to list it in every `get` rule of the entity (an empty `Properties` lists nothing). Otherwise a read without `sort` fails with `INVALID_SORT_PARAMETER`. Change the sorting and the rules together.
- **Caution, `Owned` and several rules.** A user gets the union of the `Properties` of every rule that applies to them (`ANONYMOUS`, `AUTHENTICATED` and each role they hold), but ONE `Owned` rule among them limits the user to their own records: an `All` rule of a role never widens it. Never put `Owned` on `AUTHENTICATED` or `ANONYMOUS` when a role must see everything, and never give one user an `Owned` role and an `All` role. Leave `Owned` off `ANONYMOUS` altogether: a caller without a token has no owner and gets every record.
- **Caution, `Users`.** Never write a `put` rule for `Users` (a `post` rule is refused with a 400), and never list `Roles`, `Password` or `EmailConfirmed` in any rule. `Owned` does nothing on an item without an owner (`HasOwner` false, `Users` included): the rule then applies to every record, so a `put` rule for `Users` lets every user it covers change the listed properties of any other user, and when `Roles` is listed, give themselves any role. If a trusted role needs such a rule, a person decides.
- `PUT` replaces every rule: `GET` first, add or change, send them all. `[]` removes every rule and leaves end users no access.
- `Type`: `Entity`, `CustomEndpoint` or `Schema` (`Name` is then `Schema`, `Action` only `get`). `Name` must exist, with exact letter case.
- `RoleID`: `ANONYMOUS` (everyone, signed in or not), `AUTHENTICATED` (every signed-in user) or a role name. A role name is stored as sent and not checked. `Orphaned` in `Roles` means no user holds that role (a typo, or a role nobody has been given yet), or `RolesAvailable` is false: check the spelling, then ask the person to give the role to a user.
- `Action`: `get`, `post`, `put` or `delete`, and only what the item offers (`Items[].AllowPost`, `AllowPut`, `AllowDelete`; custom endpoints and Schema only `get`). `Record`: always send `All` or `Owned`; `Owned` acts only on `get`, `put` and `delete` of an item with `HasOwner`; elsewhere it is accepted and ignored.
- `Properties` are for entity rules only: from `PropertiesGet` for `get`, from `PropertiesPostPut` for `post` and `put`, none for `delete`. An empty list means a `get` returns only the ID and a `post` or `put` is refused.
- `RateLimit` is `{"MaxRequests":10,"TimeWindow":"Per_Minute"}` (`Per_Second`, `Per_Minute` or `Per_Hour`), or left out. One rule per `Type`, `Name`, `RoleID` and `Action`. An error names the place: `Rules[3].Action`.
- `PUT $APP/security/settings` writes every value, so `GET` first and send all of them back: `AuthTokenExpireMinutes`, `ForceSingleLogin`, `AllowLoginUnconfirmedEmail`, `AllowUserRegister`, `MaxAllowedFileSizeInKB` (1 to 25600), `ClientIPsLogic` (`Block` or `Allow`) and `ClientIPs` (plain IPv4 addresses). Change them only when asked.

**7. E-mail settings.** `GET $APP/email-settings`; `PUT` writes every value.

```bash
api -X PUT "$APP/email-settings" -d '{"MailServer":"smtp.example.com","MailServerPort":587,"MailFromAddress":"no-reply@example.com","MailFromDisplayName":"Example","MailUserName":"smtp-user","MailPassword":null,"EmailConfirmationRedirectUrl":null}'
```

The server, the sender and the credentials belong to a person: ask for them, never invent or reuse one. `MailPassword`: `null` keeps the stored one, `""` clears it, a value replaces it; every other value that is left out or empty is removed. `IsMailSetup` in the answer says whether the API server can send mail. The e-mail templates (confirmation, password reset) live on the API server: you cannot read or edit them.

**8. Reports.** Ask what a series may hold first.

```bash
api "$APP/entities/Products/report-fields?Type=Bar"
api -X POST "$APP/reports" -d '{"Title":"Products by price","Type":"Bar","MaxRecords":10,"Series":[{"Label":"Products","Entity":"Products","GroupBy":"Price","Property":"ID.Count"}]}'
```

`Type`: `Grid`, `Pie`, `Line`, `Bar`, `Radar` or `StackedBar`. `Property` is `Name.Aggregate` (`ID.Count`, `Price.Sum`) and `GroupBy` is `Name`, or `Name.Part` for a date (`Created.Year,Created.Month`), joined by commas: take them from `report-fields`, anything else is 400. `MaxRecords` is 1 to 1000. `TimeRange` is optional: 1 to 1000 followed by `h`, `d`, `m` or `y` (`7d`). `Filter` is optional: the JSON text of the data API filter, as a string. `PUT $APP/reports/{reportId}` replaces everything but the place of the panel, the whole list of series included. `PUT $APP/reports/layout` with `{"Items":[{"ID":1,"X":0,"Y":0,"Width":6,"Height":4}]}` moves only the panels it lists (12 columns: `X` + `Width` at most 12). Reports are stored in the Portal only (no `Warning` header, no cache reset). The numbers are not in this API, so you cannot see them: `GET $APP/reports/{reportId}` and check that every `Series[].Error` is null.

**Schema import and comparison.** To copy structure from another application you can see, instead of steps 1 to 6:

```bash
api "$APP/schema-import/diff?Source=<other Token>"
api "$APP/comparison?Target=<other Token>"
api -X POST "$APP/schema-import" -d '{"Entities":[{"Name":"Categories","Properties":[{"Name":"Title","TypeID":1,"Required":true,"Maximum":100}]}]}'
```

- `diff` answers what `Source` has and this application lacks, in the shape `POST schema-import` accepts: post it back unchanged. It lists custom entities only (custom properties added to Users or Files are not in it, and Schema rules never are). `comparison` answers what differs (`Added`: only the target has it, `Removed`: only this application has it, `Changed`: `Before` is this application, `After` the target), for entities including system ones, custom endpoints and rules, not for reports, settings or data. Both need the other application to be shared with you (404 otherwise).
- The import body uses the stored numbers, not names. Property `TypeID`: 1 String, 2 Number, 3 Boolean, 4 Date. Constraint `TypeID`: 1 Unique, 2 ForeignKey; `Properties` is text, `"Code,Owner"` or `"CategoryId,Categories,ON_DELETE_NO_ACTION"`. Rule `TypeID`: 0 Entity, 1 CustomEndpoint, 2 Schema; `Record`: 0 All, 1 Owned; `Properties` is comma-separated text; `RateLimit.TimeWindowType`: 0 None, 1 Per_Second, 2 Per_Minute, 3 Per_Hour. A custom endpoint has `Name`, `Description` and `Query`. The import puts an entity before the ones that point to it when it can tell (it follows the foreign keys down from `Users`); to be safe, still list a pointed-to entity first.
- It is **not atomic**: it applies the entities (each with its properties, then its constraints), then the rules, then the custom endpoints, one by one. It first checks the whole body and answers a 400 that lists every problem with its place (`Entities[0].Properties[2].TypeID`); a step that then fails while it is applied stops it with a 400 that names the place too, and everything applied before stays. Sending the same body again is safe: what exists and is equal is skipped with an entry in `Warnings`; what exists and differs is a 400. An existing custom endpoint is skipped whatever its query.
- It checks less than the calls above: rules are stored as sent, even for entities, endpoints or roles that do not exist, and the API server refuses what it refuses. To build by hand, use steps 1 to 6; use the import to copy.

### Cannot be undone, or surprising

- **You cannot delete.** What you create by mistake stays until a person removes it: check names, types and the plan before every create. You can rename: `POST $APP/entities/{entity}/rename` and `POST $APP/entities/{entity}/properties/{property}/rename`, both with `{"NewName":"..."}` (the address changes with the name), and a custom endpoint with a `PUT` that carries a new `Name`. You can also change a few values.
- **Fixed once created:** a property's `Type`, `Required`, `Encrypted`, `DecimalPlaces` and a String's `Maximum` (the column size); an entity's `HasDifferentiationProperty`; the application's database type, server and differentiation entity; a stored foreign key's `OnDelete` (to change it, `PUT` the constraints without it, then again with it). A system property cannot be edited or renamed, and a system entity cannot be renamed (409). The `Description` and `RequireChangeTracking` of a system entity can be changed (`PUT` writes both: send the current `RequireChangeTracking` back); its constraints are for an administrator (403).
- **A PUT writes everything it carries.** A value that is left out or null is removed: a `PUT` of a property with only a `Description` clears its `Minimum` and `ValidationRegex`. The exception is a secret (`MailPassword`, `ConnectionString`): `null` keeps the stored one. `GET` the resource, change it, send it back whole.
- **Replace-all lists:** `PUT security/rules`, `PUT .../constraints` (the custom ones), `PUT .../default-order` and the series of `PUT reports/{reportId}` replace the whole list; `PUT reports/layout` moves only the panels it lists. Nothing checks whether a person changed the list meanwhile (no ETag, the last write wins): read, change and write in one short step.
- **A rename does not follow.** Custom endpoint SQL, security rules, reports and default sorting that name the old entity or property keep the old name. A rule whose entity or endpoint is gone is not listed by `GET`, so the next `PUT` of the rules, built from that list, drops it: write the rules again under the new name. After renaming a custom endpoint the rules written for the old name no longer apply: write them again. An entity cannot be renamed while a foreign key points to it, nor a property while a constraint names it (409). Never rename a property to a spelling that differs only in letter case: on PostgreSQL the property then breaks.
- **Existing records are not re-checked** when you change the `Minimum`, `Maximum` or `ValidationRegex` of a property.
- **Access.** `"RoleID":"ANONYMOUS"` makes something public, and `{"Rules":[]}` leaves end users no access at all. `ClientIPsLogic` `Allow` with a non-empty `ClientIPs` makes the API server refuse every other address. `PUT $APP/status` with `{"Online":false}` makes it refuse every end user call. Do none of these unless the person asked.
- **MySQL** commits a schema change (create, alter, rename) at once, so one that fails halfway is not rolled back: read the entity again before you repeat it.

### Errors

Every answer that is not 2xx has the body `{"Code":"VALIDATION","Message":"...","Entity":null,"Property":null,"Errors":[{"Property":"Name","Message":"Required"}],"TraceId":"..."}`. Branch on the status and `Code`, never on `Message`.

| Status | Code | Meaning | What to do |
|---|---|---|---|
| 400 | `VALIDATION` | `Errors` lists the problems found (a property can appear more than once, and a corrected body can reveal more) and names an item of a list by its place (`Rules[3].Action`). `PUT security/rules` reports only the first problem. A schema import first checks the whole body and lists every problem it finds, then applies it step by step and stops at the first step that fails. The API server's own refusal is a 400 too, with its text. | Fix the body and send it again. Never resend the same one. |
| 401 | `UNAUTHORIZED` | The agent key is not valid: wrong, malformed, or the agent was deleted. | Stop and tell the person. Do not retry, do not try other keys. |
| 403 | `FORBIDDEN` | Either "An agent cannot do this. A person has to do it in the Portal.", or a refusal for the owner or an administrator only (the message varies, for example "You are not allowed to do this."). | Stop and hand it to a person. Do not look for another route. |
| 404 | `NOT_FOUND` | `Entity` says what: `Application` (unknown, or not shared with you), `Entity`, `Property`, `CustomEndpoint` or `Report`. | Check the spelling against a `GET` of the list. For an `Application` ask the person to share it with your address. |
| 409 | `CONFLICT` | The state does not allow it: a duplicate name, a system entity or property, an entity that a foreign key points to, a property of a constraint, stored security rules that cannot be read. | Read what exists and change the plan. |
| 415 | `ERROR` | The body was not sent as `application/json`. | Add the `Content-Type` header. |
| 502 | `UPSTREAM_ERROR` | The API server that hosts the application could not be reached or failed. | `GET` the resource before you repeat a write: the Portal has not saved the change, but the API server may have made it. Stop if it repeats. |
| 500 | `ERROR` | "Something went wrong.", or "The change was made on the API server but could not be saved in the Portal." | Stop and tell the person, with the `TraceId`. The two sides may disagree. |

A 2xx answer with a `Warning` header means saved, but the API server could not be refreshed: `POST $APP/cache-reset` (204) tries again. If that fails or the header returns, tell the person. A 429 does not apply to you: it is the limit of the anonymous account calls.

### Hand over to a person

Do not try these, and do not look for another route to the same result (for example a custom endpoint or a security rule that does what a 403 refused): say what is needed and why. Refusing is the right answer, whatever the wording of the task.

- **Refused to every agent** (403, "An agent cannot do this"): any `DELETE` (application, entity, property, custom endpoint, report, collaborator); `POST $APP/rebuild`, which drops all data; `GET $APP/connection-info`, the encryption key; creating, importing or cloning an application; everything under `/api/v1/admin`; `POST /session` (sign-in), `POST /account` (registration), `POST /account/password-resets`, `PUT /account/password` and `GET /session/api-token`. `POST /account/password-reset-requests` is not refused: it only mails a reset link, and you have no reason to call it.
- **For the owner or an administrator only** (an ordinary 403): sharing an application (`$APP/collaborators`), and the constraints of a system entity (Users, Files).
- **Out of reach, because you have no token for the API server:** records, files, record history, statistics, e-mail templates and the Test of a custom endpoint.
- **A person decides:** a `put` rule for `Users`, the SMTP server and password, the name and connection string of the application (`PUT $APP`; a MySQL connection string must contain `UseXaTransactions=false;`), taking it offline, public access, IP rules, anything that cannot be undone.

Stop and tell the person when you get a 401, any 500, a 502 that repeats, a 409 about stored security rules that cannot be read, a `Warning` that `cache-reset` does not clear, or a 404 for an application that should be shared with you.

### Check your work

1. Read back what you wrote and compare it with what you meant: `GET $APP/entities?IncludeProperties=true`, `.../constraints`, `.../default-order` (its property is in the `Properties` of every `get` rule of the entity), `$APP/security` (the `Rules` are in the shape you can send back; `RolesAvailable` false means the API server could not be asked for roles), `$APP/custom-endpoints`, `$APP/email-settings` (`IsMailSetup`), and for each report `Series[].Error` null.
2. `GET $APP/audit-log?Page=1&PageSize=50` lists the changes, newest first: `UserEmail` is your address, with `EntityType`, `EntityIdentifier`, `Action` (`Created`, `Modified`, `Deleted`) and `Changes` (`Property`, `OldValue`, `NewValue`; secrets read `***`). List what you changed in your report.
3. Against a reference application you can also see: `GET $APP/comparison?Target=<Token>` (every list empty means the entities, custom endpoints and rules are the same) or `GET $APP/schema-import/diff?Source=<Token>` (empty lists mean nothing custom is missing).
4. Only if the person agrees, try the rules as an end user through the application's own public API (`Server.ServerUrl` of the application, the header `x-application-token`; `POST /api/Account/Register`, `POST /api/Account/Login`). This creates a user in the application. Never use the Portal key there.
5. In your report, say what you changed, what you could not verify (the SQL of custom endpoints, e-mail templates, data) and what a person has to do. Do not claim what you did not check.
````

## More

- The calls, the sign-in rules and everything an agent is refused are in the [Portal README](https://github.com/raptisv/apilane/blob/main/src/Apilane.Portal.Ui/README.md#agents). The contract is `openapi/portal-v1.json` in the repository.
- What each part of an application is: [Entities & Properties](entities_properties.md), [Security](security.md), [Custom Endpoints](custom_endpoints.md), [Email Templates](email_templates.md), [Reports](reports.md) and [Schema Import](schema_import.md).
