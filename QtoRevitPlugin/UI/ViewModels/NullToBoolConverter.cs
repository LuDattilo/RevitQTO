using System;
using System.Globalization;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Converter WPF: ritorna true se il valore non è null, false altrimenti.
    /// Usato per abilitare/disabilitare controlli in base alla presenza di
    /// un'entità nel DataContext (es. SelectedNode → ComboBox OG/OS).
    /// Singleton accessibile via x:Static dal XAML.
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public static readonly NullToBoolConverter Instance = new NullToBoolConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
