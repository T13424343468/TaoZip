using System;
using System.IO;
using System.Threading.Tasks;
using TaoZip.Core;
using Windows.Management.Deployment;

namespace TaoZip.PC.App;

/// <summary>
/// 资源管理器动态按钮 (2026-11-15 老板令): 原生 shell 扩展 TaoZipShellext.dll 的
/// 稀疏包注册与清理。注册在应用首启后台静默完成 (失败不影响主功能); 卸载由
/// 主窗退出走 RemovePackageAsync, 保持「装得上、卸得净」。
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

        var ver = typeof(ShellExtRegistration).Assembly.GetName().Version?.ToString(3) ?? "1.0.7";
        var manifestPath = WriteManifest(ver);
        var markerPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "ShellExtSparse.version");
        // 幂等: 同版本已注册过就不再碰 PackageManager (查询接口部分主机需提权, 用落盘标记替代)。
        try { if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == ver) return; }
        catch (IOException) { /* 标记读不动就按未注册走, 重复注册无害 */ }

        // 外部位置 = 安装目录 (DLL 与 app-icon.png 都在 exe 旁), 清单文件落数据目录。
        var options = new AddPackageOptions
        {
            ExternalLocationUri = new Uri(AppContext.BaseDirectory),
            AllowUnsigned = true
        };
        var mgr = new PackageManager();
        var result = await mgr.AddPackageByUriAsync(new Uri(manifestPath), options);
        if (result.IsRegistered)
        {
            try { File.WriteAllText(markerPath, ver); } catch (IOException) { /* 标记写不进不影响注册 */ }
            return;
        }
        ThemeService.LogEvent($"ShellExt注册未成: {result.ErrorText}");
    }

    public static async Task UnregisterAsync()
    {
        try
        {
            var mgr = new PackageManager();
            foreach (var p in mgr.FindPackages())
            {
                if (!string.Equals(p.Id.Name, PackageIdName, StringComparison.OrdinalIgnoreCase)) continue;
                await mgr.RemovePackageAsync(p.Id.FullName);
            }
        }
        catch (Exception ex)
        {
            ThemeService.LogEvent($"ShellExt反注册失败: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>生成稀疏包清单 (装到数据目录, 引用安装目录的 DLL 与图标)。</summary>
    private static string WriteManifest(string ver)
    {
        var dir = TaoPaths.DataDir("TaoZip");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ManifestFileName);
        // Schema per MS Learn: com:SurrogateServer + desktop4/desktop5 fileExplorerContextMenus;
        // sparse package: uap10:AllowExternalContent + mediumIL/win32App application.
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                     xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
                     xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
                     xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
                     xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
                     xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5"
                     IgnorableNamespaces="uap10 com desktop4 desktop5">
              <Identity Name="{PackageIdName}" Publisher="{PackageIdPublisher}" Version="{ver}.0" />
              <Properties>
                <DisplayName>{PackageDisplayName}</DisplayName>
                <PublisherDisplayName>Tao</PublisherDisplayName>
                <Logo>app-icon.png</Logo>
                <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
              </Properties>
              <Applications>
                <Application Id="TaoZipShellExt" Executable="TaoZip.exe"
                             uap10:TrustLevel="mediumIL" uap10:RuntimeBehavior="win32App">
                  <uap:VisualElements AppListEntry="none" DisplayName="{PackageDisplayName}"
                                      Description="TaoZip File Explorer integration"
                                      Square150x150Logo="app-icon.png" Square44x44Logo="app-icon.png"
                                      BackgroundColor="transparent" />
                  <Extensions>
                    <com:Extension Category="windows.comServer">
                      <com:ComServer>
                        <com:SurrogateServer DisplayName="TaoZip Shell Extension">
                          <com:Class Id="C1D2E3F4-5A6B-7C8D-9E0F-1A2B3C4D5E6F" Path="TaoZipShellext.dll" ThreadingModel="STA" />
                          <com:Class Id="D2E3F4A5-6B7C-8D9E-0F1A-2B3C4D5E6F7A" Path="TaoZipShellext.dll" ThreadingModel="STA" />
                        </com:SurrogateServer>
                      </com:ComServer>
                    </com:Extension>
                    <desktop4:Extension Category="windows.fileExplorerContextMenus">
                      <desktop4:FileExplorerContextMenus>
                        <desktop5:ItemType Type="*">
                          <desktop5:Verb Id="TaoZipExtract" Clsid="C1D2E3F4-5A6B-7C8D-9E0F-1A2B3C4D5E6F" />
                          <desktop5:Verb Id="TaoZipCompress" Clsid="D2E3F4A5-6B7C-8D9E-0F1A-2B3C4D5E6F7A" />
                        </desktop5:ItemType>
                      </desktop4:FileExplorerContextMenus>
                    </desktop4:Extension>
                  </Extensions>
                </Application>
              </Applications>
              <Capabilities>
                <rescap:Capability Name="runFullTrust" />
              </Capabilities>
            </Package>
            """;
        File.WriteAllText(path, xml);
        return path;
    }
}
