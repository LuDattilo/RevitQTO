using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QtoRevitPlugin.AI;
using QtoRevitPlugin.Application;
using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Dialog modale per scegliere una <see cref="PriceItem"/> da assegnare
    /// come EP. Mostra i listini disponibili (attivo preselezionato), consente
    /// filtro testuale full-text su Codice/Descrizione. Restituisce
    /// <see cref="SelectedItem"/> via <see cref="Window.DialogResult"/>.
    ///
    /// Scope UI-4 v1: solo voci da listini locali. Preferiti personali
    /// saranno integrati in v2 (richiede resolver cross-source).
    /// </summary>
    public partial class PickEpDialog : Window
    {
        private readonly List<PriceListRow> _lists = new();
        private List<EpPickRow> _allItems = new();
        private string? _categoryOstCode;
        private int _targetInstanceCount;
        private Func<QuantityMode, (double totQty, double avgQty)>? _quantityProbe;
        private bool _isInitializingRadios;

        /// <summary>
        /// Voce selezionata (non-null solo se DialogResult == true).
        /// </summary>
        public EpPickRow? SelectedItem { get; private set; }

        /// <summary>
        /// Modalità quantità scelta dall'utente (default Count, override via radio).
        /// Valido solo se <c>DialogResult == true</c>.
        /// </summary>
        public QuantityMode QuantityMode { get; private set; } = QuantityMode.Count;

        public PickEpDialog()
        {
            InitializeComponent();
            Loaded += OnDialogLoaded;
        }

        /// <summary>
        /// Inietta il contesto del batch per abilitare:
        /// - Preselezione radio tramite <see cref="QuantityModeDefaults.GetDefault"/>
        /// - Anteprima totale preventivo: richiede un probe che dato il mode
        ///   scelto calcoli <c>totQty</c> sommando e <c>avgQty</c> media per
        ///   <paramref name="instanceCount"/> elementi.
        /// </summary>
        /// <param name="ostCode">Es. "OST_Walls" — language-independent.</param>
        /// <param name="instanceCount">Numero di istanze target.</param>
        /// <param name="probe">
        /// Funzione chiamata quando l'utente cambia radio: riceve il mode
        /// scelto, restituisce (sommaQuantità, quantitàMedia) su quelle istanze.
        /// Se null, la preview mostra solo il prezzo × count.
        /// </param>
        public void SetQuantityContext(
            string? ostCode,
            int instanceCount,
            Func<QuantityMode, (double totQty, double avgQty)>? probe)
        {
            _categoryOstCode = ostCode;
            _targetInstanceCount = instanceCount;
            _quantityProbe = probe;
        }

        /// <summary>
        /// Popola la banda suggerimenti AI con i top-N risultati ottenuti dal
        /// gateway. Lista vuota → banda collassata (niente rumore visivo).
        /// Chiamare prima di ShowDialog (o dopo Loaded ma prima dell'interazione utente).
        /// </summary>
        public void SetAiSuggestions(IReadOnlyList<MappingSuggestion>? suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
            {
                AiSuggestionsBorder.Visibility = Visibility.Collapsed;
                AiSuggestionsList.ItemsSource = null;
                return;
            }

            var rows = new List<AiChipRow>(suggestions.Count);
            foreach (var s in suggestions)
            {
                if (s.PriceItem == null || string.IsNullOrWhiteSpace(s.PriceItem.Code)) continue;
                var pi = s.PriceItem;
                rows.Add(new AiChipRow
                {
                    Code = pi.Code,
                    ShortLabel = string.IsNullOrWhiteSpace(pi.ShortDesc) ? pi.Description : pi.ShortDesc,
                    ScoreLabel = $"{(int)Math.Round(s.Score * 100)}%",
                });
            }

            if (rows.Count == 0)
            {
                AiSuggestionsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            AiSuggestionsList.ItemsSource = rows;
            AiSuggestionsHint.Text = $"{rows.Count} suggerimento/i — clicca un chip per selezionare la voce";
            AiSuggestionsBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Click su un chip suggerimento: cerca la voce nella lista filtrata e
        /// la seleziona. Se non la trova (es. l'utente sta guardando un altro
        /// listino) prova a switchare listino via codice.
        /// </summary>
        private void OnAiSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string code) return;
            TrySelectByCode(code);
        }

        /// <summary>
        /// Cerca la voce per Code nella lista corrente. Se presente, la seleziona
        /// e la scrolla in view. Se assente, cerca in tutti i listini disponibili
        /// e switcha al primo listino che la contiene.
        /// </summary>
        private void TrySelectByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;

            // 1. Cerca nella ListView corrente (potrebbe essere filtrata per ricerca)
            EpPickRow? hit = null;
            if (ItemsList.ItemsSource is IEnumerable<EpPickRow> current)
            {
                foreach (var r in current)
                    if (string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = r;
                        break;
                    }
            }
            if (hit != null)
            {
                // Pulisci eventuale filtro SearchBox per essere sicuri la riga
                // rimanga visibile dopo il refilter
                if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox.Text = string.Empty;
                ItemsList.SelectedItem = hit;
                ItemsList.ScrollIntoView(hit);
                return;
            }

            // 2. Cerca nel cache allItems (stesso listino, nessun filtro)
            var inAll = _allItems.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
            if (inAll != null)
            {
                SearchBox.Text = string.Empty;
                ItemsList.SelectedItem = inAll;
                ItemsList.ScrollIntoView(inAll);
                return;
            }

            // 3. Cerca in tutti i listini e switcha a quello che la contiene
            var lib = QtoApplication.Instance?.UserLibrary?.Library;
            if (lib == null) return;
            foreach (var pl in lib.GetPriceLists())
            {
                var items = lib.GetPriceItemsByList(pl.Id);
                if (items.Any(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase)))
                {
                    var listRow = _lists.FirstOrDefault(l => l.Id == pl.Id);
                    if (listRow != null)
                    {
                        ListCombo.SelectedItem = listRow;
                        // OnListChanged ricarica _allItems → poi seleziona
                        var found = _allItems.FirstOrDefault(i =>
                            string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
                        if (found != null)
                        {
                            SearchBox.Text = string.Empty;
                            ItemsList.SelectedItem = found;
                            ItemsList.ScrollIntoView(found);
                        }
                        return;
                    }
                }
            }

            // Non trovata da nessuna parte
            FooterStatus.Text = $"Voce «{code}» non trovata nei listini disponibili.";
        }

        /// <summary>
        /// Imposta sottotitolo custom (es. "3 istanze · Muro di base").
        /// </summary>
        public void SetSubtitle(string subtitle)
        {
            if (!string.IsNullOrWhiteSpace(subtitle))
                SubtitleBlock.Text = subtitle;
        }

        private void OnDialogLoaded(object? sender, RoutedEventArgs e)
        {
            LoadLists();
            InitializeQuantityRadios();
            UpdatePreview();
            SearchBox.Focus();
        }

        /// <summary>
        /// Preseleziona il radio in base al default-per-categoria (se il caller
        /// ha passato l'OstCode via <see cref="SetQuantityContext"/>).
        /// Aggiorna l'hint testuale ("default per Walls: Area").
        /// </summary>
        private void InitializeQuantityRadios()
        {
            _isInitializingRadios = true;
            try
            {
                var defaultMode = QuantityModeDefaults.GetDefault(_categoryOstCode);
                QuantityMode = defaultMode;

                RadioCount.IsChecked = defaultMode == QuantityMode.Count;
                RadioArea.IsChecked = defaultMode == QuantityMode.Area;
                RadioVolume.IsChecked = defaultMode == QuantityMode.Volume;
                RadioLength.IsChecked = defaultMode == QuantityMode.Length;

                if (!string.IsNullOrWhiteSpace(_categoryOstCode))
                {
                    var shortCat = _categoryOstCode!.StartsWith("OST_")
                        ? _categoryOstCode.Substring(4)
                        : _categoryOstCode;
                    CategoryHintBlock.Text = $"(default per {shortCat}: {QuantityModeDefaults.DisplayLabel(defaultMode)})";
                }
                else
                {
                    CategoryHintBlock.Text = string.Empty;
                }
            }
            finally
            {
                _isInitializingRadios = false;
            }
        }

        private void OnQtyModeChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingRadios) return;
            if (!(sender is RadioButton rb) || rb.IsChecked != true) return;

            QuantityMode = rb.Name switch
            {
                nameof(RadioArea) => QuantityMode.Area,
                nameof(RadioVolume) => QuantityMode.Volume,
                nameof(RadioLength) => QuantityMode.Length,
                _ => QuantityMode.Count,
            };

            UpdatePreview();
        }

        /// <summary>
        /// Aggiorna l'anteprima totale preventivo combinando il QuantityMode
        /// scelto (che pesa sul quantità) e la voce attualmente selezionata
        /// nella ListView (che fornisce il prezzo unitario).
        /// </summary>
        private void UpdatePreview()
        {
            double totQty = _targetInstanceCount; // fallback se probe assente → Count equivalente
            double avgQty = 1.0;
            if (_quantityProbe != null)
            {
                try
                {
                    var result = _quantityProbe.Invoke(QuantityMode);
                    totQty = result.totQty;
                    avgQty = result.avgQty;
                }
                catch
                {
                    // Probe errato (es. parametro non disponibile): fallback safe.
                    totQty = _targetInstanceCount;
                    avgQty = 1.0;
                }
            }

            var unit = QuantityModeDefaults.UnitAbbrev(QuantityMode);
            double unitPrice = 0.0;
            string epHint = "—";
            if (ItemsList?.SelectedItem is EpPickRow row)
            {
                unitPrice = row.UnitPrice;
                epHint = row.Code;
            }

            var amount = totQty * unitPrice;
            PreviewQuantityBlock.Text = _targetInstanceCount > 0
                ? $"{_targetInstanceCount} istanza/e · media {avgQty:0.##} {unit} · tot {totQty:0.##} {unit}"
                : "Seleziona una voce per vedere l'anteprima";
            PreviewAmountBlock.Text = $"€ {amount:N2}";
        }

        private void LoadLists()
        {
            var lib = QtoApplication.Instance?.UserLibrary?.Library;
            if (lib == null)
            {
                FooterStatus.Text = "UserLibrary non disponibile.";
                return;
            }

            _lists.Clear();
            foreach (var l in lib.GetPriceLists())
                _lists.Add(new PriceListRow(l.Id, l.Name ?? "(senza nome)", l.IsActive));

            // Nessun listino → placeholder nel combo
            if (_lists.Count == 0)
            {
                FooterStatus.Text = "Nessun listino importato. Vai su «Setup → Listino» per caricarne uno.";
                ListCombo.IsEnabled = false;
                SearchBox.IsEnabled = false;
                return;
            }

            ListCombo.ItemsSource = _lists;
            ListCombo.DisplayMemberPath = nameof(PriceListRow.DisplayLabel);

            // Preseleziona il primo attivo, o il primo in assoluto
            var active = _lists.FirstOrDefault(x => x.IsActive) ?? _lists[0];
            ListCombo.SelectedItem = active;
        }

        private void OnListChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListCombo.SelectedItem is not PriceListRow row) return;
            var lib = QtoApplication.Instance?.UserLibrary?.Library;
            if (lib == null) return;

            var items = lib.GetPriceItemsByList(row.Id);
            _allItems = items.Select(EpPickRow.FromModel).ToList();
            ApplyFilter();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var q = (SearchBox.Text ?? string.Empty).Trim();
            IEnumerable<EpPickRow> src = _allItems;
            if (q.Length > 0)
            {
                // Match banale su Code + ShortDescription (case-insensitive).
                // Upgrade futuro: usa HybridSearchScopeResolver con FTS.
                src = _allItems.Where(i =>
                    (i.Code?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (i.ShortDescription?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            var filtered = src.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ToList();
            ItemsList.ItemsSource = filtered;
            FooterStatus.Text = $"{filtered.Count} voce/i";

            // OK abilitato solo se c'è selezione
            OkButton.IsEnabled = filtered.Count > 0 && ItemsList.SelectedItem is EpPickRow;
            UpdatePreview();
        }

        private void OnItemsListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OkButton.IsEnabled = ItemsList.SelectedItem is EpPickRow;
            UpdatePreview();
        }

        private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ItemsList.SelectedItem is EpPickRow) OnOkClick(sender, new RoutedEventArgs());
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (ItemsList.SelectedItem is EpPickRow row)
            {
                SelectedItem = row;
                DialogResult = true;
                Close();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>DTO per i chip della banda suggerimenti AI (UI-7).</summary>
    public class AiChipRow
    {
        public string Code { get; set; } = string.Empty;
        public string ShortLabel { get; set; } = string.Empty;
        public string ScoreLabel { get; set; } = string.Empty;
    }

    /// <summary>Riga listino per combo source.</summary>
    public class PriceListRow
    {
        public PriceListRow(int id, string name, bool isActive)
        {
            Id = id;
            Name = name;
            IsActive = isActive;
        }
        public int Id { get; }
        public string Name { get; }
        public bool IsActive { get; }
        public string DisplayLabel => IsActive ? $"★ {Name}" : Name;
    }

    /// <summary>Riga voce listino per ListView del dialog.</summary>
    public class EpPickRow
    {
        public int Id { get; set; }
        public int PriceListId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double UnitPrice { get; set; }
        public string UnitPriceFormatted => UnitPrice.ToString("#,##0.00 €");

        public static EpPickRow FromModel(PriceItem p) => new EpPickRow
        {
            Id = p.Id,
            PriceListId = p.PriceListId,
            Code = p.Code ?? string.Empty,
            // PriceItem espone ShortDesc; esponiamo qui come ShortDescription per
            // leggibilità UI. Fallback su Description se ShortDesc vuota.
            ShortDescription = !string.IsNullOrWhiteSpace(p.ShortDesc)
                ? p.ShortDesc
                : (p.Description ?? string.Empty),
            Description = p.Description ?? string.Empty,
            Unit = p.Unit ?? string.Empty,
            UnitPrice = p.UnitPrice,
        };
    }
}
