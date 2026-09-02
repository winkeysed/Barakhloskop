using Barakhloskop.Models;

namespace Barakhloskop.Services;

/// <summary>Справочник расширений и групп медиафайлов.</summary>
public static class MediaCatalog
{
    public static readonly IReadOnlyDictionary<string, MediaKind> KnownExtensions =
        new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase)
        {
            // картинки
            ["jpg"] = MediaKind.Image,
            ["jpeg"] = MediaKind.Image,
            ["png"] = MediaKind.Image,
            ["gif"] = MediaKind.Image,
            ["bmp"] = MediaKind.Image,
            ["webp"] = MediaKind.Image,
            ["tiff"] = MediaKind.Image,
            ["ico"] = MediaKind.Image,
            // аудио
            ["mp3"] = MediaKind.Audio,
            ["wav"] = MediaKind.Audio,
            ["flac"] = MediaKind.Audio,
            ["m4a"] = MediaKind.Audio,
            ["ogg"] = MediaKind.Audio,
            ["opus"] = MediaKind.Audio,
            ["wma"] = MediaKind.Audio,
            ["aac"] = MediaKind.Audio,
            // видео
            ["mp4"] = MediaKind.Video,
            ["avi"] = MediaKind.Video,
            ["mkv"] = MediaKind.Video,
            ["mov"] = MediaKind.Video,
            ["webm"] = MediaKind.Video,
            ["wmv"] = MediaKind.Video,
            ["flv"] = MediaKind.Video,
            ["m4v"] = MediaKind.Video,
            ["3gp"] = MediaKind.Video
        };

    /// <summary>Расширения, которые WPF (MediaElement/BitmapImage) умеет открывать без кодеков со стороны.</summary>
    public static readonly IReadOnlySet<string> PreviewFriendly =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "jpg", "jpeg", "png", "gif", "bmp", "tiff", "ico",
            "mp3", "wav", "wma", "m4a", "aac",
            "mp4", "m4v", "avi", "wmv", "mov"
        };

    public static MediaKind KindOf(string extension)
        => KnownExtensions.TryGetValue(extension, out var kind) ? kind : MediaKind.Image;

    public static bool CanPreview(MediaFile file) => PreviewFriendly.Contains(file.Extension);
}
