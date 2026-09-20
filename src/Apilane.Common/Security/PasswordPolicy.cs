namespace Apilane.Common.Security
{
    /// <summary>
    /// The single length rule for application user passwords, applied wherever a password is
    /// accepted: registration, Data/Put on Users, change password and reset password. The limits
    /// apply to the plaintext; the stored value is a fixed-size hash (see <see cref="PasswordHasher"/>),
    /// so the maximum exists only to bound the work done per attempt, not for storage reasons.
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinimumLength = 8;

        public const int MaximumLength = 400;

        /// <summary>
        /// Returns null when the password satisfies the policy, otherwise a message suitable for
        /// returning to the client.
        /// </summary>
        public static string? Validate(string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return "Required";
            }

            if (password.Length < MinimumLength)
            {
                return $"Minimum {MinimumLength} characters";
            }

            if (password.Length > MaximumLength)
            {
                return $"Maximum {MaximumLength} characters";
            }

            return null;
        }
    }
}
