# 浮光 · FloatingMusic

一个面向 **Apple Music for Windows** 的轻量桌面悬浮控制器。播放、音量、待播列表和歌词集中在一个可置顶、可调背景透明度的小窗口中。

基于 C#、WPF 和 .NET Framework 4.8。首个公开版本：**1.0.1**。

这是独立开发的第三方工具，与 Apple 无隶属关系。它控制已经运行的 Windows Apple Music 客户端，不包含音乐流媒体播放器，也不需要打开官方迷你播放器。

## 功能

| 功能 | 说明 |
| --- | --- |
| 悬浮窗口 | 拖动移动、切换置顶、调节背景透明度；文字和封面保持清晰 |
| 播放控制 | 播放 / 暂停、上一首、下一首、歌曲信息与封面 |
| 进度跳转 | 点击或拖动进度条，通过官方客户端辅助功能控件调整并核验播放位置 |
| 应用音量 | 调节 Apple Music 的 Windows 应用混音器音量，支持静音 |
| 待播列表 | 查看官方控件提供的队列，点击歌曲按当前队列顺序跳转 |
| 喜爱与减少推荐 | 调用当前歌曲的官方菜单入口；喜爱操作后尝试核验状态 |
| 在线歌词 | LRCLIB 同步歌词、纯文本歌词及版本候选 |
| 歌词显示 | 简体 / 原文切换、逐行同步、前后行预览、歌词时间校正 |
| 本地歌词 | 主动导入 LRC / TXT，支持 UTF-8、带 BOM 的 Unicode 和 GB18030 |

## 开始使用

### 环境要求

- Windows 10 1809 或更新版本，64 位。
- .NET Framework 4.8。
- 已安装并能正常播放音乐的 Apple Music for Windows。
- 在线歌词需要连接 LRCLIB；音乐本身的访问权限由 Apple Music 管理。

### 运行

1. 从本仓库的 **Releases** 下载 `FloatingMusic-v1.0.1-win-x64.zip`，解压到一个可写的文件夹。源码包需要自行构建。
2. 保持 `FloatingMusic.exe` 与 `FloatingMusic.exe.config` 位于同一目录。
3. 打开 Apple Music 并播放歌曲。
4. 双击 `FloatingMusic.exe`。

无需管理员权限、MusicKit 开发者配置或额外模型。应用不设置开机启动。关闭悬浮窗后，Apple Music 继续播放。

### 操作说明

- 拖动顶部空白区域移动窗口；图钉切换置顶；齿轮调整背景透明度。
- “词”显示 / 隐藏歌词；“简 / 原”切换简体和来源原文。
- “搜 / 选”打开歌词版本候选。选择仅在当前这次播放生效，下次可以重新选择。
- “待播列表”显示接下来播放的歌曲，右上角刷新更新队列。
- 歌词校时按钮每次调整 0.5 秒，范围 ±30 秒，切歌后重置。

## 歌词如何匹配

