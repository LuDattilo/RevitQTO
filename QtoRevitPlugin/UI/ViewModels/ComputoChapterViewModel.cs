using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Nodo del TreeView Struttura Computo: rappresenta un ComputoChapter con
    /// figli annidati e assegnazioni dirette (non ricorsive).
    ///
    /// <para>Sprint 10 step 2: supporto OG/OS con ereditarietà implicita.
    /// <see cref="OwnSoaCategoryId"/> è il codice assegnato esplicitamente su
    /// questo nodo. <see cref="EffectiveSoaDisplay"/> mostra quello effettivo
    /// risalendo la gerarchia (con marcatore ↑ se ereditato). Il parent è
    /// iniettato via <see cref="SetParent"/> dal ComputoStructureViewModel.</para>
    /// </summary>
    public partial class ComputoChapterViewModel : ObservableObject
    {
        public ComputoChapter Model { get; }
        public ObservableCollection<ComputoChapterViewModel> Children { get; } = new ObservableCollection<ComputoChapterViewModel>();
        public int DirectAssignmentsCount { get; set; }

        [ObservableProperty] private bool _isExpanded = true;
        [ObservableProperty] private bool _isSelected;

        /// <summary>Parent viewmodel per risolvere l'eredità OG/OS. Null se root.</summary>
        public ComputoChapterViewModel? Parent { get; private set; }

        public ComputoChapterViewModel(ComputoChapter model) => Model = model;

        public void SetParent(ComputoChapterViewModel? parent) => Parent = parent;

        public string DisplayLabel
        {
            get
            {
                var baseLabel = $"{Model.Code}  {Model.Name}  ({TotalCount} voci)";
                var soa = EffectiveSoaLabel;
                return string.IsNullOrEmpty(soa) ? baseLabel : $"{baseLabel}  [{soa}]";
            }
        }

        public int TotalCount => DirectAssignmentsCount + Children.Sum(c => c.TotalCount);

        // ── OG/OS eredità implicita ────────────────────────────────────────

        /// <summary>Codice SOA assegnato esplicitamente a QUESTO nodo (null = non assegnato proprio).</summary>
        public int? OwnSoaCategoryId => Model.SoaCategoryId;

        /// <summary>
        /// ID SOA effettivo: proprio se assegnato, altrimenti quello del primo
        /// antenato che lo ha. Null se nessun antenato ha codice.
        /// </summary>
        public int? EffectiveSoaCategoryId
        {
            get
            {
                if (Model.SoaCategoryId.HasValue) return Model.SoaCategoryId;
                return Parent?.EffectiveSoaCategoryId;
            }
        }

        /// <summary>
        /// Codice SOA effettivo risolto (es. "OG 1"). Set dal ComputoStructureViewModel
        /// al reload leggendo la cache di SoaCategories. Null se non c'è codice effettivo.
        /// </summary>
        public string? EffectiveSoaCode { get; set; }

        /// <summary>
        /// Label visualizzata a fianco del nome del capitolo. Include marcatore ↑
        /// se il codice è ereditato (non proprio).
        /// </summary>
        public string EffectiveSoaLabel
        {
            get
            {
                if (string.IsNullOrEmpty(EffectiveSoaCode)) return "";
                return Model.SoaCategoryId.HasValue ? EffectiveSoaCode! : $"{EffectiveSoaCode} ↑";
            }
        }

        /// <summary>
        /// Forza il refresh di DisplayLabel — utile dopo update del SoaCategoryId
        /// perché non è una ObservableProperty ma una computed property.
        /// </summary>
        public void NotifyDisplayChanged()
        {
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(EffectiveSoaCategoryId));
            OnPropertyChanged(nameof(EffectiveSoaLabel));
            OnPropertyChanged(nameof(EffectiveSoaCode));
            foreach (var c in Children) c.NotifyDisplayChanged();
        }
    }
}
