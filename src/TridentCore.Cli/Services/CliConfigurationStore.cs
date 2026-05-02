using System.Text.Json;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;

namespace TridentCore.Cli.Services;

public class CliConfigurationStore
{
    private readonly string _path = CliDataPaths.File("settings.json");

    public IReadOnlyDictionary<string, object> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, object>();
        }

        var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(
            File.ReadAllText(_path),
            FileHelper.SerializerOptions
        );
        return settings ?? new Dictionary<string, object>();
    }

    public T? Get<T>(string key, T? defaultValue = default)
    {
        var settings = Load();
        if (!settings.TryGetValue(key, out var result))
        {
            return defaultValue;
        }

        if (result is T typed)
        {
            return typed;
        }

        if (NumericConversionHelper.TryConvert(result, out T converted))
        {
            return converted;
        }

        return defaultValue;
    }

    public void Set(string key, object value)
    {
        var settings = Load().ToDictionary();
        settings[key] = value;
        Save(settings);
    }

    public bool Remove(string key)
    {
        var settings = Load().ToDictionary();
        var removed = settings.Remove(key);
        if (removed)
        {
            Save(settings);
        }

        return removed;
    }

    private void Save(IReadOnlyDictionary<string, object> settings)
    {
        var ordered = settings
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Value);
        AtomicFileWriter.WriteAllText(
            _path,
            JsonSerializer.Serialize(ordered, FileHelper.SerializerOptions)
        );
    }
}
