Visual Inspection Test Deployment - sequence 交付演示版

双击 VisualInspection.FrontendDemo.exe，依次体验：登录 → 操作员工作台 → 测试序列设置 → 应用当前配置或独立导出 → 操作员工作台。
该 EXE 为 Windows x64 自包含单文件，不要求另行安装 .NET 8 Desktop Runtime。

本地演示账户：
- 管理员：admin / Admin@123（可进入“测试序列设置”，直接应用或导出 Sequence）
- 操作员：operator / Operator@123（仅执行与统计）

Fan 检测体验：
1. 使用管理员账户登录，操作台会加载“FAN-A01 视觉检测项目”、生产型号 `FAN-A01`、IMG_1533.JPG，以及唯一的“风扇检测”测试步。
2. 点击“开始”。Folder 图源自动以 `IMG_1533` 作为序列号，不显示手工输入框。
3. 程序使用内置 fan.onnx 通过 ONNX Runtime CPU 只分析该图一次；“风扇检测”测试步内部以 AND 汇总 6 条规则，检测图保留原始图片、检测框和 `Labell / Black_wire / white_wire` 等模型原始英文 Label，不显示置信度。当前检测项按 sequence 显示“检测标签 / 判定逻辑 / 本次实测 / Result”，运行前可看到数量、存在/不存在等规则，运行后填入实际值和红绿结果。
4. 点击右上角“测试序列设置”进入设置界面；最后一步可用“应用到当前操作台”直接加载当前配置，也可用“导出 Sequence 与模型”选择保存位置。导出完成仍停留在设置页，点击“返回操作台”可不应用修改地返回。

首次登录后会把 EXE 内置的 Fan 模型和示例图解包到本机应用数据目录，因此加载操作台可能需要数秒。

边界：Fan 图像检测属于真实 ONNX Runtime CPU 执行，但只证明该模型与该演示图的接入链可运行，不代表通用精度、现场节拍或生产验收。视频读取、相机/PLC/IO 接入、图像分割/姿态推理和自定义函数执行仍保持运行门禁。
