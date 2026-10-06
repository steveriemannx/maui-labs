using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.PolluxOS.DUI.Essentials;

/// <summary>
/// File-backed <see cref="IPreferences"/> for the DUI backend.
/// </summary>
/// <remarks>
/// There is no PolluxOS preference service yet, so values are persisted as a JSON
/// map under the app data directory. Swap this for the platform service when
/// PolluxOS exposes one — the MAUI surface stays identical.
/// </remarks>
public class DuiPreferences : IPreferences
{
    readonly object _gate = new();
    Dictionary<string, object?> _values;

    public DuiPreferences()
    {
        Path = System.IO.Path.Combine(FileSystem.AppDataDirectory, "polluxos-dui.preferences.json");
        _values = Load();
    }

    public string Path { get; }

    public bool ContainsKey(string key, string? sharedName = null)
    {
        lock (_gate)
            return _values.ContainsKey(Key(key, sharedName));
    }

    public void Remove(string key, string? sharedName = null)
    {
        lock (_gate)
        {
            if (_values.Remove(Key(key, sharedName)))
                Save();
        }
    }

    public void Clear(string? sharedName = null)
    {
        lock (_gate)
        {
            var prefix = Prefix(sharedName);
            foreach (var key in _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
            Save();
        }
    }

    public string? Get(string key, string? defaultValue = null, string? sharedName = null)
        => Read<string>(key, sharedName) ?? defaultValue;

    public bool Get(string key, bool defaultValue, string? sharedName = null)
        => Read<bool?>(key, sharedName) ?? defaultValue;

    public int Get(string key, int defaultValue, string? sharedName = null)
        => Read<int?>(key, sharedName) ?? defaultValue;

    public double Get(string key, double defaultValue, string? sharedName = null)
        => Read<double?>(key, sharedName) ?? defaultValue;

    public float Get(string key, float defaultValue, string? sharedName = null)
        => Read<float?>(key, sharedName) ?? defaultValue;

    public long Get(string key, long defaultValue, string? sharedName = null)
        => Read<long?>(key, sharedName) ?? defaultValue;

    public DateTime Get(string key, DateTime defaultValue, string? sharedName = null)
        => Read<DateTime?>(key, sharedName) ?? defaultValue;

    public void Set(string key, string? value, string? sharedName = null) => Write(key, sharedName, value);
    public void Set(string key, bool value, string? sharedName = null) => Write(key, sharedName, value);
    public void Set(string key, int value, string? sharedName = null) => Write(key, sharedName, value);
    public void Set(string key, double value, string? sharedName = null) => Write(key, sharedName, value);
    public void Set(string key, float value, string? sharedName = null) => Write(key, sharedName, value);
    public void Set(string key, long value, string? sharedName = null) => Write(key, sharedName, value);
    public void Set(string key, DateTime value, string? sharedName = null) => Write(key, sharedName, value);

    T? Read<T>(string key, string? sharedName)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(Key(key, sharedName), out var raw) || raw is null)
                return default;

            if (raw is JsonElement element)
                return element.Deserialize<T>();

            if (raw is T typed)
                return typed;

            return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
        }
    }

    void Write(string key, string? sharedName, object? value)
    {
        lock (_gate)
        {
            _values[Key(key, sharedName)] = value;
            Save();
        }
    }

    static string Key(string key, string? sharedName)
        => sharedName is null ? key : $"{sharedName}/{key}";

    static string Prefix(string? sharedName)
        => sharedName is null ? string.Empty : $"{sharedName}/";

    Dictionary<string, object?> Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new Dictionary<string, object?>(StringComparer.Ordinal);

            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                   ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            // Preferences must never take the app down; start from empty instead.
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(_values));
        }
        catch (Exception)
        {
            // Best-effort persistence.
        }
    }
}
