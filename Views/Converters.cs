using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Barakhloskop.Infrastructure;
using Barakhloskop.Models;

namespace Barakhloskop.Views;

/// <summary>bool -> Visibility. Инвертируется параметром "invert".</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Непустая строка / непустая коллекция / не-null -> Visible.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Размер в байтах -> «12,4 МБ».</summary>
public sealed class BytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            long l => Humanize.Bytes(l),
            int i => Humanize.Bytes(i),
            double d => Humanize.Bytes((long)d),
            _ => "—"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Дата -> «3 года назад».</summary>
public sealed class AgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime dt ? Humanize.Age(dt) : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Тип медиа -> акцентная кисть (индикаторы и бейджи).</summary>
public sealed class KindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is MediaKind kind
            ? kind switch
            {
                MediaKind.Image => "B.Cyan",
                MediaKind.Audio => "B.Lime",
                MediaKind.Video => "B.Amber",
                _ => "B.TextDim"
            }
            : "B.TextDim";

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Число -> строка с разделителями разрядов.</summary>
public sealed class CountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int i => Humanize.Count(i),
            long l => l.ToString("N0", culture),
            _ => "0"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Умножает double на коэффициент из параметра (для полос статистики).</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value is double d ? d : 0;
        double k = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 1;
        return Math.Max(0, v * k);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Доля 0..1 -> GridLength в звёздах. С параметром "rest" возвращает остаток,
/// что даёт пропорциональные полосы без привязки к ActualWidth.
/// </summary>
public sealed class RatioToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ratio = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0
        };

        ratio = Math.Clamp(ratio, 0, 1);
        bool rest = string.Equals(parameter as string, "rest", StringComparison.OrdinalIgnoreCase);
        return new GridLength(rest ? 1 - ratio : ratio, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

