# AGENTS.md — Apilane Codebase Guide

## Project Overview

Apilane is a .NET 10 backend-as-a-service platform (ASP.NET Core API + ASP.NET MVC Portal).
It uses Microsoft Orleans for distributed actor state and targets SQLite, SQL Server, and MySQL.

**Solution:** `Apilane.sln`

| Project | Path | Purpose |
|---|---|---|
| `Apilane.Api` | `src/Apilane.Api/` | ASP.NET Core Web API entry point |
| `Apilane.Api.Core` | `src/Apilane.Api.Core/` | Business logic, Orleans grains, service abstractions |
| `Apilane.Common` | `src/Apilane.Common/` | Shared models, enums, extensions, utilities |
| `Apilane.Data` | `src/Apilane.Data/` | Data access layer (multi-DB) |
| `Apilane.Portal` | `src/Apilane.Portal/` | Admin portal web app |
| `Apilane.Portal.Ui` | `src/Apilane.Portal.Ui/` | Vue 3 single-page app served by the Portal under `/ui/` (not in the `.sln`; see its `README.md`) |
| `Apilane.Net` | `sdk/Apilane.Net/` | .NET client SDK (NuGet package) |
| `Apilane.UnitTests` | `tests/Apilane.UnitTests/` | MSTest unit tests |
| `Apilane.Api.Component.Tests` | `tests/Apilane.Api.Component.Tests/` | xUnit component/integration tests |
| `Apilane.Portal.Tests` | `tests/Apilane.Portal.Tests/` | xUnit + `WebApplicationFactory` tests of the Portal's `/api/v1` (throw-away SQLite, no Docker) |

---

## Build & Run Commands

```bash
# Build entire solution
dotnet build Apilane.sln

# Build a single project
dotnet build src/Apilane.Api/Apilane.Api.csproj

# Run the API locally (requires appsettings.Development.json)
dotnet run --project src/Apilane.Api

# Run the Portal locally
dotnet run --project src/Apilane.Portal

# Docker Compose (full stack)
docker-compose -p apilane up -d
```

---

## Test Commands

Two test frameworks coexist — use the appropriate filter syntax for each.

```bash
# Run all tests
dotnet test Apilane.sln

# Run only unit tests (MSTest)
dotnet test tests/Apilane.UnitTests/

# Run only component/integration tests (xUnit)
dotnet test tests/Apilane.Api.Component.Tests/

# Run only the Portal API tests (xUnit)
dotnet test tests/Apilane.Portal.Tests/

# Run a single MSTest method by name
dotnet test tests/Apilane.UnitTests/ --filter "TestMethod=IsRateLimited_Empty_Should_Work"

# Run a single xUnit test by fully-qualified name (partial match)
dotnet test tests/Apilane.Api.Component.Tests/ --filter "FullyQualifiedName~DataTests.GetByID"

# Run all tests in a class (both frameworks)
dotnet test --filter "ClassName=RateLimitTests"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

**Unit tests** use **MSTest** (`[TestClass]`, `[TestMethod]`).  
**Component tests** use **xUnit** (`[Fact]`, `[Theory]`) + **FakeItEasy** mocks.  
**Portal tests** use **xUnit** + `WebApplicationFactory` (`Infrastructure/PortalFactory.cs`): the real Portal in memory on a throw-away SQLite database. They need no Docker.

Component tests run against SQLite, SQL Server, MySQL and PostgreSQL. The three servers are started
as Docker containers by **Testcontainers** (see `Infrastructure/DatabaseContainers.cs`), on random
ports, and removed when the run ends. **Docker must be running**; nothing else has to be installed.

Component test **classes run in parallel** against one shared API host (`SuiteContext.Shared`). Each
class gets its own application token and its own databases, derived from the class name, so:

- Never share state between test classes, and never hardcode an application token or database name.
- Use the `HttpClient` / `ApilaneService` of the test base; they are created per test.
- A class that measures process memory or timings must opt out with
  `[Collection(nameof(SequentialTestsCollection))]`; those classes run alone after the parallel ones.

---

## Code Style

### Language & Framework
- **Target:** .NET 10.0 (`Apilane.Net` SDK targets `netstandard2.0` for broad consumer compatibility), `LangVersion: latest`
- **Nullable:** `enable` in every project — no `#nullable disable` suppressions
- **Serialization:** `System.Text.Json` only (no Newtonsoft.Json)
- **Logging:** Serilog structured logging (`_logger.LogInformation(...)`)

