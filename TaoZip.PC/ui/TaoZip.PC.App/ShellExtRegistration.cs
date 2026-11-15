using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace TaoZip.PC.App;

/// <summary>
/// 资源管理器动态按钮 (2026-11-15 老板令): 原生 shell 扩展 TaoZipShellext.dll 的
/// 稀疏包注册与清理。注册在应用首启后台静默完成 (失败不影响主功能); 卸载由
/// 主窗退出走 SparseCleanUp, 保持「装得上、卸得净」。
/// </summary>
internal static class ShellExtRegistration
{
    private const string PackageDisplayName = "TaoZip ShellExt";
    private const string PackageIdName = "Tao.TaoZip.ShellExt";
    private const string PackageIdPublisher = "CN=Tao";
    private const string ManifestFileName = "TaoZip.ShellExt.AppxManifest.xml";

    private static string DllPath =>
        Path.Combine(AppContext.BaseDirectory, "TaoZipShellext.dll");

    public static void RegisterIfReady()
    {
        // 后台一次, 不阻塞启动 (以人为本: 按钮是增强, 不是门槛)。
        _ = Task.Run(async () =>
        {
            try { await EnsureRegisteredAsync(); }
            catch (Exception ex)
            {
                ThemeService.LogEvent($"ShellExt注册失败: {ex.GetType().Name} {ex.Message}");
            }
        });
    }

    public static async Task EnsureRegisteredAsync()
    {
        if (!File.Exists(DllPath)) return; // 老包无 DLL, 不注册

        var manifestPath = WriteManifest();
        var uri = new Uri(manifestPath);
        var mgr = new PackageManager();

        if (mgr.FindPackagesForCurrentUser(PackageIdPublisher, PackageIdName).GetEnumerator().MoveNext())
            return; // 已注册, 幂等退出

        var result = await mgr.AddPackageByUriAsync(
            uri, new AddPackageOptions { ExternalLocationUri = new Uri(Path.GetDirectoryName(manifestPath)!) });
        if (result.IsRegistered) return;
        ThemeService.LogEvent($"ShellExt注册未成: {result.ErrorText}");
    }

    public static async Task UnregisterAsync()
    {
        try
        {
            var mgr = new PackageManager();
            foreach (var p in mgr.FindPackagesForCurrentUser(PackageIdPublisher, PackageIdName))
                await mgr.RemovePackageAsync(p.Id.FullName);
        }
        catch (Exception ex)
        {
            ThemeService.LogEvent($"ShellExt反注册失败: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>生成临时稀疏包清单 (装到数据目录, 不污染安装目录)。</summary>
    private static string WriteManifest()
    {
        var dir = TaoPaths.DataDir("TaoZip");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ManifestFileName);
        var ver = typeof(ShellExtRegistration).Assembly.GetName().Version?.ToString(3) ?? "1.0.7";
        var xml = $"""<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap10">
  <Identity Name="{PackageIdName}" Publisher="{PackageIdPublisher}" Version="{ver}.0" />
  <Properties>
    <DisplayName>{PackageDisplayName}</DisplayName>
    <PublisherDisplayName>Tao</PublisherDisplayName>
    <Logo>app-icon.png</Logo>
    <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
  </Properties>
  <Applications>
    <Application Id="TaoZipShellExt" Executable="TaoZip.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="{PackageDisplayName}" Description="TaoZip File Explorer integration"
                          Square150x150Logo="app-icon.png" Square44x44Logo="app-icon.png"
                          BackgroundColor="transparent" />
      <Extensions>
        <com:Extension Category="windows.comServer" xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10">
          <com:ComServer>
            <com:ExeServerExecutable>TaoZipShellext.dll</com:ExeServerExecutable>
            <com:Class Id="C1D2E3F4-5A6B-7C8D-9E0F-1A2B3C4D5E6F" DisplayName="TaoZip Extract"/>
            <com:Class Id="D2E3F4A5-6B7C-8D9E-0F1A-2B3C4D5E6F7A" DisplayName="TaoZip Compress"/>
          </com:ComServer>
        </com:Extension>
        <uap3:Extension Category="windows.fileExplorerContextMenus"
                        xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3">
          <uap3:FileExplorerContextMenus>
            <uap3:ItemType Type="*">
              <uap3:Verb Id="TaoZipExtract" Clsid="C1D2E3F4-5A6B-7C8D-9E0F-1A2B3C4D5E6F" />
              <uap3:Verb Id="TaoZipCompress" Clsid="D2E3F4A5-6B7C-8D9E-0F1A-2B3C4D5E6F7A" />
            </uap3:ItemType>
          </uap3:FileExplorerContextMenus>
        </uap3:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>""";
        File.WriteAllText(path, xml);
        return path;
    }
}
