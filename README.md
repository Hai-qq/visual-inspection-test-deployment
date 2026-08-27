# Visual Inspection Test Deployment

面向 Windows 工业检测工位的视觉测试部署端。产品不限定为 YOLO：普通视觉规则、姿态时序、图源、模型绑定与 Test Sequence 通过统一配置组合。

开发语言固定为 **C#**，使用 .NET 8、WPF 和 MVVM。产品主体不得改用 Python、C++、JavaScript/TypeScript 或其他语言；PowerShell 仅承担构建与验收编排，厂商原生运行库必须通过 C# 适配器隔离。

## 当前分支状态

- 当前开发分支为 `codex/v2-production-foundation`，严格基于 `agent/v2-wizard-ui` 的 `780143945700b3b7a18a5056f6a0debd9cf62851`；`v1.0.0` 标签冻结旧版设置界面及其完整功能基线，现有 v1 Runner、配置和验收链继续作为兼容路径保留。
- 当前分支保留并直接复用三栏操作员工作台。管理员顶栏只保留“测试序列设置 V2”，图源统一在该向导第 02 步配置，不再提供独立“图源设置”按钮。当前向导严格用于前端界面确认：底栏只保留“上一步 / 下一步”，最终页不显示 Draft、Schema/Runtime、Publish、Assign、Activate、Rollback、Deployment 或 Lifecycle。既有 V2 配置与运行基础仍保留在代码中，本轮不扩展、不从当前界面调用；现有 Operator 主运行入口继续使用 v1 兼容 Runner。
- 操作员工作台已针对工位可读性放大整体字号和关键区域，`开始 / 停止 / 复位` 使用“图标 + 短文”的实体按钮形态；开始检测前必须先录入非空产品序列号，录入方式兼容人工输入、扫码枪和二维码回填，录入后仍须显式点击“开始”。每个序列号只触发一次单件检测；普通 Folder 示例只读取下一张图片，完成后清空输入并等待下一个序列号。基础 JSONL 运行日志把图片结果与该序列号绑定；工厂格式、重复策略和最终日志格式仍待样例。检测框和 ROI 标识继续叠加，但不再在画面上显示模型输出标签与置信度文字；右侧统计入口统一使用“合格率”。复杂规则详情保持固定区域并提供局部滚动，不挤占实时图像和右侧统计。
- “测试序列设置 V2”保留 `项目信息 → 选择图源 → 导入模型 → 测试步设置 → 检查完成` 5 个顶层步骤。目标检测、图像分割与姿态测试步都只显示“基本信息、自定义函数”；目标检测和图像分割在基本信息选择 Model 后先打开 Label 列表，点选一个 Label 再打开该 Label 独立的整图/ROI 与判定配置。首次选择姿态/时序模型时自动弹出动作顺序配置，保存后返回基本信息显示摘要及“重新编辑动作顺序”入口，不再提供独立动作页签。测试步列表从上到下就是执行顺序，不再显示第二套 Sequence Plan 编辑区。
- V2 步骤卡只有在本页必填校验通过并点击“下一步”确认后才显示“已完成”绿色；直接点击后续步骤不会把跳过的中间步骤补绿，已完成页的必填内容被清空后也会立即取消完成状态。
- V2 的“导入模型”使用项目模型库；模型列表顶部并排提供“添加模型 / 删除当前”，删除始终作用于左侧当前选中模型，被测试步引用时需先改绑，项目至少保留 1 个模型。当前前端只显示模型名称、任务类型、模型文件和标签来源，任务类型保留目标检测、姿态/时序和图像分割，已移除图像分类选项。标签来源收拢为“自动识别/手动填写”下拉框，只有选择手动填写时才展开精简编辑区。Model Version、SHA-256、Adapter ID、Runtime Profile 和 Contract 等内部字段不在本轮前端显示；底层数据结构仍保留，待功能阶段再决定如何接入。
- V2“测试步设置”使用一份有序测试步列表：新增项进入列表末尾，可用上下箭头调整，列表从上到下就是当前前端展示的执行顺序。独立调用计划、重复调用参数和内部 FunctionCode/InvocationId 均不在界面显示；底层映射与运行逻辑不属于本轮前端确认范围。
- V2 的“图片文件夹”和“视频文件夹”现在是同组互斥图源，选择任一项后共用文件夹路径面板，并分别保留各自路径；不再默认把视频与图片组合使用。USB 摄像头和工业相机仅以灰态“待开发”卡保留位置，不显示设备参数，也不能选择。图片读取、视频解码/取帧以及 DirectShow/厂商 SDK 接入仍留待功能阶段。
- 目标检测和图像分割只在基本信息绑定模型；检测类型提供“目标检测（单张图）/姿态动作（连续帧）/图像分割（单张图）”三项，模型下拉框只显示与当前检测类型匹配的项目模型。切换检测类型时自动改绑可用的同类模型；没有同类模型时明确提示返回第 03 步补充。绑定或修改模型任务类型时也会同步对应测试步类型。两种单帧类型共用逐 Label、整图/多 ROI 与判定配置前端，Label 弹窗只读显示该测试步当前模型，不再提供第二个模型选择入口。切换测试步时只显示当前测试步自己的检测子项；改绑模型后立即移除不属于新模型的旧子项，并按新模型重建 Label 列表。保存只新增或替换当前 Label，重新编辑任一 Label 不得清空其他已保存子项。ROI 可新增、删除和拖拽框选，勾选多个区域时右侧判定区实时显示当前区域名称；界面明确提示“仅适用于固定摄像头”。图像分割在本轮只补齐前端类别和配置状态，不代表分割推理、掩膜判定或 Runner 已接入。
- Pose 的动作顺序、Model Binding、Label、Hold 和 Wait 集中在按需弹出的动作配置层中；保存后回到基本信息，取消则恢复弹出前的动作内容。目标检测、图像分割与姿态测试步均提供“自定义函数”页，页内可直接切换当前测试步且各步配置互不覆盖；当前只配置函数类型、名称、Python 文件、延时和说明，不执行 Python，也不代表后续工厂函数库已接入。
- schema v2 仍保留正式 Trigger Binding、Invocation Policy 与 Runner 调度契约，外部 Signal Tag 到 PLC 地址的映射只存在于 `DeploymentBinding`；本轮前端不展示旧“触发与运行”演示表单，也未新增 PLC/IO/传感器连接能力。
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

