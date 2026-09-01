using System.Collections.Generic;

namespace QtoRevitPlugin.Computo.Preflight
{
    public enum PreflightSeverity { Error, Warning }

    public sealed class PreflightFinding
    {
        public string Code { get; set; } = "";
        public PreflightSeverity Severity { get; set; }
        public long? ElementId { get; set; }
        public string Voce { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>Esito di una classe di verifica: eseguita ("checked") o saltata ("skipped") con motivo.</summary>
    public sealed class PreflightClassResult
    {
        public string ClassName { get; set; } = "";
        public string Status { get; set; } = "checked";   // "checked" | "skipped"
        public string? SkipReason { get; set; }
        public List<PreflightFinding> Findings { get; } = new List<PreflightFinding>();

        public static PreflightClassResult Skipped(string className, string reason)
            => new PreflightClassResult { ClassName = className, Status = "skipped", SkipReason = reason };
    }

    /// <summary>Report aggregato del preflight pre-consegna.</summary>
    public sealed class PreflightReport
    {
        public List<PreflightClassResult> Classes { get; } = new List<PreflightClassResult>();

        public IEnumerable<PreflightFinding> AllFindings
        {
            get
            {
                foreach (var c in Classes)
                    foreach (var f in c.Findings)
                        yield return f;
            }
        }

        public int ErrorCount
        {
            get
            {
                var n = 0;
                foreach (var f in AllFindings) if (f.Severity == PreflightSeverity.Error) n++;
                return n;
            }
        }

        public int WarningCount
        {
            get
            {
                var n = 0;
                foreach (var f in AllFindings) if (f.Severity == PreflightSeverity.Warning) n++;
                return n;
            }
        }
    }
}
