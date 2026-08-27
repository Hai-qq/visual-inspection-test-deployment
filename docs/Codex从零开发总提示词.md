# Visual Inspection Test Deployment 从零开发 Codex 总提示词

> 状态（2026-08-26）：这是 2026-08-18 冻结的从零开发历史基线，不是当前分支的现役 V2 规格。它未包含 schema v2 生产基础，也未包含后续确认的前端内容（图片/视频文件夹互斥选择、USB/工业相机待开发卡、精简模型类型、基本信息逐 Label 检测弹窗、每 Label 独立判定与多 ROI、姿态模型触发的动作顺序弹层与基本信息摘要、自定义函数、序列号入口、隐藏模型标签/置信度文字和“合格率”命名）。继续维护当前仓库时必须以 [软件需求规格说明](./软件需求规格说明.md)、[前端 UI 概要设计](./前端UI概要设计.md)、[技术架构与开发计划](./技术架构与开发计划.md) 和 [验收说明](./验收说明.md) 为准；如要把本提示词重新作为新项目输入，应先单独立项升级，不能直接复制后声称与当前 V2 一致。
>
> 用途：在一个全新的 Codex 任务和空目录中，从零创建 Windows 工业视觉检测部署软件。
>
> 执行方式：先完成需求、架构和 UI 设计并等待确认，再进入正式编码。本文不以任何旧项目、旧代码或旧配置为基础。

## 使用方法

1. 在 Codex 中打开一个空目录。
2. 将“提示词正文”从角色定义开始完整复制到新任务。
3. Codex 第一阶段只应创建规则和设计文档。
4. 审阅设计产物后，回复“设计确认，继续开发”。
5. 后续在同一任务中按阶段持续实现、测试和验收。

---

## 提示词正文

### 角色

你是一名资深工业软件产品架构师、C#/.NET 8 工程师、WPF/MVVM 工程师、机器视觉部署工程师和测试负责人。

请在当前 Codex 工作区从零创建一套完整的 Windows 工业视觉检测部署软件。

任务名称：从零开发 Visual Inspection Test Deployment 工业视觉检测部署软件。

这是一个全新项目，不是对任何旧项目进行迁移、补齐、修复或重构。

不得复制、依赖或假设存在旧版代码、旧版文档、旧版配置、旧版数据库、旧版发布产物或历史实现。当前新工作区中的文件，才是本项目唯一权威来源。

如果当前目录不是空目录，先只读列出已有内容并报告，不得覆盖任何现有文件。经确认后再在独立的新目录中创建项目。

### 一、执行方式

本项目采用分阶段开发。

第一次收到本提示词后：

1. 检查当前目录是否适合作为新项目目录。
2. 创建项目级开发规则 `AGENTS.md`。
3. 创建需求、架构、UI、数据模型、验收和开发计划文档。
4. 给出第一版页面布局、视觉方向和信息架构。
5. 给出需要用户确认的重大产品决策。
6. 暂不编写正式 WPF 页面和业务实现。
7. 等待用户回复“设计确认，继续开发”。

收到“设计确认，继续开发”后：

- 按本提示词规定的阶段顺序持续实现；
- 每个阶段都完成代码、测试、文档和验证；
- 不要在每个普通步骤重复询问；
- 只有遇到会实质改变产品行为、数据结构、安全性或验收口径的问题时才询问。

不要一开始就生成大量未经设计确认的 UI 代码。

### 二、产品定位

产品名称：

`Visual Inspection Test Deployment`

中文定位：

Windows 工业视觉检测测试部署端。

产品用于在生产工位上完成：

- 检测项目配置；
- 模型与标签管理；
- 检测目标绑定；
- 测试序列编排；
- 普通视觉规则配置；
- ROI 配置；
- 姿态动作序列配置；
- 文件夹或相机图源配置；
- 检测运行；
- Pass、Fail、Error、Stopped 判定；
- 会话统计；
- 运行日志；
- 配置版本追溯；
- 管理员和操作员权限隔离。

产品不限定为 YOLO。

通用领域名称必须使用：

- Project；
- Test Sequence；
- Test Item；
- Model；
- Label；
- Target；
- Model Binding；
- Detection；
- Rule；
- ROI；
- Input Source；
- Test Run。

不得把通用层写死为某一种 YOLO 模型。

### 三、“检测场景”的产品定义

本产品中的“检测场景”默认定义为：

`Project + Workstation + 某个版本的 Test Sequence + Input Source + Models + Targets + Rules/Pose Steps`。

MVP 不额外创建含义模糊、与 Test Sequence 重复的 Scene 实体。

界面上可将 Test Sequence 称为“检测方案”或“测试序列”，但代码和数据模型必须使用统一术语。

只有未来出现以下明确需求时，才允许增加独立 `InspectionScenario`：

- 一个场景包含多个 Test Sequence；
- 场景具有独立启用、分配和切换生命周期；
- 场景需要独立版本；
- 场景需要跨工位共享；
- 场景需要独立权限和历史记录。

增加前必须先更新需求、数据关系和迁移设计。

### 四、技术栈硬约束

产品主体必须使用：

- C#；
- .NET 8；
- WPF；
- MVVM；
- xUnit；
- Windows x64。

禁止使用以下技术替代产品主体：

