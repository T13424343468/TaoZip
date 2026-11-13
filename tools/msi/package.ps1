# 生成 TaoZip 的 Windows 安装器 (.msi)
# 特性: 微信风格自绘单页向导 (说明+同意勾选+安装目录+桌面快捷方式 同页完成);
#       桌面快捷方式可选勾选, 开始菜单默认创建; 同版本可覆盖安装; 升级卸载。
# 用法: powershell -File package.ps1 -PublishDir <publish目录> -OutDir <out目录>
# WiX 工具: 包内便携优先 (tools/msi/wix), 缺失时回落 PATH 中的 wix (dotnet tool)
param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [string]$OutDir = "out"
)
$ScriptVersion = "v1.02-1106"
Write-Host "打包脚本版本: $ScriptVersion (若日志无此行, 说明运行的是旧脚本, 请全新目录解压重试)"
$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path $PublishDir).Path
if (-not (Test-Path (Join-Path $publishRoot 'TaoZip.exe'))) {
    throw "发布目录里没找到 TaoZip.exe: $PublishDir"
}
# ---- 枚举发布文件: 组件直接嵌在各自目录内(经典写法, 兼容 WiX4) ----
$allFiles = Get-ChildItem -Path $publishRoot -Recurse -File
$script:seq = 0
$script:coreRefs = @()
$dirFiles = @{}
foreach ($f in $allFiles) {
    $rel = $f.FullName.Substring($publishRoot.Length).TrimStart('\')
    $parts = $rel -split '\\'
    $dir = if ($parts.Length -gt 1) { ($parts[0..($parts.Length-2)] -join '\') } else { '' }
    if (-not $dirFiles.ContainsKey($dir)) { $dirFiles[$dir] = @() }
    $dirFiles[$dir] += [pscustomobject]@{ Src = $f.FullName }
}
function New-CompXml([string]$src) {
    $script:seq++
    $id = "cmp$script:seq"
    $script:coreRefs += $id
    $esc = [System.Security.SecurityElement]::Escape($src)
    return "<Component Id=`"$id`"><File Source=`"$esc`" KeyPath=`"yes`" /></Component>"
}
function Get-SubDirXml([string]$prefix) {
    $xml = ''
    $childDirs = @($dirFiles.Keys | Where-Object {
        if ($prefix -eq '') { ($_ -ne '') -and (($_ -split '\\').Count -eq 1) }
        else { $_.StartsWith($prefix + '\') -and ((($_.Substring($prefix.Length + 1)) -split '\\').Count -eq 1) }
    })
    foreach ($d in $childDirs) {
        $script:seq++
        $id = "sdir$script:seq"
        $name = if ($prefix -eq '') { $d } else { $d.Substring($prefix.Length + 1) }
        $inner = Get-SubDirXml $d
        $fileXml = ''
        foreach ($entry in $dirFiles[$d]) { $fileXml += New-CompXml $entry.Src }
        $xml += "<Directory Id=`"$id`" Name=`"$name`">$inner$fileXml</Directory>"
    }
    return $xml
}
$rootFilesXml = ''
foreach ($entry in $dirFiles['']) { $rootFilesXml += New-CompXml $entry.Src }
$subDirXml = Get-SubDirXml ''
$coreRefsXml = ($script:coreRefs | ForEach-Object { "<ComponentRef Id=`"$_`" />" }) -join ''
Write-Host "安装内容编目完成: $($script:coreRefs.Count) 个文件组件"
# 安装包版本：随发布递增，覆盖安装（MajorUpgrade）依赖它判断新旧
# 默认取主工程 FileVersion，保持安装包与应用版本一致；环境变量仍可覆盖
$csprojFile = Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'TaoZip.PC\ui\TaoZip.PC.App\TaoZip.PC.App.csproj'
$csprojRaw = Get-Content $csprojFile -Raw
$csprojVer = if ($csprojRaw -match '<FileVersion>([^<]+)</FileVersion>') { $Matches[1] } elseif ($csprojRaw -match '<Version>([^<]+)</Version>') { $Matches[1] } else { '1.0.4.0' }
if ($csprojVer -match '^\d+\.\d+\.\d+$') { $csprojVer = "$csprojVer.0" }
Write-Host "主工程版本: $csprojVer"
$PkgVersion = if ($env:TAOZIP_PKG_VERSION) { $env:TAOZIP_PKG_VERSION } else { $csprojVer }
$wxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
    <Package Codepage="936" Name="TaoZip" Manufacturer="Tao" Version="$PkgVersion"
           UpgradeCode="7C3F2A9E-5D41-4B8F-9E6A-2C8B1D4F0A73" Scope="perMachine" Compressed="yes">
    <MajorUpgrade DowngradeErrorMessage="已安装了更新版本的 TaoZip，如需重装请先在“应用”中卸载。" AllowSameVersionUpgrades="yes" />
    <MediaTemplate EmbedCab="yes" />
    <CustomAction Id="SetDefaultInstallDir" Property="INSTALLFOLDER" Value="D:\Program Files\TaoZip\" Execute="immediate" Return="check" />
    <!-- 装/卸/升级前结束运行中的程序（进程残留专项，TaoDown/TaoPad 同款写法，真机验证过）:
         属性转发定位 taskkill 避免 WIX1077; Return=ignore: 程序没在运行时 taskkill 报错不影响安装 -->
    <CustomAction Id="SetKillCmd" Property="KILLCMD" Value="[SystemFolder]taskkill.exe" />
    <CustomAction Id="KillRunningTaoZip" Property="KILLCMD"
                  ExeCommand="/f /t /im TaoZip.exe"
                  Execute="immediate" Return="ignore" />
    <InstallExecuteSequence>
      <Custom Action="SetKillCmd" After="InstallValidate" />
      <Custom Action="KillRunningTaoZip" After="SetKillCmd" />
      <Custom Action="SetDefaultInstallDir" Before="CostFinalize" />
    </InstallExecuteSequence>
    <Icon Id="TaoZipIco" SourceFile="$publishRoot\app.ico" />
    <Property Id="ARPNOREPAIR" Value="1" />
    <Property Id="ARPPRODUCTICON" Value="TaoZipIco" />
    <Property Id="DESKTOP_SHORTCUT" Value="1" />
    <UI Id="TaoZipInstallUI">
      <TextStyle Id="DlgFont" FaceName="微软雅黑" Size="9" />
      <TextStyle Id="TitleFont" FaceName="微软雅黑" Size="13" Bold="yes" />
      <Property Id="DefaultUIFont" Value="DlgFont" />
      <Dialog Id="MainDlg" Width="440" Height="270" Title="[ProductName] 安装程序">
        <Control Id="Title" Type="Text" X="20" Y="12" Width="400" Height="22" Text="{\TitleFont}安装 TaoZip" />
        <Control Id="LicenseText" Type="Text" X="20" Y="42" Width="400" Height="92" Text="TaoZip 1.0，免费使用、无广告、无捆绑。本软件按“现状”提供：请仅将它用于合法合规的文件压缩与解压用途，作者不对使用本软件产生的任何损失承担责任。" />
        <Control Id="AcceptCheck" Type="CheckBox" X="20" Y="140" Width="400" Height="16" Property="LICENSE_ACCEPTED" CheckBoxValue="1" Text="我已阅读并同意以上说明" />
        <Control Id="SepLine" Type="Line" X="20" Y="164" Width="400" Height="1" />
        <Control Id="DirLabel" Type="Text" X="20" Y="174" Width="400" Height="14" Text="安装目录（可直接输入，不存在的文件夹会自动创建）：" />
        <Control Id="DirEdit" Type="PathEdit" X="20" Y="192" Width="310" Height="17" Property="INSTALLFOLDER" />
        <Control Id="DeskCheck" Type="CheckBox" X="20" Y="218" Width="320" Height="16" Property="DESKTOP_SHORTCUT" CheckBoxValue="1" Text="创建桌面快捷方式">
          <Publish Event="Remove" Value="DeskShortcutF" Condition="NOT DESKTOP_SHORTCUT" />
          <Publish Event="AddLocal" Value="DeskShortcutF" Condition="DESKTOP_SHORTCUT" />
        </Control>
        <Control Id="InstallNow" Type="PushButton" X="340" Y="240" Width="80" Height="24" Default="yes" Text="立即安装">
          <Publish Event="SpawnDialog" Value="AcceptWarnDlg" Condition="NOT LICENSE_ACCEPTED" Order="1" />
          <Publish Event="NewDialog" Value="VerifyReadyDlg" Condition="LICENSE_ACCEPTED" Order="2" />
        </Control>
        <Control Id="CancelBtn" Type="PushButton" X="340" Y="268" Width="80" Height="18" Cancel="yes" Text="取消">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
      </Dialog>
      <Dialog Id="VerifyReadyDlg" Width="440" Height="130" Title="[ProductName] 安装程序">
        <Control Id="ReadyText" Type="Text" X="20" Y="30" Width="400" Height="24" Text="准备安装 TaoZip，点击“安装”开始。" />
        <Control Id="BackBtn" Type="PushButton" X="270" Y="95" Width="70" Height="22" Text="返回">
          <Publish Event="NewDialog" Value="MainDlg" />
        </Control>
        <Control Id="InstallBtn" Type="PushButton" X="350" Y="95" Width="70" Height="22" Default="yes" Text="安装">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
      </Dialog>
      <Dialog Id="ProgressDlg" Width="440" Height="110" Title="[ProductName] 安装程序" Modeless="yes">
        <Control Id="ProgressText" Type="Text" X="20" Y="24" Width="400" Height="14" Text="正在安装，请稍候……" />
        <Control Id="ProgressBar" Type="ProgressBar" X="20" Y="48" Width="400" Height="14" Text="正在安装" />
        <Control Id="CancelBtn" Type="PushButton" X="350" Y="78" Width="70" Height="20" Cancel="yes" Text="取消">
          <Publish Event="SpawnDialog" Value="CancelDlg" />
        </Control>
      </Dialog>
      <Dialog Id="AcceptWarnDlg" Width="280" Height="90" Title="[ProductName] 安装程序">
        <Control Id="WarnText" Type="Text" X="20" Y="18" Width="240" Height="18" Text="请先勾选“我已阅读并同意以上说明”。" />
        <Control Id="OkBtn" Type="PushButton" X="110" Y="55" Width="60" Height="20" Default="yes" Text="确定">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
      </Dialog>
      <Dialog Id="CancelDlg" Width="280" Height="90" Title="[ProductName] 安装程序">
        <Control Id="CancelText" Type="Text" X="20" Y="18" Width="240" Height="18" Text="确定要取消安装吗？" />
        <Control Id="YesBtn" Type="PushButton" X="100" Y="55" Width="60" Height="20" Default="yes" Text="是">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
        <Control Id="NoBtn" Type="PushButton" X="170" Y="55" Width="60" Height="20" Text="否">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
      </Dialog>
      <Dialog Id="ExitDialog" Width="440" Height="130" Title="[ProductName] 安装程序">
        <Control Id="DoneText" Type="Text" X="20" Y="30" Width="400" Height="24" Text="TaoZip 安装完成，双击桌面图标即可使用。" />
        <Control Id="FinishBtn" Type="PushButton" X="350" Y="95" Width="70" Height="22" Default="yes" Text="完成">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
      </Dialog>
      <Dialog Id="UserExitDlg" Width="440" Height="130" Title="[ProductName] 安装程序">
        <Control Id="UserExitText" Type="Text" X="20" Y="30" Width="400" Height="24" Text="安装已取消，本机未做任何更改。" />
        <Control Id="FinishBtn" Type="PushButton" X="350" Y="95" Width="70" Height="22" Default="yes" Text="完成">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
      </Dialog>
      <Dialog Id="FatalErrorDlg" Width="440" Height="130" Title="[ProductName] 安装程序">
        <Control Id="ErrorText" Type="Text" X="20" Y="30" Width="400" Height="24" Text="安装未完成，请重试或联系开发者。" />
        <Control Id="FinishBtn" Type="PushButton" X="350" Y="95" Width="70" Height="22" Default="yes" Text="完成">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
      </Dialog>
      <InstallUISequence>
        <Custom Action="SetDefaultInstallDir" Before="CostFinalize" />
        <Show Dialog="MainDlg" Sequence="1250" />
        <Show Dialog="ProgressDlg" Before="ExecuteAction" />
        <Show Dialog="ExitDialog" OnExit="success" />
        <Show Dialog="UserExitDlg" OnExit="cancel" />
        <Show Dialog="FatalErrorDlg" OnExit="error" />
      </InstallUISequence>
    </UI>
    <StandardDirectory Id="DesktopFolder">
      <Component Id="DeskShortcutC">
        <Shortcut Id="DesktopLnk" Icon="TaoZipIco" Name="TaoZip" Target="[INSTALLFOLDER]TaoZip.exe" WorkingDirectory="INSTALLFOLDER" />
        <RegistryValue Root="HKCU" Key="Software\Tao\TaoZip" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </StandardDirectory>
    <StandardDirectory Id="ProgramFiles6432Folder">
      <Directory Id="INSTALLFOLDER" Name="TaoZip">$subDirXml$rootFilesXml</Directory>
    </StandardDirectory>
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="ApplicationProgramsFolder">
        <Component Id="MenuShortcutC">
          <Shortcut Id="MenuLnk" Icon="TaoZipIco" Name="TaoZip" Target="[INSTALLFOLDER]TaoZip.exe" WorkingDirectory="INSTALLFOLDER" />
          <RemoveFolder Id="RemoveMenuDir" On="uninstall" />
          <RegistryValue Root="HKCU" Key="Software\Tao\TaoZip" Name="MenuShortcut" Type="integer" Value="1" KeyPath="yes" />
        </Component>
      </Directory>
    </StandardDirectory>
    <Feature Id="Core" Title="TaoZip 主程序" Description="压缩解压本体（必装）" Level="1" AllowAbsent="no">$coreRefsXml
      <ComponentRef Id="MenuShortcutC" />
    </Feature>
    <Feature Id="DeskShortcutF" Title="创建桌面快捷方式" Level="1">
      <ComponentRef Id="DeskShortcutC" />
    </Feature>
  </Package>
</Wix>
"@
$wxsPath = Join-Path $env:TEMP "TaoZip.Package.wxs"
[System.IO.File]::WriteAllText($wxsPath, $wxs, (New-Object System.Text.UTF8Encoding($true)))
# WiX 工具: 包内便携优先 (tools/msi/wix), 无则回落 PATH (dotnet tool install wix)
$wixExe = Join-Path $PSScriptRoot "wix\wix.exe"
if (-not (Test-Path $wixExe)) {
    $wixOnPath = Get-Command wix.exe -ErrorAction SilentlyContinue
    if (-not $wixOnPath) { $wixOnPath = Get-Command wix -ErrorAction SilentlyContinue }
    if (-not $wixOnPath) { throw "找不到 WiX 打包工具: 包内 tools/msi/wix 缺失且 PATH 无 wix (CI 可 dotnet tool install --global wix)" }
    $wixExe = $wixOnPath.Source
}
$msi = Join-Path $OutDir "TaoZip-$PkgVersion-x64.msi"
if (Test-Path $msi) { Remove-Item $msi -Force }
Write-Host ("打包工具: $wixExe (" + (Get-Item $wixExe).Length + " bytes @ " + (Get-Item $wixExe).LastWriteTime + ")")
& $wixExe build $wxsPath -arch x64 -pdbtype none -o $msi 2>&1 | Tee-Object -FilePath (Join-Path $env:TEMP "TaoZip.msi-build.log")
if ($LASTEXITCODE -ne 0) {
    Write-Host "打包配置已保存: $wxsPath (排错用, 请把此文件与上方完整报错一并发出)"
    throw "WiX 构建 msi 失败 (请确认本机已安装 .NET 8)"
}
Write-Host "安装器已生成: $msi"
