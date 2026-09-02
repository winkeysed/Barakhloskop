namespace Barakhloskop.Models;

/// <summary>Что и где искать.</summary>
public sealed class ScanRequest
{
    public required IReadOnlyList<string> Roots { get; init; }
    /// <summary>Расширения без точки, в нижнем регистре.</summary>
    public required IReadOnlySet<string> Extensions { get; init; }
    public long MinSize { get; init; }
    public bool IncludeHidden { get; init; }
    /// <summary>Разрешить заходить в Windows, Program Files, AppData и прочие служебные каталоги.</summary>
    public bool IncludeSystemFolders { get; init; }
    public int MaxResults { get; init; } = 400_000;
    public int Threads { get; init; } = Math.Max(2, Math.Min(Environment.ProcessorCount, 8));
}

/// <summary>Снимок прогресса сканирования (обновляется примерно 10 раз в секунду).</summary>
public sealed class ScanProgress
{
    public int Directories { get; init; }
    public int FilesSeen { get; init; }
    public int Matches { get; init; }
    public long MatchedBytes { get; init; }
    public int Errors { get; init; }
    public string CurrentFolder { get; init; } = string.Empty;
    public TimeSpan Elapsed { get; init; }
}

/// <summary>Итог сканирования. Сами файлы лежат в <see cref="Services.ResultStore"/>.</summary>
public sealed class ScanResult
{
    public int Directories { get; init; }
    public int FilesSeen { get; init; }
    public int Matches { get; init; }
    public long MatchedBytes { get; init; }
    public int Errors { get; init; }
    public bool Truncated { get; init; }
    public bool Canceled { get; init; }
    public TimeSpan Elapsed { get; init; }
}
