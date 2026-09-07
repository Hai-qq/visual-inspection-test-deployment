# Visual Inspection Test Deployment

面向 Windows 工业检测工位的视觉测试部署端。产品不限定为 YOLO：普通视觉规则、姿态时序、图源、模型绑定与 Test Sequence 通过统一配置组合。

开发语言固定为 **C#**，使用 .NET 8、WPF 和 MVVM。产品主体不得改用 Python、C++、JavaScript/TypeScript 或其他语言；PowerShell 仅承担构建与验收编排，厂商原生运行库必须通过 C# 适配器隔离。

## 从 GitHub 克隆并运行

默认分支 `main` 包含当前操作员工作台、管理员测试序列设置界面及下述已实现功能的完整源码。无需复制原开发电脑上的 `bin`、`obj` 或 `artifacts`。

在 Windows x64 上安装 .NET 8 SDK（包含 WPF 构建支持），然后执行：

```powershell
git clone https://github.com/Hai-qq/visual-inspection-test-deployment.git
cd visual-inspection-test-deployment
dotnet restore VisualInspection.sln
dotnet build VisualInspection.sln --no-restore
dotnet test VisualInspection.sln --no-build --no-restore
dotnet run --project src/VisualInspection.App/VisualInspection.App.csproj --no-build
```

首次启动显示登录页。本地验收账户见下方“直接验收”；管理员登录后进入操作员工作台，点击右上角“测试序列设置”进入五步设置界面，操作员账户只开放执行与统计。恢复 NuGet 依赖需要联网。

普通克隆首次运行会自动生成确定性图片与 `detections.json` 演示数据，用于检查完整操作流程，**不是实际模型推理**。真实 ONNX 推理、Sequence 导入导出及结果保存的代码均包含在仓库中；真实检测需提供符合下述契约的 ONNX 模型与本地图片。模型、现场图片和原电脑的运行配置不随源码公开上传。内置 Fan 模型的自包含演示包需另行提供脚本要求的 Fan 模型与图片才能构建，普通源码构建不依赖这些文件。

## 当前实现状态

