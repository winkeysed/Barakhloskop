using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Barakhloskop.Services;

/// <summary>Настройки, которые запоминаются между запусками.</summary>
public sealed class AppSettings
{
    public List<string> Roots { get; set; } = new();
    public bool Images { get; set; } = true;
    public bool Audio { get; set; } = true;
    public bool Video { get; set; } = true;
    public long MinSize { get; set; }
    public bool IncludeHidden { get; set; }
    public bool IncludeSystemFolders { get; set; }
    public bool AutoPlay { get; set; } = true;
    public double Volume { get; set; } = 0.5;
}

/// <summary>Читает и пишет настройки в %AppData%\Barakhloskop\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _file;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Barakhloskop");
        _file = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_file)) return new AppSettings();
            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Битый или недоступный файл настроек не должен ломать запуск.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_file, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception)
        {
            // Не смогли сохранить — не беда, это не критичная функция.
        }
    }
}