### Formatting (from `.editorconfig`)
- Indentation: **4 spaces** (no tabs), CRLF line endings
- `using` directives: **outside** the namespace declaration
- Namespaces: **block-scoped** (not file-scoped)
- Braces: always required (`csharp_prefer_braces = true`)
- Expression-bodied members: allowed for properties/accessors, not for methods/constructors

### Naming Conventions

| Symbol | Convention | Example |
|---|---|---|
| Classes, structs, enums | PascalCase | `ApplicationService`, `AppErrors` |
| Interfaces | `I` prefix + PascalCase | `IApplicationService`, `IDataAPI` |
| Public methods & properties | PascalCase | `GetAsync`, `AuthTokenExpireMinutes` |
| Private/protected fields | `_camelCase` | `_logger`, `_apiConfiguration` |
| Async methods | `Async` suffix | `GetAsync`, `ApplicationChangedAsync` |
| Legacy DB model classes | `DBWS_` prefix | `DBWS_Application`, `DBWS_Security` |
| Test methods | `MethodName_Condition_ExpectedBehavior` | `IsRateLimited_Empty_Should_Work` |
| Constants (static classes) | PascalCase | `Globals.PrimaryKeyColumn` |

### Import Organization

Group `using` directives in this order (no blank lines between groups is acceptable):

1. `Apilane.*` namespaces (project-internal)
2. `Microsoft.*` namespaces
3. Third-party namespaces (Orleans, Serilog, FakeItEasy, etc.)
4. `System.*` namespaces

```csharp
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading.Tasks;
```

---

## Error Handling

### Domain Errors — `ApilaneException`
Throw `ApilaneException` for all domain/business logic failures:

```csharp
throw new ApilaneException(
    AppErrors.ERROR,
    "Use 'Files' controller to access files.",
    property: null,
    entity: null);
```

The exception carries an `AppErrors` enum value, an optional human-readable message,
and optional `Property`/`Entity` context for validation errors.

### Null Guards
Use the null-coalescing throw pattern for required dependencies:

```csharp
var service = httpContext.RequestServices.GetService<IQueryDataService>()
    ?? throw new Exception("could not load IQueryDataService");
```

### Resource Cleanup
Use `try/finally` for `SemaphoreSlim` and other non-`IDisposable` resources:

```csharp
_semaphore.Wait();
try { /* critical section */ }
finally { _semaphore.Release(); }
```

Never use empty `catch` blocks.

---

## Architecture Patterns

### Dependency Injection
All services use constructor injection. Register services in `Program.cs` or extension
methods under `src/Apilane.Api/Extensions/`.

```csharp
public class ApplicationService : IApplicationService
{
    private readonly ILogger<ApplicationService> _logger;
    private readonly ApiConfiguration _apiConfiguration;

    public ApplicationService(
        ILogger<ApplicationService> logger,
        ApiConfiguration apiConfiguration)
    {
        _logger = logger;
        _apiConfiguration = apiConfiguration;
    }
}
```

### Interface Abstraction
Every service class must have a corresponding interface in
`src/Apilane.Api.Core/Abstractions/`. Controllers depend only on the interface.

### Orleans Grains
Stateful distributed logic lives in `src/Apilane.Api.Core/Grains/`.
Grain interfaces extend `IGrainObserver` where change notification is needed.
Use `ValueTask<T>` for hot-path grain methods; `Task<T>` elsewhere.

### Controllers
- Inherit from `BaseApplicationApiController`
- Use `[ServiceFilter]` for cross-cutting concerns (logging, auth filters)
- Document every action with XML `<summary>` comments (Swagger is generated from them)
- Return typed `ProducesResponseType` attributes for all status codes

### Extension Methods
Place extension methods in the nearest `Extensions/` directory of the relevant project.
File name matches the type being extended: `ApplicationExtensions.cs` extends `DBWS_Application`.