- 当前实现由 `codex/v2-production-foundation` 同步到默认分支 `main`，延续 `agent/v2-wizard-ui` 的 `780143945700b3b7a18a5056f6a0debd9cf62851`；`v1.0.0` 标签冻结旧版设置界面及其完整功能基线，现有 v1 Runner、配置和验收链继续作为兼容路径保留。
- 当前分支保留三栏操作员工作台。默认内置项目为“FAN-A01 视觉检测项目”，生产型号为 `FAN-A01`，演示包预装 `fan.onnx` 与 `IMG_1533.JPG`；唯一“风扇检测”测试步以 AND 组合 6 条标签规则。操作台顶栏提供“导入测试序列”和管理员“测试序列设置”，运行端继续通过经过验证的 v1 兼容 Runner 执行受支持的 ONNX Detection。
- Folder 图源不再要求手工录入序列号：每次点击“开始”读取下一张图片，并把图片主文件名作为产品序列号。只有 Camera 图源在开始时弹出序列号窗口，确认后只执行一次检测；当前相机没有真实适配器时仍保持 NotReady。检测完成后的预览保留原始图片、检测框和模型原始英文 Output Label，不翻译为 Target 中文名，也不显示置信度；“当前检测项”使用“检测标签 / 判定逻辑 / 本次实测 / Result”表格。表格在运行前按当前 sequence 预加载数量、缺失数量、存在/不存在及比较条件，运行后填入实际值，Result 以红绿状态区分，并显示当前项的 AND/OR 组合逻辑。
- 每次运行继续写内部 JSONL 审计，同时写生产 TXT：`机台|日期|时间|工站|型号|员工号|序列号|测试结果|图片地址|`。PASS/FAIL 图片分别保存到 `results\images\pass` 与 `results\images\fail`，文件名包含序列号、毫秒时间和结果；ERROR 没有可信当前帧时图片地址留空。当前员工号取登录用户名。
- “测试序列设置”保留 `项目信息 → 选择图源 → 导入模型 → 测试步设置 → 应用与导出` 五步。项目信息使用真实产品型号格式，内置示例为 `FAN-A01`；每个 `.sequence.json` 只能包含一个型号。最终步骤把“应用到当前操作台”和“导出 Sequence 与模型”分成两个独立按钮：应用会保留当前设置中的本地图源绑定，校验后重建当前操作台，失败时保留原配置；导出会弹出保存位置并写逻辑文件及引用模型，不会自动切换或关闭操作台。导出模型写入 SHA-256，并剥离机台本地 Deployment Binding；操作台导入时校验 schema、单型号约束、模型存在性与哈希，Folder 图源在接收端统一解析为交付目录下的 `input`。
- 模型库允许删到 0 个；删除被引用模型时同步移除相应测试步，避免悬空配置。选择 ONNX 后尝试读取标签元数据，标签通过加号、减号和直接改名人工维护，不再提供“自动/手动”下拉框。模型文件和图源按钮设置最小宽度，说明式提示移到 ToolTip。
- 新增测试步后通过弹窗选择与检测类型兼容的模型。用户可见“检测子项”术语统一为“检测标签”。所有测试步统一参与序列执行和产品总判定，界面不再显示容易误解的“是否必选”；底层兼容字段仍保留。置信度字段移到规则区底部，默认 `0.5` 且可留空；其他必填字段继续使用红色 `*`。
- ROI 配置只保留一个默认 ROI；必须先导入一张标注底图，之后才能拖动框选或保存 ROI。预览使用等比例 `Uniform` 显示，ROI 坐标按实际图像区域和导入图像像素尺寸映射，灰色留白不参与框选。标注底图只用于设置，不写入 sequence，也不作为生产运行输入。
- 向导内的说明式 ToolTip 统一使用可见深色样式、约 150 ms 显示延迟、20 秒显示时长；信息图标扩展为至少 22 × 22 的透明命中区，禁用控件上的说明也可显示。
- 目标检测、图像分割与姿态测试步仍只显示“基本信息、自定义函数”；图像分割、Pose/Temporal、PT、视频、相机/PLC/IO 和自定义 Python 若无对应 C# 适配器必须保持运行门禁，不因界面可配置而宣称已接入。
- 新增 `VisualInspection.Runner`，显式按 `OrderedInvocations` 执行，提供 Trigger 去重/幂等、有界 Channel、背压、最大并发、Deadline、取消、迟到结果丢弃、帧关联、四种 Capture Policy、共享 Observation、结构化错误和 Line Result 锁存/Ack 状态机。
- V2 Infrastructure 已实现模拟 Trigger/Camera/Model/Line Adapter、未配置 fail-closed Adapter、JSON 原子持久化、schema v1→v2 迁移、静态 ONNX YOLO E2E Detection CPU Adapter 和原始/编码帧预处理。**真实 PLC、IO、工业相机、GPU、PT、YOLO Raw、Classification、Segmentation、Pose Keypoint 和 Temporal Action Adapter 尚未接入**；Production 禁止 `detections.json`/Manifest、模拟器、未配置适配器和操作员调试选源，缺少真实就绪链时保持 NotReady，绝不回退产生 Pass。

直接打开操作员工作台界面预览（跳过登录，仅用于本地 UI 评审）：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --operator-preview
```

直接打开 V2 前端预览：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-preview
```

