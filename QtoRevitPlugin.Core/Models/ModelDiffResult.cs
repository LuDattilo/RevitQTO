using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    public class ModelDiffResult
    {
        public List<string> NewElements { get; set; } = new();
        public List<string> RemovedElements { get; set; } = new();
        public bool HasChanges => NewElements.Count > 0 || RemovedElements.Count > 0;
    }

    public class ModelDiffEntry
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // Added|Removed|Modified
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public bool Resolved { get; set; }
    }
}
