using Apilane.Net.Models.Data;

namespace Apilane.Net.Extensions
{
    public static class ApilaneErrorExtensions
    {
        public static string BuildErrorMessage(this ApilaneError error) =>
            $"Apilane error | Code '{error.Code}' | Message '{error.Message}' | Entity '{error.Entity}' | Property '{error.Property}'";
    }
}
