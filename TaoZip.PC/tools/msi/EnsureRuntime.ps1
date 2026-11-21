# TaoZip 安装器运行时保障 (2026-11-21 全系安装器新标准, 老板令)
# 口径: 安装包不内置 .NET 运行时; 安装期以系统实际注册为准全盘检测所需运行时;
#   有则零下载直接用; 无则联网下载官方运行时并显示进度, 装进系统统一 .NET 目录
#   (C:\Program Files\dotnet, 与现有版本同处一地, 不单独建运行时目录);
#   无网且无运行时给通俗重试指引。
# 云测钩子: TAOZIP_FORCE_RUNTIME_DOWNLOAD=1 强制走下载路 (断言二用), 日常安装不设即走检测路。
$ErrorActionPreference = 'Stop'
# 日志落机器级固定路径: 本脚本由 WiX 自定义动作在 per-machine msiexec (SYSTEM) 上下文执行,
#   $env:TEMP 指向 C:\Windows\Temp, 云测断言按 runner 用户 TEMP 查不到 (run 44870126512 实锤);
#   ProgramData 下产品目录全用户可读, SYSTEM/用户均可写
$rtLogDir = Join-Path $env:ProgramData 'TaoZip'
if (-not (Test-Path $rtLogDir)) { New-Item -ItemType Directory -Force -Path $rtLogDir | Out-Null }
$log = Join-Path $rtLogDir 'taozip-runtime.log'
function Write-RtLog([string]$m) {
    try { Add-Content -Path $log -Value ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) } catch { }
}
function Test-DesktopRuntime10 {
    # 以系统实际注册为准: 注册表 InstalledVersions 任一 10.x 键, 或统一目录 shared 下有 10.x 桌面运行时
    try {
        $rk = Get-ChildItem 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64' -ErrorAction SilentlyContinue |
              Where-Object { $_.PSChildName -like '10.*' }
        if ($rk) { return $true }
        $shared = Get-ChildItem (Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App') -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -like '10.*' }
        return ($null -ne $shared)
    } catch { return $false }
}
if ($env:TAOZIP_FORCE_RUNTIME_DOWNLOAD -ne '1' -and (Test-DesktopRuntime10)) {
    Write-RtLog 'runtime-present zero-download'
    exit 0
}
Write-RtLog 'runtime-missing, downloading official runtime'
$url = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'
$exe = Join-Path $env:TEMP 'windowsdesktop-runtime-win-x64.exe'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing
} catch {
    Write-RtLog ("download-failed " + $_.Exception.Message)
    # 无网且无运行时: 说人话重试指引 (交互装弹窗; 静默装落日志后中止)
    $msg = "TaoZip 需要联网下载 .NET 运行时（约 60 MB），当前没有检测到网络连接。`n`n请连上网络后重新运行安装程序即可，无需其他操作。"
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show($msg, 'TaoZip 安装', 'OK', 'Information') | Out-Null
    } catch { }
    exit 1602
}
Write-RtLog 'download-ok, installing to system dotnet dir (passive 进度条)'
$p = Start-Process -FilePath $exe -ArgumentList '/install', '/passive', '/norestart' -Wait -PassThru
Write-RtLog ("installer-exit " + $p.ExitCode)
if ($p.ExitCode -notin 0, 3010) { exit $p.ExitCode }
if (-not (Test-DesktopRuntime10)) { Write-RtLog 'verify-failed'; exit 31337 }
Write-RtLog 'runtime-download-install ok'
exit 0
