using Apilane.Common.Models;
using System;

namespace Apilane.Common.Extensions
{
    public static class FilterDataExtensions
    {
        /// <summary>
        /// Returns true when this filter, or any filter nested at any depth below it, targets the
        /// given property (case-insensitive). Used to reject filters on properties the caller is not
        /// allowed to read, so the check must be exhaustive: a single match anywhere in the tree is
        /// enough, regardless of how many sibling filters follow it.
        /// </summary>
        public static bool ReferencesProperty(this FilterData? filter, string? property)
        {
            if (filter is null || string.IsNullOrWhiteSpace(property))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filter.Property) &&
                filter.Property.Trim().Equals(property.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (filter.Filters is not null)
            {
                foreach (var child in filter.Filters)
                {
                    if (child.ReferencesProperty(property))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
