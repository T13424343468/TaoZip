REM TaoZip PC 版一键构建 (需 Windows 10 1809+ 与 .NET 8 SDK)

REM 产物统一输出到 out 文件夹: TaoZip-1.0.4-x64.msi (安装器)
REM 对齐其它项目交付口径: 只出安装器, 不再单独输出绿色版 exe 文件夹;
REM 免安装绿色版始终可用: 发布目录 (publish 文件夹) 里的 TaoZip.exe 即绿色版。

@echo off

chcp 936 >nul

cd /d "%~dp0"

echo [1/4] 还原依赖...

dotnet restore ui\TaoZip.PC.App\TaoZip.PC.App.csproj || goto :err

echo [2/4] 构建...

dotnet build ui\TaoZip.PC.App -c Release -p:Platform=x64 || goto :err

echo [3/4] 生成自包含发布包 (首次较慢, 需下载运行时组件)...

dotnet publish ui\TaoZip.PC.App -c Release -r win-x64 --self-contained true -p:Platform=x64 || goto :err

set "PUBDIR=ui\TaoZip.PC.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"

if not exist "%PUBDIR%\TaoZip.exe" set "PUBDIR=ui\TaoZip.PC.App\bin\x64\Release\net8.0-windows10.0.19041.0\x64\win-x64\publish"

REM 非破坏性产出: 先建 out.new 临时目录, 打包全部成功后再整体替换 out;
REM 第 4 步失败时旧 out (含上一版 msi) 原样保留, 不再出现"点没了"。

if exist out.new rmdir /s /q out.new >nul 2>&1

if exist out.new (

  echo out.new 文件夹被占用, 请关闭相关窗口后重试

  pause

  exit /b 1

)

mkdir out.new

echo [4/4] 生成安装器 (.msi, 完全离线)...

powershell -NoProfile -ExecutionPolicy Bypass -File "tools\msi\package.ps1" -PublishDir "%PUBDIR%" -OutDir "out.new" || goto :err

REM 全部成功才替换: 此时 out.new 里只有 TaoZip-1.0.4-x64.msi

if not exist "out.new\TaoZip-1.0.4-x64.msi" (

  echo 安装器整理异常, 请把此提示发给开发助手

  pause

  exit /b 1

)

if exist out rmdir /s /q out >nul 2>&1

if exist out (

  echo 旧 out 文件夹被占用, 新产物已完整生成在 out.new 文件夹, 关闭占用它的窗口后把 out.new 改名为 out 即可

  pause

  exit /b 1

)

ren out.new out


echo.

echo 完成! 产物清单 (out 文件夹):

echo   TaoZip-1.0.4-x64.msi —— 安装器, 装到 Program Files 并带卸载; 右键压缩/解压菜单安装时可选

echo 免安装绿色版: 发布目录 (publish 文件夹) 里的 TaoZip.exe 即绿色版, 不再单独拷出, 互不干扰

pause

exit /b 0

:err

echo 构建失败, 请把上方报错内容发给开发助手

if exist "out.new\msi-build.log" echo 附件: 打包详细日志在 out.new\msi-build.log, 可与报错一起发来

pause

exit /b 1
