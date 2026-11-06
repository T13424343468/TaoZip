using System.Text;

namespace TaoZip.Model;

/// <summary>压缩包内的一个条目（文件或目录）。</summary>
public sealed class ArchiveEntry
{
    /// <summary>内部路径，统一使用 '/' 分隔。</summary>
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public bool IsEncrypted { get; init; }
    public DateTime Modified { get; init; }
}

/// <summary>压缩包概览信息。</summary>
public sealed class ArchiveInfo
{
    public required string FilePath { get; init; }
    /// <summary>格式标识：zip / 7z / rar / tar / gz / tgz / bz2 / xz。</summary>
    public required string Format { get; init; }
    public required long TotalSize { get; init; }
    public required IReadOnlyList<ArchiveEntry> Entries { get; init; }
    /// <summary>zip 全局注释（其他格式为空）。</summary>
    public string? Comment { get; init; }
    public bool IsEncrypted => Entries.Any(e => e.IsEncrypted);
    public long UncompressedTotal => Entries.Sum(e => e.Size);
    /// <summary>顶层文件/文件夹数，用于智能解压判断。</summary>
    public int TopLevelCount => Entries
        .Select(e => e.Path.TrimEnd('/'))
        .Where(p => p.Length > 0)
        .Select(p => p.Contains('/') ? p[..p.IndexOf('/')] : p)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
}

/// <summary>可创建的压缩格式。</summary>
public enum CompressFormat
{
    Zip,
    Tar,
    Tgz,   // tar.gz
    Gz,    // 单文件 gzip
}

/// <summary>zip 文件名的字符编码。</summary>
public static class FileNameEncodings
{
    public static readonly string[] Supported = { "utf-8", "gbk" };
    public static Encoding Resolve(string? name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(string.IsNullOrWhiteSpace(name) ? "utf-8" : name); }
        catch { return Encoding.UTF8; }
    }
}

/// <summary>压缩请求。</summary>
public sealed class CompressRequest
{
    /// <summary>要打包的文件或目录（可多个）。</summary>
    public required IReadOnlyList<string> SourcePaths { get; init; }
    /// <summary>输出压缩包路径。</summary>
    public required string DestPath { get; init; }
    public CompressFormat Format { get; init; } = CompressFormat.Zip;
    /// <summary>1-9；zip/tar 有效。</summary>
    public int Level { get; init; } = 6;
    /// <summary>zip 加密密码；为空则不加密。</summary>
    public string? Password { get; init; }
    /// <summary>zip 注释。</summary>
    public string? Comment { get; init; }
    /// <summary>zip 文件名编码，默认 utf-8。</summary>
    public string FileNameEncoding { get; init; } = "utf-8";
    /// <summary>大于 0 时压缩完成后按此大小（MB）切分为 .001/.002 分卷。</summary>
    public int VolumeSizeMb { get; init; }
}

/// <summary>解压请求。</summary>
public sealed class ExtractRequest
{
    public required string ArchivePath { get; init; }
    public required string DestDir { get; init; }
    public string? Password { get; init; }
    /// <summary>仅解压这些条目（内部路径）；为空解压全部。</summary>
    public IReadOnlyCollection<string>? EntryPaths { get; init; }
    /// <summary>已存在文件的处理：false=覆盖。</summary>
    public bool SkipExisting { get; init; }
    /// <summary>zip 文件名编码，默认 utf-8。</summary>
    public string FileNameEncoding { get; init; } = "utf-8";
}

/// <summary>任务状态。</summary>
public enum TaskState { Queued, Running, Done, Failed, Cancelled }

/// <summary>任务种类。</summary>
public enum TaskKind { Extract, Compress, Test }

/// <summary>一个后台压缩任务的运行记录。</summary>
public sealed class ArchiveTaskRecord
{
    public required string Id { get; init; }
    public required TaskKind Kind { get; set; }
    public required string Title { get; set; }
    public TaskState State { get; set; } = TaskState.Queued;
    public int ProgressPercent { get; set; }
    public string? Message { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
}

/// <summary>进度事件。</summary>
public sealed class ArchiveProgress
{
    public required string TaskId { get; init; }
    public int Percent { get; init; }
    public string? CurrentItem { get; init; }
}

/// <summary>历史记录。</summary>
public sealed class HistoryRecord
{
    public required string Id { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public required TaskKind Kind { get; init; }
    public required string ArchivePath { get; init; }
    public required string DestPath { get; init; }
    public int FileCount { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
}

/// <summary>应用设置。</summary>
public sealed class AppSettings
{
    public bool SmartExtract { get; set; } = true;
    public bool OverwriteExisting { get; set; } = false;
    public CompressFormat DefaultFormat { get; set; } = CompressFormat.Zip;
    public int DefaultLevel { get; set; } = 6;
    public string DefaultEncoding { get; set; } = "utf-8";
    public int VolumeSizeMb { get; set; }
    public string LastBrowseDir { get; set; } = "";
    public int MaxHistory { get; set; } = 500;
}
