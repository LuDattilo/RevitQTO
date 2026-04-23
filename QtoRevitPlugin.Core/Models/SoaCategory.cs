namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Categoria SOA (Società Organismo di Attestazione) — D.Lgs. 36/2023 All. II.12.
    /// Due tipi:
    /// - <c>OG</c> Opere Generali (OG 1..13): edifici civili/industriali, strutture,
    ///   restauro, strade, dighe, acquedotti, gas, impianti sportivi, ecc.
    /// - <c>OS</c> Opere Specializzate (OS 1..35): impiantistica elettrica, idrico-sanitaria,
    ///   antincendio, lavorazioni specialistiche (scavi archeologici, consolidamenti, ecc.)
    ///
    /// <para>Un <c>ComputoChapter</c> può avere un <c>SoaCategoryId</c> (FK nullable).
    /// L'eredità è implicita: se un nodo non ha codice SOA proprio, eredita quello
    /// del primo antenato che lo ha. Risoluzione lato UI/ViewModel, no duplicazione in DB.</para>
    ///
    /// <para>La tabella è seedata al primo avvio del DB (schema v8). I record sono
    /// di sola lettura — rappresentano un elenco normativo standard.</para>
    /// </summary>
    public class SoaCategory
    {
        public int Id { get; set; }
        /// <summary>Codice normativo, es. "OG 1", "OS 28". UNIQUE.</summary>
        public string Code { get; set; } = "";
        /// <summary>Descrizione breve, es. "Edifici civili e industriali".</summary>
        public string Description { get; set; } = "";
        /// <summary>"OG" oppure "OS".</summary>
        public string Type { get; set; } = "";
        /// <summary>Ordine di visualizzazione nel dropdown (1..13 per OG, 14..48 per OS).</summary>
        public int SortOrder { get; set; }
    }
}
