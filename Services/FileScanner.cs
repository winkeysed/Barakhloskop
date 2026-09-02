using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Barakhloskop.Models;

namespace Barakhloskop.Services;

/// <summary>Потокобезопасное хранилище найденных файлов.</summary>
public sealed class ResultStore
{
    private readonly ConcurrentBag<MediaFile> _items = new();
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public bool TryAdd(MediaFile file, int limit)
    {
        if (Interlocked.Increment(ref _count) > limit)
        {
            Interlocked.Decrement(ref _count);
            return false;
        }

        _items.Add(file);
        return true;
    }

    public List<MediaFile> Snapshot() => _items.ToList();
}

/// <summary>
/// Многопоточный обход файловой системы. Директории лежат в общем стеке,
/// воркеры разбирают их параллельно; ошибки доступа просто считаются.
/// </summary>
public sealed class FileScanner
{
    private static readonly string[] SystemFolderNames =
    {
        "windows", "winsxs", "program files", "program files (x86)", "programdata",
        "$recycle.bin", "system volume information", "appdata", "recovery",
        "msocache", "perflogs", "node_modules", ".git", ".svn", "onedrivetemp",
        "packagecache", "installer", "assembly", "dotnet", "temp", "tmp", "cache"
    };

    private readonly EnumerationOptions _fileOptions;

    public FileScanner()
    {
        _fileOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false
        };
    }

    /// <summary>Запускает сканирование. Прогресс приходит в <paramref name="onProgress"/> из пула потоков.</summary>
    public async Task<ScanResult> ScanAsync(
        ScanRequest request,
        ResultStore store,
        Action<ScanProgress>? onProgress,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var pending = new ConcurrentStack<string>();
        var visited = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in request.Roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var normalized = Normalize(root);
            if (Directory.Exists(normalized) && visited.TryAdd(normalized, 0))
                pending.Push(normalized);
        }

        var counters = new Counters();
        var truncated = 0;
        var idleWorkers = 0;
        int workers = Math.Max(1, request.Threads);
        string currentFolder = request.Roots.Count > 0 ? request.Roots[0] : string.Empty;

        using var progressTimer = onProgress is null
            ? null
            : new Timer(_ => onProgress(new ScanProgress
            {
                Directories = counters.Directories,
                FilesSeen = counters.FilesSeen,
                Matches = store.Count,
                MatchedBytes = counters.MatchedBytes,
                Errors = counters.Errors,
                CurrentFolder = Volatile.Read(ref currentFolder),
                Elapsed = stopwatch.Elapsed
            }), null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));

        var tasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                bool counted = false;
                var spin = new SpinWait();

                while (true)
                {
                    if (token.IsCancellationRequested || Volatile.Read(ref truncated) == 1) return;

                    if (!pending.TryPop(out var dir))
                    {
                        // Пустой стек: помечаемся простаивающим. Если все простаивают — работа закончена.
                        if (!counted)
                        {
                            counted = true;
                            Interlocked.Increment(ref idleWorkers);
                        }

                        if (Volatile.Read(ref idleWorkers) >= workers) return;

                        spin.SpinOnce();
                        if (spin.NextSpinWillYield) Thread.Sleep(2);
                        continue;
                    }

                    if (counted)
                    {
                        counted = false;
                        Interlocked.Decrement(ref idleWorkers);
                    }

                    Volatile.Write(ref currentFolder, dir);
                    counters.IncrementDirectories();
                    ProcessDirectory(dir, request, store, pending, visited, counters, ref truncated, token);
                }
            }, CancellationToken.None);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        stopwatch.Stop();

        return new ScanResult
        {
            Directories = counters.Directories,
            FilesSeen = counters.FilesSeen,
            Matches = store.Count,
            MatchedBytes = counters.MatchedBytes,
            Errors = counters.Errors,
            Truncated = Volatile.Read(ref truncated) == 1,
            Canceled = token.IsCancellationRequested,
            Elapsed = stopwatch.Elapsed
        };
    }

    private void ProcessDirectory(
        string dir,
        ScanRequest request,
        ResultStore store,
        ConcurrentStack<string> pending,
        ConcurrentDictionary<string, byte> visited,
        Counters counters,
        ref int truncated,
        CancellationToken token)
    {
        try
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", _fileOptions))
            {
                if (token.IsCancellationRequested || Volatile.Read(ref truncated) == 1) return;

                var attributes = SafeAttributes(entry);
                if (attributes == 0) continue;

                bool hidden = (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                if (hidden && !request.IncludeHidden) continue;

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!request.IncludeSystemFolders && IsSystemFolder(entry.Name)) continue;
                    var full = Normalize(entry.FullName);
                    if (visited.TryAdd(full, 0)) pending.Push(full);
                    continue;
                }

                counters.IncrementFilesSeen();

                var ext = Extension(entry.Name);
                if (ext.Length == 0 || !request.Extensions.Contains(ext)) continue;

                if (entry is not FileInfo info) continue;

                long size;
                DateTime modified, created;
                try
                {
                    size = info.Length;
                    modified = info.LastWriteTime;
                    created = info.CreationTime;
                }
                catch (IOException)
                {
                    counters.IncrementErrors();
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    counters.IncrementErrors();
                    continue;
                }

                if (size < request.MinSize) continue;

                var file = new MediaFile(info.FullName, info.Name, ext, size, modified, created, MediaCatalog.KindOf(ext));
                if (store.TryAdd(file, request.MaxResults))
                    counters.AddBytes(size);
                else
                    Interlocked.Exchange(ref truncated, 1);
            }
        }
        catch (UnauthorizedAccessException) { counters.IncrementErrors(); }
        catch (DirectoryNotFoundException) { counters.IncrementErrors(); }
        catch (PathTooLongException) { counters.IncrementErrors(); }
        catch (IOException) { counters.IncrementErrors(); }
    }

    private static FileAttributes SafeAttributes(FileSystemInfo entry)
    {
        try
        {
            return entry.Attributes;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string Extension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
    }

    private static bool IsSystemFolder(string name)
    {
        foreach (var candidate in SystemFolderNames)
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string Normalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }
        catch (Exception)
        {
            return path;
        }
    }

    private sealed class Counters
    {
        private int _directories;
        private int _filesSeen;
        private int _errors;
        private long _matchedBytes;

        public int Directories => Volatile.Read(ref _directories);
        public int FilesSeen => Volatile.Read(ref _filesSeen);
        public int Errors => Volatile.Read(ref _errors);
        public long MatchedBytes => Interlocked.Read(ref _matchedBytes);

        public void IncrementDirectories() => Interlocked.Increment(ref _directories);
        public void IncrementFilesSeen() => Interlocked.Increment(ref _filesSeen);
        public void IncrementErrors() => Interlocked.Increment(ref _errors);
        public void AddBytes(long value) => Interlocked.Add(ref _matchedBytes, value);
    }
}
