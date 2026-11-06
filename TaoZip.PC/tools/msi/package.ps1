# 生成 TaoZip 的 Windows 安装器 (.msi)
# 特性: 自定义安装目录 + 功能树勾选(桌面/开始菜单快捷方式) + 升级卸载
# 用法: powershell -File package.ps1 -PublishDir <publish目录> -OutDir <out目录>
# 离线打包: 工具已随包内置 (tools/msi/wix), 无需联网
param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [string]$OutDir = "out"
)
$ScriptVersion = "v91-1106"
Write-Host "打包脚本版本: $ScriptVersion (若日志无此行, 说明运行的是旧脚本, 请全新目录解压重试)"
$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path $PublishDir).Path
if (-not (Test-Path (Join-Path $publishRoot 'TaoZip.exe'))) {
    throw "发布目录里没找到 TaoZip.exe: $PublishDir"
}

# ---- 枚举发布文件: 目录树(支持多级) + 组件清单 ----
$allFiles = Get-ChildItem -Path $publishRoot -Recurse -File
$script:dirId = 0
$script:dirRefs = @{}
$tree = [ordered]@{}
foreach ($f in $allFiles) {
    $rel = $f.FullName.Substring($publishRoot.Length).TrimStart('\')
    $parts = $rel -split '\\'
    $node = $tree
    for ($i = 0; $i -lt $parts.Length - 1; $i++) {
        $seg = $parts[$i]
        if (-not $node.Contains($seg)) { $node[$seg] = [ordered]@{} }
        $node = $node[$seg]
    }
}
function Get-DirXml($node, [string]$prefix) {
    $xml = ""
    foreach ($seg in $node.Keys) {
        $script:dirId++
        $id = "SUBDIR$script:dirId"
        $childPath = if ($prefix) { "$prefix\$seg" } else { $seg }
        $script:dirRefs[$childPath] = $id
        $inner = Get-DirXml $node[$seg] $childPath
        $xml += "<Directory Id=`"$id`" Name=`"$seg`">$inner</Directory>"
    }
    return $xml
}
$dirDefs = Get-DirXml $tree ""

# ---- 用户级旧右键键清理: 应用运行时写 HKCU 同名键, 卸载清不到会残留并遮蔽系统级新键 ----
# 装时清 (removeOnInstall, 旧键不再遮蔽新键) + 卸载时清 (removeOnUninstall, 卸载即干净)
# 万物各有其名: 同一键的装/卸两条登记必须各立其名, 同名即冲突 (真机 WIX0091 教训)
$hkcuStaleKeys = @(
    (0..7 | ForEach-Object { "Software\Classes\SystemFileAssociations\$(@('.zip','.7z','.rar','.tar','.gz','.tgz','.bz2','.xz')[$_])\shell\TaoZip.Extract" })
    "Software\Classes\*\shell\TaoZip.Compress"
    "Software\Classes\Directory\shell\TaoZip.Compress"
    "Software\Classes\TaoZip.Archive"
    "Software\TaoZip\FileAssocBackup"
)
$hkcuCleanup = ""
$i = 0
foreach ($k in $hkcuStaleKeys) {
    $i++
    $hkcuCleanup += "<RemoveRegistryKey Id=`"HkcuCleanInstall$i`" Root=`"HKCU`" Key=`"$k`" Action=`"removeOnInstall`" />"
    $hkcuCleanup += "<RemoveRegistryKey Id=`"HkcuCleanUninstall$i`" Root=`"HKCU`" Key=`"$k`" Action=`"removeOnUninstall`" />"
}

$components = ""
foreach ($f in $allFiles) {
    $rel = $f.FullName.Substring($publishRoot.Length).TrimStart('\')
    $parts = $rel -split '\\'
    $parent = if ($parts.Length -gt 1) { ($parts[0..($parts.Length-2)] -join '\') } else { "" }
    $dirRef = if ($parent) { $script:dirRefs[$parent] } else { "INSTALLFOLDER" }
    $src = ($publishRoot + '\' + $rel).Replace('\', '/')
    $esc = [System.Security.SecurityElement]::Escape($src)
    $components += "<Component Directory=`"$dirRef`"><File Source=`"$esc`" /></Component>"
}

$wxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Codepage="936" Name="TaoZip" Manufacturer="Tao" Version="1.0.4.0"
           UpgradeCode="B7A3C9D2-4E5F-4A6B-8C1D-2F3E4A5B6C7D" Scope="perMachine" Compressed="yes">
    <MajorUpgrade Schedule="afterInstallInitialize" DowngradeErrorMessage="已安装了更新版本的 TaoZip，如需重装请先在“应用”中卸载。" AllowSameVersionUpgrades="yes" />
    <!-- 默认装 D 盘 (D:\Program Files\TaoZip, 界面可改): 与 TaoPad 相同的"目录同名属性覆盖"写法,
         该写法在用户真机反复验证可装; 此前的自定义动作写法在该机触发 2723 (动作类型不受支持), 弃用 -->
    <Property Id="INSTALLFOLDER" Value="D:\Program Files\TaoZip" />
    <!-- 装/卸/升级前先结束运行中的 TaoZip: 与 TaoPad 完全相同的双动作写法
         (属性转发定位 taskkill, 避免 Value 内嵌 [SystemFolder] 的 WIX1077);
         Return=ignore: 程序本来没在运行时 taskkill 报错不影响安装 -->
    <CustomAction Id="SetKillCmd" Property="KILLCMD" Value="[SystemFolder]taskkill.exe" />
    <CustomAction Id="KillRunningTaoZip" Property="KILLCMD"
                  ExeCommand="/f /im TaoZip.exe"
                  Execute="immediate" Return="ignore" />
    <InstallExecuteSequence>
      <Custom Action="SetKillCmd" After="InstallValidate" />
      <Custom Action="KillRunningTaoZip" After="SetKillCmd" />
    </InstallExecuteSequence>
    <MediaTemplate EmbedCab="yes" />
    <!-- 卸载列表图标：设置-应用/软件管理里显示 TaoZip 自己的图标；源按发布根目录 app.ico（csproj 已固定拷到根） -->
    <Icon Id="TaoZipIco" SourceFile="$publishRoot\app.ico" />
    <Property Id="ARPPRODUCTICON" Value="TaoZipIco" />
    <Property Id="ARPNOREPAIR" Value="1" />
    <Property Id="DESKTOP_SHORTCUT" Value="1" />
    <Property Id="CTX_MENU" Value="1" />
    <Property Id="CTX_COMPRESS" Value="1" />
    <Property Id="ASSOC_ALL" Value="1" />

    <UI Id="TaoZipInstallUI">
      <TextStyle Id="DlgFont" FaceName="微软雅黑" Size="9" />
      <TextStyle Id="TitleFont" FaceName="微软雅黑" Size="13" Bold="yes" />
      <Property Id="DefaultUIFont" Value="DlgFont" />

      <Dialog Id="MainDlg" Width="440" Height="344" Title="[ProductName] 安装程序">
        <Control Id="Title" Type="Text" X="20" Y="12" Width="400" Height="22" Text="{\TitleFont}安装 TaoZip" />
        <Control Id="LicenseText" Type="Text" X="20" Y="42" Width="400" Height="92" Text="TaoZip 1.0，免费使用。本软件按“现状”提供：请仅将它用于合法合规的文件压缩与解压用途，作者不对使用本软件产生的任何损失承担责任。" />
        <Control Id="AcceptCheck" Type="CheckBox" X="20" Y="140" Width="400" Height="16" Property="LICENSE_ACCEPTED" CheckBoxValue="1" Text="我已阅读并同意以上说明" />
        <Control Id="SepLine" Type="Line" X="20" Y="164" Width="400" Height="1" />
        <Control Id="DirLabel" Type="Text" X="20" Y="174" Width="400" Height="14" Text="安装目录（可直接输入，不存在的文件夹会自动创建）：" />
        <Control Id="DirEdit" Type="PathEdit" X="20" Y="192" Width="310" Height="17" Property="INSTALLFOLDER" />
        <Control Id="DeskCheck" Type="CheckBox" X="20" Y="218" Width="310" Height="16" Property="DESKTOP_SHORTCUT" CheckBoxValue="1" Text="创建桌面快捷方式">
          <Publish Event="Remove" Value="DeskShortcutF" Condition="NOT DESKTOP_SHORTCUT" />
          <Publish Event="AddLocal" Value="DeskShortcutF" Condition="DESKTOP_SHORTCUT" />
        </Control>
        <Control Id="CtxCheck" Type="CheckBox" X="20" Y="238" Width="310" Height="16" Property="CTX_MENU" CheckBoxValue="1" Text="添加右键“解压到此处”菜单（压缩包文件）">
          <Publish Event="Remove" Value="CtxMenuF" Condition="NOT CTX_MENU" />
          <Publish Event="AddLocal" Value="CtxMenuF" Condition="CTX_MENU" />
        </Control>
        <Control Id="CtxCompressCheck" Type="CheckBox" X="20" Y="258" Width="310" Height="16" Property="CTX_COMPRESS" CheckBoxValue="1" Text="添加右键“使用TaoZip压缩”菜单（任意文件/文件夹，支持多选）">
          <Publish Event="Remove" Value="CtxCompressF" Condition="NOT CTX_COMPRESS" />
          <Publish Event="AddLocal" Value="CtxCompressF" Condition="CTX_COMPRESS" />
        </Control>
        <Control Id="AssocCheck" Type="CheckBox" X="20" Y="278" Width="310" Height="16" Property="ASSOC_ALL" CheckBoxValue="1" Text="关联常见压缩格式（zip / 7z / rar / tar 等）">
          <Publish Event="Remove" Value="AssocFile" Condition="NOT ASSOC_ALL" />
          <Publish Event="AddLocal" Value="AssocFile" Condition="ASSOC_ALL" />
        </Control>
        <Control Id="InstallNow" Type="PushButton" X="340" Y="282" Width="80" Height="24" Default="yes" Text="立即安装">
          <Publish Event="SpawnDialog" Value="AcceptWarnDlg" Condition="NOT LICENSE_ACCEPTED" Order="1" />
          <Publish Event="NewDialog" Value="VerifyReadyDlg" Condition="LICENSE_ACCEPTED" Order="2" />
        </Control>
        <Control Id="CancelBtn" Type="PushButton" X="340" Y="310" Width="80" Height="18" Cancel="yes" Text="取消">
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
        <Control Id="DoneText" Type="Text" X="20" Y="30" Width="400" Height="24" Text="TaoZip 安装完成，双击压缩包或右键即可使用。" />
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
        <!-- 显式 CostFinalize (与 TaoPad 相同, 950): 先把默认目录算好再弹窗, 目录框不会空着 -->
        <CostFinalize Sequence="950" />
        <Show Dialog="MainDlg" Sequence="1250" />
        <Show Dialog="ProgressDlg" Before="ExecuteAction" />
        <Show Dialog="ExitDialog" OnExit="success" />
        <Show Dialog="UserExitDlg" OnExit="cancel" />
        <Show Dialog="FatalErrorDlg" OnExit="error" />
      </InstallUISequence>
    </UI>

    <StandardDirectory Id="DesktopFolder" />
          <StandardDirectory Id="ProgramFiles6432Folder">
            <Directory Id="INSTALLFOLDER" Name="TaoZip">$dirDefs</Directory>
          </StandardDirectory>    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="ApplicationProgramsFolder" />
    </StandardDirectory>

    <Feature Id="Core" Title="TaoZip 主程序" Description="压缩解压本体与运行时文件（必装）" Level="1" AllowAbsent="no">$components
      <Component Directory="ApplicationProgramsFolder">
        <Shortcut Id="MenuLnk" Name="TaoZip" Target="[INSTALLFOLDER]TaoZip.exe" Icon="TaoZipIco" WorkingDirectory="INSTALLFOLDER" />
        <RemoveFolder Id="RemoveMenuDir" On="uninstall" />
        <RegistryValue Root="HKCU" Key="Software\Tao\TaoZip" Name="MenuShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
      <!-- 用户级旧右键键清理: 装时清掉残留 (新键不被旧键遮蔽), 卸载时清掉应用运行时写的键 (卸载即干净) -->
      <Component Directory="INSTALLFOLDER" Guid="C4D8E9F5-7A9B-4C1D-8E3F-2B4C5D6E7F80">
        <RegistryValue Root="HKCU" Key="Software\Tao\TaoZip" Name="HkcuAssocCleanup" Type="integer" Value="1" KeyPath="yes" />
        $hkcuCleanup
      </Component>
    </Feature>

    <Feature Id="AssocFile" Title="关联常见压缩格式" Level="1">
      <Component Directory="INSTALLFOLDER" Guid="8F4E9B21-3C57-4D8A-9E12-6A7B8C9D0E1F">
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\TaoZip.Archive" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\TaoZip.Archive">
          <RegistryValue Type="string" Value="TaoZip 压缩包文档" KeyPath="yes" />
          <RegistryKey Key="DefaultIcon"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" /></RegistryKey>
          <RegistryKey Key="shell\open\command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.zip"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.7z"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.rar"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.tar"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.gz"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.tgz"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.bz2"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
        <RegistryKey Root="HKLM" Key="Software\Classes\.xz"><RegistryValue Name="OpenWithProgids" Type="string" Value="TaoZip.Archive" /></RegistryKey>
      </Component>
    </Feature>

    <Feature Id="CtxMenuF" Title="右键菜单：解压到此处" Level="1">
      <Component Directory="INSTALLFOLDER" Guid="9A5F0C32-4D68-4E9B-AF23-7B8C9D0E1F2A">
        <RegistryValue Root="HKLM" Key="Software\Tao\TaoZip" Name="ContextMenu" Type="integer" Value="1" KeyPath="yes" />
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.zip\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.zip\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.7z\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.7z\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.rar\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.rar\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.tar\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.tar\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.gz\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.gz\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.tgz\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.tgz\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.bz2\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.bz2\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.xz\shell\TaoZip.Extract" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\.xz\shell\TaoZip.Extract">
          <RegistryValue Type="string" Value="使用TaoZip解压到此处" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /extract-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
      </Component>
    </Feature>

    <Feature Id="CtxCompressF" Title="右键菜单：压缩任意文件" Level="1">
      <Component Directory="INSTALLFOLDER" Guid="A16B1D43-5E79-4FAC-BF34-8C9D0E1F2A3B">
        <RegistryValue Root="HKLM" Key="Software\Tao\TaoZip" Name="CtxCompress" Type="integer" Value="1" KeyPath="yes" />
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\*\shell\TaoZip.Compress" Action="removeOnUninstall" />
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\*\shell\TaoZip.Compress" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\*\shell\TaoZip.Compress">
          <RegistryValue Type="string" Value="使用TaoZip压缩" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /compress-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\Directory\shell\TaoZip.Compress" Action="removeOnUninstall" />
        <RemoveRegistryKey Root="HKLM" Key="Software\Classes\SystemFileAssociations\Directory\shell\TaoZip.Compress" Action="removeOnUninstall" />
        <RegistryKey Root="HKLM" Key="Software\Classes\Directory\shell\TaoZip.Compress">
          <RegistryValue Type="string" Value="使用TaoZip压缩" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot;,0" />
          <RegistryKey Key="command"><RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]TaoZip.exe&quot; /compress-here &quot;%1&quot;" /></RegistryKey>
        </RegistryKey>
      </Component>
    </Feature>

    <Feature Id="DeskShortcutF" Title="创建桌面快捷方式" Level="1">
      <Component Directory="DesktopFolder">
        <Shortcut Id="DesktopLnk" Name="TaoZip" Target="[INSTALLFOLDER]TaoZip.exe" Icon="TaoZipIco" WorkingDirectory="INSTALLFOLDER" />
        <RegistryValue Root="HKCU" Key="Software\Tao\TaoZip" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </Feature>
  </Package>
</Wix>
"@

$wxsPath = Join-Path $env:TEMP "TaoZip.Package.wxs"
[System.IO.File]::WriteAllText($wxsPath, $wxs, (New-Object System.Text.UTF8Encoding($true)))

# 完全离线: 使用包内便携 WiX 工具与界面扩展, 无需联网
$wixExe = Join-Path $PSScriptRoot "wix\wix.exe"
if (-not (Test-Path $wixExe)) { throw "包内打包工具缺失 ($wixExe)，请使用完整包重解压" }

$msi = Join-Path $OutDir "TaoZip-1.0.4-x64.msi"
if (Test-Path $msi) { Remove-Item $msi -Force }
Write-Host ("打包工具: " + (Get-Item $wixExe).Length + " bytes @ " + (Get-Item $wixExe).LastWriteTime)
& $wixExe build $wxsPath -pdbtype none -o $msi 2>&1 | Tee-Object -FilePath (Join-Path $OutDir "msi-build.log")
if ($LASTEXITCODE -ne 0) {
    Write-Host "打包配置已保存: $wxsPath (排错用, 请把此文件与上方完整报错一并发出)"
    throw "WiX 构建 msi 失败 (请确认本机已安装 .NET 8)"
}
Write-Host "安装器已生成: $msi"
