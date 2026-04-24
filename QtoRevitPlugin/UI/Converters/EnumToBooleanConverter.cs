using System;
using System.Globalization;
using System.Windows.Data;

namespace QtoRevitPlugin.UI.Converters
{
    /// <summary>
    /// Converter WPF classico per bindare RadioButton.IsChecked a un enum property.
    /// ConverterParameter contiene il nome dell'enum value (case-insensitive).
    /// Binding OneWay: enum → bool. Mode=TwoWay: bool → enum (solo se IsChecked=true).
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool b) || !b || parameter == null) return Binding.DoNothing;
            return Enum.Parse(targetType, parameter.ToString()!);
        }
    }
}
