using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Extraction;
using QtoRevitPlugin.Formula;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Search;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM per la <c>MappingView</c> (Sprint 4 Task 3). Espone 3 tab logicamente indipendenti:
    /// <list type="bullet">
    ///   <item><b>Tab 0 — Famiglie (Sorgente A)</b>: preview read-only delle aggregazioni FamilyType Revit
    ///   per categoria. Il salvataggio reale EP→famiglia è Sprint 5 (Tagging).</item>
    ///   <item><b>Tab 1 — Locali (Sorgente B)</b>: CRUD in-memory di <see cref="RoomMappingConfig"/>
    ///   (formule NCalc per Room/MEPSpace). Persistenza su tabella <c>RoomMappings</c> → Sprint 5.</item>
    ///   <item><b>Tab 2 — Voci manuali (Sorgente C)</b>: CRUD in-memory di voci manuali non-modellabili
    ///   (oneri sicurezza, ponteggi, noli). Persistenza su tabella <c>ManualItems</c> → Sprint 5.</item>
    /// </list>
    /// Per ora TUTTA la persistenza è in-memory (ObservableCollection): il focus è la UI + i pattern di editing.
    /// </summary>
    public partial class MappingViewModel : ViewModelBase
    {
        // =====================================================================
        // Tab switching (0 Famiglie, 1 Locali, 2 Manuali)
        // =====================================================================

        [ObservableProperty] private int _activeTabIndex;

        // =====================================================================
        // Tab 1 — Famiglie (Sorgente A)
        // =====================================================================

        /// <summary>Categorie Revit popolari (da <see cref="SelectionService.PopularCategories"/>).</summary>
        public ObservableCollection<CategoryItemVm> FamilyCategories { get; } = new();

        [ObservableProperty] private CategoryItemVm? _selectedFamilyCategory;

        /// <summary>Aggregazioni FamilyType → count per la categoria selezionata.</summary>
        public ObservableCollection<FamilyTypeRow> FamilyTypes { get; } = new();

        [ObservableProperty] private string _familyStatus = "Seleziona una categoria per vedere le famiglie.";

        // =====================================================================
        // Tab 2 — Locali (Sorgente B)
        // =====================================================================

        /// <summary>Lista in-memory delle configurazioni di mapping Room→EP. TODO Sprint 5: persistere su <c>RoomMappings</c>.</summary>
        public ObservableCollection<RoomMappingConfigVm> RoomMappings { get; } = new();

        [ObservableProperty] private RoomMappingConfigVm? _selectedRoomMapping;

        /// <summary>La form editor (card inline) è visibile solo quando non null.</summary>
        [ObservableProperty] private RoomMappingConfigVm? _editingRoomMapping;

        [ObservableProperty] private string _roomStatus = "Nessuna formula configurata.";

        /// <summary>Risultato dell'ultimo "Test formula" (visualizzato sotto la textbox formula).</summary>
        [ObservableProperty] private string _roomTestResult = string.Empty;

        // =====================================================================
        // Tab 3 — Voci manuali (Sorgente C)
        // =====================================================================

        /// <summary>Lista in-memory delle voci manuali. TODO Sprint 5: persistere su <c>ManualItems</c>.</summary>
        public ObservableCollection<ManualItemVm> ManualItems { get; } = new();

        [ObservableProperty] private ManualItemVm? _selectedManualItem;

        /// <summary>La form editor (card inline) è visibile solo quando non null.</summary>
        [ObservableProperty] private ManualItemVm? _editingManualItem;

        [ObservableProperty] private double _manualTotal;

        [ObservableProperty] private string _manualStatus = "Nessuna voce manuale inserita.";

        // =====================================================================
        // Ctor
        // =====================================================================

        public MappingViewModel()
        {
            // Popola categorie popolari (stesso set usato da SelectionView)
            foreach (var (bic, label) in SelectionService.PopularCategories)
                FamilyCategories.Add(new CategoryItemVm(bic, label));

            // Reagisci ad aggiunte/rimozioni manuali per aggiornare il totale
            ManualItems.CollectionChanged += (_, _) => RecalcManualTotal();
        }

        // =====================================================================
        // Tab 1 — Famiglie (Sorgente A)
        // =====================================================================

        partial void OnSelectedFamilyCategoryChanged(CategoryItemVm? value) => RefreshFamilyTypes();

        /// <summary>
        /// Ricarica l'aggregazione FamilyType per la categoria selezionata dal documento Revit attivo.
        /// Raggruppa per (FamilyName, TypeName) e conta istanze. Read-only: l'assegnazione EP è Sprint 5.
        /// </summary>
        public void RefreshFamilyTypes()
        {
            FamilyTypes.Clear();
            if (SelectedFamilyCategory == null)
            {
                FamilyStatus = "Seleziona una categoria per vedere le famiglie.";
                return;
            }

            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                FamilyStatus = "Nessun documento Revit attivo.";
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var instances = new FilteredElementCollector(doc)
                    .OfCategory(SelectedFamilyCategory.Bic)
                    .WhereElementIsNotElementType()
                    .ToElements();

                var groups = instances
                    .Select(el => ExtractFamilyTypeKey(el, doc))
                    .Where(k => k != null)
                    .GroupBy(k => (k!.Value.Family, k.Value.Type))
                    .Select(g => new FamilyTypeRow(
                        family: g.Key.Family,
                        type: g.Key.Type,
                        instanceCount: g.Count()))
                    .OrderBy(r => r.Family)
                    .ThenBy(r => r.Type)
                    .ToList();

                foreach (var row in groups)
                    FamilyTypes.Add(row);

                sw.Stop();
                FamilyStatus = groups.Count == 0
                    ? $"Nessuna istanza di categoria «{SelectedFamilyCategory.Label}» nel documento."
                    : $"{groups.Count} tipo(i) · {groups.Sum(g => g.InstanceCount)} istanze · categoria «{SelectedFamilyCategory.Label}» · {sw.ElapsedMilliseconds} ms";
            }
            catch (Exception ex)
            {
                FamilyStatus = $"Errore lettura famiglie: {ex.Message}";
            }
        }

        private static (string Family, string Type)? ExtractFamilyTypeKey(Element el, Document doc)
        {
            if (el is FamilyInstance fi)
            {
                var fam = fi.Symbol?.FamilyName ?? "";
                var typ = fi.Symbol?.Name ?? "";
                return (fam, typ);
            }

            // System family (Wall/Floor/Roof/...) — leggi ElementType
            var typeId = el.GetTypeId();
            if (typeId == null) return null;
#if REVIT2025_OR_LATER
            if (typeId.Value == ElementId.InvalidElementId.Value) return null;
#else
            if (typeId.IntegerValue == ElementId.InvalidElementId.IntegerValue) return null;
#endif
            if (doc.GetElement(typeId) is ElementType et)
            {
                return (et.FamilyName ?? "", et.Name ?? "");
            }
            return (el.Category?.Name ?? "", el.Name ?? "");
        }

        // =====================================================================
        // Tab 2 — Locali (Sorgente B)
        // =====================================================================

        /// <summary>
        /// Apre la form editor con un nuovo <see cref="RoomMappingConfigVm"/> vuoto — la card si renderà
        /// nella view. Il Save persiste in-memory (Sprint 5: persistenza DB).
        /// </summary>
        public void BeginAddRoomMapping()
        {
            EditingRoomMapping = new RoomMappingConfigVm
            {
                EpCode = string.Empty,
                EpDescription = string.Empty,
                Unit = string.Empty,
                Formula = string.Empty,
                TargetCategory = RoomTargetCategory.Rooms,
                RoomNameFilter = string.Empty
            };
            RoomTestResult = string.Empty;
            RoomStatus = "Nuova formula — compila i campi e salva.";
        }

        /// <summary>Apre la form editor con una copia della formula selezionata.</summary>
        public void BeginEditRoomMapping()
        {
            if (SelectedRoomMapping == null) return;
            EditingRoomMapping = SelectedRoomMapping.CloneForEdit();
            RoomTestResult = string.Empty;
            RoomStatus = $"Modifica formula «{SelectedRoomMapping.EpCode}».";
        }

        /// <summary>Annulla la modifica corrente (chiude la card senza salvare).</summary>
        public void CancelRoomMapping()
        {
            EditingRoomMapping = null;
            RoomTestResult = string.Empty;
        }

        /// <summary>
        /// Salva la config editor in <see cref="RoomMappings"/>.
        /// Se l'Id è &gt; 0 è un update (replace in-place); altrimenti add.
        /// </summary>
        public void SaveRoomMapping()
        {
            if (EditingRoomMapping == null) return;
            if (string.IsNullOrWhiteSpace(EditingRoomMapping.EpCode))
            {
                RoomStatus = "Codice EP obbligatorio.";
                return;
            }
            if (string.IsNullOrWhiteSpace(EditingRoomMapping.Formula))
            {
                RoomStatus = "Formula obbligatoria.";
                return;
            }

            // Validazione sintattica della formula (senza resolver)
            var engine = new FormulaEngine();
            if (!engine.Validate(EditingRoomMapping.Formula, out var err))
            {
                RoomStatus = $"Formula non valida: {err}";
                return;
            }

            if (EditingRoomMapping.Id == 0)
            {
                // Nuovo — assegna Id fittizio locale per distinguere nelle update
                EditingRoomMapping.Id = NextLocalId(RoomMappings.Select(r => r.Id));
                RoomMappings.Add(EditingRoomMapping);
                RoomStatus = $"Aggiunta formula «{EditingRoomMapping.EpCode}» (non ancora persistita · Sprint 5).";
            }
            else
            {
                var existing = RoomMappings.FirstOrDefault(r => r.Id == EditingRoomMapping.Id);
                if (existing != null)
                {
                    var idx = RoomMappings.IndexOf(existing);
                    RoomMappings[idx] = EditingRoomMapping;
                    RoomStatus = $"Aggiornata formula «{EditingRoomMapping.EpCode}» (non ancora persistita · Sprint 5).";
                }
            }
            EditingRoomMapping = null;
            RoomTestResult = string.Empty;
        }

        /// <summary>Elimina la formula selezionata dalla lista in-memory.</summary>
        public void DeleteRoomMapping()
        {
            if (SelectedRoomMapping == null) return;
            var removed = SelectedRoomMapping.EpCode;
            RoomMappings.Remove(SelectedRoomMapping);
            SelectedRoomMapping = null;
            RoomStatus = $"Eliminata formula «{removed}».";
        }

        /// <summary>
        /// Valuta la formula della card editor sul PRIMO Room/MEPSpace valido del documento Revit.
        /// Utile per debug rapido — mostra valore + eventuali identificatori irrisolti.
        /// </summary>
        public void TestRoomFormula()
        {
            RoomTestResult = string.Empty;

            if (EditingRoomMapping == null || string.IsNullOrWhiteSpace(EditingRoomMapping.Formula))
            {
                RoomTestResult = "Compila la formula prima di testare.";
                return;
            }

            var doc = QtoApplication.Instance?.CurrentUiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                RoomTestResult = "Nessun documento Revit attivo.";
                return;
            }

            var bic = EditingRoomMapping.TargetCategory == RoomTargetCategory.MEPSpaces
                ? BuiltInCategory.OST_MEPSpaces
                : BuiltInCategory.OST_Rooms;

            var firstSpatial = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .OfType<SpatialElement>()
                .FirstOrDefault(s => s.Area > 0);

            if (firstSpatial == null)
            {
                RoomTestResult = $"Nessun {(EditingRoomMapping.TargetCategory == RoomTargetCategory.Rooms ? "Room" : "MEP Space")} valido nel documento (Area > 0).";
                return;
            }

            try
            {
                var resolver = new RevitParameterResolver(firstSpatial);
                var engine = new FormulaEngine();
                var result = engine.Evaluate(EditingRoomMapping.Formula, resolver);

                if (!result.IsValid)
                {
                    RoomTestResult = $"Errore: {result.Error}";
                    return;
                }

                var unresolved = result.UnresolvedIds.Count > 0
                    ? $" · identificatori irrisolti: {string.Join(", ", result.UnresolvedIds)} (sostituiti con 0)"
                    : "";
                var unit = string.IsNullOrWhiteSpace(EditingRoomMapping.Unit) ? "" : $" {EditingRoomMapping.Unit}";
                RoomTestResult = $"Test su «{firstSpatial.Name}» → {result.Value:F3}{unit}{unresolved}";
            }
            catch (Exception ex)
            {
                RoomTestResult = $"Eccezione: {ex.Message}";
            }
        }

        // =====================================================================
        // Tab 3 — Voci manuali (Sorgente C)
        // =====================================================================

        public void BeginAddManualItem()
        {
            EditingManualItem = new ManualItemVm
            {
                EpCode = string.Empty,
                EpDescription = string.Empty,
                Unit = string.Empty,
                Quantity = 0,
                UnitPrice = 0,
                Notes = string.Empty
            };
            ManualStatus = "Nuova voce manuale — compila i campi e salva.";
        }

        public void BeginEditManualItem()
        {
            if (SelectedManualItem == null) return;
            EditingManualItem = SelectedManualItem.CloneForEdit();
            ManualStatus = $"Modifica voce «{SelectedManualItem.EpCode}».";
        }

        public void CancelManualItem()
        {
            EditingManualItem = null;
        }

        public void SaveManualItem()
        {
            if (EditingManualItem == null) return;
            if (string.IsNullOrWhiteSpace(EditingManualItem.EpCode))
            {
                ManualStatus = "Codice EP obbligatorio.";
                return;
            }
            if (EditingManualItem.Quantity < 0 || EditingManualItem.UnitPrice < 0)
            {
                ManualStatus = "Quantità e prezzo devono essere ≥ 0.";
                return;
            }

            // IsNew distingue creazione vs update (indipendente dall'Id che potrà
            // collidere con rowid SQLite dopo la persistenza).
            if (EditingManualItem.IsNew)
            {
                EditingManualItem.Id = NextLocalId(ManualItems.Select(m => m.Id));
                ManualItems.Add(EditingManualItem);
                // Dopo l'inserimento in-memory la voce NON è più "nuova" per futuri edit:
                // IsNew tornerà a true solo su duplica o nuovo Add esplicito.
                EditingManualItem.IsNew = false;
                ManualStatus = $"Aggiunta voce «{EditingManualItem.EpCode}» (non ancora persistita · Sprint 5).";
            }
            else
            {
                var existing = ManualItems.FirstOrDefault(m => m.Id == EditingManualItem.Id);
                if (existing != null)
                {
                    var idx = ManualItems.IndexOf(existing);
                    ManualItems[idx] = EditingManualItem;
                    ManualStatus = $"Aggiornata voce «{EditingManualItem.EpCode}».";
                }
            }
            EditingManualItem = null;
            RecalcManualTotal();
        }

        /// <summary>Duplica la voce selezionata (copia con nuovo Id in-memory).</summary>
        public void DuplicateManualItem()
        {
            if (SelectedManualItem == null) return;
            var copy = SelectedManualItem.CloneForEdit();
            copy.Id = NextLocalId(ManualItems.Select(m => m.Id));
            copy.IsNew = true;  // copia = nuova riga da persistere
            copy.EpDescription = (copy.EpDescription ?? "") + " (copia)";
            ManualItems.Add(copy);
            ManualStatus = $"Duplicata voce «{copy.EpCode}».";
        }

        public void DeleteManualItem()
        {
            if (SelectedManualItem == null) return;
            var removed = SelectedManualItem.EpCode;
            ManualItems.Remove(SelectedManualItem);
            SelectedManualItem = null;
            ManualStatus = $"Eliminata voce «{removed}».";
            RecalcManualTotal();
        }

        /// <summary>
        /// Cerca nella UserLibrary (listini globali) il primo match esatto/FTS per il codice passato
        /// e copia codice/descrizione/UM/prezzo nella voce manuale in editing.
        /// Per ora usa ricerca a singolo livello (FindByCodeExact → primo SearchFts).
        /// </summary>
        public string LookupFromUserLibrary(string query)
        {
            if (EditingManualItem == null) return "Apri prima una voce in editing.";
            if (string.IsNullOrWhiteSpace(query)) return "Digita un codice o parola chiave da cercare.";

            var repo = QtoApplication.Instance?.UserLibrary?.Library;
            if (repo == null) return "UserLibrary non disponibile.";

            try
            {
                var service = new PriceItemSearchService(repo);
                var res = service.Search(query.Trim(), maxResults: 1);
                if (res.Count == 0) return $"Nessun risultato per «{query}».";

                var item = res.Items[0];
                EditingManualItem.EpCode = item.Code ?? "";
                EditingManualItem.EpDescription = !string.IsNullOrEmpty(item.ShortDesc)
                    ? item.ShortDesc
                    : item.Description ?? "";
                EditingManualItem.Unit = item.Unit ?? "";
                EditingManualItem.UnitPrice = item.UnitPrice;
                return $"Trovato «{item.Code}» (livello {res.Level}) — campi aggiornati.";
            }
            catch (Exception ex)
            {
                return $"Errore ricerca: {ex.Message}";
            }
        }

        private void RecalcManualTotal()
        {
            ManualTotal = ManualItems.Sum(m => m.Quantity * m.UnitPrice);
            if (ManualItems.Count == 0)
            {
                ManualStatus = "Nessuna voce manuale inserita.";
            }
        }

        // Ogni cambio di voce editata dovrebbe aggiornare il totale live.
        // Usa overload con old+new per de-registrare l'handler precedente (evita memory leak
        // e doppie notifiche se lo stesso ManualItemVm viene riusato — Cancel + re-Edit).
        partial void OnEditingManualItemChanging(ManualItemVm? oldValue, ManualItemVm? newValue)
        {
            if (oldValue != null) oldValue.PropertyChanged -= OnEditingItemPropertyChanged;
            if (newValue != null) newValue.PropertyChanged += OnEditingItemPropertyChanged;
        }

        private void OnEditingItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            => RecalcManualTotal();

        private static int NextLocalId(IEnumerable<int> existing)
        {
            var max = existing.DefaultIfEmpty(0).Max();
            return max + 1;
        }
    }

    // =========================================================================
    // DTO / row VMs
    // =========================================================================

    /// <summary>Riga aggregata del DataGrid Famiglie (Tab 1).</summary>
    public class FamilyTypeRow
    {
        public FamilyTypeRow(string family, string type, int instanceCount)
        {
            Family = family;
            Type = type;
            InstanceCount = instanceCount;
            AssignedEp = "—";        // Placeholder Sprint 5 (Tagging)
            EstimatedPrice = "—";    // Placeholder Sprint 5 (cross-ref UserLibrary)
        }

        public string Family { get; }
        public string Type { get; }
        public int InstanceCount { get; }
        public string AssignedEp { get; set; }
        public string EstimatedPrice { get; set; }
    }

    /// <summary>
    /// View model osservabile per <see cref="RoomMappingConfig"/>. Wrappa il model core aggiungendo notifiche.
    /// Id = 0 → voce non ancora in lista (usato per distinguere add vs update).
    /// </summary>
    public partial class RoomMappingConfigVm : ObservableObject
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private string _epCode = string.Empty;
        [ObservableProperty] private string _epDescription = string.Empty;
        [ObservableProperty] private string _unit = string.Empty;
        [ObservableProperty] private string _formula = string.Empty;
        [ObservableProperty] private RoomTargetCategory _targetCategory = RoomTargetCategory.Rooms;
        [ObservableProperty] private string _roomNameFilter = string.Empty;

        public string TargetCategoryLabel => TargetCategory == RoomTargetCategory.MEPSpaces ? "MEP Spaces" : "Rooms";

        /// <summary>Copia profonda per l'editor (così Cancel non modifica la riga originale).</summary>
        public RoomMappingConfigVm CloneForEdit() => new()
        {
            Id = this.Id,
            EpCode = this.EpCode,
            EpDescription = this.EpDescription,
            Unit = this.Unit,
            Formula = this.Formula,
            TargetCategory = this.TargetCategory,
            RoomNameFilter = this.RoomNameFilter
        };

        /// <summary>Converte in model persistibile (Sprint 5 userà questo per InsertRoomMapping).</summary>
        public RoomMappingConfig ToModel() => new()
        {
            Id = this.Id,
            EpCode = this.EpCode,
            EpDescription = this.EpDescription,
            Unit = this.Unit,
            Formula = this.Formula,
            TargetCategory = this.TargetCategory,
            RoomNameFilter = this.RoomNameFilter
        };
    }

    /// <summary>
    /// View model osservabile per <see cref="ManualQuantityEntry"/>. Il Total è calcolato live.
    /// Id = 0 → voce non ancora in lista.
    /// </summary>
    public partial class ManualItemVm : ObservableObject
    {
        [ObservableProperty] private int _id;
        /// <summary>True se la voce è stata creata localmente e non ancora persistita su DB.
        /// Sprint 5: usare IsNew invece della convenzione Id==0 per evitare collisioni con
        /// rowid SQLite (autoincrement DB potrebbe generare Id=N già presente in-memory).</summary>
        [ObservableProperty] private bool _isNew = true;
        [ObservableProperty] private string _epCode = string.Empty;
        [ObservableProperty] private string _epDescription = string.Empty;
        [ObservableProperty] private string _unit = string.Empty;
        [ObservableProperty] private double _quantity;
        [ObservableProperty] private double _unitPrice;
        [ObservableProperty] private string _notes = string.Empty;

        public double Total => Quantity * UnitPrice;

        public string TotalFormatted => $"€ {Total:N2}";
        public string UnitPriceFormatted => $"€ {UnitPrice:N2}";

        partial void OnQuantityChanged(double value)
        {
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(TotalFormatted));
        }
        partial void OnUnitPriceChanged(double value)
        {
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(TotalFormatted));
            OnPropertyChanged(nameof(UnitPriceFormatted));
        }

        public ManualItemVm CloneForEdit() => new()
        {
            Id = this.Id,
            IsNew = this.IsNew,
            EpCode = this.EpCode,
            EpDescription = this.EpDescription,
            Unit = this.Unit,
            Quantity = this.Quantity,
            UnitPrice = this.UnitPrice,
            Notes = this.Notes
        };

        public ManualQuantityEntry ToModel() => new()
        {
            Id = this.Id,
            EpCode = this.EpCode,
            EpDescription = this.EpDescription,
            Unit = this.Unit,
            Quantity = this.Quantity,
            UnitPrice = this.UnitPrice,
            Notes = this.Notes
        };
    }
}
