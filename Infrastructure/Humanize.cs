using System.Globalization;

namespace Barakhloskop.Infrastructure;

/// <summary>Форматирование чисел, размеров и дат в человекочитаемый русский текст.</summary>
public static class Humanize
{
    private static readonly string[] Units = { "Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ" };

    public static string Bytes(long bytes)
    {
        if (bytes < 0) return "?";
        double value = bytes;
        int unit = 0;
        while (value >= 1024d && unit < Units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        string number = unit == 0
            ? value.ToString("0", CultureInfo.CurrentCulture)
            : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture);
        return $"{number} {Units[unit]}";
    }

    public static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Plural(long n, string one, string few, string many)
    {
        long abs = Math.Abs(n) % 100;
        long last = abs % 10;
        if (abs is >= 11 and <= 14) return many;
        if (last == 1) return one;
        if (last is >= 2 and <= 4) return few;
        return many;
    }

    public static string Files(int n) => $"{Count(n)} {Plural(n, "файл", "файла", "файлов")}";

    public static string Folders(int n) => $"{Count(n)} {Plural(n, "папка", "папки", "папок")}";

    /// <summary>Возраст файла словами: «7 лет назад», «3 месяца назад», «сегодня».</summary>
    public static string Age(DateTime moment)
    {
        var span = DateTime.Now - moment;
        if (span < TimeSpan.Zero) return "из будущего";

        int days = (int)span.TotalDays;
        if (days <= 0) return "сегодня";
        if (days == 1) return "вчера";
        if (days < 31) return $"{days} {Plural(days, "день", "дня", "дней")} назад";

        int months = (int)(days / 30.4);
        if (months < 12) return $"{months} {Plural(months, "месяц", "месяца", "месяцев")} назад";

        int years = (int)(days / 365.25);
        int rest = (int)((days - years * 365.25) / 30.4);
        string yearsText = $"{years} {Plural(years, "год", "года", "лет")}";
        return rest >= 2 ? $"{yearsText} и {rest} мес. назад" : $"{yearsText} назад";
    }

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    public static string Seconds(TimeSpan span)
    {
        if (span.TotalSeconds < 10) return $"{span.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} с";
        if (span.TotalMinutes < 1) return $"{(int)span.TotalSeconds} с";
        return $"{(int)span.TotalMinutes} мин {span.Seconds:00} с";
    }

    /// <summary>Сокращает длинный путь с многоточием в середине.</summary>
    public static string ShortPath(string path, int max = 52)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= max) return path;
        int tail = (max - 3) * 2 / 3;
        int head = max - 3 - tail;
        return string.Concat(path.AsSpan(0, head), "...", path.AsSpan(path.Length - tail));
    }

    public static string Ellipsis(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..(max - 1)] + "…";
}
