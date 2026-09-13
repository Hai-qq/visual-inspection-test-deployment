# 第三方组件与现场资产

本仓库原创代码与项目标识采用 [MIT](LICENSE)。第三方组件保留各自许可，不因仓库许可而重新授权。

| 组件 | 作用 | 上游入口 |
|---|---|---|
| .NET / WPF | 桌面运行时与界面框架 | https://github.com/dotnet/wpf |
| ONNX Runtime | CPU 模型推理 | https://github.com/microsoft/onnxruntime |
| SkiaSharp | 图像处理 | https://github.com/mono/SkiaSharp |
| xUnit、Microsoft.NET.Test.Sdk、coverlet.collector | 自动化测试 | 各项目文件中的 NuGet PackageReference |

精确版本以 `src/`、`tests/` 下的 `.csproj` 为准；分发构建产物时须保留 NuGet 包及其传递依赖附带的许可声明。

模型权重、现场照片、厂商 SDK 和业务数据不随本仓库授权。使用者须自行取得授权并提供符合模型契约的本地文件。验收数据适配器不代表真实模型推理。
