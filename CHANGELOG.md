# Changelog

## 10.4.3 (2026-10-08)
- feat(portal): let the Claude connector register with the MCP sign-in service

## 10.4.2 (2026-10-08)
- perf(api): build applications faster by adding a table's columns in one call

## 10.4.1 (2026-10-08)
- feat(portal): preview the changes before an existing custom endpoint is saved

## 10.4.0 (2026-10-07)
- feat(portal): add OAuth-authorized MCP connections
- fix: agent rename and mail restrictions, schema import validation and SQL errors
- fix: first-admin setup, session revocation, email limits and agent sharing
- feat(portal): add granular agent permissions and access discovery
- fix: data API page size and property names, PostgreSQL id sequence, mail logging and forgot-password limit, schema import rules
- docs: check every page against the code, new Portal agent guide

## 10.3.0 (2026-10-05)
- fix: transactions on every server database, one installation key, Portal help sheets
- docs: redraw all images from the new Portal UI
- feat(branding): new favicon and app icons, redrawn overview, docs share tags

## 10.2.0 (2026-10-05)
- feat(portal)!: replace the Razor portal with a Vue UI and management API
- fix(security): apply rate limits wherever access is decided
- fix(security): revoke tokens reliably, canonical app token, safe file ids
- fix(security): bind tokens to their app
- test: run component test classes in parallel
- test: run component tests on Testcontainers instead of local servers
- fix(security): confine custom endpoint SQL on SQLite to its own database

## 10.1.1 (2026-10-01)
- fix(sdk): Apilane.Net 10.1.1
- fix(sdk): URL-encode query values in Apilane.Net requests
- fix(portal): stop /Info/GetApplication from timing out on large apps

## 10.1.0 (2026-10-01)
- **Upgrade the API and the Portal together.** They no longer accept the installation key or the portal user's token in the URL, so a 10.1.0 service and an older one cannot talk to each other.
- fix(security): keep the installation key out of URLs, git and defaults
- The key is sent and accepted only in the `x-installation-key` header, and the portal user's token only in the `Authorization` header. There is no fallback to the old `?key=`, `?installationKey=` or `?authToken=` parameters.
- fix(security): close filter/sort property authorization bypass
- fix(security): close portal XSS sinks and stop embedding app keys in pages
- fix(security): scope portal edits and deletes to the current application
- fix(security): close SQL injection through the Stats groupBy suffix
- fix(security): match string filter values literally, fix Unicode SQL literals

## 10.0.0 (2026-09-17)
- chore: upgrade to .NET 10 and migrate portal UI to Tailwind CSS v4 + Alpine.js

## 8.7.3 (2026-09-14)
- fix(import): root FK-dependency ordering at Users, not only the differentiation entity

## 8.7.2 (2026-09-14)
- fix(security): require app owner for CustomController.TestQuery

## 8.7.1 (2026-07-03)
- fix(data): quote table names in GetDataById and ExistsTable
- feat(docs): Update docs
- feat(swagger): scope Swagger to the calling application + copy-pasteable token

## 8.7.0 (2026-06-25)
- feat(reports)!: Report dashboard with resizable multi-series panels

## 8.6.0 (2026-06-23)
- feat(auth)!: add request signing (HMAC) authentication

## 8.5.3 (2026-06-20)
- feat(history): Persist last record state on historic data before record deletion + history in transaction scope
- feat(history): Prevent deletion of historic data on record deletion

## 8.5.2 (2026-06-03)
- feat(files): Introduced files cloud storage providers
- fix(licenses): Update licenses

## 8.5.1 (2026-06-02)
- fix(import): Fixed import

## 8.5.0 (2026-06-01)
- feat(refactor): Remove noaccess.db + In-memory rate limit + Orleans clustering options

## 8.4.12 (2026-03-07)
- feat(portal): Added tree and matrix to visualize security

## 8.4.11 (2026-03-05)
- feat(portal): Simplified and optimized security view

## 8.4.10 (2026-03-05)
- feat(nuget): Microsoft.Data.SqlClient 6.1.4 + Opentelemetry 1.9.0
- feat(data_api + sdk): Introduced custom ednpoints on transaction operations + Apilane.Net 8.4.10
- feat(docs): Added docs action
- feat(docs): Improved documentation

## 8.4.9 (2026-03-03)
- feat(audit): Introduced audit logs for applications and admin

## 8.4.8 (2026-03-02)
- feat(data_api + sdk): Introduced transaction operations + Apilane.Net 8.4.8

## 8.4.7 (2026-03-01)
- feat(import): Introduced import entities,properties,cusotmendpoints,security functionality + enhanced application comparison

## 8.4.6 (2025-12-12)
- fix(portal): Optimize application loading

## 8.4.5 (2025-06-24)
- fix(seo): Add robots meta tag with noindex, nofollow to prevent search engine indexing

## 8.4.4 (2025-02-06)
- feat(api): Fix string property max db length

## 8.4.3 (2025-02-06)
- feat(api): Fix entites tree level

## 8.4.2 (2024-12-22)
- feat(api): Rate limit email endpoints

## 8.4.1 (2024-12-14)
- fix(sln): Restructure solution

## 8.4.0 (2024-12-01)
- feat(api): Simplify ApplicationDataStoreFactory

## 8.3.0 (2024-11-29)
- feat(api): Set ApplicationService to observe application grain for changes with IGrainObserver

## 8.2.0 (2024-11-26)
- fix(api): Included get schema in application security

## 8.1.0 (2024-11-12)
- fix(api): Refactor clear application cache

## 8.0.1 (2024-11-11)
- fix(api): Local cache application for 5 seconds + Mark Application grain as AlwaysInterleave

## 8.0.0 (2024-10-08)
- Init