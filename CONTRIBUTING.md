# 贡献指南

本项目面向 Windows 工业视觉检测工位。先阅读 [README](README.md)、[开发约定](AGENTS.md)与 [Sequence 交付说明](docs/Sequence与模型交付说明.md)。

## 本地检查

Windows x64 与 .NET 8 SDK：

```powershell
dotnet restore VisualInspection.sln
dotnet build VisualInspection.sln --no-restore --configuration Release
dotnet test VisualInspection.sln --no-build --no-restore --configuration Release
```

规则变化补充对应 C# / xUnit 测试；WPF 改动同时复查默认和最小窗口尺寸。现场设备、模型与图片只在已获授权的本地环境验收，不提交至仓库。CI 构建和单元测试不代替真实设备或生产验收。

生产 TXT 与结果文件名按机台本地时间保存；测试不能假定运行器位于北京时间。时间相关测试需检查日期边界，文件名断言需区分 Windows 与 Unix 的非法字符规则。GitHub Windows CI 在 UTC 环境执行完整解决方案测试。

## 修改边界

产品主体保持 C#、.NET 8、WPF 和 MVVM。Core 不依赖 WPF 或厂商 SDK；设备与推理运行时通过 Infrastructure 的适配器接入。没有适配器的图源、模型或执行能力应保留运行门禁，不能为演示而绕过。

先开 Issue 说明较大功能的用途、输入输出契约与验收方式。Fork 后建立分支，一次 PR 处理一个问题；附实际测试结果、未验证范围和界面截图（如适用）。同步受影响文档，不把演示数据或配置入口描述为已接入能力。

原创贡献按 [MIT](LICENSE) 分发；引入第三方代码或资产时同步[第三方声明](THIRD_PARTY_NOTICES.md)。讨论遵守[社区约定](CODE_OF_CONDUCT.md)，漏洞使用[私密报告](SECURITY.md)。
