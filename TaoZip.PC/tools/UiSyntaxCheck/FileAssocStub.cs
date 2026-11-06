// ==== FileAssoc 桩: 真实实现用注册表, 桩仅保持同签名供语法检查 ====
namespace TaoZip.PC.App
{
    public static class FileAssoc
    {
        public static readonly string[] Extensions =
            { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz" };
        public static bool IsDefault(string ext) => false;
        public static bool HasContextMenu(string ext) => false;
        public static bool AllContextMenu() => false;
        public static bool HasCompressMenu() => false;
        public static void SetDefault(string ext) { }
        public static void RemoveDefault(string ext) { }
        public static void SetContextMenuAll() { }
        public static void RemoveContextMenuAll() { }
        public static void SetCompressMenuAll() { }
        public static void RemoveCompressMenuAll() { }
        public static void RepairStaleCommands() { }
        public static void RefreshExplorer() { }
    }
}
