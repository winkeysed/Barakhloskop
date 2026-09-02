using System.IO;

namespace Barakhloskop.Services;

/// <summary>Готовые наборы корневых папок для сканирования.</summary>
public static class RootPresets
{
    /// <summary>Все локальные фиксированные диски + подключённые съёмные.</summary>
    public static List<string> AllDrives()
    {
        var list = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType is DriveType.Fixed or DriveType.Removable)
                        list.Add(drive.RootDirectory.FullName);
                }
                catch (IOException)
                {
                    // Диск отвалился между вызовами — пропускаем.
                }
            }
        }
        catch (Exception)
        {
            // Совсем не смогли перечислить диски — вернём хотя бы системный.
        }

        if (list.Count == 0)
        {
            var system = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(system)) list.Add(system);
        }

        return list;
    }

    /// <summary>Пользовательские медиапапки: профиль, Загрузки, Рабочий стол, Картинки, Музыка, Видео.</summary>
    public static List<string> UserFolders()
    {
        var candidates = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            DownloadsFolder()
        };

        var result = new List<string>();
        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!Directory.Exists(path)) continue;
            if (!result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
        }

        if (result.Count == 0)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(profile)) result.Add(profile);
        }

        return result;
    }

    public static string? DownloadsFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return null;

        foreach (var name in new[] { "Downloads", "Загрузки" })
        {
            var candidate = Path.Combine(profile, name);
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }
}
