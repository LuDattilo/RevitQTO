using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>Converte bool→Visibility inversa (true → Collapsed, false → Visible). Per "nessun dato" overlay.</summary>
    public class InverseBoolToVisibility : IValueConverter
    {
        public static readonly InverseBoolToVisibility Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>null → false, not null → true. Per abilitare bottoni quando c'è una selezione.</summary>
    public class NotNullToBool : IValueConverter
    {
        public static readonly NotNullToBool Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
