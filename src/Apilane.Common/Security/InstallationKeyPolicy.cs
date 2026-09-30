using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Common.Security
{
    /// <summary>
    /// Sanity checks for the installation key: the shared secret with which the portal and the API
    /// authenticate each other. It gates every application's configuration (connection strings,
    /// mail credentials, encryption key), so a default or trivial value must be called out loudly.
    /// </summary>
    public static class InstallationKeyPolicy
    {
        public const int MinimumLength = 30;

        /// <summary>The placeholder used in the committed appsettings.json files.</summary>
        public const string ExamplePlaceholder = "REPLACE-WITH-A-LONG-RANDOM-SECRET";

        /// <summary>
        /// Values that have appeared in public repositories or documentation. They authenticate
        /// nothing and are treated the same as an unset key.
        /// </summary>
        public static readonly IReadOnlyCollection<string> KnownDefaults = new[]
        {
            "8dc64403-0f5b-4723-9aa7-42004841d838",
            "c1aaa1e9-d8fc-4302-a217-9fd6cf25fdd5",
            "f2aaa1e9-d8fc-4302-a217-9fd6cf25fdd5"
        };

        /// <summary>
        /// Returns a human-readable description of what is wrong with the key, or null when it
        /// looks acceptable.
        /// </summary>
        public static string? Validate(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "InstallationKey is not configured";
            }

            var trimmed = key.Trim();

            if (KnownDefaults.Any(d => d.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return "InstallationKey is a publicly known default value";
            }

            if (trimmed.StartsWith("REPLACE-WITH", StringComparison.OrdinalIgnoreCase))
            {
                return "InstallationKey is still the placeholder from appsettings.json";
            }

            if (trimmed.Length < MinimumLength)
            {
                return $"InstallationKey is shorter than {MinimumLength} characters";
            }

            return null;
        }
    }
}