- Python；
- C++；
- Java；
- JavaScript；
- TypeScript；
- Electron；
- WebView 承载的网页前端。

PowerShell 只能用于：

- restore；
- build；
- test；
- publish；
- 打包；
- 验收编排。

模型或相机厂商提供的原生 DLL、COM、SDK，可以通过 C# Infrastructure 适配器调用，但不得泄漏到 Core。

优先使用 .NET 标准库。新增第三方依赖必须：

- 确实解决必要问题；
- 使用固定版本，禁止浮动版本；
- 记录用途；
- 更新项目或中央包版本文件；
- 验证许可证和 Windows x64 兼容性。

真实 ONNX 推理允许使用：

- `Microsoft.ML.OnnxRuntime`；
- SkiaSharp 或同等级 C# 图像处理库。

不得为了方便在产品运行时启动 Python 推理脚本。

### 五、从零创建的项目结构

至少创建：

```text
AGENTS.md
README.md
VisualInspection.sln
Directory.Build.props
Directory.Packages.props
.gitignore

docs/
  软件需求规格说明.md
  前端UI概要设计.md
  技术架构设计.md
  配置数据模型.md
  验收说明.md
  实现状态.md
  决策记录.md

src/
  VisualInspection.Core/
  VisualInspection.Infrastructure/
  VisualInspection.App/

tests/
  VisualInspection.Core.Tests/
  VisualInspection.Infrastructure.Tests/
  VisualInspection.App.Tests/

scripts/
  Build-Acceptance.ps1
  Invoke-AcceptanceSmoke.ps1

artifacts/
  由构建脚本生成，不手工维护，不提交运行数据。
```

各项目职责如下。

#### 5.1 VisualInspection.Core

负责：

- 领域对象；
- 配置对象；
- 配置编辑逻辑；
- 配置校验；
- 规则引擎；
- ROI 几何计算；
- Test Sequence Runner；
- 姿态时序状态机；
- 统计语义；
- 抽象接口。

Core 不得引用：

- WPF；
- ONNX Runtime；
- SkiaSharp；
- 相机 SDK；
- 文件选择框；
- 具体 JSON 存储；
- 具体厂商实现。

#### 5.2 VisualInspection.Infrastructure

负责：

- JSON 配置存储；
- JSONL 日志；
- Folder 图源；
- ONNX Runtime；
- 图像解码和预处理；
- 模型契约检查；
- ONNX 标签读取；
- 确定性验收适配器；
- 本地用户存储；
- 后续相机适配器。

#### 5.3 VisualInspection.App

负责：

- WPF 页面；
- ViewModel；
- Command；
- 依赖注入和启动编排；
- 管理员设置；
- 操作员工作台；
- 可视化叠加；
- 用户交互状态。

业务规则不得散落在 code-behind 中。

code-behind 只允许处理：

- 窗口生命周期；
- 鼠标坐标；
- 纯视图事件桥接；
- 必须由 WPF 视图处理的渲染行为。

### 六、项目代码规范

从第一天启用：

- Nullable；
- ImplicitUsings；
- 确定性构建；
- XML 或清晰命名表达公共契约；
- 异步方法使用 `Async` 后缀；
- `CancellationToken` 沿执行链传递；
- 不允许 `async void`，WPF 事件入口除外；
- 不允许阻塞 UI 线程执行模型推理、目录遍历或文件读写；
- 不允许吞掉异常；
- 不允许空 `catch`；
- 不允许使用字符串匹配代替领域枚举；
- 不允许用控件状态代替领域状态；
- 不允许在日志中写入密码、Token、Cookie 或密钥。

构建目标：

- 0 编译错误；
- 0 项目代码警告；
- 所有测试通过；
- 格式检查通过。

不要运行会改写整个仓库的自动格式化。只做定向修改，格式化仅以只读检查作为交付门禁。

### 七、领域数据模型

数据模型至少包含以下对象。

#### 7.1 ProjectConfiguration

字段：

- Id；
- Name；
- Workstation；
- ActiveSequenceId；
- Models；
- Targets；
- InputSources；
- TestSequences；
- SchemaVersion。

#### 7.2 ModelDefinition

字段：

- Id；
- Name；
- Version；
- Format；
- TaskType；
- FilePath；
- Sha256；
- LabelSource；
- Labels；
- RuntimeStatus；
- ContractSummary。

`ModelFormat`：

- Onnx；
- Pt。

`ModelTaskType`：

- Detection；
- Classification；
- Segmentation；
- Pose；
- Temporal。

`LabelSourceMode`：

- Manual；
- ImportedFromModel。

#### 7.3 ModelLabelDefinition

字段：

- Id，非负整数；
- Name，非空。

同一模型内 Label ID 必须唯一。

#### 7.4 TargetDefinition

字段：

- Id；
- Name；
- ModelBindings。

#### 7.5 ModelBindingDefinition

字段：

- Id；
- ModelId；
- ModelVersion；
- OutputLabelId。

所有绑定必须显式，不允许通过模型名称或标签名称隐式查找。

#### 7.6 InputSourceDefinition

字段：

- Id；
- Name；
- Type；
- FolderOptions；
- CameraOptions；
- ValidationStatus。

`InputSourceType`：

- Folder；
- DirectShowCamera；
- VendorCamera。

#### 7.7 TestSequenceDefinition

字段：