### Filtering — the `IN` idiom
Filters have no dedicated `IN` operator. On a **numeric** property, `contains` / `notcontains`
compile to SQL `IN (...)` / `NOT IN (...)` when the value is a comma-joined list of numbers (see
`SqlFilterDataExtensions`). `FilterOperator.contains` + `string.Join(",", ids)` is the established
Apilane "IN" idiom — used internally, e.g. in `ApplicationDataService`:

```csharp
new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.contains,
    string.Join(",", ids), PropertyType.Number); // -> WHERE [ID] IN (...)
```

In the SDK the same idiom is `new FilterItem("ID", FilterOperator.contains, string.Join(",", ids))`.
On a **string** property `contains` is a substring `LIKE`, not set membership.

---

## Portal API and UI

The Portal has a Vue 3 single-page app (`src/Apilane.Portal.Ui`, served under `/ui/`) with a screen for
every page of the Portal, on top of a JSON management API (`/api/v1`, in `src/Apilane.Portal/Api`,
contract in `openapi/portal-v1.json`). The older Razor views and MVC controllers still run next to it
until the cut-over described in `src/Apilane.Portal.Ui/MIGRATION.md`.

- API controllers inherit `PortalApiControllerBase`. Request and response shapes live only in
  `Api/V1/Contracts` (never EF models, never secrets; the one response that carries a secret is
  `connection-info`, the encryption key shown on demand). Service interfaces go in `src/Apilane.Portal/Abstractions/`.
- Writes (POST, PUT, DELETE) are rejected without the header `X-Apilane-Portal: 1`.
- New screens and endpoints go into the SPA and `/api/v1` only. Do not change or extend the MVC controllers
  and Razor views: they keep working as they are and are removed in one go, in the order `MIGRATION.md` gives.
- `InfoController` (`/Info/...`) and `AuthenticateController.InRole` are not part of that removal: API servers
  and external apps call them, so their addresses and answers must stay exactly as they are
  (`tests/Apilane.Portal.Tests/LegacyServiceEndpointsTests.cs` pins them).
- `openapi/portal-v1.json` is written by the Portal tests, never by hand. After any API change follow
  "When the API changes" in `src/Apilane.Portal.Ui/README.md` (test run with `UPDATE_OPENAPI=1`,
  `npm run api:types`, `npm run build`) and commit the JSON and the regenerated
  `src/Apilane.Portal.Ui/src/lib/api-types.ts` together.
- UI layout, commands and conventions are in `src/Apilane.Portal.Ui/README.md`. Node is needed only in that folder.
- What the SPA does differently from the Razor pages on purpose, the decisions still open and the plan for
  removing the Razor pages are in `src/Apilane.Portal.Ui/MIGRATION.md`.

---

## Testing Conventions

### Unit Tests (MSTest)
```csharp
[TestClass]
public class RateLimitTests
{
    [TestMethod]
    public void IsRateLimited_Empty_Should_Work()
    {
        // Arrange
        var list = new List<DBWS_Security.RateLimitItem?>();

        // Act
        var result = list.IsRateLimited(out _, out _);

        // Assert
        Assert.IsFalse(result);
    }
}
```

### Component Tests (xUnit)
```csharp
public class DataTests : AppicationTestsBase
{
    public DataTests() : base(SuiteContext.Shared) { }

    [Theory]
    [ClassData(typeof(StorageConfigurationTestData))]
    public async Task GetData_Should_Return_Results(DatabaseType dbType, ...)
    { ... }
}
```

- Mock external services with **FakeItEasy**: `A.Fake<IPortalInfoService>()`
- Inherit `AppicationTestsBase` for shared HTTP client, cluster client, and mock setup
- Use `IEnumerable<object[]>` test data classes for `[Theory]` / `[DataRow]` scenarios

---

## Key Constraints

- **Never suppress nullability:** do not add `!` casts or `#nullable disable` to work around warnings — fix the root cause
- **No `dynamic` types** for API request/response — use typed models or `Dictionary<string, object?>`
- **No Newtonsoft.Json** — use `System.Text.Json` throughout
- **No file-scoped namespaces** — keep block-scoped to match existing files
- **Async all the way** — do not use `.Result` or `.Wait()` on tasks; propagate `async/await`
- **Do not commit** secrets or connection strings — use `appsettings.*.json` (gitignored) or environment variables
