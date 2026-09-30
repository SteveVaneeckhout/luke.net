using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LukeNet.App;

/// <summary>Formats a byte count as a human readable size (e.g. "1.4 MB").</summary>
public sealed class ByteSizeConverter : IValueConverter
{
    public static readonly ByteSizeConverter Instance = new();

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {Units[unit]}";
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? Format(bytes) : value;

    // Display only: never write the formatted text back into the bound object.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}

/// <summary>Shows booleans as "yes" (true) or nothing (false), which keeps grids easy to scan.</summary>
public sealed class YesConverter : IValueConverter
{
    public static readonly YesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "yes" : "";

    // Display only: never write the formatted text back into the bound object.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