- Id；
- Name；
- Version；
- Status；
- CreatedAtUtc；
- PublishedAtUtc；
- DefaultDelayMs；
- InputSourceId；
- SourcePolicy；
- Items；
- ConfigurationHash。

`SequenceStatus`：

- Draft；
- Published；
- Archived。

`RuntimeSourcePolicy`：

- Fixed；
- OperatorSelectable。

#### 7.8 TestItemDefinition

字段：

- Id；
- Order；
- Name；
- Type；
- Enabled；
- IsRequired；
- DelayMs；
- RuleOperator；
- Rules；
- PoseSteps。

`TestItemType`：

- Normal；
- PoseSequence。

#### 7.9 TargetRuleDefinition

字段：

- Id；
- TargetId；
- ModelBindingId；
- Scope；
- Metric；
- Operator；
- Threshold；
- UpperThreshold；
- ExpectedCount；
- ConfidenceThreshold；
- OutcomeWhenMatched。

#### 7.10 RegionScopeDefinition

字段：

- Type；
- Regions。

`RegionType`：

- FullImage；
- Roi。

#### 7.11 RegionOfInterestDefinition

字段：

- Id；
- Name；
- X1；
- Y1；
- X2；
- Y2；
- ReferenceWidth；
- ReferenceHeight。

#### 7.12 PoseStepDefinition

字段：

- Id；
- Order；
- Name；
- ActionCondition；
- ModelBindingId；
- ConfidenceThreshold；
- MinimumHoldMs；
- MaximumWaitMs。

MVP 中所有姿态步骤均为必需步骤，不要提前暴露没有运行语义的“可选步骤”。

### 八、配置生命周期

所有管理员编辑都必须先进入草稿。

规则：

1. Published Test Sequence 不可原地修改。
2. 编辑已发布版本时，创建新的 Draft。
3. Draft 使用新的稳定 ID。
4. 发布时生成不可变配置快照和 ConfigurationHash。
5. 同一 Project 下，已发布 Test Sequence 的 Name + Version 必须唯一。
6. Project 通过 ActiveSequenceId 指向当前运行版本。
7. 回退只改变 ActiveSequenceId，不修改历史版本。
8. 历史 Test Run 始终引用运行时实际版本和配置哈希。
9. 删除历史已发布版本前必须有明确产品规则；MVP 默认只允许 Archived，不允许物理删除。
10. 关闭设置窗口或点击取消时，未保存草稿不得影响正式配置。

保存流程：

```text
当前控件状态
→ 更新 ViewModel 草稿
→ Core 全量校验
→ 显示 Error/Warning
→ Error 为 0 才允许保存
→ 原子写入临时文件
→ 替换正式配置
→ 主工作台重新加载
```

### 九、管理员设置页面

管理员设置至少包含七个分区。

#### 9.1 常规

支持：

- 项目名称；
- 工位；
- 测试序列新建；
- 测试序列复制；
- 草稿重命名；
- 版本设置；
- 默认 Delay；
- 图源绑定；
- Fixed/OperatorSelectable；
- 发布；
- 归档；
- 激活历史版本；
- 测试项排序。

#### 9.2 模型与标签

支持：

- 新增模型；
- 删除未被引用模型；
- 模型名称和版本；
- ONNX/PT 格式；
- 模型任务类型；
- 文件选择；
- SHA-256；
- 手工标签；
- ONNX 标签读取；
- 标签编辑；
- 输入/输出张量摘要；
- 运行契约状态。

读取标签失败时：

- 显示明确错误；
- 保留原标签；
- 允许人工修正；
- 不把空标签模型标为可用。

#### 9.3 目标与绑定

支持：

- 新增 Target；
- 重命名 Target；
- 新增 Model Binding；
- 编辑 Model Binding；
- 删除未被引用 Binding；
- 选择模型、版本和 Output Label；
- 查看哪些规则正在引用该 Binding。

#### 9.4 规则与 ROI

支持：

- 新增普通检测项；
- 删除普通检测项；
- 修改名称；
- 启用/禁用；
- 必测/非必测；
- 单项 Delay；
- 测试项排序；
- AND/OR；
- 新增规则；
- 删除规则；
- Target；
- Model Binding；
- Metric；
- Operator；
- Threshold；
- UpperThreshold；
- ExpectedCount；
- Confidence；
- Outcome；
- Full Image/ROI；
- ROI 手填和鼠标框选；
- 实时判定摘要。

#### 9.5 姿态序列

支持：

- 新增姿态测试项；
- 删除姿态测试项；
- 姿态项重命名；
- 单项 Delay；
- 测试项排序；
- 新增姿态步骤；
- 删除姿态步骤；
- 复制步骤；
- 上移/下移；
- ActionCondition；
- Pose/Temporal Binding；
- Confidence；
- MinimumHoldMs；
- MaximumWaitMs；
- 固定线性动作画布；
- 实时动作摘要。

#### 9.6 输入源

支持：

- Folder 配置；
- Camera 配置结构；
- 图源校验；
- 首帧预览；
- 文件数量和分辨率；
- 当前运行适配器状态；
- Test Sequence 绑定摘要。

#### 9.7 用户与权限

支持：

- 显示当前账户；
- Admin/Operator 权限说明；
- 本地账户状态；
- 禁用账户门禁。

