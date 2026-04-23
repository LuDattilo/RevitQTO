using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// <see cref="WorkflowStepStatus"/> → glyph testuale (Lucide/Unicode) usato
    /// nei chip della HomeView. Niente emoji a colori: carattere mono coerente
    /// col brand monospace slate/teal.
    ///   Locked    → "🔒"
    ///   Available → "·"
    ///   Current   → "▶"
    ///   Done      → "✔"
    /// </summary>
    public class StepStatusToGlyphConverter : IValueConverter
    {
        public static readonly StepStatusToGlyphConverter Instance = new StepStatusToGlyphConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is WorkflowStepStatus s ? s switch
            {
                WorkflowStepStatus.Done => "✔",
                WorkflowStepStatus.Current => "▶",
                WorkflowStepStatus.Available => "·",
                _ => "🔒",
            } : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// <see cref="WorkflowStepStatus"/> → colore sfondo chip (BrushResource).
    /// Per evitare dipendenza hard dal theme resolvo runtime via
    /// Application.Current.TryFindResource — così se il theme cambia i chip
    /// si adeguano automaticamente.
    /// </summary>
    public class StepStatusToBackgroundConverter : IValueConverter
    {
        public static readonly StepStatusToBackgroundConverter Instance = new StepStatusToBackgroundConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not WorkflowStepStatus s) return Brushes.Transparent;

            var resKey = s switch
            {
                WorkflowStepStatus.Done => "BrandAccentSoftBrush",
                WorkflowStepStatus.Current => "BrandAccentDeepBrush",
                WorkflowStepStatus.Available => "PanelSubBrush",
                _ => "ChromeBgBrush", // Locked
            };

            if (System.Windows.Application.Current?.TryFindResource(resKey) is Brush b)
                return b;

            // Fallback hard-coded se theme non caricato (test headless, designer)
            return s switch
            {
                WorkflowStepStatus.Done => new SolidColorBrush(Color.FromRgb(0x16, 0x75, 0x6D)),
                WorkflowStepStatus.Current => new SolidColorBrush(Color.FromRgb(0x0B, 0x4E, 0x48)),
                WorkflowStepStatus.Available => new SolidColorBrush(Color.FromRgb(0x1C, 0x19, 0x17)),
                _ => new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x10)),
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// <see cref="WorkflowStepStatus"/> → bool "abilitato" per bottone.
    /// Locked → false, tutti gli altri → true.
    /// </summary>
    public class StepStatusToEnabledConverter : IValueConverter
    {
        public static readonly StepStatusToEnabledConverter Instance = new StepStatusToEnabledConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is WorkflowStepStatus s && s != WorkflowStepStatus.Locked;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// <see cref="WorkflowStepStatus"/> → opacity (Locked = 0.4, resto = 1.0).
    /// Usato per dimmare i chip non accessibili.
    /// </summary>
    public class StepStatusToOpacityConverter : IValueConverter
    {
        public static readonly StepStatusToOpacityConverter Instance = new StepStatusToOpacityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is WorkflowStepStatus s && s == WorkflowStepStatus.Locked ? 0.4 : 1.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
