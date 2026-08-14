# 项目开发约定

## 项目与技术栈

- 产品：Visual Inspection Test Deployment，Windows 工业视觉检测部署端。
- 开发语言硬约束：产品源码必须使用 C#；不得改用 Python、C++、JavaScript/TypeScript 或其他语言实现产品主体。
- 技术栈：C#、.NET 8、WPF、MVVM；测试使用 C# 与 xUnit。
- PowerShell 仅用于构建、发布和验收编排；厂商原生 SDK/运行库只能封装在 C# 适配器之后，不改变产品的 C# 主体语言。
- 权威需求与设计位于 `docs/`，当前实现状态与命令以 `README.md` 为入口。

## 常用命令

```powershell
dotnet restore VisualInspection.sln
dotnet build VisualInspection.sln --no-restore
dotnet test VisualInspection.sln --no-restore
dotnet run --project src/VisualInspection.App/VisualInspection.App.csproj
powershell -ExecutionPolicy Bypass -File scripts\Build-Acceptance.ps1
```

## 代码边界

- `VisualInspection.Core` 保持纯领域逻辑，不引用 WPF、相机 SDK 或具体模型运行时。
- `VisualInspection.Infrastructure` 实现图源、模型和存储适配器；厂商差异不得泄漏到 Core。
- `VisualInspection.App` 负责界面、绑定和交互状态，不在 code-behind 堆放业务判定。
- 新增或修改判定逻辑时同步更新 `VisualInspection.Core.Tests`。
- 模型、现场图片、日志、凭据和环境文件不得写入仓库。
- `detections.json` 仅是确定性验收适配器，不得描述为模型推理。真实运行时当前只支持静态 `Float[1,3,H,W]` 输入、端到端 `Float[1,N,6]` 输出的 ONNX Detection；其他 ONNX/PT/姿态契约没有适配器时必须保持运行门禁。

## 文档与验证

- 每个可独立验收的功能增量内同步受影响的 README、需求、UI 或技术文档。
- 实现状态必须区分“已实现”“演示数据”“待接入”，不得把计划写成现状。
- 交付前至少运行相关测试和解决方案构建，并按全局约定执行 `neat-freak` 文档同步审计。
