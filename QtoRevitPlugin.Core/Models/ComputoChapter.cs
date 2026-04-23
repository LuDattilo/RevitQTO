using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Capitolo/categoria del computo, gerarchia 3 livelli (SuperCat=1, Cat=2, SubCat=3).
    /// Definito per-sessione dall'utente, indipendente dalla gerarchia del prezzario.
    /// </summary>
    public class ComputoChapter
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int? ParentChapterId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Level { get; set; }       // 1=Super, 2=Cat, 3=Sub
        public int SortOrder { get; set; }
        /// <summary>FK verso <see cref="SoaCategory"/> (Sprint 10 step 2) — OG/OS
        /// assegnato a questo nodo. Null = eredita dal parent (risoluzione lato VM).</summary>
        public int? SoaCategoryId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
