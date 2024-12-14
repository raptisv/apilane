namespace Apilane.Data.Utilities
{
    public static class SqlUtilis
    {
        public static string GetString(object? val, string defaultValue = "")
        {
            return val is not null ?
                (val.ToString() ?? string.Empty).Replace("'", "''").Trim() :
                defaultValue;
        }
    }
}
