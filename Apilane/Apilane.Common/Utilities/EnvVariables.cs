using Apilane.Common.Enums;
using System;

namespace Apilane.Common.Utilities
{
    public static class EnvVariables
    {
        public static HostingEnvironment GetEnvironment(
            string envVariable, 
            HostingEnvironment defaultValue = HostingEnvironment.Development)
        {
            var value = Environment.GetEnvironmentVariable(envVariable);

            return Enum.TryParse(value, out HostingEnvironment result) 
                ? result 
                : defaultValue;
        }
    }
}