生成包含完整“登录 → 操作台 → V2 设计 → 返回操作台”路径及 Fan 模型/示例图的 Windows x64 自包含单文件 EXE：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-FrontendDemo.ps1
```

默认产物位于 `artifacts\frontend-demo-v0.2-YYYYMMDD-win-x64\VisualInspection.FrontendDemo.exe`。双击后先显示登录页；管理员登录进入型号 `FAN-A01`、已加载 `IMG_1533.JPG` 的 Fan 操作台，点击“开始”后以 `IMG_1533` 作为序列号，由内置 `fan.onnx` 通过 ONNX Runtime CPU 执行一次分析，再由唯一的“风扇检测”测试步汇总 6 条 AND 规则；“测试序列设置”可直接应用当前配置，也可另选位置导出 Sequence 与模型。该演示 EXE 不要求目标机器另行安装 .NET 8 Desktop Runtime；首次登录后解包内置模型和图片可能需要数秒。此结果只证明指定模型与指定图片的接入链，不代表通用精度、现场节拍或生产能力；视频读取、相机/PLC/IO、分割/姿态推理、自定义函数及 V2 生产运行能力仍未扩展。

生成 8.31 代表性 sequence、对应模型、说明文档和 SHA-256 清单：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-SequenceDelivery.ps1
```

默认输出到 `artifacts\FAN-A01-Receiver-Package-20260829`，包含 `FAN-A01.sequence.json`、按型号命名的 `FAN-A01.onnx`、生产商操作台接入说明、`input\IMG_1533.JPG` 运行验证图和 SHA-256 清单。生产商只消费我方导出的 Sequence 与模型并开发操作台，不实现我方设置端、配置发布或版本迁移；代表图只用于让其操作台在导入后立即执行一次 Folder 验证，不是生产图片或模型性能证据。交付格式与导入步骤见 [Sequence 与模型交付说明](./docs/Sequence与模型交付说明.md)，会议变更和验收口径见 [8.27 SE 会议变更基线](./docs/8.27SE会议变更基线.md)。脚本不执行夸克上传。

直接打开 USB 图源选中状态用于回归检查：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-source-preview
```

直接打开多模型库步骤用于评审：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-models-preview
```

直接打开逐 Label 检测配置入口用于评审：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-roi-preview
```

直接打开判定条件实时摘要步骤用于评审：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-rule-preview
```

直接打开自定义函数页用于评审（参数名为兼容既有回归入口而保留）：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-trigger-preview
```

生成用于视觉核验的首屏快照：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-snapshot
```

