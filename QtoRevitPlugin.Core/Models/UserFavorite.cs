using System;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Voce preferita dell'utente, salvata nella UserLibrary.db (globale).
    /// Riflette una voce di listino al momento dell'aggiunta — se l'item
    /// originale viene cancellato, il preferito resta con i dati storici.
    /// </summary>
    public class UserFavorite
    {
        public int Id { get; set; }
        public int? PriceItemId { get; set; }
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public double UnitPrice { get; set; }
        public string ListName { get; set; } = "";
        public int? ListId { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public string Note { get; set; } = "";
    }
}
