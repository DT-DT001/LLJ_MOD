using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx;

namespace LLJDeviceLink;

public sealed class ConfigStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public ModSettings Settings { get; private set; }

    public ConfigStore()
    {
        _path = Path.Combine(Paths.ConfigPath, "LLJDeviceLink", "settings.json");
        Settings = Load();
        if (EventCatalog.MigrateBindings(Settings))
        {
            BackupPreMigrationConfig();
            Save();
            Plugin.LogSource.LogInfo("事件配置已升级为精简目录；旧文件已保留为 settings.pre-1.3.0.json");
        }
    }

    private void BackupPreMigrationConfig()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var backup = Path.Combine(Path.GetDirectoryName(_path)!, "settings.pre-1.3.0.json");
            if (!File.Exists(backup)) File.Copy(_path, backup);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"旧事件配置备份失败，但仍会继续升级：{ex.Message}");
        }
    }

    private ModSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<ModSettings>(File.ReadAllText(_path), _options) ?? new ModSettings();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"配置读取失败，将使用默认值：{ex}");
        }

        return new ModSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, _options));
            File.Move(temporary, _path, true);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"配置保存失败：{ex}");
        }
    }
}