生成可双击直达 V2 确认稿的 Windows x64 自包含单文件 EXE：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-FrontendDemo.ps1
```

默认产物位于 `artifacts\frontend-demo-v0.2-YYYYMMDD-win-x64\VisualInspection.FrontendDemo.exe`。该演示 EXE 不要求目标机器另行安装 .NET 8 Desktop Runtime；它仅代表已确认的前端范围，不扩展视频读取、相机/PLC/IO、分割/姿态推理、自定义函数或 V2 生产运行能力。普通 `VisualInspection.App.exe` 的登录启动行为不变。

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
- 当前帧、精确 ROI、检测框、目标名、置信度、规则标准、实测值、逐项结果和运行日志会随执行更新；结构化日志按天写入 JSON Lines；
- 首次启动自动生成 PASS/FAIL 两组可复现图片和带空间检测框的 `detections.json`，用于在没有真实模型/相机硬件时验收完整流程及统计；
- 提供端到端执行冒烟 `--acceptance-smoke`、主窗口/设置窗口渲染冒烟、正常启动生命周期冒烟、发布脚本和验收回执。

内置 `detections.json` 仍是**确定性验收适配器，不是模型推理**，仅用于 v1 兼容验收、V2 Acceptance 与自动回归。V2 Production 明确禁止 Manifest 回退；真实推理当前只承诺上述 ONNX Detection 契约。原始 YOLO 输出、动态输入、Classification、Segmentation、Pose/Temporal、PT 安全加载、DirectShow、厂商相机、真实 Trigger/Line Adapter、生产账户管理和历史查询仍待适配。详细范围与步骤见 [验收说明](./docs/验收说明.md)。

需求基线见 [软件需求规格说明](./docs/软件需求规格说明.md)，界面基线见 [前端 UI 概要设计](./docs/前端UI概要设计.md)，实现边界见 [技术架构与开发计划](./docs/技术架构与开发计划.md)。V2 的组件边界、执行安全语义与现场接入点见 [V2 生产架构设计](./docs/V2生产架构设计.md)，草稿、迁移、发布、激活和回滚见 [V2 配置迁移与发布](./docs/V2配置迁移与发布.md)。

## 直接验收

Release 入口：

```text
artifacts\acceptance-zh-CN\VisualInspection.App.exe
```

可复制压缩包：`artifacts\VisualInspection-v0.6.0-onnx-yolo-e2e-zh-CN-win-x64.zip`。

运行环境为 Windows x64，需安装 .NET 8 Desktop Runtime。启动后先进入登录窗口。本地验收账户为管理员 `admin / Admin@123`、操作员 `operator / Operator@123`；先输入任意非空验收序列号，再点击“开始”，内置四项序列应全部“通过”，右侧“当前会话”的通过数只增加 1。固定密码仅用于本地验收。

对于只包含普通检测项的 Folder Test Sequence，一个序列号只检测文件夹中的下一张受支持图片。再次检测必须重新录入序列号；同一应用会话内按文件夹排序逐张前进，到末尾后从第一张重新开始，不能再用一个序列号一次生成 15 个统计结果。

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
