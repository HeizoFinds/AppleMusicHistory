# Apple Music 播放历史

我管 Apple Music 的个性化推荐连播叫「假电台」。它挂着「电台」的名字，实际做的就是为你挑歌，一首接一首地放。歌名和艺人明明都在播放栏里，可我在 Windows 上切到下一首，再翻播放历史，却找不到刚才那首。我觉得这是 Apple Music 在播放记录上的不作为，所以写了这个工具。

在 Windows 上记录 Apple Music 桌面客户端播过的歌曲，包括没有加入资料库的推荐曲。只要客户端向 Windows 报出正在播放的新歌名，记录器就把时间、歌名、艺人和专辑存到本机。听几秒就切歌也可以留下记录。

## 使用

1. 从 [Releases](https://github.com/HeizoFinds/AppleMusicHistory/releases/latest) 下载 `AppleMusicHistory.exe`，放到准备长期使用的文件夹，双击运行。
2. 在 Apple Music 里播放歌曲。历史按时间倒序显示，可以搜索歌名、艺人或专辑，选中一行后按 `Ctrl+C` 复制歌名和艺人。
3. 点窗口右上角的关闭按钮会收进托盘并继续记录。双击托盘图标或再次打开程序即可查看历史。在窗口中勾选「登录时自动记录」，下次登录 Windows 后便会自动启动。要停止记录，在托盘菜单选「退出记录器」。

## 本地数据

历史保存在 `%LOCALAPPDATA%\AppleMusicHistory\history.sqlite3`。备份时可以从托盘退出记录器，再复制这个文件。

记录从程序运行时开始。Apple Music 未向 Windows 提供歌名的曲目可能漏记。同一首歌连续循环时，能否区分每次播放取决于客户端是否报告进度重置。

## 从源码构建

在 64 位 Windows 上使用 PowerShell。构建脚本使用系统的 .NET Framework C# 编译器，以及 Windows 10 或 11 SDK 中的 `Windows.winmd`。程序通过 Windows 系统媒体会话读取播放信息，通过系统自带的 SQLite 保存历史。

```powershell
.\build.ps1 -Test
.\build.ps1
```

构建完成后，`AppleMusicHistory.exe` 位于项目根目录。测试使用临时数据库，覆盖切歌、暂停、循环、重启后的去重，以及历史窗口的绘制和搜索。

项目使用 [MIT 许可证](LICENSE)。
