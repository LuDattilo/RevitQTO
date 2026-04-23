using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>Converter WPF: true → Visible, false → Collapsed. Singleton accessibile via x:Static.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new BoolToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Converter WPF: true → Collapsed, false → Visible. Utile per "nascondi se X è vero" / "mostra se X è falso".</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBoolToVisibilityConverter Instance = new InverseBoolToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converter WPF: stringa non vuota → Visible, null/empty/whitespace → Collapsed.
    /// Utile per badge o altri elementi visibili solo quando c'è una label da mostrare
    /// (es. "Fase: Demolizioni" nella MappingView phase-bound).
    /// </summary>
    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public static readonly StringNotEmptyToVisibilityConverter Instance = new StringNotEmptyToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converter WPF: int &gt; 0 → Visible, 0/negativo → Collapsed.
    /// Usato per sezioni "mostra solo se ci sono N elementi" (es. tabella anomalie
    /// visibile solo se AnomaliesCount &gt; 0).
    /// </summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public static readonly IntToVisibilityConverter Instance = new IntToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
