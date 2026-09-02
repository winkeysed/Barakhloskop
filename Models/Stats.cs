using Barakhloskop.Infrastructure;

namespace Barakhloskop.Models;

/// <summary>Статистика по одному типу медиа для полос в панели итогов.</summary>
public sealed class KindStat
{
    public required MediaKind Kind { get; init; }
    public required string Title { get; init; }
    public int Count { get; init; }
    public long Bytes { get; init; }
    /// <summary>Доля от максимального количества среди типов, 0..1.</summary>
    public double Ratio { get; init; }

    public string CountText => Humanize.Count(Count);
    public string BytesText => Humanize.Bytes(Bytes);
}

/// <summary>Папка-рекордсмен по количеству найденных файлов.</summary>
public sealed class FolderStat
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public int Count { get; init; }
    public long Bytes { get; init; }
    public double Ratio { get; init; }

    public string CountText => Humanize.Files(Count);
    public string BytesText => Humanize.Bytes(Bytes);
}
