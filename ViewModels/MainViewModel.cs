using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Barakhloskop.Infrastructure;
using Barakhloskop.Models;
using Barakhloskop.Services;

namespace Barakhloskop.ViewModels;

/// <summary>Порядок сортировки списка находок.</summary>
public enum FindingSort
{
    Natural,
    Biggest,
    Oldest,
    Newest
}

/// <summary>Вся логика окна: настройка поиска, сканирование, рулетка, статистика.</summary>
public sealed class MainViewModel : ObservableObject
{
    private const int HistoryLimit = 14;
    private const int ListLimit = 2000;
    private const int NoRepeatWindow = 24;

    private readonly FileScanner _scanner = new();
    private readonly RoflGenerator _rofl = new();
    private readonly SettingsService _settingsService = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _spinTimer;
    private readonly DispatcherTimer _phraseTimer;
    private readonly Random _random = Random.Shared;

    private List<MediaFile> _all = new();
    private readonly List<string> _recentPaths = new();
    private CancellationTokenSource? _scanCts;
    private int _spinTicks;

    public MainViewModel()
    {
        Roots = new ObservableCollection<string>();
        History = new ObservableCollection<MediaFile>();
        KindStats = new ObservableCollection<KindStat>();
        TopFolders = new ObservableCollection<FolderStat>();
        Findings = new ObservableCollection<MediaFile>();

        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning && Roots.Count > 0 && HasAnyKind);
        CancelCommand = new RelayCommand(CancelScan, () => IsScanning);
        SpinCommand = new RelayCommand(Spin, () => !IsSpinning && _all.Count > 0);
        AddFolderCommand = new RelayCommand(AddFolder, () => !IsScanning);
        RemoveRootCommand = new RelayCommand(p => RemoveRoot(p as string), _ => !IsScanning);
        PresetDrivesCommand = new RelayCommand(() => ApplyPreset(RootPresets.AllDrives()), () => !IsScanning);
        PresetUserCommand = new RelayCommand(() => ApplyPreset(RootPresets.UserFolders()), () => !IsScanning);
        ClearRootsCommand = new RelayCommand(() => { Roots.Clear(); OnRootsChanged(); }, () => !IsScanning && Roots.Count > 0);
        OpenFileCommand = new RelayCommand(OpenFile, () => CurrentFile is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => CurrentFile is not null);
        CopyPathCommand = new RelayCommand(CopyPath, () => CurrentFile is not null);
        ShowFileCommand = new RelayCommand(p => Show(p as MediaFile, addToHistory: true));

        Roots.CollectionChanged += (_, _) => OnRootsChanged();

        _spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _spinTimer.Tick += OnSpinTick;

        _phraseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _phraseTimer.Tick += (_, _) => StatusPhrase = _rofl.NextScanPhrase();