歌词来自 [LRCLIB](https://lrclib.net/)，不是 Apple Music 官方歌词。

检索会尝试原始、简体和繁体名称，候选匹配会统一大小写、全半角、空格和标点，并识别形如“陶喆-David Tao”的中英文并列歌手名。匹配还会核对歌曲时长、专辑信息以及明确标注的现场版本。

- 优先使用符合匹配规则的同步歌词。
- 多个可靠候选的歌词内容不同时，交由用户选择。
- 候选列表显示歌手、专辑、时长、同步 / 纯文本状态，以及需要核对的差异。
- 仅同名、版本不同或时长不符的候选不会直接作为可靠匹配。
- 简体转换在本地完成，保留原文和时间轴。
- 纯文本没有时间轴，应用不会生成或猜测同步时间。

规则匹配仍可能存在误差。遇到错配，可通过“搜 / 选”更换版本，或主动导入本地 LRC。

## 联网与本地文件

**不下载模型，也不下载歌曲音频。** 在线歌词、候选和手动选择仅在内存中使用，退出后清空；不保存在线歌词文件或歌词选择记录。

在线查询默认开启，可在设置里关闭。检索只向 `lrclib.net` 发送歌名、歌手查询；时长与专辑用于本地匹配。程序不要求 Apple 密码或账户令牌，也不上传本地歌词。

运行期间可能生成以下本地文件：

| 文件 | 用途 |
| --- | --- |
| `preferences.ini` | 窗口位置、置顶、背景透明度、联网歌词与简体显示偏好 |
| `lyrics/` | 用户主动导入的本地歌词副本，供下次使用 |
| `seek-status.txt` | 最近一次进度跳转的状态与核验结果 |
| `error.log` | 程序启动异常时生成的错误信息 |

开发诊断命令还可能生成会话信息与诊断文件。反馈问题或分享文件前，请检查其中是否含有歌曲名、本地路径等个人信息。发布包不包含个人偏好、歌词、听歌记录或账户数据。

## 实现与限制

播放与歌曲信息使用 Windows 系统媒体会话，应用音量使用 Core Audio。进度、待播列表和歌曲菜单通过 UI Automation 访问官方客户端控件。

程序不通过坐标定位或模拟键鼠操作官方客户端，也不会主动激活、还原或置顶官方窗口。相关操作可能临时展开官方队列面板或歌曲菜单，并在操作后尝试恢复。

需要了解的限制：

- **待播点播是按队列逐首跳转。** 点击靠后的歌曲需要更久，可能短暂经过前面的歌曲。程序会核对队列与实际播放结果；若发生变化或失败，会停止，可能停留在中间歌曲。
- **列表长度取决于官方控件提供的内容。** 不保证能一次读取完整队列；切歌或修改队列后可手动刷新。
- **官方客户端更新可能影响兼容性。** 进度跳转已在 Apple Music `1.1540.23042.0` 的窗口最小化场景中验证，其他版本的兼容性尚未确认。
- **音量是 Windows 应用混音器音量。** 它与 Apple Music 内部音量属于不同层级。
- **喜爱和减少推荐依赖官方菜单。** 已接入菜单入口与状态读取，实际提交音乐偏好的完整流程尚未完成验证。
- **歌词完整度受来源限制。** 歌手别名、曲目版本、服务可用性和歌词时间轴质量都可能影响结果。

## 从源码构建

无需完整 .NET SDK，脚本使用 Windows 的 .NET Framework 64 位 C# 编译器。

在仓库根目录运行 PowerShell：

```powershell
.\src\build.ps1
```

生成的 `FloatingMusic.exe` 位于仓库根目录，运行时保留旁边的 `FloatingMusic.exe.config`。

首次构建需要下载 `Microsoft.Windows.SDK.Contracts` **10.0.26100.4948**，来源为微软 NuGet。缓存保存在 `src/.build/`，后续构建可复用。

已有 Windows SDK Contracts 元数据时，可以离线指定目录：

```powershell
.\src\build.ps1 -ContractsDirectory 'D:\SDK\Contracts\ref\netstandard2.0'
```

也可以指定输出文件路径；父目录需已存在：

```powershell
.\src\build.ps1 -OutputPath '.\FloatingMusic.exe'
```

## 验证

内置的离线测试覆盖歌词时间轴解析、简体转换、版本过滤、候选歧义、重复记录和中英文并列歌手名，不修改 Apple Music 播放状态：

```powershell
$process = Start-Process -FilePath '.\FloatingMusic.exe' `
  -ArgumentList '--test-lyrics' -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput '.\lyrics-tests.txt'
$process.WaitForExit()
Get-Content '.\lyrics-tests.txt'
```

桌面媒体控制仍需在实际 Windows / Apple Music 环境中验证。反馈问题时，请提供 Windows 版本、Apple Music 版本、触发步骤与错误提示；提交日志前检查其中是否含有歌曲名或本地路径。

## 源码结构

```text
src/
├── Player.cs          # 主窗口、媒体会话、设置与入口
├── Player.xaml       # 主播放器界面
├── PointerSlider.cs  # 进度 / 音量滑块交互
├── AppleSeek.cs      # 官方进度控件与隔离调用
├── AudioVolume.cs    # Core Audio 应用会话音量
├── LibraryBridge.cs  # 队列、歌曲菜单及系统切歌
├── LibraryPanel.cs   # 待播列表与偏好按钮
├── Queue.xaml        # 待播列表 / 歌词候选共用窗口样式
├── Lyrics.cs         # 歌词解析、匹配与同步显示
├── LyricCandidates.cs# 候选检索、排序、选择及测试
└── build.ps1         # 构建脚本
```

## 参考与致谢

- [LRCLIB](https://lrclib.net/)：歌词检索与时间轴数据。
- [windows-apple-music-remote](https://github.com/ahmetberber/windows-apple-music-remote)：Windows Apple Music 辅助功能控件交互的实现参考。
- Microsoft Windows 系统媒体会话、UI Automation、Core Audio 与 WPF。

Apple Music 名称及相关商标属于其权利人；歌词和音乐内容不随项目分发。

参考项目的版权和许可声明见 [第三方声明](THIRD_PARTY_NOTICES.md)。

## 许可证

本项目采用 [MIT 许可证](LICENSE)，允许使用、修改、分发和商业使用；分发软件副本或实质部分时须保留版权与许可声明。

Copyright (c) 2026 mik-absurd。第三方版权与许可声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。项目许可证不授予 Apple 商标或第三方歌词、音乐内容的权利。
