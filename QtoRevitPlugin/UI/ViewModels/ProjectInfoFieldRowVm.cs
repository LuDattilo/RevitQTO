using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Riga editoriale di un singolo campo della scheda Informazioni Progetto
    /// (Sprint UI-6). Tiene insieme:
    /// <list type="bullet">
    ///   <item>Il <see cref="FieldKey"/> stabile (es. <c>RUP</c>).</item>
    ///   <item>Il valore corrente (<see cref="Value"/>) bound al TextBox.</item>
    ///   <item>La lista <see cref="ParamSources"/> dei parametri Revit selezionabili
    ///         (BuiltIn + Shared + voce speciale "+ Aggiungi parametro condiviso…").</item>
    ///   <item>Il parametro attualmente selezionato (<see cref="SelectedParamName"/>),
    ///         persistito come <see cref="RevitParamMapping"/> nel .cme.</item>
    /// </list>
    ///
    /// <para>Il VM radice (<see cref="ProjectInfoViewModel"/>) ascolta i cambi di
    /// <see cref="Value"/> per riflettere lo stato sul backing store originale
    /// (le 11 proprietà string) e di <see cref="SelectedParamName"/> per salvare
    /// il mapping via repo e rileggere il valore da Revit.</para>
    /// </summary>
    public partial class ProjectInfoFieldRowVm : ObservableObject
    {
        public string FieldKey { get; }
        public string Label { get; }
        public string SuggestedSpName { get; }

        /// <summary>Valore del campo (bidirezionale con TextBox).</summary>
        [ObservableProperty] private string _value = string.Empty;

        /// <summary>Lista delle sorgenti disponibili mostrate nel ComboBox.</summary>
        public List<ParamSourceOption> ParamSources { get; } = new();

        /// <summary>Opzione attualmente selezionata nel dropdown.</summary>
        [ObservableProperty] private ParamSourceOption? _selectedSource;

        /// <summary>
        /// Nome del parametro Revit mappato (null se "(manuale)"). Equivalente a
        /// <c>SelectedSource?.ParamName</c>; esposto come proprietà diretta per
        /// comodità del VM radice.
        /// </summary>
        public string? SelectedParamName => SelectedSource?.ParamName;

        public ProjectInfoFieldRowVm(string fieldKey)
        {
            FieldKey = fieldKey;
            Label = ProjectInfoFieldKeys.DisplayNameFor(fieldKey);
            SuggestedSpName = ProjectInfoFieldKeys.SuggestedSharedParamNameFor(fieldKey);
        }

        partial void OnSelectedSourceChanged(ParamSourceOption? value)
        {
            // Notifica il VM radice (via evento) che l'utente ha cambiato sorgente:
            // il VM radice deciderà se persistere il mapping e rileggere il valore.
            OnPropertyChanged(nameof(SelectedParamName));
            SourceChanged?.Invoke(this, value);
        }

        /// <summary>
        /// Evento "sorgente cambiata" sottoscritto dal VM radice. Passa <c>this</c>
        /// e la nuova opzione (può essere null se reset a manuale).
        /// </summary>
        public event EventHandler<ParamSourceOption?>? SourceChanged;
    }

    /// <summary>
    /// Una voce del dropdown "sorgente" di un <see cref="ProjectInfoFieldRowVm"/>.
    /// Tre tipi:
    /// <list type="bullet">
    ///   <item><see cref="Kind.Manual"/> — "(manuale)", no mapping, TextBox libero.</item>
    ///   <item><see cref="Kind.Param"/> — parametro esistente (BuiltIn o Shared).</item>
    ///   <item><see cref="Kind.AddShared"/> — voce speciale che apre il dialog di
    ///         creazione SP quando selezionata.</item>
    /// </list>
    /// </summary>
    public class ParamSourceOption
    {
        public enum SourceKind { Manual, Param, AddShared }

        public SourceKind Kind { get; }
        public string ParamName { get; }
        public string DisplayName { get; }
        public bool IsBuiltIn { get; }
        /// <summary>Valore corrente letto da Revit al momento dell'enumerazione (preview).</summary>
        public string? CurrentValue { get; }

        private ParamSourceOption(
            SourceKind kind,
            string paramName,
            string displayName,
            bool isBuiltIn,
            string? currentValue)
        {
            Kind = kind;
            ParamName = paramName;
            DisplayName = displayName;
            IsBuiltIn = isBuiltIn;
            CurrentValue = currentValue;
        }

        public static ParamSourceOption Manual() =>
            new(SourceKind.Manual, string.Empty, "(manuale)", false, null);

        public static ParamSourceOption AddShared() =>
            new(SourceKind.AddShared, string.Empty, "+ Aggiungi parametro condiviso…", false, null);

        public static ParamSourceOption Param(string paramName, string displayName, bool isBuiltIn, string? currentValue) =>
            new(SourceKind.Param, paramName, displayName, isBuiltIn, currentValue);

        public override string ToString() => DisplayName;
    }
}
