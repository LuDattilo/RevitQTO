using System;
using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    public enum FavoriteScope
    {
        Project,
        Personal
    }

    public class FavoriteSet
    {
        public string Name { get; set; } = "";
        public FavoriteScope Scope { get; set; } = FavoriteScope.Personal;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<FavoriteItem> Items { get; set; } = new();
    }

    public class FavoriteItem
    {
        public string Code { get; set; } = "";
        public string ShortDesc { get; set; } = "";
        public string Unit { get; set; } = "";
        public double UnitPrice { get; set; }

        // Campi estesi (aggiunti in Fase 4) — opzionali, retrocompatibili con JSON legacy:
        // i file favorites.personal.json / favorites.project.json salvati prima non
        // avranno queste proprietà, JsonSerializer leggerà i default.

        /// <summary>Descrizione completa (multi-line) al momento dell'aggiunta.</summary>
        public string Description { get; set; } = "";

        /// <summary>Nome del listino da cui la voce è stata aggiunta (es. "Firenze 2025").</summary>
        public string ListName { get; set; } = "";

        /// <summary>Id del listino al momento dell'aggiunta. Può diventare stale se il listino viene rimosso.</summary>
        public int? ListId { get; set; }

        /// <summary>Timestamp UTC di quando l'utente ha aggiunto la voce ai preferiti.</summary>
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Nota personale libera associata al preferito.</summary>
        public string Note { get; set; } = "";
    }
}
