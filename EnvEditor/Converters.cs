using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EnvEditor.Models;

namespace EnvEditor;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type? t, object? p, CultureInfo? c) => value is bool b && !b;
    public object ConvertBack(object? value, Type? t, object? p, CultureInfo? c) => value is bool b && !b;
}

public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type? t, object? p, CultureInfo? c) => value switch
    {
        SyncState.InSync => Brushes.Gray,
        SyncState.LocalOnly => Brushes.SteelBlue,
        SyncState.RemoteOnly => Brushes.Green,
        SyncState.Different => Brushes.Orange,
        SyncState.PendingRemove => Brushes.DarkOrange,
        SyncState.Unsupported => Brushes.Red,
        _ => Brushes.Gray
    };
    public object ConvertBack(object? v, Type? t, object? p, CultureInfo? c) => throw new NotSupportedException();
}
