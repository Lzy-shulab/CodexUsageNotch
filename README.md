# CodexUsageNotch · Codex 额度浮窗

一个适用于 Windows 的 Codex 桌面伴随工具。把剩余额度、重置时间和完整重置次数放进标题栏中的小胶囊，工作时抬眼就能看到。

支持 Pro 的单额度显示，以及 Plus 的「5 小时 + 本周」双额度显示；自动适配明暗主题、窗口大小和系统 DPI。

**[下载单文件 EXE](https://github.com/Lzy-shulab/CodexUsageNotch/releases/latest/download/CodexUsageNotch.exe)** · **[下载完整便携包](https://github.com/Lzy-shulab/CodexUsageNotch/releases/latest/download/CodexUsageNotch-1.1.1-win-x64.zip)** · [查看发布说明](https://github.com/Lzy-shulab/CodexUsageNotch/releases/latest)

## 四种界面

| Pro · 深色 | Pro · 浅色 |
| :---: | :---: |
| ![Pro 深色模式：单环周额度和完整重置到期列表](docs/images/pro-dark.png) | ![Pro 浅色模式：单环周额度和完整重置到期列表](docs/images/pro-light.png) |

| Plus · 深色 | Plus · 浅色 |
| :---: | :---: |
| ![Plus 深色模式：蓝色周额度外环、绿色五小时内环](docs/images/plus-dark.png) | ![Plus 浅色模式：蓝色周额度外环、绿色五小时内环](docs/images/plus-light.png) |

以上为程序实际渲染的界面，数值为演示数据。图中展开的是右侧「完整重置」的到期列表。

## 功能与操作

- **Pro 单额度**：保留绿色圆环和剩余百分比的紧凑布局。
- **Plus 双额度**：蓝色外环表示本周剩余，绿色内环表示 5 小时剩余；文字显示为 `5h 99%/本周 80%`。
- **点击左侧额度**：查看各周期的剩余比例和重置时间。
- **中间重置时间**：纯展示，点击无操作；双额度时显示周额度重置时间。
- **点击右侧完整重置**：查看可用次数及各次到期时间。
- **右键浮窗**：立即刷新或退出。

浮窗跟随 Codex 窗口，仅在 Codex 位于前台时显示，不占任务栏位置。Pro 和 Plus 使用相同的高度、字号和缩放规则，窄窗口自动压缩文案。

程序每 10 秒通过本机 Codex app-server 只读获取额度，不发送模型请求，也不执行完整重置。单、双额度布局按实际返回的额度窗口识别，无需手动选择会员类型。这是独立伴随程序。

## 使用

1. 在 Windows x64 电脑上安装 Codex 桌面客户端并登录账号。
2. 下载 `CodexUsageNotch.exe`，双击运行。EXE 内置 .NET 运行环境。
3. 切回 Codex，即可在标题栏看到浮窗；更新版本前，从旧浮窗的右键菜单退出。

便携 ZIP 额外包含四张效果图、使用说明，以及 `Preview-Pro-Dark.cmd`、`Preview-Pro-Light.cmd`、`Preview-Plus-Dark.cmd`、`Preview-Plus-Light.cmd` 四个演示入口。演示使用模拟数据，可以与正常浮窗同时运行；切换演示前，先右键退出已打开的演示。

也可以从命令行预览：

```powershell
# Pro 深色预览
.\CodexUsageNotch.exe --preview --demo --theme=dark

# Plus 浅色预览
.\CodexUsageNotch.exe --preview --demo=plus --theme=light
```

添加 `--popover` 可在预览启动时展开完整重置详情。

## 从源码构建

需要 Windows 和 .NET 10 SDK。

```powershell
git clone https://github.com/Lzy-shulab/CodexUsageNotch.git
cd CodexUsageNotch
.\scripts\Build.ps1 -Portable
```

输出：

- `dist\CodexUsageNotch-1.1.1-win-x64\CodexUsageNotch.exe`：自包含单文件程序。
- `dist\CodexUsageNotch-1.1.1-win-x64.zip`：附带演示入口、效果图和说明的便携包。

构建会执行功能自检。界面检查和效果图导出命令：

```powershell
dotnet run --project CodexUsageNotch.Tests -c Release -- --ui-check
dotnet run --project CodexUsageNotch.Tests -c Release -- --gallery
```

Pro 已通过本机真实账号读取验证；Plus 双额度已通过模拟响应和明暗界面检查，尚未使用真实 Plus 账号验证。

如需安装到当前用户目录并设置开机启动：

```powershell
.\scripts\Install.ps1 -PayloadRoot .\dist\CodexUsageNotch-1.1.1-win-x64
```

卸载：`.\scripts\Uninstall.ps1`。

## 项目结构

| 目录 | 内容 |
| --- | --- |
| `CodexUsageNotch` | WPF 主程序、额度解析、主题与窗口跟随 |
| `CodexUsageNotch.Tests` | 功能自检、界面检查和四张效果图导出 |
| `scripts` | 构建、安装、卸载 |
| `docs/images` | Pro / Plus 的明暗效果图 |
