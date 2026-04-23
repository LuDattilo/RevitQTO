using System;
using System.Globalization;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>Converter WPF: bool IsFavorite → "★" (true) o "☆" (false).</summary>
    public class BoolToStarConverter : IValueConverter
    {
        public static readonly BoolToStarConverter Instance = new BoolToStarConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "★" : "☆";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
