using Apilane.Common;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Api.Models.AppModules.Authentication
{
    public class Users
    {
        public required long ID { get; set; }
        public required string Email { get; set; } = null!;
        public required string Username { get; set; } = null!;
        public required bool EmailConfirmed { get; set; }
        public required string Password { get; set; } = null!;
        public required string? Roles { get; set; }
        public required long Created { get; set; }
        public required long LastLogin { get; set; }

        /// <summary>
        /// If the application has differentiation entity, this should have the diff property value for this user.
        /// </summary>
        public required long? DifferentiationPropertyValue { get; set; }

        public List<string> GetRoles()
        {
            return Utils.GetString(Roles).Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
    }
}
