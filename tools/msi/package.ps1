# 薄转发: 打包脚本唯一真身在 TaoZip.PC/tools/msi/package.ps1 (v95-1121 起, 含资源管理器按钮 DLL 携带与安装器运行时新标准)。
# 本文件仅为兼容旧调用路径保留, 请勿在此改逻辑; 改打包逻辑请去真身文件。
& (Join-Path $PSScriptRoot '..\..\TaoZip.PC\tools\msi\package.ps1') @args
exit $LASTEXITCODE
