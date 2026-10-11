# 源码导航

源码版本为 0.8.8。Core 负责数据，App 负责窗口、编辑和系统集成；采用直接事件与控制器。

## 按功能找代码

| 要改的功能 | 当前入口与实现 |
| --- | --- |
| 数据格式、保存、实例锁、永久删除 | [NoteData](WeNote.Core/NoteData.cs)、[NoteAppearance](WeNote.Core/NoteAppearance.cs)、[NoteStore](WeNote.Core/NoteStore.cs) |
| 启动、新建、显示、隐藏、退出 | [Program](WeNote.App/Program.cs)、[NoteController](WeNote.App/NoteController.cs) |
| 收纳关系、取出、释放 | [NoteController.Collection](WeNote.App/NoteController.Collection.cs) |
| 保存调度、拖动缩放、关闭、格式提示 | [NoteWindow](WeNote.App/NoteWindow.xaml.cs)及其[界面](WeNote.App/NoteWindow.xaml) |
| 左侧标签、开关动画、排序 | [NoteWindow.Collection](WeNote.App/NoteWindow.Collection.cs)、[CollectionDrag](WeNote.App/NoteWindow.CollectionDrag.cs) |
| 三种编辑格式、粘贴入口、链接编辑 | [NoteEditor](WeNote.App/NoteEditor.xaml.cs)、[NoteEditor.Links](WeNote.App/NoteEditor.Links.cs)及其[界面](WeNote.App/NoteEditor.xaml) |
| Markdown/HTML/富文本转换、行距、文字 | [FormattedDocuments](WeNote.App/FormattedDocuments.cs)、[DocumentText](WeNote.App/DocumentText.cs) |
| 粘贴文字链接、地址规则、系统打开 | [PastedLinks](WeNote.App/PastedLinks.cs)、[NoteLinks](WeNote.App/NoteLinks.cs) |
| 手动插入或修改链接窗口 | [LinkWindow](WeNote.App/LinkWindow.xaml.cs)及其[界面](WeNote.App/LinkWindow.xaml) |
| 外观、程序与快捷键设置 | [NoteController.Settings](WeNote.App/NoteController.Settings.cs)、[SettingsWindow](WeNote.App/SettingsWindow.xaml.cs)、[NoteTheme](WeNote.App/NoteTheme.cs) |
| 桌面挂载、原生窗口坐标与恢复 | [DesktopHost](WeNote.App/DesktopHost.cs) |
| 托盘、自启、全局快捷键 | [TrayMenu](WeNote.App/TrayMenu.cs)、[WindowsStartup](WeNote.App/WindowsStartup.cs)、[GlobalHotkeys](WeNote.App/GlobalHotkeys.cs) |

## 主要调用关系

启动：Program.Main 选择 Data，建立 NoteStore 与 NoteController；Start 加载便签及偏好，先恢复收纳关系，再恢复桌面模式和可见状态。全部隐藏时找回最近一张根便签；普通浮窗前置。托盘和全局快捷键由控制器管理。

正文保存：NoteEditor.ContentChanged → NoteWindow.MarkDirty → 400ms 计时器 → SaveNow → Snapshot → NoteController.Save → NoteStore.Save。正文变更才重新生成编辑内容；拖动、缩放只标记元信息，移动期间暂停保存，结束后续写。NoteStore 刷新临时文件后替换 JSON，正常覆盖保留上一版 .bak；同一目录只允许一个写入实例。

关闭与退出：×、Ctrl+W 和窗口关闭进入 HideNote；主便签先收起展开的子项。普通关闭与 CollapseCollectedNote 共用 SaveAndHide，将待写正文和隐藏状态一次提交，成功才隐藏；失败还原隐藏标记并保留窗口、正文和错误。ReleaseNote 也将正文与关系一次提交，失败还原关系。RequestExit 先 FlushAll，成功后清理窗口和托盘。
删除与收纳：垃圾桶、菜单、标签右键 → NoteWindow.ConfirmDelete → NoteController.DeleteNote。取消不动数据；确认删除主便签时先 ReleaseNote 保存并释放子项，再删除主便签自己的文件。NoteStore.Delete 清理对应 JSON、.bak、.tmp 及旧 Deleted 中同编号副本。取出、展开、收起和排序由收纳控制器协调；标签控件复用，顺序写入主便签的 CollectionOrder。

设置：设置、格式提示、快捷键及自启入口集中在 NoteController.Settings。SaveDefaults、TrySetFormatWarnings 和 TrySaveHotkeys 共用 TrySavePreferences，落盘成功才提交内存偏好；快捷键保留自己的校验、系统注册与存储失败恢复。WindowsStartup 单独读写 HKCU Run 的 WeNote 值。

编辑与链接：NoteEditor.PreparePaste 处理 RTF/HTML、Markdown 和普通文字，FormattedDocuments／DocumentText 做格式与文字转换，PastedLinks 识别正文内链接；文档遍历统一为 DocumentText.Elements，文档工具不依赖编辑器。NoteEditor.Links 负责菜单、修改、取消和点击，直接使用自己的 RichTextBox 或修改 Markdown 源码；NoteLinks 只负责地址规则与系统打开。本地文件定位到文件夹，网页调用浏览器。

显示复用：NoteWindow.CreateConfirmation 共用删除与格式提示的主题和窗口参数，各自仍处理内容、按钮、取消、提示偏好与防重复。托盘与 EXE 共用 Assets/zhuobianji.ico，托盘读取程序集内嵌资源，发布时不依赖外部图标文件。
桌面：NoteWindow.TrySetDesktopMode → DesktopHost.SetAttached，管理父窗口、样式和屏幕坐标，原生挂载失败恢复原状态。主便签保留 92px 透明标签区域，Snapshot 保存主正文卡片的位置和宽度。原生恢复处理有实际用途，不能当作无用防御删除。

## 构建与检查

在 Windows 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)，下载源码后，在项目根目录运行：

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1

程序输出到 `.local/build/current/桌边记.exe`，随附说明和第三方许可。它是包含运行库的 Windows x64 单文件程序，使用者无需安装 SDK。

[dotnet.cmd](../scripts/dotnet.cmd) 依次查找 `DOTNET_ROOT`、系统 PATH 和本机已有的 `F:\dotnet`；SDK 可以安装在其他位置。构建成功后替换 current，失败保留原输出。

本地更新时，先从托盘退出程序，再运行 [install-app.ps1](../scripts/install-app.ps1)。它将程序放到项目内的 `桌边记` 文件夹，保留 Data 和自启选择；`archive/rollback` 只保留上一版程序。首次试用也可以直接打开构建输出中的 EXE。

功能检查统一用 [test-app.ps1](../scripts/test-app.ps1)，不指定功能只列出入口。例如：

    .\scripts\test-app.ps1 -Group storage,close

功能分组及隔离条件见[检查说明](../tests/README.md)。[安装检查](../scripts/test-install-app.ps1)仍使用独立目录、假程序和临时注册表键。

依赖固定为 Markdig 1.4.0、AngleSharp 1.8.3、WPF UI 4.3.0；第三方和内含运行库的许可见根目录三份 notices 文件，也随构建输出分发。富文本 XAML、Markdown 源码及纯文本保存在 Version 2 JSON；Data 在所运行 EXE 旁边，保留早期版本记录的读取能力。
