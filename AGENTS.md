# AGENTS.md — Apilane Codebase Guide

## Project Overview

Apilane is a .NET 8 backend-as-a-service platform (ASP.NET Core API + Blazor Portal).
It uses Microsoft Orleans for distributed actor state and targets SQLite, SQL Server, and MySQL.

**Solution:** `Apilane.sln`

| Project | Path | Purpose |
|---|---|---|
| `Apilane.Api` | `src/Apilane.Api/` | ASP.NET Core Web API entry point |
| `Apilane.Api.Core` | `src/Apilane.Api.Core/` | Business logic, Orleans grains, service abstractions |
| `Apilane.Common` | `src/Apilane.Common/` | Shared models, enums, extensions, utilities |
| `Apilane.Data` | `src/Apilane.Data/` | Data access layer (multi-DB) |
| `Apilane.Portal` | `src/Apilane.Portal/` | Admin portal web app |
| `Apilane.Net` | `sdk/Apilane.Net/` | .NET client SDK (NuGet package) |
| `Apilane.UnitTests` | `tests/Apilane.UnitTests/` | MSTest unit tests |
| `Apilane.Api.Component.Tests` | `tests/Apilane.Api.Component.Tests/` | xUnit component/integration tests |

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
**Component tests** use **xUnit** (`[Collection]`, `[Fact]`, `[Theory]`) + **FakeItEasy** mocks.

---

## Code Style

### Language & Framework
- **Target:** .NET 8.0, `LangVersion: latest`
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
[Collection(nameof(ApilaneApiComponentTestsCollection))]
public class DataTests : AppicationTestsBase
{
    public DataTests(SuiteContext suiteContext) : base(suiteContext) { }

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