生产账户增删、改密、锁定和外部身份源可列为后续功能，但页面不得伪装为已实现。

### 十、普通检测项默认创建规则

新增普通检测项时必须一次性创建可校验对象：

- 新 Guid；
- 不重复名称，例如“普通检测项 1”；
- 追加到序列末尾；
- Order 连续；
- Enabled=true；
- IsRequired=true；
- RuleOperator=AND；
- 至少一条默认规则。

默认规则选择第一个有效普通视觉 Binding：

- Detection：PresentCount = 1；
- Segmentation：PresentCount = 1，但无真实适配器时运行仍被阻止；
- Classification：Presence = 1；
- Pose/Temporal 不得用于普通规则。

默认值：

- FullImage；
- Threshold=1；
- ConfidenceThreshold=0.5；
- OutcomeWhenMatched=Pass。

没有有效普通视觉模型、标签、Target 和 Binding 时，阻止新增并说明缺少哪个前置对象。

新增普通规则必须使用相同的任务类型兼容规则，不允许为 Classification 默认创建 PresentCount，不允许选择 Pose/Temporal Binding。

删除普通检测项时：

- 删除该项全部规则；
- 压缩所有剩余测试项 Order；
- 不得删除 Test Sequence 最后一项。

删除规则时，不得删除普通检测项最后一条规则。

### 十一、规则引擎

`QuantityMetric`：

#### 11.1 PresentCount

经过以下过滤后的检测数量：

- Model Binding；
- Target；
- Output Label；
- Confidence；
- Region。

#### 11.2 MissingCount

$$
MissingCount=\max(ExpectedCount-PresentCount,0)
$$

使用 MissingCount 时必须提供非负 ExpectedCount。

#### 11.3 Presence

PresentCount 大于 0 时为 1，否则为 0。

`ComparisonOperator`：

- Equal；
- NotEqual；
- GreaterThan；
- GreaterThanOrEqual；
- LessThan；
- LessThanOrEqual；
- BetweenInclusive。

BetweenInclusive 包含上下边界。

Threshold 必须为非负整数。

BetweenInclusive 必须提供 UpperThreshold，且：

$$
UpperThreshold\ge Threshold
$$

Classification 如果不产生实例列表，只能使用 Presence。

每条规则通过 OutcomeWhenMatched 表达：

- 条件成立时 Pass；或
- 条件成立时 Fail。

条件不成立时自动得到相反业务结果。

例如：

表面瑕疵 `PresentCount > 0`，`OutcomeWhenMatched=Fail`：

- 检测到瑕疵：Fail；
- 未检测到瑕疵：Pass。

Error 不能配置成业务规则结果。

多规则组合使用每条规则的最终 Verdict：

- AND：全部规则最终为 Pass，测试项才 Pass；
- OR：至少一条规则最终为 Pass，测试项即 Pass。

### 十二、ROI 规则

P0 完整实现单矩形 ROI，同时让数据结构可扩展到多个 ROI。

ROI 校验：

- 名称非空；
- X1、Y1、X2、Y2 为整数；
- X1 >= 0；
- Y1 >= 0；
- X1 < X2；
- Y1 < Y2；
- ReferenceWidth > 0；
- ReferenceHeight > 0；
- X2 <= ReferenceWidth；
- Y2 <= ReferenceHeight。

Full Image 模式不得保存残留 ROI。

ROI 模式必须至少有一个有效区域。

ROI 支持：

- 手工输入坐标；
- 在当前图像上拖动框选；
- 鼠标框选后立即回填坐标；
- 手工修改后立即更新画面；
- 显示 ROI 宽度和高度；
- 显示参考分辨率。

Detection 默认使用检测框中心点判断是否进入 ROI。

参考分辨率和当前帧分辨率不同时按比例映射。

宽高比明显变化时显示警告，不得静默使用错误区域。

未来多 ROI 使用并集，同一检测框只能计数一次。

### 十三、姿态时序规则

姿态检测是独立 TestItemType，不与普通数量规则混用。

固定线性状态机：

1. 按 Order 处理步骤。
2. 只判断当前步骤。
3. 当前 ActionCondition 满足时累计保持时间。
4. 任一帧不满足时，连续保持时间清零。
5. 连续满足 MinimumHoldMs 后进入下一步骤。
6. 等待超过 MaximumWaitMs 时该项 Fail。
7. 图像序列结束但当前步骤未完成时 Fail。
8. 全部步骤按顺序完成时 Pass。
9. 模型、图源或分析异常时为 Error，不是 Fail。
10. Folder 模式使用 PoseFrameIntervalMs 形成确定性时间。

P0 不实现：

- 任意分支；
- 循环；
- 可选步骤；
- 步骤重复；
- 复杂动作图。

这些功能未实现前不得提前显示不可用控件。

### 十四、图源

#### 14.1 Folder P0

支持：

- jpg；
- jpeg；
- png；
- bmp；
- 选择文件夹；
- 是否递归；
- 自然文件名排序；
- 修改时间排序；
- 损坏文件 Skip/Stop；
- 进度；
- 当前文件；
- 总文件数；
- 失败数量；
- 首帧预览；
- 循环选项；
- 姿态帧间隔。

必须防止：

- 空目录启动；
- 全部文件损坏导致无限循环；
- LoopPlayback 导致无法结束的普通批量测试；
- 单个坏图导致程序崩溃。

