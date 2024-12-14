using System.Reflection;

namespace Apilane.Common.Utilities
{
    public static class Versioning
    {
        public static string GetVersion(this Assembly assebmly)
        {
            var version = assebmly.GetName().Version;
            return $"{version!.Major}.{version.Minor}.{version.Build}";
        }
    }
}
