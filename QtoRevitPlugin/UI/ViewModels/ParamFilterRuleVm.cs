using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Services;
using System;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// VM di una regola parametrica composta. Osservabile per reagire ai cambiamenti
    /// dei campi con debounce nella ricerca. Converte l'enum ParamOperator in una
    /// label leggibile (contiene / = / ≠ / &gt; / &lt; / ≥ / ≤) per la ComboBox UI.
    /// </summary>
    public partial class ParamFilterRuleVm : ObservableObject
    {
        [ObservableProperty] private string _parameterName = "";
        [ObservableProperty] private ParamOperator _operator = ParamOperator.Contains;
        [ObservableProperty] private string _value = "";

        public static string[] OperatorLabels { get; } =
        {
            "contiene",
            "=",
            "\u2260",   // ≠
            ">",
            "<",
            "\u2265",   // ≥
            "\u2264"    // ≤
        };

        /// <summary>Binding bidirezionale per la ComboBox operatore nel XAML.</summary>
        public string OperatorLabel
        {
            get => OperatorLabels[(int)Operator];
            set
            {
                var idx = Array.IndexOf(OperatorLabels, value);
                if (idx >= 0) Operator = (ParamOperator)idx;
            }
        }

        partial void OnOperatorChanged(ParamOperator value) => OnPropertyChanged(nameof(OperatorLabel));

        public ParamFilterRule ToModel() => new ParamFilterRule
        {
            ParameterName = ParameterName?.Trim() ?? "",
            Operator = Operator,
            Value = Value?.Trim() ?? ""
        };
    }
}
