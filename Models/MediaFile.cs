namespace Barakhloskop.Models;

/// <summary>Тип найденного медиафайла.</summary>
public enum MediaKind
{
    Image,
    Audio,
    Video
}

/// <summary>Один найденный файл со всеми метаданными, которые нужны интерфейсу.</summary>
public sealed class MediaFile
{
    public MediaFile(string path, string name, string extension, long size, DateTime modified, DateTime created, MediaKind kind)
    {
        Path = path;
        Name = name;
        Extension = extension;
        Size = size;
        Modified = modified;
        Created = created;
        Kind = kind;
        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string Path { get; }
    public string Name { get; }
    /// <summary>Расширение в нижнем регистре, без точки.</summary>
    public string Extension { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public DateTime Created { get; }
    public MediaKind Kind { get; }
    public string Folder { get; }

    public string FolderName
    {
        get
        {
            var trimmed = Folder.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            var leaf = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
        }
    }

    public int Depth => Path.Count(c => c == System.IO.Path.DirectorySeparatorChar);

    public string KindTitle => Kind switch
    {
        MediaKind.Image => "картинка",
        MediaKind.Audio => "аудио",
        MediaKind.Video => "видео",
        _ => "файл"
    };

    public string KindGlyph => Kind switch
    {
        MediaKind.Image => "IMG",
        MediaKind.Audio => "MP3",
        MediaKind.Video => "VID",
        _ => "???"
    };

    public override string ToString() => Path;
}
