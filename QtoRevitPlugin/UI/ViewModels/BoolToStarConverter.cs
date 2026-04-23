using System;
using System.Globalization;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Converter WPF: bool IsFavorite → "★" (true) o stringa vuota (false).
    /// Scelta UX (testo utente 2026-04-23): la stellina deve essere SOLO un indicatore
    /// affermativo — presente se la voce è nei preferiti, assente altrimenti.
    /// La variante ☆ era percepita come rumore visivo su un listino di migliaia di voci.
    /// </summary>
    public class BoolToStarConverter : IValueConverter
    {
        public static readonly BoolToStarConverter Instance = new BoolToStarConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "★" : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
