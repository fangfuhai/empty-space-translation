# 空格翻译视觉标识

原创几何标识：海蓝色空格键帽，白色空格符号。海蓝呼应作者方赋海的“海”；主窗口署名为 **by linthon · 方赋海**。小图标不放姓名，保持清晰。

- 主色：`#087FB8`；键帽底边：`#075780`；符号：白色。
- 软件界面：浅蓝底 `#F4F9FD`，深蓝文字，白色卡片，海蓝主按钮/进度条，深蓝提示浮窗；按钮和链接的交互状态使用同色系。色值集中维护在 `BrandTheme.cs`。
- 正常托盘：蓝色键帽；暂停/尚未就绪：灰色键帽加暂停标记，区别不只依赖颜色。
- SVG 原稿：`source/src/Assets/logo.svg`。
- 透明 PNG：`source/src/Assets/logo.png`，512 × 512。
- Windows 图标：`app.ico`、`paused.ico`，各含 16、20、24、32、40、48、64、128、256 像素帧。
- `ApplicationIcon` 嵌入 EXE 供资源管理器/桌面快捷方式使用；托盘和窗口从程序集内嵌资源读取，无需外部图标路径。

在 Windows PowerShell 中运行 `source/branding/Generate-Icons.ps1` 可重新生成 PNG 和 ICO；使用系统 System.Drawing，不需要安装字体或第三方绘图库。SVG 是对应的可编辑矢量原稿。

使用新版后，桌面快捷方式应指向 `release-v1.0.15-fix10/SpaceTranslate.exe`。旧快捷方式仍指向旧 EXE 时不会自动改变。当前构建保留配套 DLL、deps.json、runtimeconfig.json；不要只复制 EXE。

本轮只调整图标、署名和版本标识，未改变翻译、快捷键、剪贴板交付及模型配置。未自动退出旧程序或修改已有桌面快捷方式。
