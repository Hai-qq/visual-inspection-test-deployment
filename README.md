# Visual Inspection Test Deployment

面向 Windows 工业检测工位的视觉测试部署端。产品不限定为 YOLO：普通视觉规则、姿态时序、图源、模型绑定与 Test Sequence 通过统一配置组合。

开发语言固定为 **C#**，使用 .NET 8、WPF 和 MVVM。产品主体不得改用 Python、C++、JavaScript/TypeScript 或其他语言；PowerShell 仅承担构建与验收编排，厂商原生运行库必须通过 C# 适配器隔离。

## 当前分支状态

- `v1.0.0` 标签冻结了旧版设置界面及其完整功能基线；该标签中的应用程序集版本仍为 v0.6.0。
- 当前 `agent/v2-wizard-ui` 分支新增“测试序列设置 V2”前端原型：顶部按真实配置流程展示 8 个步骤，下方一次只显示当前步骤；步骤条可横向滚动，不把参考图中的 5 步写死。
- V2 步骤卡只有在本页必填校验通过并点击“下一步”确认后才显示“已完成”绿色；直接点击后续步骤不会把跳过的中间步骤补绿，已完成页的必填内容被清空后也会立即取消完成状态。
- V2 的“导入模型”已改为项目模型库：左侧可连续添加、选择和删除多个模型，右侧为当前模型独立设置名称、任务类型、ONNX/PT 文件及标签/动作名称来源；“编排检测项”的模型下拉框直接读取同一模型库，不再使用写死选项。已被检测项绑定的模型会提示先改绑再删除。
- V2 的“编排检测项”提供有序卡片、加号新增、减号删除、上下移动、必选/可选状态和检测类型；姿态动作已并入检测类型，后续页面只显示目标或姿态所需设置。必填字段使用红色 `*`，信息与操作图标提供 ToolTip。
- V2 的图片文件夹、USB 摄像头和工业相机图源卡均可整卡点击；当前选中卡使用浅绿背景、深绿边框和选中图标，不保留固定在初始图源上的假高亮。
- V2“检测内容”的 ROI 预览区已支持真实鼠标拖拽框选：拖动时矩形和基于 `640 × 480` 参考画布的 `X1/Y1/X2/Y2` 实时同步，“重新框选区域”会给出操作提示；过小框选和 Esc 取消会保留上一次有效 ROI。
- V2“判定条件”的最终判定摘要已改为实时生成：目标检测会跟随当前模型标签、全图/ROI、ROI 坐标、数量等于/范围/大于和输入值变化；姿态检测会跟随当前动作、保持时间和最大等待时间变化。数量范围会显示最大值输入，最小值大于最大值时阻止进入下一步。
- V2 当前只实现页面、上一步/下一步、顶部跳转、模型库/检测项/姿态动作的前端加减排序、类型联动、动态模型选择、ROI 前端框选、判定摘要联动和最终确认状态；页面使用示例数据，**不会从磁盘导入模型，也不会读取、保存或发布项目配置，ROI 预览也尚未绑定真实图源**。旧版设置代码仍保留，待 V2 界面评审通过后再分步接入业务。

直接打开 V2 前端预览：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-preview
```

直接打开 USB 图源选中状态用于回归检查：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-source-preview
```

直接打开多模型库步骤用于评审：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-models-preview
```

直接打开 ROI 鼠标框选步骤用于评审：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-roi-preview
```

直接打开判定条件实时摘要步骤用于评审：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-rule-preview
```

生成用于视觉核验的首屏快照：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-snapshot
```

生成检测项编排或姿态类型的视觉核验快照：

```powershell
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-items-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-pose-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-source-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-models-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-roi-snapshot
dotnet run --project src\VisualInspection.App\VisualInspection.App.csproj -- --v2-wizard-rule-snapshot
```

## 当前可验收版本

当前版本为 **v0.6.0 ONNX YOLO 端到端推理验收版**，已形成可直接启动的 Windows Release：

