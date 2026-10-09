using System.Collections;
using System.Globalization;
using System.Windows.Data;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// A live view of a block list without its property lines, for the Elements tree: only blocks with a
/// body (elements, and sub-scopes such as <c>MeshInfo</c> or <c>Segment</c>), in file order. A
/// property's place is the Properties panel. The view follows inserts and deletes in the list.
/// </summary>
public sealed class ElementsOnlyConverter : IValueConverter
{
    public static ListCollectionView View(IList blocks) => new(blocks) { Filter = o => o is HudBlock { HasBody: true } };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IList list ? View(list) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