生成测试步基本信息或姿态类型的视觉核验快照：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-items-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-pose-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-source-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-models-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-roi-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-rule-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-trigger-snapshot
```

## v1.0.0 兼容验收基线

`v1.0.0` 标签冻结的是 **v0.6.0 ONNX YOLO 端到端推理验收版**，以下列表描述该兼容基线，不代表当前分支新增前端控件已经接入生产运行：

- 操作工工作台采用单页三栏布局：左侧测试序列和图标短文执行按钮、中部实时帧与当前项、右侧固定会话统计，执行与统计始终同屏；
- 操作工工作台、管理员图源页、示例项目、测试项、状态、日志与运行异常提示均使用简体中文；
- 独立兼容图源窗口仍支持文件夹路径选择、递归、排序、损坏文件策略、循环、姿态帧间隔、首帧校验预览及配置保存，但操作台不再提供入口；新前端统一从 V2 向导第 02 步进入图源配置；
- 管理员“测试序列设置”提供常规、模型与标签、目标与绑定、规则与 ROI、姿态序列、输入源、用户与权限七个分区；“规则与 ROI”可新增、删除普通检测项，并为新增项建立可继续编辑的默认规则；
- “模型与标签”可从 ONNX `metadata_props` 中读取 `names`、`labels`、`class_names` 或 `classes`，兼容 JSON 与常见 Python 字典/列表文本，并允许人工检查和修正；
- 已接入真实 ONNX Runtime CPU 推理：支持静态 `Float[1,3,H,W]` 输入和端到端 `Float[1,N,6]` 输出的 Detection 模型，使用等比例 Letterbox、RGB/0–1 预处理，并把 `x1,y1,x2,y2,confidence,classId` 还原为原图像素坐标；
- 真实 ONNX 检测结果按 Output Label 映射到明确的 Target/Model Binding，规则置信度、Full Image/ROI 计数、加粗且分辨率自适应的检测框/标签叠加、Pass/Fail 和日志共用同一份输出；模型契约不匹配时保持运行门禁；
- 模型/标签、目标主绑定、数量规则、逻辑关系、阈值、全图/ROI、单项延时和姿态步骤均可编辑并经过统一配置校验后持久化；
- ROI 可手工填写坐标，也可在当前图像上拖动鼠标框选；姿态步骤以线性动作画布展示，并支持新增、删除和上下移动；
- 启动时进入中文登录窗口；管理员可访问全部设置，操作员登录后只显示测试执行与统计；本地密码仅以 PBKDF2-SHA256 哈希保存；
- Folder 支持 `.jpg`、`.jpeg`、`.png`、`.bmp`，具有确定排序、进度、坏图 Skip/Stop 与空转保护；全部启用项均为普通检测项时，每次录入一个序列号并点击“开始”只读取排序后的下一张图片；
- 单件执行以“一个序列号 + 一张图片 + 一套完整普通 Test Sequence”为一个统计对象；同一图片的多个普通项共享一次模型分析，完成后只累加一个 Pass/Fail/Error，并把序列号、图片名和结果写入同一日志记录；
- Test Sequence Runner 执行逐项 Delay、普通数量规则及固定线性姿态动作序列；含姿态项的文件夹仍按连续帧时序执行，不套用普通图片批量语义；
- 普通规则支持在场数量、缺失数量、是否存在，以及 `=`、`!=`、`>`、`>=`、`<`、`<=`、闭区间和 AND/OR；
- 检测框按目标、模型绑定和置信度筛选；ROI 使用检测框中心点归属，支持参考尺寸缩放与多 ROI 去重，Full Image/ROI 数量会进入同一规则引擎；
- 姿态项按顺序、连续保持时间和最大等待时间判定；
- Pass、Fail、Error、Stopped 分离，右侧统计支持数量/比率切换；数量模式同时显示横向数量条和下方通过/不通过饼图，比率模式显示大圆环，Error 不进入两种图表；
- 当前帧、精确 ROI、检测框和模型原始 Output Label 会随执行显示；Target 中文名只用于当前项及规则表。置信度继续用于检测筛选、规则判定与结构化记录，但不绘制在检测图上。规则标准、实测值、逐项结果和运行日志同步更新；结构化日志按天写入 JSON Lines；
- 普通开发运行在没有真实模型/相机硬件时自动生成 Fan PASS/FAIL 两组可复现图片和带空间检测框的 `detections.json`；前端演示 EXE 则内置已验证的 `fan.onnx` 和 `IMG_1533.JPG`，优先执行真实 ONNX Runtime CPU 检测；
- 提供端到端执行冒烟 `--acceptance-smoke`、主窗口/设置窗口渲染冒烟、正常启动生命周期冒烟、发布脚本和验收回执。

内置 `detections.json` 仍是**确定性验收适配器，不是模型推理**，仅用于 v1 兼容验收、V2 Acceptance 与自动回归。V2 Production 明确禁止 Manifest 回退；真实推理当前只承诺上述 ONNX Detection 契约。原始 YOLO 输出、动态输入、Classification、Segmentation、Pose/Temporal、PT 安全加载、DirectShow、厂商相机、真实 Trigger/Line Adapter、生产账户管理和历史查询仍待适配。详细范围与步骤见 [验收说明](./docs/验收说明.md)。

需求基线见 [软件需求规格说明](./docs/软件需求规格说明.md)，界面基线见 [前端 UI 概要设计](./docs/前端UI概要设计.md)，实现边界见 [技术架构与开发计划](./docs/技术架构与开发计划.md)。V2 的组件边界、执行安全语义与现场接入点见 [V2 生产架构设计](./docs/V2生产架构设计.md)，草稿、迁移、发布、激活和回滚见 [V2 配置迁移与发布](./docs/V2配置迁移与发布.md)。

## 直接验收

Release 入口：

```text
artifacts\acceptance-zh-CN\VisualInspection.App.exe
```

可复制压缩包：`artifacts\VisualInspection-v0.6.0-onnx-yolo-e2e-zh-CN-win-x64.zip`。

运行环境为 Windows x64，需安装 .NET 8 Desktop Runtime。启动后先进入登录窗口。本地验收账户为管理员 `admin / Admin@123`、操作员 `operator / Operator@123`；Folder 图源直接点击“开始”，程序以当前图片主文件名作为序列号，内置“风扇检测”单项及其 6 条 AND 规则应产生一个完整产品结果。固定密码仅用于本地验收。

对于只包含普通检测项的 Folder Test Sequence，每次“开始”只检测文件夹中的下一张受支持图片；同一应用会话内按文件夹排序逐张前进，到末尾后从第一张重新开始。Camera 图源才会在每次开始时弹窗录入序列号。

端到端冒烟：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Invoke-AcceptanceSmoke.ps1
```