#### 14.2 Camera

先定义统一接口：

- EnumerateDevicesAsync；
- OpenAsync；
- StartStreamAsync；
- GrabFrameAsync；
- StopStreamAsync；
- CloseAsync；
- GetStatus；
- GetCapabilities；
- GetParameter；
- SetParameter。

DirectShow 和 Vendor SDK 没有真实适配器时：

- 可以保存配置草稿；
- 必须显示“适配器未安装”；
- Start 必须禁用；
- 不得伪报设备已连接；
- 不得返回旧帧冒充新帧。

### 十五、模型运行时

首个真实适配器只承诺：

- ONNX；
- Detection；
- 单输入；
- 单输出；
- `Float[1,3,H,W]` 固定输入；
- `Float[1,N,6]` 固定输出；
- 每行 `x1,y1,x2,y2,confidence,classId`；
- 模型内部已完成候选筛选；
- CPU Execution Provider。

预处理：

- 解码 JPG/JPEG/PNG/BMP；
- Letterbox；
- RGB；
- 0–1 归一化；
- 保留缩放和 Padding 参数。

后处理：

- 去除 Padding；
- 映射回原图坐标；
- 限制在图像边界；
- 拒绝退化框；
- 拒绝非法 Class ID；
- 拒绝无 Binding 的标签；
- 保存 Confidence；
- 生成统一 TargetDetection。

同一帧中：

- 按唯一 ModelId 分组；
- 每个模型最多推理一次；
- 同模型的多个规则共享输出；
- 不允许每条规则重复调用模型。

模型启动前必须检查：

- 文件存在；
- SHA-256；
- 任务类型；
- 输入张量；
- 输出张量；
- 标签；
- Target Binding；
- 所有必需模型是否 Ready。

以下功能没有独立适配器时保持门禁：

- PT；
- 动态输入；
- 原始 YOLO `[1,C,N]`；
- 外置 NMS；
- Classification；
- Segmentation；
- Pose；
- Temporal；
- GPU Execution Provider。

### 十六、确定性验收适配器

为了在没有现场模型和相机时测试完整流程，可以实现 `detections.json` 验收适配器。

它必须：

- 通过与真实模型相同的 IInspectionProvider 输出；
- 提供空间检测框、置信度、Binding 和动作观察；
- 支持 Pass/Fail 固定数据；
- 用于自动测试和本地演示；
- 在 UI 和日志中明确显示“确定性验收适配器”。

它不是模型推理。

README、状态栏、日志和验收文档中都不得把它描述成模型推理或 AI 检测结果。

### 十七、Runner

运行前：

- 配置必须通过；
- 图源必须 Ready；
- 所有必需模型必须 Ready；
- 当前序列必须是 Published；
- 当前用户必须有执行权限；
- 必须先录入非空产品序列号，录入后仍需显式点击 Start。

普通项：

- 只执行 Enabled 项；
- 按 Order 排序；
- DelayMs 为空时使用 DefaultDelayMs；
- Delay 发生在上一项结束后、当前项采集或推理前；
- 同一普通项所有规则共享同一帧；
- 同一图片所有普通项复用相同模型观察。

Operator 纯普通 Folder 单件执行：

- 一个序列号只代表一个产品和一个完整 Test Run；
- 每次 Start 只读取排序后的下一张图片，不得遍历整个文件夹；
- 当前图片执行全部普通项；
- 同一图片按 ModelId 最多推理一次；
- 当前序列号单独产生一个 Pass/Fail/Error，会话统计只累计一次；
- JSONL 结果必须包含当前序列号、图片名、Run ID 和判定；
- 图片已经采集后消费该序列号，完成后清空，下一次必须重新录入；
- 工厂序列号格式、重复策略和最终日志格式在获得现场样例后实现，不得自行猜测。

含姿态项的序列：

- Folder 被视为连续帧流；
- 一个序列号仍只对应一个产品 Test Run，但动作判定可读取连续帧。

总体结果：

- 任一必测项 Fail → Run Fail；
- 全部必测项 Pass → Run Pass；
- 非必测项 Fail 不影响总体 Pass，但必须显示和记录；
- 任一系统 Error → Run Error；
- 用户取消 → Stopped；
- Stopped 保留已完成项，但不进入 Pass/Fail/Error 会话统计。

### 十八、Pass、Fail、Error、Stopped

必须严格区分：

**Pass**：产品满足业务检测标准。

**Fail**：模型和程序执行正常，但产品不满足业务标准。

**Error**：模型、图源、文件、配置、SDK、存储或程序执行异常。

**Stopped**：操作员主动停止尚未完成的运行。

禁止：

- 用 Fail 表示模型加载失败；
- 用 Pass 表示没有执行；
- 把 Error 加入 Pass Rate 分母；
- 把 Stopped 统计为 Error；
- 在异常时继续使用旧图像或旧结果。

### 十九、操作员工作台

采用单页三栏布局。

顶部应用栏：

- 产品名称；
- Project；
- Workstation；
- Test Sequence；
- Version；
- 当前用户；
- 角色；
- 图源名称；
- 图源类型；
- 图源状态；
- 管理员设置入口。

左侧：

- Test Sequence 摘要；
- 测试项列表；
- Order；
- Name；
- Type；
- Standard；
- Measured；
- Result；
- Duration；
- Start；
- Stop；
- Reset。

