namespace TaoZip.BLL;

/// <summary>
/// 分卷工具：按大小把压缩包切分为 .001/.002… 分卷，或从分卷合并还原。
/// 兼容 Bandizip/7-Zip 常见的 .001 分卷命名。
/// </summary>
public static class VolumeHelper
{
    /// <summary>切分文件为 .001 分卷；totalBytes 为每卷字节数。</summary>
    public static List<string> SplitFile(string path, long totalBytes, bool keepOriginal = true)
    {
        if (totalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(totalBytes));
        var len = new FileInfo(path).Length;
        if (len <= totalBytes)
            throw new InvalidOperationException("文件小于分卷大小，无需分卷");

        var first = path + ".001";
        var parts = new List<string>();
        using (var src = File.OpenRead(path))
        {
            int index = 1;
            var buf = new byte[1 << 20];
            while (src.Position < src.Length)
            {
                var partPath = path + "." + index.ToString("D3");
                parts.Add(partPath);
                using var dst = File.Create(partPath);
                long remain = Math.Min(totalBytes, src.Length - src.Position);
                while (remain > 0)
                {
                    var read = src.Read(buf, 0, (int)Math.Min(buf.Length, remain));
                    if (read <= 0) break;
                    dst.Write(buf, 0, read);
                    remain -= read;
                }
                index++;
            }
        }
        if (!keepOriginal) File.Delete(path);
        return parts;
    }

    /// <summary>从 .001 分卷合并还原原文件。</summary>
    public static string MergeVolumes(string firstPartPath, bool keepParts = false)
    {
        if (!firstPartPath.EndsWith(".001"))
            throw new InvalidOperationException("请选择第一个分卷（.001）");
        var original = firstPartPath[..^4];
        if (File.Exists(original) && !keepParts)
            throw new InvalidOperationException("目标文件已存在：" + original);

        var parts = new List<string>();
        for (int i = 1; ; i++)
        {
            var p = original + "." + i.ToString("D3");
            if (!File.Exists(p)) break;
            parts.Add(p);
        }
        if (parts.Count == 0) throw new FileNotFoundException("找不到分卷文件");

        using var dst = File.Create(original);
        foreach (var p in parts)
        using (var s = File.OpenRead(p))
            s.CopyTo(dst, 1 << 20);
        if (!keepParts)
            foreach (var p in parts) File.Delete(p);
        return original;
    }

    /// <summary>判断是否为分卷序列的第一个卷。</summary>
    public static bool IsFirstVolume(string path) => path.EndsWith(".001");

    /// <summary>列出同一文件的全部分卷。</summary>
    public static List<string> ListVolumes(string firstPartPath)
    {
        var original = firstPartPath[..^4];
        var parts = new List<string>();
        for (int i = 1; ; i++)
        {
            var p = original + "." + i.ToString("D3");
            if (!File.Exists(p)) break;
            parts.Add(p);
        }
        return parts;
    }
}
