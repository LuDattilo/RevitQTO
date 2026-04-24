using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Models.Computi;
using QtoRevitPlugin.Services.Computi;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    public enum CmeNavMode { Chapters, Categories, Wbs, Flat }

    /// <summary>
    /// Plan C-5: VM della Redazione CME. Vista live dei MeasurementRow (VCItem) creati
    /// dall'assegnazione (Plan C-6), organizzati per Capitoli / Categorie / WBS / lineare.
    /// Read-only in questa iterazione; editing rimandato a C-5.1.
    /// </summary>
    public partial class CmeEditorViewModel : ViewModelBase
    {
        private int _documentId;

        public ObservableCollection<CmeNavNode> NavTree { get; } = new();
        public ObservableCollection<CmeVociRow> VisibleRows { get; } = new();
        public ObservableCollection<QuadroGroupRow> PerCapitoloRows { get; } = new();
        public ObservableCollection<QuadroGroupRow> PerCategoriaRows { get; } = new();

        [ObservableProperty] private CmeNavMode _navigationMode = CmeNavMode.Chapters;
        [ObservableProperty] private CmeNavNode? _selectedNavNode;
        [ObservableProperty] private double _totaleNetto;
        [ObservableProperty] private string _statusMessage = "";

        public CmeEditorViewModel()
        {
            if (QtoApplication.Instance?.SessionManager != null)
            {
                var sm = QtoApplication.Instance.SessionManager;
                sm.SessionChanged += (_, _) => Reload();
                sm.ActiveEpChanged += (_, _) => Reload();
                // Plan C-6: refresh automatico dopo ogni assegnazione da Selezione
                sm.AssignmentsChanged += (_, _) => Reload();
            }
            Reload();
        }

        partial void OnNavigationModeChanged(CmeNavMode value)
        {
            BuildNavTree();
            RefreshVisibleRows();
        }

        partial void OnSelectedNavNodeChanged(CmeNavNode? value) => RefreshVisibleRows();

        [RelayCommand]
        private void Reload()
        {
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            var sess = QtoApplication.Instance?.SessionManager?.ActiveSession;
            if (repo == null || sess == null) { _documentId = 0; StatusMessage = "Nessuna sessione."; return; }

            try
            {
                var docSvc = new ComputoDocumentService(repo);
                var doc = docSvc.GetOrCreate(sess.Id);
                _documentId = doc.Id;
                BuildNavTree();
                RefreshVisibleRows();
                StatusMessage = $"Documento #{_documentId} · {VisibleRows.Count} voci · € {TotaleNetto:N2}";
            }
            catch (System.Exception ex) { StatusMessage = $"Errore: {ex.Message}"; }
        }

        private void BuildNavTree()
        {
            NavTree.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (repo == null || _documentId == 0) return;

            switch (NavigationMode)
            {
                case CmeNavMode.Chapters:
                    {
                        var all = new ChapterService(repo).GetAll(_documentId);
                        var lookup = all.ToDictionary(n => n.Id, n => new CmeNavNode
                        {
                            Kind = "Chapter", RefId = n.Id,
                            Label = $"{n.Codice} · {n.DesSintetica}",
                            Level = n.Level
                        });
                        foreach (var node in all)
                        {
                            var vm = lookup[node.Id];
                            if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                                parent.Children.Add(vm);
                            else
                                NavTree.Add(vm);
                        }
                        break;
                    }
                case CmeNavMode.Categories:
                    {
                        var all = new CategoryService(repo).GetAll(_documentId);
                        var lookup = all.ToDictionary(n => n.Id, n => new CmeNavNode
                        {
                            Kind = "Category", RefId = n.Id,
                            Label = $"{n.Codice} · {n.DesSintetica}",
                            Level = n.Level
                        });
                        foreach (var node in all)
                        {
                            var vm = lookup[node.Id];
                            if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                                parent.Children.Add(vm);
                            else
                                NavTree.Add(vm);
                        }
                        break;
                    }
                case CmeNavMode.Wbs:
                    {
                        var all = new WbsService(repo).GetAll(_documentId, "WbsComputo");
                        var lookup = all.ToDictionary(n => n.Id, n => new CmeNavNode
                        {
                            Kind = "Wbs", RefId = n.Id,
                            Label = $"{n.Codice} · {n.DesSintetica}",
                            Level = n.Level.ToString()
                        });
                        foreach (var node in all)
                        {
                            var vm = lookup[node.Id];
                            if (node.ParentId.HasValue && lookup.TryGetValue(node.ParentId.Value, out var parent))
                                parent.Children.Add(vm);
                            else
                                NavTree.Add(vm);
                        }
                        break;
                    }
                case CmeNavMode.Flat:
                    // Nessun albero: tabella mostra tutte le righe
                    break;
            }
        }

        private void RefreshVisibleRows()
        {
            VisibleRows.Clear();
            var repo = QtoApplication.Instance?.SessionManager?.Repository;
            if (repo == null || _documentId == 0)
            {
                TotaleNetto = 0;
                return;
            }

            var msvc = new MeasurementService(repo);
            var rows = msvc.GetRows(_documentId);

            var piIds = rows.Select(r => r.PriceItemId).Distinct().ToList();
            var priceItems = piIds.Count > 0
                ? repo.GetPriceItems(piIds).ToDictionary(p => p.Id)
                : new Dictionary<int, PriceItem>();

            foreach (var row in rows)
            {
                if (!priceItems.TryGetValue(row.PriceItemId, out var pi)) continue;

                // Filtro per nodo navigatore selezionato
                if (SelectedNavNode != null && NavigationMode != CmeNavMode.Flat)
                {
                    bool match = NavigationMode switch
                    {
                        CmeNavMode.Chapters   => pi.SpCapId == SelectedNavNode.RefId
                                              || pi.CapId == SelectedNavNode.RefId
                                              || pi.SbCapId == SelectedNavNode.RefId,
                        CmeNavMode.Categories => row.SpCatId == SelectedNavNode.RefId
                                              || row.CatId == SelectedNavNode.RefId
                                              || row.SbCatId == SelectedNavNode.RefId,
                        CmeNavMode.Wbs        => row.WbsComputoNodeId == SelectedNavNode.RefId,
                        _ => true
                    };
                    if (!match) continue;
                }

                var vmRow = new CmeVociRow
                {
                    RowId = row.Id,
                    Code = pi.Code,
                    DesRidotta = !string.IsNullOrEmpty(pi.ShortDesc) ? pi.ShortDesc : pi.Description,
                    Unit = pi.Unit,
                    Quantita = row.Quantita,
                    UnitPrice = pi.Prezzo1 > 0 ? pi.Prezzo1 : pi.UnitPrice,
                };
                vmRow.Importo = vmRow.Quantita * vmRow.UnitPrice;

                foreach (var sub in msvc.GetSubRows(row.Id))
                {
                    vmRow.SubRows.Add(new CmeMisuraRow
                    {
                        SubId = sub.Id,
                        Descrizione = sub.Descrizione ?? "",
                        PartiUguali = sub.PartiUguali,
                        Lunghezza = sub.Lunghezza,
                        Larghezza = sub.Larghezza,
                        HPeso = sub.HPeso,
                        Quantita = sub.Quantita
                    });
                }

                VisibleRows.Add(vmRow);
            }
            TotaleNetto = VisibleRows.Sum(r => r.Importo);
        }
    }

    /// <summary>Nodo dell'albero navigatore (Capitolo/Categoria/Wbs).</summary>
    public class CmeNavNode
    {
        public string Kind { get; set; } = "";
        public int RefId { get; set; }
        public string Label { get; set; } = "";
        public string Level { get; set; } = "";
        public ObservableCollection<CmeNavNode> Children { get; } = new();
    }

    /// <summary>Riga DataGrid tabella voci (VCItem + riassunto).</summary>
    public partial class CmeVociRow : ObservableObject
    {
        public int RowId { get; set; }
        public string Code { get; set; } = "";
        public string DesRidotta { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Quantita { get; set; }
        public double UnitPrice { get; set; }
        public double Importo { get; set; }

        public ObservableCollection<CmeMisuraRow> SubRows { get; } = new();

        public string QuantitaFormatted => Quantita.ToString("N2");
        public string UnitPriceFormatted => UnitPrice > 0 ? $"€ {UnitPrice:N2}" : "—";
        public string ImportoFormatted => Importo > 0 ? $"€ {Importo:N2}" : "—";
    }

    public class CmeMisuraRow
    {
        public int SubId { get; set; }
        public string Descrizione { get; set; } = "";
        public double PartiUguali { get; set; }
        public double? Lunghezza { get; set; }
        public double? Larghezza { get; set; }
        public double? HPeso { get; set; }
        public double Quantita { get; set; }

        public string Formula
        {
            get
            {
                var parts = new List<string> { PartiUguali.ToString("0.###") };
                if (Lunghezza.HasValue) parts.Add(Lunghezza.Value.ToString("0.###"));
                if (Larghezza.HasValue) parts.Add(Larghezza.Value.ToString("0.###"));
                if (HPeso.HasValue) parts.Add(HPeso.Value.ToString("0.###"));
                return string.Join(" × ", parts) + $" = {Quantita:N3}";
            }
        }
    }

    public class QuadroGroupRow
    {
        public string Label { get; set; } = "";
        public double Totale { get; set; }
        public double PercentualeIncidenza { get; set; }
        public string TotaleFormatted => $"€ {Totale:N2}";
        public string PercentualeFormatted => $"{PercentualeIncidenza:N1}%";
    }
}
