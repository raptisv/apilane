using System.Linq;

namespace Apilane.Net.Models.Account
{
    public interface IApiUser
    {
        long Created { get; set; }
        string Email { get; set; }
        bool EmailConfirmed { get; set; }
        long ID { get; set; }
        long? LastLogin { get; set; }
        string Password { get; set; }
        string Roles { get; set; }
        string Username { get; set; }

        bool IsInRole(string role);
    }

    public class ApiUser : IApiUser
    {
        public long ID { get; set; }
        public long Created { get; set; }
        public string Email { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public bool EmailConfirmed { get; set; }
        public long? LastLogin { get; set; }
        public string Roles { get; set; } = null!;

        public bool IsInRole(string role)
        {
            return !string.IsNullOrWhiteSpace(Roles)
                && Roles.Split(',').Contains(role);
        }
    }

}
