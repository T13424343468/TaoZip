using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TaoZip.PC.App;

/// <summary>极简托盘图标: 显示/气泡提示/移除, 点击图标唤起窗口 (Win32 Shell_NotifyIcon)。</summary>
/// <remarks>托盘消息托管在宿主窗句柄上 (子类化转发)。TaoDBM 真机教训 (2026-11-03): 自建消息窗口
/// 在真机上可能创建失败, 托盘整条链路失效——已整体移除该环节, 回调经宿主窗过程转发;
/// explorer 重启后按 TaskbarCreated 自动补挂。</remarks>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000;   // WM_APP
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int GWLP_WNDPROC = -4;
    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessageW("TaskbarCreated");   // explorer 重启后补挂

    private readonly nint _ownerHwnd;   // 宿主窗句柄: 托盘回调唯一标识 + 消息入口 (不再自建窗口)
    private readonly nint _prevProc;    // 子类化前宿主窗原窗口过程
    private readonly WndProcDelegate _procDelegate;   // 防 GC
    private readonly string _tip;
    private readonly Action _onClick;
    private nint _hIcon;
    private bool _added;
    private bool _disposed;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int idx, nint value);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProcW(nint proc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetModuleFileNameW(nint module, StringBuilder name, int size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int idx, nint[] large, nint[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    public TrayIcon(nint ownerHwnd, string tip, Action onClick)
    {
        _ownerHwnd = ownerHwnd;
        _tip = tip;
        _onClick = onClick;
        _procDelegate = WndProc;
        // 子类化宿主窗接管消息: 不再自建消息窗口 (真机创建失败根因), 托盘回调经宿主窗过程转发
        _prevProc = SetWindowLongPtrW(_ownerHwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_procDelegate));

        var exe = new StringBuilder(512);
        _ = GetModuleFileNameW(0, exe, 512);
        var large = new nint[1];
        _ = ExtractIconExW(exe.ToString(), 0, large, null, 1);
        _hIcon = large.Length > 0 ? large[0] : 0;

        AddToShell();
    }

    /// <summary>向 Shell 注册托盘图标; hWnd 必须随消息一起上送, 否则 NIM_ADD 静默失败。</summary>
    private void AddToShell()
    {
        if (_disposed || _ownerHwnd == 0) return;
        var nid = NewData();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        _added = Shell_NotifyIconW(NIM_ADD, ref nid);
        if (_added)
        {
            nid.uVersion = 4;   // 通知图标新版回调语义
            _ = Shell_NotifyIconW(NIM_SETVERSION, ref nid);
        }
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _ownerHwnd,   // 标识托盘项的唯一键: 增/改/删/气泡都必须带上, 缺了 NIM_ADD 直接静默失败
        uID = 1,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _hIcon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (!_disposed)
        {
            if (msg == WM_APP_TRAY)
            {
                var m = unchecked((int)lParam) & 0xFFFF;
                if (m == WM_LBUTTONUP || m == WM_LBUTTONDBLCLK) _onClick();
            }
            else if (msg == WM_TASKBARCREATED && !_added)
            {
                AddToShell();   // explorer 重启后自动补挂
            }
        }
        return CallWindowProcW(_prevProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>弹出气泡提示 (在已显示的托盘图标上)。</summary>
    public void Balloon(string title, string text)
    {
        if (!_added || _disposed) return;
        var nid = NewData();
        nid.uFlags = NIF_INFO;
        nid.szInfo = text;
        nid.szInfoTitle = title;
        _ = Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            var nid = NewData();
            _ = Shell_NotifyIconW(NIM_DELETE, ref nid);
            _added = false;
        }
        if (_prevProc != 0) _ = SetWindowLongPtrW(_ownerHwnd, GWLP_WNDPROC, _prevProc);   // 还原宿主窗过程
        if (_hIcon != 0) { _ = DestroyIcon(_hIcon); _hIcon = 0; }
        GC.KeepAlive(_procDelegate);
    }
}
