# TaoZip PC 真人鼠标路径实测（云端 Windows）
# 闭环: 命令行打开压缩包 -> 真实鼠标点「完整性测试」核对状态栏
#      -> 真实鼠标点「智能解压」核对落盘 -> 真实鼠标点「历史记录」核对历史栏
# 定位一律 UIA 读真实坐标, 禁止硬编码像素点。
param([Parameter(Mandatory = $true)][string]$ExePath)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path evidence | Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(250);
    mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
    System.Threading.Thread.Sleep(80);
    mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
  }
}
"@

function Snap($path) {
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

function Find-ByName($root, $name, [int]$timeoutSec = 60) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
      (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    if ($el) { return $true, $el }
    Start-Sleep -Milliseconds 1500
  }
  return $false, $null
}

function Real-Click($el) {
  $pt = $el.GetClickablePoint()
  Write-Host ("真实鼠标点击 [{0}] 坐标 ({1},{2})" -f $el.Current.Name, [int]$pt.X, [int]$pt.Y)
  [Mouse]::Click([int]$pt.X, [int]$pt.Y)
}

# ---- 1) 造测试数据与压缩包 ----
$data = Join-Path $PWD 'pc-e2e-data'
Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$data\src\sub" | Out-Null
Set-Content "$data\src\hello.txt" 'TaoZip 真人鼠标路径实测'
Set-Content "$data\src\sub\nested.txt" 'nested content'
$zip = "$data\taoe2e.zip"
Compress-Archive -Path "$data\src\*" -DestinationPath $zip -Force
Write-Host "测试包已生成: $zip"

# ---- 2) 命令行启动, 直开压缩包 ----
Start-Process $ExePath -ArgumentList "`"$zip`""
$proc = $null
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline -and -not $proc) {
  $proc = Get-Process TaoZip -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if (-not $proc) { Start-Sleep -Seconds 2 }
}
if (-not $proc) { Snap 'evidence\pc-fail-nowin.png'; throw '启动后主窗口句柄未出现' }
Start-Sleep -Seconds 8
if (-not (Get-Process TaoZip -ErrorAction SilentlyContinue)) { throw '启动后进程退出' }
Snap 'evidence\pc-1-launch.png'

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
Write-Host "主窗口: $($win.Current.Name)"

# ---- 3) 真人点击「完整性测试」-> 状态栏出现「测试通过」 ----
$ok, $btnTest = Find-ByName $win '完整性测试'
if (-not $ok) { throw 'UIA 未定位到「完整性测试」按钮' }
Real-Click $btnTest
$found = $false
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 3
  $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) {
    $n = $e.Current.Name
    if ($n -and $n -like '*测试通过*') { $found = $true; Write-Host "状态栏: $n"; break }
  }
  if ($found) { break }
}
Snap 'evidence\pc-2-test.png'
if (-not $found) { throw '点击「完整性测试」后状态栏未见「测试通过」' }

# ---- 4) 真人点击「智能解压」-> 落盘核验 ----
$ok, $btnSmart = Find-ByName $win '智能解压'
if (-not $ok) { throw 'UIA 未定位到「智能解压」按钮' }
Real-Click $btnSmart
$dest = Join-Path $data 'taoe2e'
$ok2 = $false
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 3
  if ((Test-Path "$dest\hello.txt") -and (Test-Path "$dest\sub\nested.txt")) { $ok2 = $true; break }
}
Snap 'evidence\pc-3-extract.png'
if (-not $ok2) { throw "智能解压后未在 $dest 找到解压产物" }
Write-Host "落盘核验通过: $dest"

# ---- 5) 真人点击「历史记录」-> 历史搜索栏出现 ----
$ok, $btnHist = Find-ByName $win '历史记录'
if (-not $ok) { throw 'UIA 未定位到「历史记录」按钮' }
Real-Click $btnHist
$ok3, $clear = Find-ByName $win '清理全部' 30
Snap 'evidence\pc-4-history.png'
if (-not $ok3) { throw '点击「历史记录」后未见历史搜索栏' }

Write-Host 'PC 真人鼠标路径实测: 全流程通过（打开/完整性测试/智能解压/历史记录）'
