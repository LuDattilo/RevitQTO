namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Mapping configurabile tra un campo della scheda Informazioni Progetto (FieldKey)
    /// e il parametro di Revit da cui leggerlo (ParamName su ProjectInformation).
    /// Persistito nella tabella <c>RevitParamMapping</c> (schema v9) del .cme.
    /// </summary>
    public class RevitParamMapping
    {
        public int Id { get; set; }
        public int SessionId { get; set; }

        /// <summary>Chiave stabile del campo CME (vedi <see cref="ProjectInfoFieldKeys"/>).</summary>
        public string FieldKey { get; set; } = "";

        /// <summary>
        /// Nome del parametro Revit sorgente (case-sensitive come LookupParameter).
        /// null o stringa vuota = nessun mapping (solo input manuale).
        /// </summary>
        public string? ParamName { get; set; }

        /// <summary>True = parametro BuiltIn (Name/ClientName/Address/...). False = Shared.</summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>True = non sovrascrivere il campo se l'utente ha già valorizzato manualmente.
        /// Default true (comportamento conservativo).</summary>
        public bool SkipIfFilled { get; set; } = true;
    }
}
