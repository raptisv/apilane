using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Apilane.Common.Utilities
{
    public static class EnumProvider<T> where T : struct, Enum
    {
        /// <summary>
        /// Reads a member of the enum by its name, in any letter case.
        /// </summary>
        public static T Parse(string value)
        {
            return Enum.Parse<T>(value, ignoreCase: true);
        }

        /// <summary>
        /// The text of the member's <see cref="DisplayAttribute"/> (from its resource type, when it has one),
        /// or the member's own name when it has no such attribute.
        /// </summary>
        public static string GetDisplayValue(T value)
        {
            var name = value.ToString();

            var display = typeof(T).GetField(name)?.GetCustomAttribute<DisplayAttribute>(inherit: false);

            return display?.GetName() ?? name;
        }
    }
}
