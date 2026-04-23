using System;

namespace Apilane.Api.Filters
{
    /// <summary>
    /// Suppresses the automatic system tables migration that runs on each application's first request.
    /// Apply to actions where the application database may be in an inconsistent or intentionally empty state,
    /// such as Degenerate.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class SkipSystemTablesMigrationAttribute : Attribute
    {
    }
}
