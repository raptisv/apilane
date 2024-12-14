using Apilane.Common;
using Apilane.Common.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8629 // Nullable value type may be null.

namespace Apilane.Portal.Models
{
    public static class EnumHelper<T>
    {
        public static IList<Tuple<string, string, string, bool>> GetEnumStringAndValueAndDescription(Enum value)
        {
            var enumValues = new List<T>();

            foreach (FieldInfo fi in value.GetType().GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                enumValues.Add((T)Enum.Parse(value.GetType(), fi.Name, false));
            }

            List<Tuple<string, string, string, bool>> Result = new List<Tuple<string, string, string, bool>>();

            foreach (var item in enumValues)
            {
                Result.Add(new Tuple<string, string, string, bool>(item.ToString(), GetDisplayValue(EnumProvider<T>.Parse(item.ToString())), GetDisplayDescription(EnumProvider<T>.Parse(item.ToString())), GetDisplayAutoGenerate(EnumProvider<T>.Parse(item.ToString()))));
            }

            return Result;
        }

        public static string GetDisplayDescription(T value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());

            var descriptionAttributes = fieldInfo.GetCustomAttributes(
                typeof(DisplayAttribute), false) as DisplayAttribute[];

            if (descriptionAttributes[0].ResourceType != null)
                return EnumProvider<T>.LookupResource(descriptionAttributes[0].ResourceType, descriptionAttributes[0].Description);

            if (descriptionAttributes == null) return string.Empty;
            return (descriptionAttributes.Length > 0) ? descriptionAttributes[0].Description : value.ToString();
        }

        public static bool GetDisplayAutoGenerate(T value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());

            var descriptionAttributes = fieldInfo.GetCustomAttributes(
                typeof(DisplayAttribute), false) as DisplayAttribute[];

            if (descriptionAttributes[0].ResourceType != null)
                return Utils.GetBool(EnumProvider<T>.LookupResource(descriptionAttributes[0].ResourceType, descriptionAttributes[0].AutoGenerateField.ToString()));

            if (descriptionAttributes == null) return false;
            return descriptionAttributes[0].GetAutoGenerateField().HasValue ? descriptionAttributes[0].GetAutoGenerateField().Value : false;
        }

        public static string GetDisplayValue(T value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());

            var descriptionAttributes = fieldInfo.GetCustomAttributes(
                typeof(DisplayAttribute), false) as DisplayAttribute[];

            if (descriptionAttributes[0].ResourceType != null)
                return EnumProvider<T>.LookupResource(descriptionAttributes[0].ResourceType, descriptionAttributes[0].Name);

            if (descriptionAttributes == null) return string.Empty;
            return (descriptionAttributes.Length > 0) ? descriptionAttributes[0].Name : value.ToString();
        }
    }
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8603 // Possible null reference return.
#pragma warning restore CS8629 // Nullable value type may be null.