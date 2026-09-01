using System.Collections.Generic;
using System.Linq;

namespace QtoRevitPlugin.Computo.Extraction
{
    /// <summary>Uno strato di struttura composita, pronto per l'exploder: il codice prezzo del suo
    /// materiale (null se il materiale non ne ha), lo spessore, una densità opzionale per le voci a peso,
    /// e l'UM della voce di listino del codice (mq | mc | kg).</summary>
    public sealed class LayerInput
    {
        public string? Code { get; set; }
        public double WidthMm { get; set; }
        public double? Density { get; set; }
        public string Um { get; set; } = "";
        public string MaterialName { get; set; } = "";
        /// <summary>Descrizione breve di listino (PriMus DesRidotta). Null/blank ripiega sul nome materiale — mai sul codice.</summary>
        public string? Description { get; set; }
        /// <summary>Descrizione estesa di listino (PriMus DesEstesa). Null/blank ripiega su Description.</summary>
        public string? ExtendedDescription { get; set; }
    }

    /// <summary>Un contributo di misura prodotto per uno strato prezzato.</summary>
    public sealed class LayerContribution
    {
        public string Code { get; set; } = "";
        public string Um { get; set; } = "";
        public double Quantity { get; set; }
        public string MaterialName { get; set; } = "";
        public string ShortDescription { get; set; } = "";
        public string ExtendedDescription { get; set; } = "";
        /// <summary>False quando la quantità non è calcolabile (voce a peso senza densità, UM ignota). La voce è comunque emessa, marcata "da completare a mano".</summary>
        public bool Computed { get; set; } = true;
        public string Note { get; set; } = "";
    }

    public sealed class LayerExplosionResult
    {
        /// <summary>Nessuno strato porta un codice prezzo: l'elemento è fatturato intero sul percorso 1:1 esistente.</summary>
        public bool UseDirect { get; set; }
        public List<LayerContribution> Contributions { get; } = new List<LayerContribution>();
        /// <summary>Strati con materiale reale ma senza codice prezzo: candidati ad assegnazione manuale, riportati per non scartarli in silenzio.</summary>
        public List<string> UncodedMaterials { get; } = new List<string>();
    }

    /// <summary>
    /// Esplode un elemento stratificato in un contributo di misura per ogni strato prezzato (cardinalità
    /// 1:N). Portato dal modulo Computo di Pulse, con le invarianti contabili:
    ///
    ///  - REPLACE, non ADD: se anche un solo strato è prezzato, la riga whole-element su quel volume/area
    ///    NON è emessa — gli strati la partizionano (fatturare elemento E strati sovrastima il computo).
    ///  - Volume = area faccia × spessore (metri). MAI GetMaterialArea × spessore (superficie esposta, ~2×).
    ///  - Peso = area faccia × spessore × densità; densità mancante ⇒ contributo FLAGGATO, non bloccato.
    ///  - Uno strato membrana (spessore 0) è escluso da volume/peso, tenuto per le voci ad area.
    ///  - Il computo è MULTI-UNIT: ogni contributo porta la propria UM.
    ///
    /// Puro e unit-agnostic: l'area faccia è in metri quadri, lo spessore in millimetri.
    /// </summary>
    public static class LayerComputoExploder
    {
        private const double MmToM = 0.001;

        public static LayerExplosionResult Explode(double faceAreaM2, IReadOnlyList<LayerInput> layers)
        {
            var result = new LayerExplosionResult();
            layers = layers ?? new List<LayerInput>();

            var anyCoded = layers.Any(l => !string.IsNullOrWhiteSpace(l.Code));
            if (!anyCoded)
            {
                result.UseDirect = true;
                foreach (var l in layers)
                    if (!string.IsNullOrWhiteSpace(l.MaterialName)) result.UncodedMaterials.Add(l.MaterialName);
                return result;
            }

            foreach (var l in layers)
            {
                if (string.IsNullOrWhiteSpace(l.Code))
                {
                    if (!string.IsNullOrWhiteSpace(l.MaterialName)) result.UncodedMaterials.Add(l.MaterialName);
                    continue;
                }

                var um = (l.Um ?? "").Trim().ToLowerInvariant();
                var widthM = l.WidthMm * MmToM;

                // Una membrana (spessore 0) non ha volume né peso da fatturare, ma un'area reale.
                if ((um == "mc" || um == "m3" || um == "kg") && l.WidthMm <= 0) continue;

                var shortDesc = !string.IsNullOrWhiteSpace(l.Description) ? l.Description!.Trim() : l.MaterialName;
                var c = new LayerContribution
                {
                    Code = l.Code!.Trim(),
                    Um = um,
                    MaterialName = l.MaterialName,
                    ShortDescription = shortDesc,
                    ExtendedDescription = !string.IsNullOrWhiteSpace(l.ExtendedDescription) ? l.ExtendedDescription!.Trim() : shortDesc,
                };
                if (um == "mq" || um == "m2")
                {
                    c.Quantity = faceAreaM2;
                }
                else if (um == "mc" || um == "m3")
                {
                    c.Quantity = faceAreaM2 * widthM;
                }
                else if (um == "kg")
                {
                    if (l.Density is double d && d > 0)
                    {
                        c.Quantity = faceAreaM2 * widthM * d;
                    }
                    else
                    {
                        c.Computed = false;
                        c.Quantity = 0;
                        c.Note = "densità mancante per la voce a peso: da completare a mano";
                    }
                }
                else
                {
                    // Una UM la cui quantità non deriva da uno strato (m, cad, a corpo): il codice appartiene
                    // allo strato ma la misura non è area/volume/peso — riportata da completare a mano
                    // invece di inventare un numero.
                    c.Computed = false;
                    c.Quantity = 0;
                    c.Note = "UM '" + (l.Um ?? "") + "' non derivabile da uno strato: da completare a mano";
                }
                result.Contributions.Add(c);
            }

            return result;
        }
    }
}
