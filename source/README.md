# 开发入口

当前源码版本 1.1.0 Preview。使用、构建和测试说明统一维护在 [根目录 README](../README.md)。

- src/App.cs：WinForms 入口、托盘、捕获、替换和剪贴板交付。
- src/Core.cs：配置、触发逻辑、文本保护、Ollama 请求及解析校验。
- src/Native.cs：Windows 输入钩子和剪贴板实现。
- tests/：基础回归，不操作真实桌面。
- regression/：定向回归；真实模型测试为显式可选模式。

个人配置、日志、模型、编译缓存及发布文件不纳入 Git。