- 操作工工作台采用单页三栏布局：左侧测试序列、中部实时帧与当前项、右侧固定会话统计，执行与统计始终同屏；
- 操作工工作台、管理员图源页、示例项目、测试项、状态、日志与运行异常提示均使用简体中文；
- 管理员图源页支持文件夹路径选择、递归、排序、损坏文件策略、循环、姿态帧间隔、首帧校验预览及配置保存；
- 管理员“测试序列设置”提供常规、模型与标签、目标与绑定、规则与 ROI、姿态序列、输入源、用户与权限七个分区；“规则与 ROI”可新增、删除普通检测项，并为新增项建立可继续编辑的默认规则；
- “模型与标签”可从 ONNX `metadata_props` 中读取 `names`、`labels`、`class_names` 或 `classes`，兼容 JSON 与常见 Python 字典/列表文本，并允许人工检查和修正；
- 已接入真实 ONNX Runtime CPU 推理：支持静态 `Float[1,3,H,W]` 输入和端到端 `Float[1,N,6]` 输出的 Detection 模型，使用等比例 Letterbox、RGB/0–1 预处理，并把 `x1,y1,x2,y2,confidence,classId` 还原为原图像素坐标；
- 真实 ONNX 检测结果按 Output Label 映射到明确的 Target/Model Binding，规则置信度、Full Image/ROI 计数、检测框叠加、Pass/Fail 和日志共用同一份输出；模型契约不匹配时保持运行门禁；
- 模型/标签、目标主绑定、数量规则、逻辑关系、阈值、全图/ROI、单项延时和姿态步骤均可编辑并经过统一配置校验后持久化；
- ROI 可手工填写坐标，也可在当前图像上拖动鼠标框选；姿态步骤以线性动作画布展示，并支持新增、删除和上下移动；
- 启动时进入中文登录窗口；管理员可访问全部设置，操作员登录后只显示测试执行与统计；本地密码仅以 PBKDF2-SHA256 哈希保存；
- Folder 支持 `.jpg`、`.jpeg`、`.png`、`.bmp`，具有确定排序、进度、坏图 Skip/Stop 与空转保护；全部启用项均为普通检测项时，一次“开始测试”会按顺序遍历全部图片；
- 文件夹批量执行以“一张图片的一套完整普通 Test Sequence”为一个统计对象；同一图片的多个普通项共享一次模型分析，逐图更新当前画面、日志及右侧 Pass/Fail/Error；
- Test Sequence Runner 执行逐项 Delay、普通数量规则及固定线性姿态动作序列；含姿态项的文件夹仍按连续帧时序执行，不套用普通图片批量语义；
- 普通规则支持在场数量、缺失数量、是否存在，以及 `=`、`!=`、`>`、`>=`、`<`、`<=`、闭区间和 AND/OR；
- 检测框按目标、模型绑定和置信度筛选；ROI 使用检测框中心点归属，支持参考尺寸缩放与多 ROI 去重，Full Image/ROI 数量会进入同一规则引擎；
- 姿态项按顺序、连续保持时间和最大等待时间判定；
- Pass、Fail、Error、Stopped 分离，右侧统计支持数量/比率切换；数量模式同时显示横向数量条和下方通过/不通过饼图，比率模式显示大圆环，Error 不进入两种图表；
- 当前帧、精确 ROI、检测框、目标名、置信度、规则标准、实测值、逐项结果和运行日志会随执行更新；结构化日志按天写入 JSON Lines；
- 首次启动自动生成 PASS/FAIL 两组可复现图片和带空间检测框的 `detections.json`，用于在没有真实模型/相机硬件时验收完整流程及统计；
- 提供端到端执行冒烟 `--acceptance-smoke`、主窗口/设置窗口渲染冒烟、正常启动生命周期冒烟、发布脚本和验收回执。

内置 `detections.json` 仍是**确定性验收适配器，不是模型推理**，用于没有受支持模型时回归完整流程。真实推理当前只承诺上述 ONNX Detection 契约；原始 YOLO 输出、动态输入、Classification、Segmentation、Pose/Temporal、PT 安全加载、DirectShow、厂商相机、生产账户管理、测试序列版本快照和历史查询仍待适配。没有可用 ONNX 运行时或验收清单时，界面会阻止开始测试。详细范围与步骤见 [验收说明](./docs/验收说明.md)。

需求基线见 [软件需求规格说明](./docs/软件需求规格说明.md)，界面基线见 [前端 UI 概要设计](./docs/前端UI概要设计.md)，实现边界见 [技术架构与开发计划](./docs/技术架构与开发计划.md)。

## 直接验收

Release 入口：

```text
artifacts\acceptance-zh-CN\VisualInspection.App.exe
```

可复制压缩包：`artifacts\VisualInspection-v0.6.0-onnx-yolo-e2e-zh-CN-win-x64.zip`。

运行环境为 Windows x64，需安装 .NET 8 Desktop Runtime。启动后先进入登录窗口。本地验收账户为管理员 `admin / Admin@123`、操作员 `operator / Operator@123`；点击“开始测试”，内置四项序列应全部“通过”，右侧“当前会话”的通过数增加 1。固定密码仅用于本地验收。

对于只包含普通检测项的 Folder Test Sequence，点击一次“开始测试”会检测文件夹中的全部受支持图片；右侧统计按已完成图片逐张累加，而不是只增加 1。2026-08-14 使用 `fan.onnx` 对 `Fan\OK` 的 15 张图片实测为通过 15、不通过 0、错误 0、跳过 0。

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
```

项目配置采用 schema v1 信封并以同目录临时文件原子替换。Folder 相对路径以应用目录解析，现场配置建议使用绝对路径。

## 目录

```text
src/VisualInspection.App             WPF 工作台、Admin 图源页、经典设置页、V2 顺序向导前端、内置验收数据与启动编排
src/VisualInspection.Core            配置、Test Sequence 编辑、规则、分析/模型契约与 Runner
src/VisualInspection.Infrastructure  Folder、ONNX Runtime/标签读取、YOLO 端到端输出、验收清单、JSON 配置与日志
tests/VisualInspection.Core.Tests    规则、配置/序列编辑、ONNX 元数据/真实模型与文件夹批量探针、存储、Folder 与 Runner 回归测试
scripts/                             Release 构建与端到端冒烟脚本
docs/                                需求、UI、架构与验收说明
```
