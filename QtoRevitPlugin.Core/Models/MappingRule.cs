using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    public class MappingRulesConfig
    {
        public int Version { get; set; } = 1;
        public List<MappingRule> Rules { get; set; } = new();
    }

    public class MappingRule
    {
        public string RevitCategory { get; set; } = "";
        public string DefaultParam { get; set; } = "Count";
        public List<string> AllowedParams { get; set; } = new();
        public List<string> HashParams { get; set; } = new();
        public string UnitDisplay { get; set; } = "";
        public int RoundingDecimals { get; set; } = 2;
        public bool VuotoPerPieno { get; set; }
    }
}
