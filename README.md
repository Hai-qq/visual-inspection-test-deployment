<p align="center"><img src="docs/assets/icon.svg" width="112" height="112" alt="Visual Inspection 检测框与确认标记"></p>

<h1 align="center">Visual Inspection<br>Test Deployment</h1>
<p align="center">为 Windows 检测工位配置、执行和交付视觉测试序列。</p>
<p align="center">
  <a href="https://github.com/Hai-qq/visual-inspection-test-deployment/actions/workflows/ci.yml"><img src="https://github.com/Hai-qq/visual-inspection-test-deployment/actions/workflows/ci.yml/badge.svg" alt="Windows 构建与测试"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-246aab" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/C%23-.NET_8_%C2%B7_WPF-246aab" alt="C# .NET 8 WPF">
</p>
<p align="center"><a href="#快速开始">快速开始</a> · <a href="docs/Sequence与模型交付说明.md">交付 Sequence</a> · <a href="CONTRIBUTING.md">参与开发</a> · <a href="https://github.com/Hai-qq/visual-inspection-test-deployment/issues/new/choose">反馈问题</a></p>

用一个操作员工作台完成图片检测、规则判定与结果保存；管理员通过五步向导配置图源、模型、检测标签和 ROI，并导出可校验的 Sequence。产品使用 **C#、.NET 8、WPF 和 MVVM**，包含领域逻辑、适配器和自动化测试。

## 能做什么

- **配置与交付**：五步设置向导，导入/导出单型号 `.sequence.json`；模型文件使用 SHA-256 校验，现场图源绑定与便携配置分离。
- **检测与判定**：Folder 图片输入、受支持的 ONNX Detection、全图或 ROI、标签数量/缺失/存在规则及 AND/OR 组合。
- **操作与追溯**：管理员与操作员入口、图像和检测框预览、PASS/FAIL/ERROR 状态、JSONL 审计、TXT 结果和结果图片。

## 当前支持范围

| 能力 | 当前边界 |
|---|---|
| 平台 | Windows x64，.NET 8；WPF 界面不在 macOS/Linux 原生运行 |
| 图源 | 本地图片文件夹；相机、视频及现场触发无真实适配器时保持 NotReady |
| 真实推理 | ONNX Runtime CPU；静态 `Float[1,3,H,W]` 输入、端到端 `Float[1,N,6]` Detection 输出 |
| 其他模型 | 原始 YOLO 输出、动态输入、PT、分割、姿态等不能仅凭导入成功视为可运行 |
| 自定义函数 | 当前保存函数元数据，不执行函数 |
| 验收数据 | `detections.json` 是确定性测试适配器，不能作为真实模型结果 |

本项目按源码提供。现场模型、照片、设备 SDK 和生产配置需自行准备；本仓库未发布正式安装包。自动测试与配置校验不等于现场生产验收。

## 快速开始

在 **Windows x64** 安装 Git 与 **.NET 8 SDK**，从 PowerShell 运行：

```powershell
git clone https://github.com/Hai-qq/visual-inspection-test-deployment.git
cd visual-inspection-test-deployment
dotnet restore VisualInspection.sln
dotnet build VisualInspection.sln --no-restore
dotnet test VisualInspection.sln --no-build --no-restore
dotnet run --project src/VisualInspection.App/VisualInspection.App.csproj --no-build
```

恢复 NuGet 依赖需要联网，普通源码构建不依赖现场模型或图片。

### 第一次检测

1. 启动后进入登录页；本地验收账户及其限制见[实现与验收记录](docs/IMPLEMENTATION.md#直接验收)。这些账户仅用于本地验收，生产账户管理尚待接入。
2. 管理员进入“测试序列设置”，依次配置项目信息、Folder 图源、符合契约的模型、测试步与标签规则。
3. 选择“应用到当前操作台”，或导出后通过“导入测试序列”加载；两项操作相互独立。
4. 点击“开始”检测文件夹中的下一张图片，以图片主文件名作为产品序列号。查看检测框、规则结果及保存记录。

每次启动都等待显式导入，不自动恢复历史项目，也不默认加载示例图片。跨机器交付的目录结构与模型约束见 [Sequence 与模型交付说明](docs/Sequence与模型交付说明.md)。

## 架构与文档

源码项目位于 `src/VisualInspection.*`，下表省略共同前缀。

| 目录 | 职责 |
|---|---|
| `App` | WPF 工作台、登录与设置向导 |
| `Core` | 领域模型、规则、配置与安全状态机 |
| `Infrastructure` | Folder/ONNX 适配器、预处理、存储与日志 |
| `Runner` | V2 序列编排、触发调度和结果发布 |
| `tests/` | 领域逻辑、V2 执行和 WPF 编辑状态回归 |

[实现状态与历史验收](docs/IMPLEMENTATION.md) · [验收步骤](docs/验收说明.md) · [V2 架构](docs/V2生产架构设计.md) · [配置迁移与发布](docs/V2配置迁移与发布.md) · [需求规格](docs/软件需求规格说明.md)

运行数据默认保存在以下目录，包括配置、审计和结果图片；不要将该目录提交到 Git 或直接附在公开 Issue 中。

```text
%LocalAppData%\VisualInspectionTestDeployment
```

## 参与维护

构建、测试与贡献边界见 [CONTRIBUTING](CONTRIBUTING.md)。报告问题请提供系统版本、项目提交、最小 Sequence 和脱敏日志，模型及图像必须有分享授权。讨论遵守[社区约定](CODE_OF_CONDUCT.md)，漏洞请[私密报告](SECURITY.md)。

原创代码及项目标识使用 [MIT License](LICENSE)，第三方组件和现场资产范围见[第三方声明](THIRD_PARTY_NOTICES.md)。
