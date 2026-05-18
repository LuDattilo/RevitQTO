using QtoRevitPlugin.Models;
using System;

namespace QtoRevitPlugin.Search
{
    public static class FavoriteSearchMatcher
    {
        public static bool Matches(FavoriteItem item, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            var q = query.Trim();
            return Contains(item.Code, q)
                   || Contains(item.ShortDesc, q)
                   || Contains(item.Description, q)
                   || Contains(item.ListName, q);
        }

        private static bool Contains(string? value, string query)
        {
            var text = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
