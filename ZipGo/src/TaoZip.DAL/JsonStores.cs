using System.Text.Json;
using System.Text.Json.Serialization;
using TaoZip.Model;

namespace TaoZip.DAL;

/// <summary>JSON 文件存储基类：原子写入 + 损坏兜底。</summary>
public abstract class JsonStore<T> where T : class
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    protected JsonStore(string path) => _path = path;

    protected T? LoadRaw()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch { return null; } // 损坏文件当不存在处理
    }

    protected void SaveRaw(T data)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }
}

/// <summary>历史记录存储。</summary>
public sealed class HistoryStore : JsonStore<List<HistoryRecord>>
{
    private readonly int _max;
    private List<HistoryRecord> _cache;

    public HistoryStore(string path, int max = 500) : base(path)
    {
        _max = Math.Max(20, max);
        _cache = LoadRaw() ?? new List<HistoryRecord>();
    }

    public IReadOnlyList<HistoryRecord> All => _cache;

    public void Add(HistoryRecord record)
    {
        _cache.Add(record);
        if (_cache.Count > _max)
            _cache = _cache.Skip(_cache.Count - _max).ToList();
        SaveRaw(_cache);
    }

    public void Clear()
    {
        _cache.Clear();
        SaveRaw(_cache);
    }
}

/// <summary>设置存储。</summary>
public sealed class SettingsStore : JsonStore<AppSettings>
{
    private const string DefaultPath = "taozip_settings.json";
    private readonly string _path;

    public SettingsStore(string? path = null)
        : base(path ?? Path.Combine(AppContext.BaseDirectory, DefaultPath))
        => _path = path ?? Path.Combine(AppContext.BaseDirectory, DefaultPath);

    public AppSettings Load() => LoadRaw() ?? new AppSettings();

    public void Save(AppSettings settings) => SaveRaw(settings);
}