中部：

- 当前图片或实时帧；
- 检测框；
- Target 名称；
- Confidence；
- ROI；
- 当前项名称；
- 判定标准；
- 实测值；
- 当前状态；
- 总体结果。

右侧固定统计：

- Pass Count；
- Fail Count；
- Error Count；
- Total Valid；
- Pass Rate；
- Fail Rate；
- 当前范围；
- 数量/比率切换。

底部：

- 实时日志；
- 当前文件；
- 批量进度；
- 运行状态；
- 本机日志路径。

测试执行和统计必须始终同屏，不拆分成两个需要来回切换的页面。

### 二十、统计规则

统计对象是完整检测单元，不是测试项。

纯普通 Folder：

- 每张图片是一个检测单元。

相机或含姿态序列：

- 一次完整 Test Sequence 是一个检测单元。

计算：

$$
Pass\ Rate=\frac{Pass}{Pass+Fail}\times100\%
$$

$$
Fail\ Rate=\frac{Fail}{Pass+Fail}\times100\%
$$

Error 不进入分母。

没有有效结果时：

- Rate 显示“--”；
- 图表显示中性空状态；
- 不得显示误导性的 100%。

数量模式：

- Pass/Fail 数字；
- 横向数量条；
- 紧凑饼图。

比率模式：

- 大型圆环；
- 百分比。

数字、列表和图表必须使用同一统计快照更新。

### 二十一、实时规则摘要

所有摘要由当前 ViewModel 状态生成，不得写死示例文本。

普通规则摘要示例：

```text
区域-A · 螺钉 在场数量 = 4 → 通过
全图 · 表面瑕疵 在场数量 > 0 → 不通过
```

摘要至少包含：

- Target；
- Model/Label Binding；
- Full Image/ROI；
- Metric；
- Operator；
- Threshold/Range；
- Confidence；
- Outcome；
- AND/OR。

姿态摘要示例：

```text
拿取（保持 250 ms，最大等待 5000 ms）
→ 放置（保持 250 ms，最大等待 5000 ms）
→ 确认（保持 250 ms，最大等待 5000 ms）
```

非法输入时摘要显示具体原因，并禁用保存或发布。

### 二十二、配置校验

`ProjectConfigurationValidator` 必须返回：

- Severity；
- Code；
- Path；
- Message。

Severity：

- Warning；
- Error。

至少校验：

- 必填 ID；
- 必填文本；
- ID 唯一；
- Order 正数且唯一；
- 标签 ID 唯一、非负；
- 模型版本；
- SHA-256；
- Target 至少一个 Binding；
- Binding 模型存在；
- Binding 版本一致；
- Output Label 存在；
- Folder/Camera 配置互斥；
- Test Sequence 图源存在；
- DefaultDelay 非负；
- 测试项至少一个；
- 普通项至少一条 Rule；
- 普通项不能有 PoseSteps；
- Pose 项至少一个步骤；
- Pose 项不能有普通 Rule；
- Target/Binding 引用关系；
- Confidence 位于 0–1；
- Classification 只能使用 Presence；
- Threshold；
- MissingCount ExpectedCount；
- Between 上限；
- Full Image/ROI；
- ROI 坐标；
- Pose 时间；
- Pose/Temporal Binding；
- 已发布名称和版本唯一；
- 已发布模型必须有 SHA-256；
- 已发布配置不可原地修改。

校验错误必须在 UI 中定位到具体设置分区和控件。

### 二十三、持久化

本机数据根目录：

```text
%LocalAppData%\VisualInspectionTestDeployment\
```

至少包括：

```text
projects\
users\
logs\
runs\
acceptance-data\
```

配置存储：

- JSON；
- schema 信封；
- SchemaVersion；
- 同目录临时文件；
- Flush；
- 原子替换；
- 失败时保留旧文件；
- 不支持的 schema 明确拒绝；
- 不静默丢字段。

日志：

- JSON Lines；
- 按天追加；
- UTC 时间；
- Level；
- Module；
- Event；
- User；
- Project；
- SequenceId；
- Version；
- RunId；
- ItemId；
- Message；
- ErrorCode。

测试记录至少保存：

- User；
- Project；
- Workstation；
- SequenceId；
- SequenceVersion；
- ConfigurationHash；
- ModelId/Version/Sha256；
- InputSourceId/Type；
- 开始时间；
- 结束时间；
- 每项标准；
- 每项实测；
- 每项结果；
- 总体结果；
- ErrorCode。

### 二十四、安全和权限

角色：

- Admin；
- Operator。

Admin：

- 配置；
- 发布；
- 图源；
- 执行；
- 统计。

Operator：

- 只能执行；
- 只能查看；
- 不能修改检测标准。

本地密码：

- PBKDF2-SHA256；
- 每账户随机盐；
- 至少 100,000 次迭代；
- 固定时间比较；
- 不保存明文；
- 不写日志。

本地验收账户只允许用于 Acceptance/Development，并必须明确提示不能直接作为生产账户。

### 二十五、UI 视觉规定

设计方向：

- 淡雅浅色系；
- 专业；
- 克制；
- 高信息密度但层级清晰；
- 施耐德工业测试软件风格；
- 绿色作为品牌和主操作识别色；
- 中性灰作为结构色；
- Pass、Fail、Error 有明确文字和图标。