        LoadSettings();
    }

    // ================= Корневые папки =================

    public ObservableCollection<string> Roots { get; }

    private void ApplyPreset(IEnumerable<string> paths)
    {
        Roots.Clear();
        foreach (var path in paths) Roots.Add(path);
        OnRootsChanged();
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку для раскопок",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        foreach (var path in dialog.FolderNames)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!Roots.Contains(path, StringComparer.OrdinalIgnoreCase)) Roots.Add(path);
        }

        OnRootsChanged();
    }

    private void RemoveRoot(string? path)
    {
        if (path is null) return;
        Roots.Remove(path);
        OnRootsChanged();
    }

    private void OnRootsChanged()
    {
        Raise(nameof(RootsSummary));
        RefreshCommands();
    }

    public string RootsSummary => Roots.Count switch
    {
        0 => "Папки не выбраны",
        1 => "1 источник",
        _ => $"{Humanize.Count(Roots.Count)} {Humanize.Plural(Roots.Count, "источник", "источника", "источников")}"
    };

    // ================= Фильтры =================

    private bool _scanImages = true;
    public bool ScanImages
    {
        get => _scanImages;
        set { if (Set(ref _scanImages, value)) OnFiltersChanged(); }
    }

    private bool _scanAudio = true;
    public bool ScanAudio
    {
        get => _scanAudio;
        set { if (Set(ref _scanAudio, value)) OnFiltersChanged(); }
    }

    private bool _scanVideo = true;
    public bool ScanVideo
    {
        get => _scanVideo;
        set { if (Set(ref _scanVideo, value)) OnFiltersChanged(); }
    }

    private double _minSizeMb;
    public double MinSizeMb
    {
        get => _minSizeMb;
        set
        {
            if (Set(ref _minSizeMb, Math.Round(value, 1))) Raise(nameof(MinSizeText));
        }
    }

    public string MinSizeText => MinSizeMb < 0.05
        ? "любой размер"
        : $"от {MinSizeMb.ToString(MinSizeMb < 10 ? "0.0" : "0")} МБ";

    private bool _includeHidden;
    public bool IncludeHidden
    {
        get => _includeHidden;
        set => Set(ref _includeHidden, value);
    }

    private bool _includeSystemFolders;
    public bool IncludeSystemFolders
    {
        get => _includeSystemFolders;
        set => Set(ref _includeSystemFolders, value);
    }

    private bool _onlyPreviewable = true;
    public bool OnlyPreviewable
    {
        get => _onlyPreviewable;
        set => Set(ref _onlyPreviewable, value);
    }

    private bool HasAnyKind => ScanImages || ScanAudio || ScanVideo;

    private void OnFiltersChanged()
    {
        Raise(nameof(FiltersSummary));
        RefreshCommands();
    }

    public string FiltersSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (ScanImages) parts.Add("картинки");
            if (ScanAudio) parts.Add("музыка");
            if (ScanVideo) parts.Add("видео");
            return parts.Count == 0 ? "ничего не выбрано" : string.Join(" · ", parts);
        }
    }

    // ================= Сканирование =================

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!Set(ref _isScanning, value)) return;
            Raise(nameof(IsIdle));
            RefreshCommands();
        }
    }

    public bool IsIdle => !IsScanning;

    private string _statusPhrase = "Готов копать. Выберите папки и жмите кнопку.";
    public string StatusPhrase
    {
        get => _statusPhrase;
        private set => Set(ref _statusPhrase, value);
    }

    private string _currentFolder = string.Empty;
    public string CurrentFolder
    {
        get => _currentFolder;
        private set => Set(ref _currentFolder, value);
    }

    private int _seenFiles;
    public int SeenFiles
    {
        get => _seenFiles;
        private set => Set(ref _seenFiles, value);
    }

    private int _seenDirectories;
    public int SeenDirectories
    {
        get => _seenDirectories;
        private set => Set(ref _seenDirectories, value);
    }

    private int _totalFiles;
    public int TotalFiles
    {
        get => _totalFiles;
        private set
        {
            if (!Set(ref _totalFiles, value)) return;
            Raise(nameof(HasResults));
        }
    }

    private long _totalBytes;
    public long TotalBytes
    {
        get => _totalBytes;
        private set => Set(ref _totalBytes, value);
    }

    private int _errors;
    public int Errors
    {
        get => _errors;
        private set => Set(ref _errors, value);
    }

    private string _elapsedText = "0,0 с";
    public string ElapsedText
    {
        get => _elapsedText;
        private set => Set(ref _elapsedText, value);
    }

    private string _scanSummary = string.Empty;
    public string ScanSummary
    {
        get => _scanSummary;
        private set => Set(ref _scanSummary, value);
    }

    public bool HasResults => TotalFiles > 0;

    private async Task ScanAsync()
    {
        if (IsScanning) return;

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ext, kind) in MediaCatalog.KnownExtensions)
        {
            bool take = kind switch
            {
                MediaKind.Image => ScanImages,
                MediaKind.Audio => ScanAudio,
                MediaKind.Video => ScanVideo,
                _ => false
            };
            if (take) extensions.Add(ext);
        }

        if (extensions.Count == 0 || Roots.Count == 0) return;

        var request = new ScanRequest
        {
            Roots = Roots.ToList(),
            Extensions = extensions,
            MinSize = (long)(MinSizeMb * 1024 * 1024),
            IncludeHidden = IncludeHidden,
            IncludeSystemFolders = IncludeSystemFolders
        };

        var store = new ResultStore();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanSummary = string.Empty;
        StatusPhrase = _rofl.NextScanPhrase();
        _phraseTimer.Start();

        ResetResults();

        try
        {
            var result = await _scanner.ScanAsync(request, store, OnProgress, _scanCts.Token);

            SeenDirectories = result.Directories;
            SeenFiles = result.FilesSeen;
            Errors = result.Errors;
            ElapsedText = Humanize.Seconds(result.Elapsed);

            _all = store.Snapshot();
            TotalFiles = _all.Count;
            TotalBytes = _all.Sum(f => f.Size);

            BuildStats();
            RebuildFindings();

            var rank = _rofl.HoarderRank(TotalFiles);
            HoarderTitle = rank.Title;
            HoarderComment = rank.Comment;
            ScanSummary = _rofl.ScanSummary(result);
            CurrentFolder = string.Empty;

            StatusPhrase = TotalFiles > 0
                ? $"Найдено {Humanize.Files(TotalFiles)} на {Humanize.Bytes(TotalBytes)}. Крутите рулетку."
                : _rofl.EmptyResultLine();

            if (TotalFiles > 0) Spin();
        }
        finally
        {
            _phraseTimer.Stop();
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void OnProgress(ScanProgress progress)
    {
        // Прогресс приходит с таймера пула потоков — переносим в UI-поток.
        _dispatcher.InvokeAsync(() =>
        {
            SeenDirectories = progress.Directories;
            SeenFiles = progress.FilesSeen;
            TotalFiles = progress.Matches;
            TotalBytes = progress.MatchedBytes;
            Errors = progress.Errors;
            ElapsedText = Humanize.Seconds(progress.Elapsed);
            CurrentFolder = Humanize.ShortPath(progress.CurrentFolder, 64);
        }, DispatcherPriority.Background);
    }

    private void CancelScan()
    {
        _scanCts?.Cancel();
        StatusPhrase = "Отменяю… доскажу, что успел найти.";
    }

    private void ResetResults()
    {
        _all = new List<MediaFile>();
        _recentPaths.Clear();
        History.Clear();
        KindStats.Clear();
        TopFolders.Clear();
        Findings.Clear();
        CurrentFile = null;
        Verdict = string.Empty;
        TotalFiles = 0;
        TotalBytes = 0;
        SeenFiles = 0;
        SeenDirectories = 0;
        Errors = 0;
        HoarderTitle = string.Empty;
        HoarderComment = string.Empty;
        Raise(nameof(FindingsCaption));
    }

    // ================= Статистика =================

    public ObservableCollection<KindStat> KindStats { get; }
    public ObservableCollection<FolderStat> TopFolders { get; }

    private string _hoarderTitle = string.Empty;
    public string HoarderTitle
    {
        get => _hoarderTitle;
        private set => Set(ref _hoarderTitle, value);
    }

    private string _hoarderComment = string.Empty;
    public string HoarderComment
    {
        get => _hoarderComment;
        private set => Set(ref _hoarderComment, value);
    }

    private void BuildStats()
    {
        KindStats.Clear();
        TopFolders.Clear();
        if (_all.Count == 0) return;

        var groups = _all.GroupBy(f => f.Kind)
            .Select(g => (Kind: g.Key, Count: g.Count(), Bytes: g.Sum(f => f.Size)))
            .OrderByDescending(g => g.Count)
            .ToList();

        int maxCount = groups.Count > 0 ? groups.Max(g => g.Count) : 1;
        foreach (var group in groups)
        {
            KindStats.Add(new KindStat
            {
                Kind = group.Kind,
                Title = group.Kind switch
                {
                    MediaKind.Image => "Картинки",
                    MediaKind.Audio => "Музыка",
                    MediaKind.Video => "Видео",
                    _ => "Прочее"
                },
                Count = group.Count,
                Bytes = group.Bytes,
                Ratio = maxCount == 0 ? 0 : (double)group.Count / maxCount
            });
        }

        var folders = _all.GroupBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Path: g.Key, Count: g.Count(), Bytes: g.Sum(f => f.Size)))
            .OrderByDescending(g => g.Count)
            .Take(6)
            .ToList();

        int maxFolder = folders.Count > 0 ? folders.Max(f => f.Count) : 1;
        foreach (var folder in folders)
        {
            var leaf = Path.GetFileName(folder.Path.TrimEnd(Path.DirectorySeparatorChar));
            TopFolders.Add(new FolderStat
            {
                Name = string.IsNullOrEmpty(leaf) ? folder.Path : leaf,
                Path = folder.Path,
                Count = folder.Count,
                Bytes = folder.Bytes,
                Ratio = maxFolder == 0 ? 0 : (double)folder.Count / maxFolder
            });
        }
    }

    // ================= Список находок =================

    public ObservableCollection<MediaFile> Findings { get; }

    private string _search = string.Empty;
    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) RebuildFindings();
        }
    }

    private FindingSort _sort = FindingSort.Natural;

    public bool SortNatural
    {
        get => _sort == FindingSort.Natural;
        set { if (value) ApplySort(FindingSort.Natural); }
    }

    public bool SortBiggest
    {
        get => _sort == FindingSort.Biggest;
        set { if (value) ApplySort(FindingSort.Biggest); }
    }

    public bool SortOldest
    {
        get => _sort == FindingSort.Oldest;
        set { if (value) ApplySort(FindingSort.Oldest); }
    }

    public bool SortNewest
    {
        get => _sort == FindingSort.Newest;
        set { if (value) ApplySort(FindingSort.Newest); }
    }

    private void ApplySort(FindingSort sort)
    {
        if (_sort == sort) return;
        _sort = sort;
        Raise(nameof(SortNatural));
        Raise(nameof(SortBiggest));
        Raise(nameof(SortOldest));
        Raise(nameof(SortNewest));
        RebuildFindings();
    }

    public string FindingsCaption => _all.Count switch
    {
        0 => "Пока пусто",
        _ when Findings.Count < _all.Count => $"Показаны {Humanize.Count(Findings.Count)} из {Humanize.Count(_all.Count)}",
        _ => Humanize.Files(Findings.Count)
    };

    private void RebuildFindings()
    {
        Findings.Clear();
        if (_all.Count == 0)
        {
            Raise(nameof(FindingsCaption));
            return;
        }

        IEnumerable<MediaFile> query = _all;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var needle = Search.Trim();
            query = query.Where(f => f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                                     || f.FolderName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        query = _sort switch
        {
            FindingSort.Biggest => query.OrderByDescending(f => f.Size),
            FindingSort.Oldest => query.OrderBy(f => f.Modified),
            FindingSort.Newest => query.OrderByDescending(f => f.Modified),
            _ => query
        };

        foreach (var file in query.Take(ListLimit)) Findings.Add(file);
        Raise(nameof(FindingsCaption));
    }

    // ================= Рулетка =================

    public ObservableCollection<MediaFile> History { get; }

    private MediaFile? _currentFile;
    public MediaFile? CurrentFile
    {
        get => _currentFile;
        private set
        {
            if (!Set(ref _currentFile, value)) return;
            Raise(nameof(HasCurrent));
            Raise(nameof(CurrentAgeText));
            Raise(nameof(CurrentSizeText));
            Raise(nameof(CurrentPathShort));
            Raise(nameof(CanPreviewCurrent));
            RaisePreviewFlags();
            RefreshCommands();
        }
    }

    public bool HasCurrent => CurrentFile is not null;

    public bool CanPreviewCurrent => CurrentFile is not null && MediaCatalog.CanPreview(CurrentFile);

    public string CurrentSizeText => CurrentFile is null ? "—" : Humanize.Bytes(CurrentFile.Size);

    public string CurrentAgeText => CurrentFile is null ? "—" : Humanize.Age(CurrentFile.Modified);

    public string CurrentPathShort => CurrentFile is null ? string.Empty : Humanize.ShortPath(CurrentFile.Path, 70);

    /// <summary>Ставится из View, если файл не открылся (битый или без кодека).</summary>
    private bool _previewFailed;
    public bool PreviewFailed
    {
        get => _previewFailed;
        set
        {
            if (Set(ref _previewFailed, value)) RaisePreviewFlags();
        }
    }

    public bool IsImagePreview => Previewable && CurrentFile!.Kind == MediaKind.Image;
    public bool IsVideoPreview => Previewable && CurrentFile!.Kind == MediaKind.Video;
    public bool IsAudioPreview => Previewable && CurrentFile!.Kind == MediaKind.Audio;
    public bool HasPlayableMedia => IsVideoPreview || IsAudioPreview;
    public bool IsUnsupported => CurrentFile is not null && (!CanPreviewCurrent || PreviewFailed);
    public bool IsStageEmpty => CurrentFile is null;

    private bool Previewable => CurrentFile is not null && CanPreviewCurrent && !PreviewFailed;

    private void RaisePreviewFlags()
    {
        Raise(nameof(IsImagePreview));
        Raise(nameof(IsVideoPreview));
        Raise(nameof(IsAudioPreview));
        Raise(nameof(HasPlayableMedia));
        Raise(nameof(IsUnsupported));
        Raise(nameof(IsStageEmpty));
    }

    private string _verdict = string.Empty;
    public string Verdict
    {
        get => _verdict;
        private set => Set(ref _verdict, value);
    }

    private bool _isSpinning;
    public bool IsSpinning
    {
        get => _isSpinning;
        private set
        {
            if (!Set(ref _isSpinning, value)) return;
            RefreshCommands();
        }
    }

    private string _spinLabel = string.Empty;
    public string SpinLabel
    {
        get => _spinLabel;
        private set => Set(ref _spinLabel, value);
    }

    /// <summary>Событие для code-behind: пора обновить превью и плеер.</summary>
    public event Action<MediaFile?>? PreviewRequested;

    private void Spin()
    {
        if (_all.Count == 0 || IsSpinning) return;

        var target = PickRandom();
        if (target is null)
        {
            StatusPhrase = "С такими фильтрами предпросмотра ничего не осталось. Снимите галочку.";
            return;
        }

        PreviewRequested?.Invoke(null);
        IsSpinning = true;
        _spinTicks = 0;
        _pendingFile = target;
        _spinTimer.Start();
    }

    private MediaFile? _pendingFile;

    private void OnSpinTick(object? sender, EventArgs e)
    {
        _spinTicks++;

        // Мелькание случайных имён — эффект «крутящегося барабана».
        SpinLabel = _all[_random.Next(_all.Count)].Name;

        if (_spinTicks < 13) return;

        _spinTimer.Stop();
        IsSpinning = false;
        SpinLabel = string.Empty;
        Show(_pendingFile, addToHistory: true);
        _pendingFile = null;
    }

    private MediaFile? PickRandom()
    {
        var pool = OnlyPreviewable ? _all.Where(MediaCatalog.CanPreview).ToList() : _all;
        if (pool.Count == 0) return null;

        // До 40 попыток вытащить файл, которого не было в последних показах.
        for (int i = 0; i < 40; i++)
        {
            var candidate = pool[_random.Next(pool.Count)];
            if (!_recentPaths.Contains(candidate.Path, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }

        return pool[_random.Next(pool.Count)];
    }

    private void Show(MediaFile? file, bool addToHistory)
    {
        if (file is null) return;

        _previewFailed = false;
        CurrentFile = file;
        Verdict = _rofl.Verdict(file);

        if (addToHistory)
        {
            _recentPaths.Add(file.Path);
            if (_recentPaths.Count > NoRepeatWindow) _recentPaths.RemoveAt(0);

            var existing = History.FirstOrDefault(f => string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) History.Remove(existing);
            History.Insert(0, file);
            while (History.Count > HistoryLimit) History.RemoveAt(History.Count - 1);
        }

        PreviewRequested?.Invoke(file);
    }

    // ================= Действия с файлом =================

    private void OpenFile()
    {
        if (CurrentFile is null) return;
        TryStart(new ProcessStartInfo(CurrentFile.Path) { UseShellExecute = true });
    }

    private void OpenFolder()
    {
        if (CurrentFile is null) return;
        TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{CurrentFile.Path}\"") { UseShellExecute = true });
    }

    private void CopyPath()
    {
        if (CurrentFile is null) return;
        try
        {
            Clipboard.SetText(CurrentFile.Path);
            StatusPhrase = "Путь скопирован. Теперь вы официально знаете, где это лежит.";
        }
        catch (Exception)
        {
            StatusPhrase = "Буфер обмена занят кем-то другим. Попробуйте ещё раз.";
        }
    }

    private void TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception ex)
        {
            StatusPhrase = $"Не открылось: {ex.Message}";
        }
    }

    // ================= Команды =================

    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SpinCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveRootCommand { get; }
    public RelayCommand PresetDrivesCommand { get; }
    public RelayCommand PresetUserCommand { get; }
    public RelayCommand ClearRootsCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand ShowFileCommand { get; }

    private void RefreshCommands()
    {
        ScanCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SpinCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        RemoveRootCommand.RaiseCanExecuteChanged();
        PresetDrivesCommand.RaiseCanExecuteChanged();
        PresetUserCommand.RaiseCanExecuteChanged();
        ClearRootsCommand.RaiseCanExecuteChanged();
        OpenFileCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        CopyPathCommand.RaiseCanExecuteChanged();
    }

    // ================= Плеер (состояние хранит VM, воспроизводит View) =================

    private bool _autoPlay = true;
    public bool AutoPlay
    {
        get => _autoPlay;
        set => Set(ref _autoPlay, value);
    }

    private double _volume = 0.5;
    public double Volume
    {
        get => _volume;
        set => Set(ref _volume, Math.Clamp(value, 0, 1));
    }

    // ================= Настройки =================

    private void LoadSettings()
    {
        var settings = _settingsService.Load();

        var roots = settings.Roots.Where(Directory.Exists).ToList();
        if (roots.Count == 0) roots = RootPresets.UserFolders();
        foreach (var root in roots) Roots.Add(root);

        _scanImages = settings.Images;
        _scanAudio = settings.Audio;
        _scanVideo = settings.Video;
        if (!_scanImages && !_scanAudio && !_scanVideo) _scanImages = _scanAudio = _scanVideo = true;

        _minSizeMb = Math.Clamp(settings.MinSize / 1024d / 1024d, 0, 50);
        _includeHidden = settings.IncludeHidden;
        _includeSystemFolders = settings.IncludeSystemFolders;
        _autoPlay = settings.AutoPlay;
        _volume = Math.Clamp(settings.Volume, 0, 1);

        Raise(nameof(ScanImages));
        Raise(nameof(ScanAudio));
        Raise(nameof(ScanVideo));
        Raise(nameof(MinSizeMb));
        Raise(nameof(MinSizeText));
        Raise(nameof(IncludeHidden));
        Raise(nameof(IncludeSystemFolders));
        Raise(nameof(AutoPlay));
        Raise(nameof(Volume));
        Raise(nameof(FiltersSummary));
        Raise(nameof(RootsSummary));
    }

    public void SaveSettings()
    {
        _settingsService.Save(new AppSettings
        {
            Roots = Roots.ToList(),
            Images = ScanImages,
            Audio = ScanAudio,
            Video = ScanVideo,
            MinSize = (long)(MinSizeMb * 1024 * 1024),
            IncludeHidden = IncludeHidden,
            IncludeSystemFolders = IncludeSystemFolders,
            AutoPlay = AutoPlay,
            Volume = Volume
        });
    }

    public void Shutdown()
    {
        _scanCts?.Cancel();
        _spinTimer.Stop();
        _phraseTimer.Stop();
        SaveSettings();
    }
}
