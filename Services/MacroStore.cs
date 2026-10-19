using System.Text.Json;
using System.Text.Json.Serialization;
using DailyMacro.Models;
using AppInfo = DailyMacro.AppInfo;

namespace DailyMacro.Services;

internal static class MacroStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string Folder { get; } = ResolveFolder();

    public static string MacrosPath => Path.Combine(Folder, "macros.json");
    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    private static string ResolveFolder()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(roaming, AppInfo.Name);
        Directory.CreateDirectory(folder);

        var legacy = Path.Combine(roaming, "DailyMacro");
        foreach (var file in new[] { "macros.json", "settings.json" })
        {
            var dest = Path.Combine(folder, file);
            var src = Path.Combine(legacy, file);
            if (!File.Exists(dest) && File.Exists(src))
                File.Copy(src, dest);
        }

        return folder;
    }

    public static bool HasExistingMacrosFile()
    {
        try
        {
            return File.Exists(MacrosPath) && new FileInfo(MacrosPath).Length > 40;
        }
        catch
        {
            return false;
        }
    }

    public static MacroLibrary LoadMacros()
    {
        Directory.CreateDirectory(Folder);
        var candidates = new[]
        {
            MacrosPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DailyMacro", "macros.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "macros.json")
        };

        MacroLibrary? best = null;
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var loaded = TryReadFile(path);
            if (loaded is null)
                continue;
            if (best is null || loaded.Macros.Count > best.Macros.Count)
                best = loaded;
        }

        if (best is { Macros.Count: > 0 })
        {
            if (!File.Exists(MacrosPath) || new FileInfo(MacrosPath).Length < 40 || TryReadFile(MacrosPath)?.Macros.Count < best.Macros.Count)
                SaveMacros(best);
            return best;
        }

        return new MacroLibrary();
    }

    private static MacroLibrary? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 2)
                return null;
            return ReadLibrary(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            TryWriteError("macros-load.log", ex);
            return null;
        }
    }

    private static MacroLibrary? ReadLibrary(string json)
    {
        var library = JsonSerializer.Deserialize<MacroLibrary>(json, JsonOptions);
        if (library is { Macros.Count: > 0 })
            return library;

        try
        {
            var list = JsonSerializer.Deserialize<List<MacroDefinition>>(json, JsonOptions);
            if (list is { Count: > 0 })
                return new MacroLibrary { Macros = list };
        }
        catch
        {
            // file was not a raw array
        }

        return library;
    }

    public static void SaveMacros(MacroLibrary library)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(MacrosPath, JsonSerializer.Serialize(library, JsonOptions));
    }

    public static AppSettings LoadSettings()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            TryWriteError("settings-load.log", ex);
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static void ExportMacro(MacroDefinition macro, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(macro, JsonOptions));

    public static MacroDefinition? ImportMacro(string path)
    {
        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<MacroDefinition>(json, JsonOptions);
        }
        catch
        {
            var library = ReadLibrary(json);
            return library?.Macros.FirstOrDefault();
        }
    }

    private static void TryWriteError(string fileName, Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(Folder, fileName), ex.ToString());
        }
        catch
        {
            // ignore log failures
        }
    }
}