完整重建、测试、发布和冒烟：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Acceptance.ps1
```

## 开发命令

```powershell
dotnet restore VisualInspection.sln
dotnet build VisualInspection.sln --no-restore
dotnet test VisualInspection.sln --no-restore
dotnet run --project src/VisualInspection.App/VisualInspection.App.csproj
```

## 运行数据

```text
%LocalAppData%\VisualInspectionTestDeployment\projects\<project-id>.json
%LocalAppData%\VisualInspectionTestDeployment\users\users.json
%LocalAppData%\VisualInspectionTestDeployment\acceptance-data\sample-set-*\
%LocalAppData%\VisualInspectionTestDeployment\logs\inspection-YYYYMMDD.jsonl
%LocalAppData%\VisualInspectionTestDeployment\results\logs\inspection-results-YYYYMMDD.txt
%LocalAppData%\VisualInspectionTestDeployment\results\images\pass\
%LocalAppData%\VisualInspectionTestDeployment\results\images\fail\
%LocalAppData%\VisualInspectionTestDeployment\acceptance-smoke-result.json
%LocalAppData%\VisualInspectionTestDeployment\v2-configuration\drafts\<draft-id>.json
%LocalAppData%\VisualInspectionTestDeployment\v2-configuration\packages\<package-id>.json
%LocalAppData%\VisualInspectionTestDeployment\v2-configuration\assignments\<deployment-id>.json
%LocalAppData%\VisualInspectionTestDeployment\v2-configuration\active\<deployment-id>.json
```

v1 兼容项目仍采用 schema v1 信封。V2 Draft、Assignment 与 Active Pointer 使用同目录临时文件原子替换；Published Package 使用 Create-New 写入且不允许覆盖。V2 便携 Project Snapshot 与 Deployment Binding 分离，Folder 相对路径以运行时基准目录解析，现场部署建议使用经校验的绝对路径或 Artifact URI。

## 目录

```text
src/VisualInspection.App             WPF 工作台、Admin 图源页、经典设置页、V2 顺序向导前端、内置验收数据与启动编排
src/VisualInspection.Core            v1 兼容域模型，以及纯领域 schema v2、触发/帧/Adapter 契约、规则与安全状态机
src/VisualInspection.Infrastructure  Folder、ONNX Runtime/标签读取、帧预处理、V2 Adapter/注册表/运行校验、配置生命周期存储与日志
src/VisualInspection.Runner          V2 Sequence Orchestrator、触发调度、采集协调、步骤执行和安全结果发布
tests/VisualInspection.Core.Tests    规则、配置/序列编辑、ONNX 元数据/真实模型与文件夹批量探针、存储、Folder 与 Runner 回归测试
tests/VisualInspection.V2.Tests      schema/生命周期、Trigger/Frame、Runner、Pose、Line、Production Policy 和预处理测试
tests/VisualInspection.Tests.app     V2 WPF 编辑状态与正式 Draft 双向映射测试（项目名：VisualInspection.App.Tests）
scripts/                             Release 构建与端到端冒烟脚本
docs/                                需求、UI、架构与验收说明
```
