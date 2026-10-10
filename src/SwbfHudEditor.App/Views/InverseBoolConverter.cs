using System.Globalization;
using System.Windows.Data;

namespace SwbfHudEditor.App.Views;

/// <summary>Not: for a toggle that should ignore clicks while the popup it opened is open.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
