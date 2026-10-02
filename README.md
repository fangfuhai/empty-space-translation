# SpaceTranslate 空格翻译

<img src="source/src/Assets/logo.svg" alt="空格翻译 Logo" width="128" />

Windows 后台输入翻译工具。写完中文，快速按两次空格，通过本机 Ollama 翻译；也支持选区中英互译。适用于日常聊天、商务沟通，不自动发送消息。

稳定版本：**1.0.15 / FIX10**，构建标识 `ST-20261001-FIX10-Blue`。作者：方赋海（Linthon）。

另有 **1.1.0 功能试用版**：自定义 TXT 词库、多语种目标选择、故障日志导出。新增语言的模型质量仍需验证，仅交付剪贴板。见 [试用说明](docs/FEATURES-1.1.md) 和 [词库模板](docs/glossary-example.txt)。

## 下载

- [1.0.15 稳定版 ZIP](https://github.com/fangfuhai/empty-space-translation/raw/refs/heads/main/downloads/SpaceTranslate-1.0.15-win-x64.zip)：日常中英翻译。[发布说明](docs/RELEASE-1.0.15.md)
- [1.1.0 Preview 试用版 ZIP](https://github.com/fangfuhai/empty-space-translation/raw/refs/heads/main/downloads/SpaceTranslate-1.1.0-preview-win-x64.zip)：自定义词库、多语种、日志导出。[发布说明](docs/RELEASE-1.1.0-preview.md)
- [下载包 SHA-256 校验值](downloads/SHA256.txt)

完整解压后运行其中的 SpaceTranslate.exe，保留全部配套文件。本包需 .NET 8 Windows Desktop Runtime x64 和本地 Ollama 模型。**当前仓库源码对应 1.1.0 试用版；1.0.15 提供已保留的运行包，不冒充具有独立历史源码标签。**

## 使用方式

| 操作 | 默认行为 |
| --- | --- |
| 快速双空格 | 读取当前输入框全文，含中文时译成英文 |
| Ctrl+Alt+E | 将选中文字译成英文 |
| Ctrl+Alt+Z | 将选中文字译成简体中文 |
| Ctrl+Alt+P | 暂停 / 恢复翻译 |
| 留在原输入框且未编辑 | 完成后核对原文，再原位替换 |
| 捕获完成后切窗或继续输入 | 翻译继续，结果交付剪贴板，手动 Ctrl+V 粘贴 |

双空格默认间隔 450 ms；中文输入法选词结束后再触发。快捷键翻译需要先选中文字。程序不会代按发送键。

## 开始使用

1. 使用 Windows 10 / 11 x64，安装并启动 [Ollama](https://ollama.com/download/windows)。下载模型需要网络，已准备好的本地模型可用于离线翻译。
2. 当前用户验证配置为 `qwen2.5:7b`；全新配置的源码默认值仍为 `qwen3:4b`。程序按配置使用模型，已有用户保留原配置即可，不会自动换成更大的模型。
3. 本次本地包位于 `release-v1.0.15-fix10/`，依赖 .NET 8 Windows Desktop Runtime x64。EXE、DLL、deps.json、runtimeconfig.json 四个文件必须保留在同一目录。Git 仓库不包含本地发布目录，源码构建方式见下文。
4. 双击 SpaceTranslate.exe。需要时点击“准备模型”，等待“准备好了”，再点击“隐藏到后台”。首次准备可能下载配置中指定的模型，耗时与网络、模型和硬件有关。
5. 先用记事本测试双空格；英文译中文请选中文字后按 Ctrl+Alt+Z。升级时从托盘退出旧程序，再启动新版，避免同时运行多个版本。

## 配置与资源

配置位于 `%LOCALAPPDATA%\SpaceTranslate\settings.json`。通过主窗口“编辑配置”打开，保存后重启程序。可设置模型、快捷键、Prompt、超时、双空格间隔；不要上传完整个人配置。

当前验证使用 qwen2.5:7b，每个请求独立，不保留聊天上下文。生成参数：temperature 0.1、num_ctx 8192、num_predict 2048、repeat_penalty 1.05，keep_alive 为 15 分钟。校验失败最多修复重试一次，共享原任务超时。默认超时 30 秒、输入上限 4000 字符。

内置少量整句精确匹配，如“我知道了”“你可以放心”“那我就拭目以待吧”，命中时无需模型请求。不会将短语替换进长句。速度取决于模型加载、文本长度、GPU/CPU 和内存；本轮未更换用户模型或增加内存参数。模型资源占用与客户端体积不同。

## 翻译原则与边界

- 保留情绪、拒绝强度、不确定性、疑问和承诺，不擅自添加保证、问候或销售话术。
- 金额和币种整体保护，不做汇率换算；保护数字、型号和日期，恢复后交付。
- 支持中文数字日期、省略“日”和 ISO 日期。例如“二〇二六年九月二十九日” → 2026-09-29，“十月二十七” → Oct 27；没有年份就不补年份。
- 空译文、缺失/重复/损坏的保护信息会被拒绝。英文中文残留会触发检查，中文专名可能因此需要人工处理。
- JSON 有效、长度正常和数字齐全都不能证明语义完整，重要内容请核对后发送。
- 复制粘贴受 Windows 焦点、输入活动和控件行为影响，不能保证所有应用完全一致。

## 隐私和排错

只接受本机 Ollama HTTP 地址，没有云翻译回退、聊天数据库或会话记忆。个人配置、日志、模型不随源码发布。诊断日志记录任务阶段、数量和错误信息，分享前仍应检查并去除个人路径或意外敏感内容。

剪贴板不需要预先有文字。捕获失败时保留原文；剪贴板交付失败时保留当前结果，供托盘重试复制。复现问题时记录版本、快捷键、翻译方向、界面提示、是否切窗/继续输入和去隐私后的测试文字即可，勿公开私人聊天或完整配置。

## 开发和测试

C# / .NET 8 WinForms。需要 Windows 和 .NET 8 SDK。入口为 [App.cs](source/src/App.cs)，翻译核心为 [Core.cs](source/src/Core.cs)，Windows 接口为 [Native.cs](source/src/Native.cs)。

```powershell
# 仓库根目录；两组测试无需真实桌面或 Ollama
dotnet run --project source/tests/Tests.csproj
dotnet run --project source/regression/Regression.csproj

# 当前源码为 1.1.0 Preview，不能覆盖保留的稳定版目录
dotnet build source/src/SpaceTranslate.csproj -c Release -p:SelfContained=false -p:PublishSingleFile=false -o release-v1.1.0-preview

# 可选自包含发布，体积更大
dotnet publish source/src/SpaceTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release-self-contained

# 可选真实模型测试，读取个人配置并占用模型资源
dotnet run --project source/regression/Regression.csproj -- live-feedback
```

FIX8 记录：基础测试 52 PASS，定向测试 18 PASS，均 0 FAIL / 0 SKIP；本机固定样本 14 条返回，0 FAIL / 0 SKIP，并检查关键语义。构建 0 警告 / 0 错误。这是当次验证记录，不是所有机器和输入的效果保证。

详细要求见 [需求与验收记录](docs/REQUIREMENTS.md)，更新见 [更新记录](docs/CHANGELOG.md)。源码采用 [MIT](LICENSE.txt)；外部模型和运行时遵循各自许可，见 [贡献与依赖](CREDITS.md)。