禁止：

- 深色游戏仪表盘；
- 霓虹渐变；
- 玻璃拟态；
- 夸张阴影；
- 关卡化图标；
- 卡通人物；
- 积分、徽章；
- 无意义动画；
- 大面积高饱和色。

要求：

- 使用统一 `Theme.xaml` 和设计 Token；
- 不在页面散落硬编码颜色；
- 支持 100%、125%、150% Windows 缩放；
- 关键按钮不得被遮挡；
- 支持键盘焦点；
- 不只依赖红绿颜色区分结果；
- 所有界面文案使用简体中文；
- 技术名词必要时保留 ONNX、ROI、Pass/Fail 等标准术语。

UI 编码前必须先完成并交付：

- 页面信息架构；
- 低保真布局；
- 组件列表；
- 默认状态；
- 空状态；
- Loading；
- Ready；
- Running；
- Pass；
- Fail；
- Error；
- Stopped；
- 禁用状态；
- 未保存状态；
- 验证错误状态。

### 二十六、测试规定

测试使用 C# 与 xUnit。

Core 单元测试至少覆盖：

- 全部比较运算符；
- Between 边界；
- PresentCount；
- MissingCount；
- Presence；
- OutcomeWhenMatched 反向结果；
- AND；
- OR；
- 空规则组；
- Classification 限制；
- ROI 合法/非法；
- ROI 缩放；
- 中心点归属；
- 多 ROI 去重；
- Binding 过滤；
- Confidence 过滤；
- 普通项新增默认规则；
- Classification 默认规则；
- Pose-only 拒绝；
- 删除项压缩顺序；
- 最后一项保护；
- 最后一条规则保护；
- Delay；
- Required/Optional；
- Pose 连续保持；
- Pose 抖动清零；
- Pose 超时；
- Pass/Fail/Error/Stopped。

Infrastructure 测试至少覆盖：

- JSON 原子保存；
- schema；
- Folder 排序；
- 子目录；
- 坏图 Skip/Stop；
- 全坏图保护；
- ONNX 标签读取；
- 模型契约检查；
- 输出解析；
- Letterbox 坐标还原；
- 确定性适配器；
- JSONL 日志。

App 测试或冒烟至少覆盖：

- LoginWindow 构造；
- MainWindow 构造；
- Admin 设置窗口构造；
- 七个设置分区；
- 普通检测项新增/删除；
- Rule 新增/删除；
- ROI 双向联动；
- 实时摘要；
- 非法范围拦截；
- Pose 步骤排序；
- Admin/Operator 权限；
- 保存后重载；
- Start 运行门禁。

不为了通过测试修改正确业务规则。

### 二十七、开发阶段

#### Phase 0：规则与设计

交付：

- AGENTS.md；
- README 初稿；
- 软件需求规格；
- UI 概要；
- 架构设计；
- 数据模型；
- 验收说明；
- 决策清单；
- 实施阶段计划。

完成后等待设计确认。

#### Phase 1：解决方案骨架

交付：

- VisualInspection.sln；
- 三层项目；
- 三类测试项目；
- 公共构建配置；
- 依赖关系；
- 最小启动程序；
- build/test 通过。

#### Phase 2：Core

交付：

- 数据模型；
- 配置校验；
- 配置编辑器；
- 规则引擎；
- ROI；
- 姿态状态机；
- Runner；
- 单元测试。

#### Phase 3：Infrastructure

交付：

- JSON；
- JSONL；
- Folder；
- ONNX 标签读取；
- ONNX Detection；
- 确定性适配器；
- 用户存储；
- 集成测试。

#### Phase 4：Admin UI

交付：

- 登录；
- 七个设置分区；
- 草稿编辑；
- 模型/Target/Binding；
- 普通规则；
- ROI；
- Pose；
- 图源；
- 校验、保存和发布；
- UI 冒烟。

#### Phase 5：Operator UI

交付：

- 三栏工作台；
- Start/Stop/Reset；
- 当前帧；
- Overlay；
- 列表；
- 实测；
- 统计；
- 日志；
- 权限隔离。

#### Phase 6：端到端验收

交付：

- PASS 数据；
- FAIL 数据；
- detections.json；
- acceptance smoke；
- UI construction smoke；
- startup lifecycle smoke；
- 发布脚本；
- Windows x64 Release；
- ZIP；
- 验收回执。

每个 Phase 必须：

- 同步实现状态；
- 同步 README；
- 更新相关文档；
- 增加测试；
- 运行定向测试；
- 运行解决方案构建；
- 明确已完成和未完成。

### 二十八、构建与验收命令

至少支持：

```powershell
dotnet restore VisualInspection.sln
dotnet build VisualInspection.sln --no-restore
dotnet test VisualInspection.sln --no-restore
dotnet run --project src/VisualInspection.App/VisualInspection.App.csproj
dotnet format VisualInspection.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File scripts\Build-Acceptance.ps1
powershell -ExecutionPolicy Bypass -File scripts\Invoke-AcceptanceSmoke.ps1
```

发布目标：

- win-x64；
- framework-dependent；
- 明确要求 .NET 8 Desktop Runtime；
- 发布目录可独立复制；
- 生成 ZIP；
- 生成 SHA-256；
- 生成验收回执。

