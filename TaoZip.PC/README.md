# TaoZip PC 版 (WinUI 3 + .NET 8)

轻量压缩工具：压缩 zip / tar.gz，解压 zip / 7z / rar(仅解压) / tar / gz / bz2 / xz，
完整性测试、智能解压、历史记录。水墨主题默认自带（Tao 系列主题，可在工具栏切换水墨/浅色/深色）。
无文件管理器，专注压缩本身。

## 文档
- `docs/TECH_MANUAL.md` — 架构、模块说明、格式矩阵、安全设计、已知边界
- `docs/TEST_GUIDE.md` — 自动化测试、验收清单

## 结构

- `src/TaoZip.Core` — 压缩核心 (跨平台纯逻辑)
- `tests/TaoZip.Core.Tests` — 四层测试 29 项 (单元 14 / 集成 6 / 系统 4 / 暴力 5, `dotnet test` 可跑)
- `ui/TaoZip.PC.App` — WinUI 3 界面 (需 Windows 构建)
- `build.bat` — 一键构建脚本 (非破坏性: 打包失败时旧 out 原样保留)

## Windows 上构建运行

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. 双击 `build.bat`（或命令行执行），跑完共四步：还原 → 构建 → 自包含发布 → 生成安装器后整体替换 out
3. 产物统一输出到 **`out` 文件夹**（对齐其它项目交付口径，只出安装器）：
   - `out\TaoZip-1.0-x64.msi` —— 微信风格简洁安装器：欢迎 → 选目录 → 安装，三步即完；桌面+开始菜单快捷方式自动创建；默认装到 D:\Program Files\TaoZip（界面可改），可在"应用和功能"里卸载（打包工具已内置包中，全程离线）
   - 免安装绿色版：发布目录（publish 文件夹）里的 `TaoZip.exe` 即绿色版，不再单独拷出，互不干扰
   - 注：.msi 由 Windows 侧生成（依赖系统安装器组件），沙箱内已验证 WiX 配置语法与工具链安装

## 边界说明

- rar / 7z 仅支持解压（授权与格式限制），不支持创建；界面与文档均已说明。
- zip 加密压缩暂不支持；分卷暂不支持。
- 智能解压：压缩包内只有一个根文件夹时直接解出该文件夹，否则自动建同名文件夹防散落。
