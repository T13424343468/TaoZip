# TaoZip PC 版技术文档

> 版本 1.0 ｜ WinUI 3 + .NET 8 ｜ 定位：轻量压缩工具（无文件管理器）

## 一、总体架构

```
TaoZip.PC/
├── src/TaoZip.Core          压缩核心层 (net8.0, 跨平台纯逻辑, 无 UI 依赖)
│   ├── ArchiveService.cs        压缩/解压/测试/智能解压/条目列表
│   └── HistoryStore.cs          操作历史 (JSON 落盘, 上限 500 条)
├── tests/TaoZip.Core.Tests  核心自动化测试 (xunit, 9 项)
├── ui/TaoZip.PC.App         WinUI 3 界面层 (Windows 构建)
│   ├── MainWindow.xaml          主窗口: 工具栏/列表/状态栏
│   └── MainWindow.xaml.cs       交互逻辑: 打开/解压/压缩/测试/历史/拖放
├── tools/UiSyntaxCheck      界面代码类型桩编译验证 (开发期工具)
├── build.bat                一键构建 (Windows)
└── docs/                    本文档与 TEST_GUIDE
```

依赖方向：`ui → Core → SharpCompress`。界面层不直接接触压缩库，
所有归档操作统一走 `ArchiveService`，保证三端(Android/PC/未来 TV)可共享逻辑层。

## 二、核心模块说明

### ArchiveService
| 能度 | 方法 | 说明 |
|------|------|------|
| 压缩 | `CreateZip(paths, dest)` | zip 普通压缩，UTF-8 条目名，保留顶层文件夹结构 |
| 压缩 | `CreateTarGz(paths, dest)` | tar.gz (SharpCompress TarWriter, GZip) |
| 解压 | `Extract(archive, dest)` | 按内容自动识别格式；逐条目流式写出；含路径安全防护 |
| 解压 | `SmartExtract(archive, base)` | 智能解压，语义见下 |
| 测试 | `Test(archive)` | 读尽全部条目（zip 触发 CRC 校验），返回条目数/解压后总字节 |
| 列表 | `ListEntries(archive)` | 条目名/是否目录/字节大小 (tar.gz 自动解包处理) |
| 探测 | `DetectSingleRoot(archive)` | 判断压缩包是否只含一个根文件夹 |

**格式支持矩阵**

| 格式 | 解压 | 压缩 |
|------|:---:|:---:|
| zip | ✅ | ✅ |
| tar / tar.gz / gz | ✅ | tar.gz ✅ |
| bz2 / xz | ✅ | — |
| 7z | ✅ | ❌ (格式规范限制) |
| rar | ✅ | ❌ (授权限制, 仅解压) |

**智能解压语义**
- 包内只有一个根文件夹（条目含子路径或目录条目）→ 直接解到目标目录，根文件夹即包结构；
- 否则（散文件/多根）→ 自动创建"压缩包同名文件夹"再解，避免散落一地。

**安全设计**
- 路径穿越防护 (ZipSlip)：条目名含 `..` 或绝对路径时拒绝并抛 `InvalidFormatException`；
- tar.gz 双重流：先解 GZip 到临时文件（GZip 流不可 seek），再按 tar 打开，用完即删；
- 界面任务全部后台线程执行，失败捕获并写入历史。

### HistoryStore
- 位置：`%APPDATA%\TaoZip\history.json`；
- 记录：时间/动作(打开·解压·压缩·测试)/源路径/目标路径/结果/错误信息；
- 上限 500 条，读失败自动重置为空列表（不阻塞主功能）。

## 三、界面层要点
- 主窗口 960×640，尺寸经 `AppWindow.Resize` 设置（WinUI3 不支持 XAML 窗口尺寸属性）；
- 拖放：拖入压缩包 = 打开浏览；拖入普通文件/文件夹 = 弹出保存框直接压缩；
- 历史列表双击条目 → 资源管理器定位原压缩包；
- 长任务（解压/压缩/测试）在后台线程，状态栏实时反馈，失败进历史。

## 四、测试策略
- 核心层 9 项 xunit 用例：中文名回环、tar.gz 回环、文件夹子目录结构、
  完好/坏包测试、ZipSlip 拦截、智能解压双场景、条目列表(含 tar)、历史读写；
- 界面层用"类型桩"工程 `tools/UiSyntaxCheck` 做编译级验证，
  在无 Windows 环境下提前暴露 C# 级错误（本次交付前实际拦下 8 处）；
- XAML 属性级校验依赖 Windows 构建期 XamlCompiler（无法跨平台模拟）。

## 五、已知边界
1. zip 加密压缩、分卷暂不支持（界面与 README 已声明）；
2. rar/7z 仅解压；
3. 自包含发布包约 150MB（含 .NET 运行时），压缩后约 70-80MB；
4. 首次执行 build.bat 第三步需联网下载发布组件。