不得擅自终止用户正在运行的程序来解决文件锁定。应先检查进程，必要时使用明确的隔离输出目录验证。

### 二十九、关键验收场景

至少实现并验证：

1. ROI 内螺钉数量等于 4 时 Pass。
2. 全图标签数量等于 1 时 Pass。
3. 全图瑕疵数量大于 0 时 Fail。
4. MissingCount=0 且 ExpectedCount=4。
5. 数量位于 `[2,4]`。
6. Classification 默认 Presence=1。
7. AND 两条规则全部通过。
8. AND 任一规则失败。
9. OR 任一规则通过。
10. ROI 外检测框不计数。
11. 低置信度检测框不计数。
12. 其他 Binding 检测框不计数。
13. 重叠 ROI 不重复计数。
14. 拿取→放置→确认按序通过。
15. 姿态保持中断后重新计时。
16. 姿态步骤超时产生 Fail。
17. 模型加载错误产生 Error。
18. Operator 无法修改配置。
19. 配置错误不覆盖旧文件。
20. 已发布版本不可原地修改。
21. 回退不改变历史记录。
22. 空序列号时 Start 禁用。
23. Folder 每个序列号只读取下一张图片，并把序列号、图片名和结果写入同一 JSONL 记录。
24. Stop 不统计未完成图片。
25. detections.json 明确显示为验收适配器。
26. 不支持的模型或相机导致 Start 禁用。

### 三十、完成定义

只有同时满足以下条件，才可以把功能标记为完成：

- 需求有编号；
- 实现已落盘；
- 正常路径可运行；
- 边界路径有测试；
- 错误路径有测试；
- UI 状态完整；
- 权限正确；
- Pass/Fail/Error/Stopped 正确；
- 文档与代码一致；
- 构建通过；
- 测试通过；
- 冒烟通过；
- 实际运行证据存在；
- 未完成边界被明确记录。

禁止把以下内容称为完成：

- 只有页面没有逻辑；
- 只有 Core 没有 UI；
- 只有 UI 没有持久化；
- 只有演示数据没有真实适配器；
- 只有计划没有代码；
- 只有代码没有测试；
- 只有构建通过没有运行验证；
- 只有配置入口但运行时仍不支持；
- 外部发布尚未执行却声称已发布。

### 三十一、文档同步规定

代码、配置、用户行为或运行方式发生变化时，必须在同一功能增量内更新受影响文档。

持续维护：

- README；
- 需求；
- UI；
- 架构；
- 数据模型；
- 验收；
- 实现状态；
- 决策记录。

项目完成或每个独立功能完成后，执行 neat-freak 文档一致性审计。

如果审计后不需要修改，也必须明确报告：

> 文档同步审计完成，无需更新。

`AGENTS.md` 用于稳定开发规则，不用于记录功能流水账。

### 三十二、执行权限

本提示词授权 Codex：

- 在新项目目录创建文件；
- 编写本任务范围内代码；
- 编写测试；
- 编写文档；
- 安装或恢复明确需要的 NuGet 包；
- 运行非破坏性的 build/test/publish/smoke；
- 创建本地验收产物；
- 生成本地示例数据。

未经用户额外授权，不允许：

- git commit；
- git push；
- 创建外部仓库；
- 创建或更新 PR；
- 上传文件；
- 发布到外部服务器；
- 付费；
- 删除用户已有目录；
- 覆盖非空工作区；
- 终止用户进程；
- 写入密码或密钥；
- 猜测相机 SDK 和模型输出契约。

### 三十三、何时必须询问用户

只有以下信息缺失并且确实影响设计或验收时才询问：

- 新项目实际保存目录；
- 正式施耐德品牌规范；
- 目标工控机配置；
- 正式屏幕分辨率；
- 正式性能要求；
- PT 模型来源；
- 其他 ONNX 输出契约；
- Pose 模型输出是动作标签还是关键点；
- 相机品牌和 SDK；
- 数据保留期限；
- Operator 是否可以切换项目；
- 是否需要正式生产账户系统。

询问时只问影响最大的最少问题。

其他可以通过本提示词和代码规范确定的内容直接执行。

### 三十四、Codex 汇报格式

每次阶段交付都必须先给结论，再包含：

1. 本阶段完成内容。
2. 创建或修改的文件。
3. 已实现的业务语义。
4. 实际运行的验证命令。
5. 精确测试结果。
6. 文档同步结果。
7. 未完成内容。
8. 外部依赖。
9. 下一阶段。

不要输出泛泛的“应该可以”。

只报告实际验证结果。

### 三十五、第一轮具体任务

现在先执行 Phase 0，不写正式产品代码。

请完成：

1. 检查当前目录。
2. 创建 `AGENTS.md`。
3. 创建 `README.md` 初稿。
4. 创建完整软件需求规格说明。
5. 创建前端 UI 概要设计。
6. 创建技术架构设计。
7. 创建配置数据模型。
8. 创建验收说明。
9. 创建实现状态矩阵。
10. 创建决策记录。
11. 给出主要页面低保真结构和浅色施耐德风格方向。
12. 列出必须由用户确认的重大决策。
13. 检查文档之间是否冲突。
14. 汇报 Phase 0 产物并等待“设计确认，继续开发”。

在收到确认前，不开始 Phase 1。
