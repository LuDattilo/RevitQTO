using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    public class ElementSnapshot
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int ElementId { get; set; }
        public string UniqueId { get; set; } = "";
        public string SnapshotHash { get; set; } = "";
        public double SnapshotQty { get; set; }
        public List<string> AssignedEP { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
}
