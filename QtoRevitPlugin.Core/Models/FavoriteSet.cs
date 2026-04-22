using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    public class FavoriteSet
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<FavoriteItem> Items { get; set; } = new();
    }

    public class FavoriteItem
    {
        public string Code { get; set; } = "";
        public string ShortDesc { get; set; } = "";
        public string Unit { get; set; } = "";
        public double UnitPrice { get; set; }
    }
}
