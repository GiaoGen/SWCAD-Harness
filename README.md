# CAD Harness v0.2 — Milestones 0–8 + M9A

M0 工程设施已补齐：`CadHarness.sln`、固定版本 .NET 8 SDK、独立构建与 Bootstrap 运行器。标准 SDK/MSBuild 构建已通过；之前仅验证 Roslyn 编译的限制已解除。工具链安装在工作区，未修改系统安装。

```powershell
.\scripts\setup-dotnet.ps1
.\scripts\build.ps1
.\scripts\test-bootstrap.ps1
```

构建仅编译，不调用 SOLIDWORKS；Bootstrap 检查不激活 COM。SDK/interop 配置和结果见 `docs/milestone-0-verification.md`。

本目录按 `Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` 的 Milestone 1 实现纯 C# CAD Operation IR 与类型系统。未复制 v0.1 代码。

最新扩展 **M9A COMPLETE**：原生参数 mutation 注册表、厚度/通孔孔径事务编辑、阵列实例读回和相应 Planner 能力投影。完整 M9 泛化评估尚未执行，M9B+ 未实现。M0–M8 以下各节保留各阶段验收时的范围；M9A 的当前能力见末节。

Milestone 2 已增加单个居中矩形拉伸的最小 SOLIDWORKS 后端，Milestone 3 已增加该拉伸的 CADState、身份与持久引用恢复。Milestone 4 已增加可组合的通孔、盲孔、线性/矩形阵列、圆角和倒角处理器，并通过 G1、G2 创建验收。Milestone 5 已实现语义 Binder、五类设计关系、依赖图，并通过两孔、2×2、2×3 居中编辑验收。Milestone 6 已实现通用事务、ChangeSet/DirtySet、增量验证、完整验证升级与回滚。Milestone 7 已加入运行时能力投影、严格单计划 Planner、确定性 fixture、可配置 OpenAI Responses LLM 适配器和 CLI；默认 0 Parts。Milestone 8 已实现可选 IBoundedJudge、受控语义候选选择和严格响应检查；无 Judge/Jev 时仍可工作。Milestone 9 未实现。

包含 `CadProgram`、`OperationNode`、`OperationKind`、`OperationInput`、`OperationParameter`、`OperationContract`、`OperationRegistry`、语义类型/角色、严格 JSON 解析与序列化、程序验证，以及从契约生成的 JSON Schema。

九类操作均有输入、参数、前置条件、效果和后置条件契约：

| 操作 | 输入 | 参数 |
|---|---|---|
| `create_extrude` | 无 | `profile`, `depthMm` |
| `create_through_hole` | `host` | `diameterMm`, 可选 `placement` |
| `create_blind_hole` | `host` | `diameterMm`, `depthMm`, 可选 `placement` |
| `create_linear_pattern` | `seed`, 可选 `direction` | `count`, `spacingMm` |
| `create_rectangular_pattern` | `seed`, 可选 `directionX`, `directionY` | `countX`, `countY`, 可选 `spacingXMm`, `spacingYMm` |
| `create_circular_pattern` | `seed`, `axis` | `count`, 可选 `angleDeg` |
| `apply_fillet` | `edges` 数组 | `radiusMm` |
| `apply_chamfer` | `edges` 数组 | `distanceMm` |
| `edit_parameter` | `target` | `parameter` 枚举、与该枚举对应的数值 `value` |

`centered_rectangle_profile` 与 `circle_profile` 是轮廓类型，schema 中有独立定义；它们嵌入拉伸操作的 `profile` 字段，其 `kind` 分别为 PRD 示例中的 `centered_rectangle` 与 `circle`。

输入可以采用 PRD 的语义引用字符串，或显式类型形式：

```json
{"semanticId":"base_plate.top_face","type":"planar_face"}
```

字符串简写采用契约中首个允许的类型；其他类型应明确声明。例如圆周阵列的参考轴应使用 `type: reference_axis`。对于本计划中已创建的实体，还会检查声明类型是否与操作输出一致、引用是否出现在创建之后。外部语义引用仅作类型检查，实际绑定留给后续里程碑。

长度单位为毫米，角度为度。所有数值必须有限；尺寸必须为正；计数必须为 32 位整数并满足操作的最小值。线性/圆周阵列至少两个实例，矩形阵列总实例数至少两个。参数编辑采用有限枚举，并对本计划内目标检查参数归属。

`relations` 支持 PRD 的八种关系声明，不执行关系求解。为兼容 PRD 示例，矩形阵列间距可省略；契约要求在未来执行前补齐或由关系求解。省略方向、放置和角度同样仅表示未显式指定，本阶段不决定原生执行行为。

解析规则：版本严格为 `0.2`，1–12 个操作，至多 48 个关系，每个边集合至多 64 项，JSON 至多 128 KiB、嵌套深度至多 32。拒绝未知/重复字段、未知枚举、字符串数值、注释、尾逗号及非有限数值。ID 使用小写语义标识符；原生 API/COM 名称、显示名称和代码没有专用可输入字段，并通过封闭字段、枚举、标识符格式和原生名称禁用规则拦截。程序中没有代码执行入口。

`schemas/cad-program.schema.json` 是 Draft 2020-12 结构 schema，来自同一操作契约。解析器另行检查重复 JSON 字段、可表示的有限数值、标识符唯一性、跨操作类型、顺序及参数归属。schema 验证不能替代这些检查；调用方以 `CadProgramJson.Parse()` 返回的 `IsValid` 为准。失败含稳定 `Code`、JSON `Path`、`Message`，不会返回部分可用程序。

运行本阶段纯测试：

```powershell
.\scripts\test.ps1
```

脚本优先使用 M0 在工作区安装的固定 SDK；无 SDK 时仍支持 PowerShell 7 自带 Roslyn 与已安装的 .NET 8 运行时。两条路径均把源码编译警告视为错误；标准 SDK/MSBuild 全工程编译已在 M0/M3 验收通过。

显式使用离线编译器或重新生成 schema：

```powershell
.\scripts\test.ps1 -UseBundledCompiler
.\scripts\test.ps1 -WriteSchema
```

构建产物放在忽略的 `artifacts/` 或项目 `bin/obj` 中。测试不需要第三方包，不连接或启动 SOLIDWORKS，不打开/创建 Part。

上述 Milestone 1 项目和纯测试不调用 SOLIDWORKS。语义绑定器、关系引擎由 M5 实现，HTTP Planner 由 M7 实现；Jev 和基准仍未实现。

## Milestone 2 — 单个原生矩形拉伸

`src/CadHarness.SolidWorks` 是 C#、Windows x64、STA 线程上的进程外后端。提供 SOLIDWORKS COM 连接、毫米到米的转换、`CreateExtrudeHandler`、居中矩形轮廓后端和临时执行上下文。原生 COM 类型、选择和 API 调用仅存在于后端及原生测试项目，Milestone 1 的 IR 保持不变。

执行入口在创建 Part 前检查完整 IR：必须恰好一个 `create_extrude`、一个 `centered_rectangle` 轮廓、零关系。其他轮廓、操作、组合或关系在创建前返回结构化失败。生产后端使用输入尺寸，没有写死验收尺寸或零件预设。后端要求空 Part 和原点上的 XY 对齐构造平面，按参考平面的原生变换选择平面，不依赖中英文显示名称。草图创建时的 `AddToDB`/`DisplayWhenAdded` 设置在 `finally` 中恢复。

纯预检测试（不连接或启动 SOLIDWORKS）：

```powershell
.\scripts\test-milestone2.ps1
```

仅编译：

```powershell
.\scripts\test-milestone2.ps1 -BuildOnly
```

原生测试使用 `tests/CadHarness.SolidWorks.Tests/Fixtures/extrude.json` 中的 80 × 50 mm 矩形、10 mm 拉伸，检查且只检查一个实体、宽、高、深和重建成功，并检查规定的资源与文档生命周期。尺寸来自实体的原生极值点，而非输入值或近似包围盒。

```powershell
.\scripts\test-milestone2.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

`-PartTemplate` 可传本机的 `.prtdot` 路径，或设置 `CAD_HARNESS_PART_TEMPLATE`；省略时读取 SOLIDWORKS 的默认 Part 模板设置。脚本从 COM 注册表发现安装的 `api/redist`，也可使用 `-InteropDir` 或 `SOLIDWORKS_INTEROP_DIR` 指定。SDK 直接构建需传 `-p:SolidWorksInteropDir=...`。无 SDK 时，脚本用 PowerShell 7 自带 Roslyn 离线编译相同源码及当前库依赖，不运行其他里程碑测试。

原生测试会附加运行中的 SOLIDWORKS；没有可附加实例时通过 COM 激活。激活复用已有进程时不会将其认作测试所有会话。每次创建前检查进程响应、打开的测试文档数及可获取的 GDI 数；GDI ≥ 7000、无响应或已有打开的测试文档时返回 `TEST_RESOURCE_LIMIT`。测试 Part 在几何操作前注册为测试所有；无论成功失败，都会尝试仅关闭并丢弃该测试 Part，再恢复原活动文档。附加的用户会话不退出；由测试启动的会话只在所有文档已关闭时调用 `ExitApp` 请求退出，不强制终止进程。

本阶段最多 2 次 Part 创建尝试，常规使用 1 个 Part。`artifacts/milestone2/native-budget.json` 将预算跨测试进程保存并锁定，`native-result.json` 保存最近一次结果。预算用完返回 `ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED`，不会再启动应用或创建 Part；不要删除预算账本来绕过限制。现有验收已使用 1 次，不需要额外原生测试。

验证结果：8/8 M2 纯测试通过；SOLIDWORKS 2024 原生测试通过，实体数 1，实测 80 × 50 × 10 mm，重建成功，创建 1 个 Part、关闭 1 个 Part，测试所有文档剩余 0。详细记录见 `docs/milestone-2-verification.md`。

M2 限制：只执行单个居中矩形拉伸，草图宽高通过创建坐标构造。原生测试使用 `gb_part.prtdot`，测试前无活动文档；其他模板和已有用户活动文档的恢复分支未扩展验证。该阶段本身没有状态捕获，后续由 M3 增加。

## Milestone 3 — CADState 与持久身份

`src/CadHarness.State` 是不依赖 SOLIDWORKS 的新 v0.2 状态库，提供 `DocumentIdentity`、`FeatureNode`、`SemanticEntityNode`、`ParameterNode`、`ParameterBinding`、引用健康状态、严格状态加载与 `AtomicStateStore`。不读取或迁移 v0.1 状态。M3 验收时关系和依赖只保留空槽；M5 已扩展为严格解码的类型化关系、依赖边及几何元数据。

后端用原生自定义属性持久保存文档 GUID 与当前配置 GUID，并记录配置名称、已保存 Part 的绝对路径；恢复时必须全部匹配。文档属性名为 `CADHarness.v0.2.DocumentId`，配置属性名为 `CADHarness.v0.2.ConfigurationId`。配置属性存放在该配置的属性管理器中，不修改全局应用偏好。

`PersistentReferenceAdapter` 使用 `GetPersistReference3` / `GetObjectByPersistReference3`，载荷以 base64 写入状态。恢复检查原生返回状态、目标接口类型、特征定义和语义所有权，不按显示名称查找，不做几何猜测或重解析。原生拉伸定义接口允许 Instant3D 等类型名称差异。当前捕获一个拉伸、一个 `FeatureRef` 语义实体和一个 `extrusion_depth` 参数绑定；读取值来自绑定所有者的原生拉伸定义，以毫米返回。捕获 ID 来自 IR，生产代码没有固定板件预设。

状态文件版本为 `0.2`；未知字段、重复字段、缺失字段、非法枚举、非有限数值、错误所有权或无绑定参数会被拒绝。原子提交先在同目录创建唯一临时文件并 `Flush(true)`，再通过 `File.Replace` 或首次 `File.Move` 发布；不先删除旧文件。纯测试验证了只读目标导致替换失败时旧状态仍完整、临时文件被清理。

```powershell
.\scripts\test-milestone3.ps1
```

该命令仅运行 M3 的 13 个纯测试，不激活 COM。原生验收命令为：

```powershell
.\scripts\test-milestone3.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

原生案例复用 M2 的单拉伸 JSON：创建一个 Part，写入持久身份，保存原生文件，捕获并原子提交状态，重新从磁盘加载，在没有原特征句柄的新执行上下文中用持久引用恢复，并读取深度 10 mm，最后关闭测试 Part、丢弃未保存更改并恢复活动状态。测试生命周期、资源守卫和预算组件来自本项目 M2，M3 使用独立预算账本；没有复制旧 v0.1 测试。

M3 验收结果：标准 SDK 构建 0 警告/错误，13/13 纯测试通过，原生恢复/参数读取通过。累计创建 **2** 个 Part，关闭 **2** 个，剩余测试文档 **0**。首轮恢复被过窄的原生类型名称检查拒绝，修正为原生定义接口后第二轮通过，两轮均完成清理。**M3 的 2 个 Part 预算已用满；再运行 `-Live` 会返回 `ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED`，不会创建第三个 Part。**

当前有效原生文件和状态位于 `artifacts/milestone3/CADHarnessM3Test_plate_2.SLDPRT`、`artifacts/milestone3/cad-state.json`；仅保留最新 `native-result.json` 与累计 `native-budget.json`。第一轮失败案例的原生文件已删除。完整记录见 `docs/milestone-3-verification.md`。

M3 限制：本次以新上下文和重新加载的文件验证恢复，未重启控制器进程，也未额外打开原生文件重测。只捕获拉伸的 `FeatureRef` 与深度，尚无面/边实体持久状态捕获、草图宽高参数绑定、参数编辑、Save As 后身份迁移或配置重命名支持。原子提交只针对 JSON 文件，不是 CAD 文件与状态的联合事务。M4 的组合输出在当前执行上下文中登记；M3 捕获器拒绝多特征组合，避免将不完整快照当作组合状态。

## Milestone 4 — 可组合特征后端

`FeatureBackendRegistry` 按 `OperationKind` 提供现有矩形拉伸和六种新增处理器：`CreateThroughHoleHandler`、`CreateBlindHoleHandler`、`CreateLinearPatternHandler`、`CreateRectangularPatternHandler`、`ApplyFilletHandler`、`ApplyChamferHandler`。`CompositionBackend` 先对整个 IR 验证支持范围及输入顺序，再逐操作执行。G1/G2 仅是测试 JSON，生产代码没有零件预设或专用流程。

当前构建使用一个初始、原点居中、XY 对齐的矩形拉伸。后端在创建时登记直接原生输出，通过持久引用取得后续输入，不按特征显示名或界面坐标选择。输出包括 `.top_face`、`.bottom_face`、`.body`、`.direction_x`、`.direction_y` 以及四个竖直外角边 `.outer_edge_1` 至 `.outer_edge_4`；角边顺序为局部坐标 `(-x,-y)`、`(-x,+y)`、`(+x,-y)`、`(+x,+y)`。孔另输出 `.wall_face`。这属于当前创建序列的输出连接，CADState 候选绑定与失效后重新解析留给 M5/M6。

孔使用顶面的局部 XY 毫米坐标，缺省位置为原点；圆必须严格位于矩形轮廓内，盲孔深度须小于板厚。草图点通过原生模型到草图变换转换。阵列包括种子在内计数，缺省方向为局部正 X/Y；显式方向当前支持已登记的线性边，方向朝其最大绝对分量的正方向。矩形阵列每个计数大于 1 的方向必须显式提供间距，支持 `1×N`/`N×1`；每次阵列最多 1024 个实例。圆角使用常半径，倒角使用等距双距离定义。每个处理器检查原生特征存在、错误/警告、重建，以及孔直径/端条件/位置、阵列计数/间距或圆角/倒角尺寸。

```powershell
.\scripts\test-milestone4.ps1
```

该命令只运行 M4 的纯测试，不激活 COM。原生验收按单个最小组合选择，测试后关闭并丢弃 Part、恢复原活动状态：

```powershell
.\scripts\test-milestone4.ps1 -Live -Case g1 -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone4.ps1 -Live -Case g2 -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone4.ps1 -Live -Case aux -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

结果：**17/17 纯测试通过**，G1、G2 和盲孔/倒角最小组合均原生通过。检查了最终孔数量、位置、孔壁上下边界、实体尺寸与体积；G1 另检查四个 R3 圆角。创建 **3** 个 Part、关闭 **3** 个，测试所有文档剩余 **0**；累计预算 **3/4**。独立账本位于 `artifacts/milestone4/native-budget.json`，三项结果集中在 `native-result.json`；禁止删除账本绕过预算。验收已经通过，不需要重复原生运行。记录见 `docs/milestone-4-verification.md`。

M4 独立入口限制：圆盘轮廓和旋转轴路径未扩展，PRD 的条件项 CircularPattern 延后并提前返回 `OPERATION_UNSUPPORTED`。`CompositionBackend` 不执行 `relations`、参数编辑、完整组合状态捕获/恢复、自动拓扑重解析、通用事务或回滚；失败会报告是否已开始修改、未回滚、未提交状态。原生测试在所有路径上负责清理。G2 此阶段只验收创建，厚度和孔径编辑属于后续阶段；未运行历史回归、广泛原生套件、LLM 或性能基准。M5 的关系路径见下节。

## Milestone 5 — 设计关系与语义 Binder

`SemanticEntityBinder` 从 CADState 查询候选，依次过滤输入契约的语义类型、引用/所有者健康状态、所有权、几何及依赖。一个候选直接绑定，零个返回 `BINDING_UNRESOLVED`；多个仅在显式首选所有者提供唯一证据时进行最多 64 个候选的确定性排序，否则返回 `BINDING_AMBIGUOUS`。不使用 LLM/Jev，不按枚举顺序选第一个。后端再通过持久引用检查原生对象接口、实际几何与操作合法性。

`DesignRelationEngine` 提供独立处理器：

| 关系 | M5 的有限语义 |
|---|---|
| `hosted_on` | 孔的关系引用须与 typed host 输入一致，原生读回检查草图的宿主面 |
| `pattern_seed` | 阵列关系引用须与 typed seed 输入一致，原生读回检查种子特征引用 |
| `equal_spacing` | subject 为线性/矩形阵列，reference 为其种子；每个重复方向使用明确的统一间距，不猜测缺失长度，不允许跳过实例或变实例草图 |
| `centered_about` | subject 为阵列，reference 为宿主局部框架；按计数、间距和方向计算总跨度，让种子落在负半跨度位置 |
| `symmetric_about_axis` | subject 为阵列，reference 为宿主局部 X/Y 中心轴；只约束垂直于该轴的布局中心坐标 |

耦合的种子偏移公式在关系处理器中实现，原生编辑路径只应用求解结果；冲突赋值明确返回 `RELATION_VIOLATED`。`.local_frame`、`.axis_x`、`.axis_y` 有类型化框架/轴几何，以拉伸特征持久引用作为逻辑锚点；中心轴用于关系计算，没有新建 SOLIDWORKS 参考轴特征。初始矩形顶面和 X/Y 方向仍是当前支持的原生几何范围。

`DependencyGraph` 区分 `native_input` 与 `relation_constraint`：原生创建依赖必须无环，关系影响允许种子与阵列之间的循环，通过有限 visited 集求影响闭包。M6 在此基础上加入 ChangeSet/DirtySet、通用事务与增量验证配置。

`RelationBackend.Create(context, program)` 先求解关系并预检完整计划，再复用 M4 特征处理器创建。`context.CaptureBindingState()` 返回包含实际原生参数绑定、语义几何、关系、依赖、文档/配置 GUID 和 revision 的 CADState。未保存 Part 的 `savedPath` 明确为空，表示当前会话状态；M3 的耐久单拉伸捕获仍要求已保存路径。JSON 关系/依赖保留既有数组槽，由 `StateRelationData` 严格解码，不接受未知/重复字段、未知枚举、无效端点或错误引用类型。

`RelationBackend.Edit(context, state, editOperation)` 接受 typed `edit_parameter`，本阶段仅支持线性/矩形阵列的计数与间距，绑定目标及参数所有者、核对文档/配置/revision/关系依赖后求解新种子位置，更新绑定的圆形草图及原生阵列标量定义，再重建并读回关系。保留当前原生种子和方向选择；不支持激活/停用阵列方向的维度转换。修改失败会将会话标为不可继续使用，返回修改/回滚/提交状态；没有自动回滚或耐久状态提交。

```powershell
.\scripts\test-milestone5.ps1
```

M5 纯测试 **39/39 PASS**。原生验收实际执行以下命令，累计 Part 上限为 **3**：

```powershell
# 首次原生尝试的编辑发生 COM 服务器异常，已清理；不要重复运行。
.\scripts\test-milestone5.ps1 -Live -Case linear -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
# 修正为只编辑必要的标量定义后，用一个 Part 同时完成两孔与 2×2 任务。
.\scripts\test-milestone5.ps1 -Live -Case combined -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone5.ps1 -Live -Case rect2x3 -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

三项要求均已通过：两孔间距 **40→50 mm**，孔 X **±20→±25**；2×2 的 X 间距 **60→70 mm**，孔 X **±30→±35**、Y **±15**；2×3 的 Y 计数 **3→4**，形成居中的 2×4，Y **-30、-10、10、30 mm**。检查了实际实体孔位、数量、孔径、贯穿边界、体积、原生关系引用、参数读回和阵列特征持久引用。每次调用只创建一个 Part，并关闭/丢弃、恢复原活动状态。

累计创建 **3**、关闭 **3**、剩余测试文档 **0**，**M5 预算 3/3 已用满**。进一步 `-Live` 在附加应用和创建 Part 前返回 `ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED`；禁止删除预算账本绕过限制。只保留 `artifacts/milestone5/native-budget.json` 与集中结果 `native-result.json`，没有保存额外原生文件。完整文件清单、失败尝试与验证记录见 `docs/milestone-5-verification.md`。

M5 入口限制：只支持当前构建会话和初始 XY 矩形框架上的孔阵列关系；没有完整组合控制器重启恢复、原生文件重开编辑、任意拓扑重解析、厚度/孔径等其他参数原生编辑。`Edit(context, state, edit)` 是 M5 的会话入口，`stateCommitted=false`；需要回滚与原子状态提交时使用下面的 M6 入口。未实现 Jev、规划器、性能基准或 stepwise baseline。

## Milestone 6 — 通用事务与增量验证

`MutationTransaction<TPrepared, TRollback>` 位于纯状态层，通过 `IMutationBackend` 调度加载、输入解析、预检、回滚快照、执行、重建、必需后置条件、一次允许的恢复、最终验证和原子提交。事务中没有阵列布局公式或 COM 类型。执行失败、验证失败、提交失败都会恢复原生值和会话 metadata、重建并完整验证恢复状态；不提交新状态。回滚失败保留原错误与回滚错误，并禁止继续使用该原生会话。

`ChangeSet` 显式记录修改的特征、参数及可能失效的实体；依赖图扩展 `DirtySet`。宿主/框架等读取依赖不会因被读取而将共享宿主上的独立布局标脏。Level 0 始终检查 API 结果、托管特征错误/警告、重建及事务完整性；Level 1 只读取直接修改的参数；Level 2 读取受影响实体和关系。Level 3 在新模型最终化、拓扑高风险、恢复、引用重解析、漂移怀疑、显式完整验证、基准故障注入时启用。回滚后的完整验证单独记录 `Rollback` 原因。

当前原生适配器 `TransactionalParameterBackend` 接入已有线性/矩形阵列计数和间距编辑。计数编辑自动按拓扑风险升级；间距编辑默认增量验证。保存原生阵列标量及实际种子位置，执行时仅设置请求的标量；耦合种子位置仍由 M5 的关系处理器求解。最终状态从原快照合并受影响字段，普通编辑不调用完整 `CaptureBindingState()`。`ValidationReads` 返回每个验证阶段的参数、实体、关系读取集合。

```csharp
// Create 完成后，先持久化最终化快照；store 为该文档独立的状态文件。
var store = new AtomicStateStore(statePath);
store.Commit(context.CaptureBindingState());
var result = new RelationBackend().Edit(context, store, editOperation);
// 需要读取集合时直接保留适配器：
var adapter = new TransactionalParameterBackend(context);
var transaction = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter);
var next = transaction.Execute(nextEditOperation);
var reads = adapter.ValidationReads;
```

```powershell
.\scripts\test-milestone6.ps1
# 验收已通过，无需重复创建 Part。
.\scripts\test-milestone6.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

M6 纯测试 **33/33 PASS**，Release 构建 **0 警告、0 错误**。原生验证在同一个 Part 上完成间距 **40→50 mm**、越界间距 **500 mm** 的预检拒绝、真实修改后注入后置条件失败的回滚、真实 `File.Replace` 失败的回滚、恢复后显式完整验证编辑 **50→52 mm**。两种修改后失败均保持已提交状态文件逐字节不变。普通编辑读取 **1/9 参数、9/21 实体、5/10 关系**，独立矩形阵列未进入实体/关系读取集合；全部 5 个托管特征仍接受 Level 0 状态检查。

创建 **1**、关闭/丢弃 **1**、测试所有文档剩余 **0**，原活动状态恢复，预算 **1/3**。独立耐久账本 `artifacts/milestone6/native-budget.json` 不得删除以绕过限制；原生各次调用记录追加到 `native-result.json`。没有历史回归、广泛原生套件、性能基准或 LLM 调用。详细文件清单、证据与限制见 `docs/milestone-6-verification.md`。

M6 入口限制：原生适配器仍使用当前构建会话，支持范围为既有阵列标量编辑；方向激活/停用、厚度/孔径等参数、组合重开/控制器重启恢复及任意拓扑重解析未扩展。原子提交针对 CADState JSON，未实现原生 Part 文件与 JSON 的跨文件联合提交。原生适配器当前不启用自动恢复策略；通用事务的一次可选恢复与完整升级已做纯验证。读取集合验证不等同于性能提升测量。

## Milestone 7 — Planner 与运行时能力投影

已将本次新增的 mandatory capability projection 要求写入 PRD M7。IR 的 `OperationRegistry` 表示类型系统的词汇，规划时只使用 `SolidWorksPlanningRuntime` 从当前后端生成的 `RuntimeCapabilityCatalog`。Planner 提示、Structured Outputs schema、严格解析器和能力检查均使用这份投影，不读取完整 IR registry/schema。能力检查之后仍执行现有后端的纯预检；Planner 没有原生执行回调。

| 范围 | Planner 可收到的能力 |
|---|---|
| 创建操作 | 当前注册的矩形拉伸、通孔/盲孔、线性/矩形阵列、圆角、倒角，共 7 类 |
| 轮廓 | 拉伸只接受 `centered_rectangle`；孔内部的圆形草图不代表可执行圆盘拉伸或独立 circle_profile 操作 |
| 关系 | `hosted_on`、`pattern_seed`、`equal_spacing`、`centered_about`、`symmetric_about_axis` |
| 当前模型编辑 | 只提供健康且唯一绑定的阵列目标及其活动方向计数/间距；计数值保持活动方向数 |
| 提前排除 | 圆盘拉伸、圆周阵列、厚度/孔径/圆角半径/倒角距离编辑、未实现关系、失效或漂移绑定 |

创建模式要求一个初始 XY 矩形拉伸、在计划内引用前序输出、孔使用初始顶面、重复方向给出明确间距并满足有限实例/宿主范围。编辑模式每个计划只有一个 `edit_parameter`，沿用运行时现有关系，不与创建混合。纯 snapshot 和 live session 投影共用规则；live 入口额外核对文档/配置/revision/依赖与可用会话。最终原生执行仍由 M5 创建或 M6 事务入口负责。

`CadPlanner` 接收自然语言，向 `IStructuredPlanSource` 请求一次响应，解析严格 envelope，再进行投影 IR 验证、能力检查与运行时预检。结果为 planned、unsupported、rejected、failed 或 cancelled，失败没有部分可执行程序。`FixturePlanSource` 使用调用方提供的精确意图→响应映射；未匹配意图明确 unsupported，生产代码没有板件预设。`OpenAiPlanSource` 使用一条可配置 frontier 模型 Responses 请求，发送 `text.format=json_schema`、`strict=true`、`store=false`；无工具、无循环代理、无自动重试。

```powershell
.\scripts\test-milestone7.ps1
.\scripts\plan.ps1 -Intent '创建一块80×50×10毫米的矩形板，两个直径8毫米的通孔，沿X轴居中间距40毫米' `
  -Fixtures tests/CadHarness.Planning.Tests/Fixtures/intent-responses.json `
  -Output artifacts/milestone7/plan.json
```

使用 frontier LLM 时设置 `CAD_HARNESS_PLANNER_MODEL` 和 `OPENAI_API_KEY`，然后运行 `plan.ps1 -Intent <意图> -UseLlm`。模型名称由部署方显式指定，无自动选择/替换；API key 只进入 Authorization header。可选 `CAD_HARNESS_PLANNER_ENDPOINT` 为 HTTPS Responses URL，默认 `https://api.openai.com/v1/responses`；可选 `CAD_HARNESS_PLANNER_MAX_OUTPUT_TOKENS` 默认 8192（256–32768），`CAD_HARNESS_PLANNER_TIMEOUT_SECONDS` 默认 120（1–300）。响应限制 512 KiB；拒绝 refusal、未完成响应、工具调用、多段计划和无效结构，支持取消。实际 API 可用性需要部署方自己的凭据和模型权限，本次只做 mock HTTP 验证。

已有模型编辑规划使用 `-ModelProgram <当前创建程序.json> -State <对应CADState.json>`；此模式生成 M6 可接收的编辑 IR，不会连接/重开 Part。应用内可使用：

```csharp
var runtime = SolidWorksPlanningRuntime.ForConstruction();
// 已有 live session：SolidWorksPlanningRuntime.ForEdit(context, committedState)
var planner = new CadPlanner(runtime, source);
var result = await planner.PlanAsync(userIntent, cancellationToken);
// 只有 result.Succeeded 才将 result.Program 交给对应 M5/M6 执行入口。
```

M7 **47/47 纯测试通过**，中文创建/编辑及 unsupported CLI 流程通过。全解决方案现有 **13 个项目**构建 0 警告、0 错误。创建 **0**、关闭 **0** Parts；未激活 SOLIDWORKS COM，未做可选原生 smoke，未发出真实付费模型请求，也未重跑 M0–M6 功能/原生测试。fixture 结果 `ModelCalls=0`；LLM 适配器记录每次规划的一次模型请求尝试，mock 测试不代表真实模型调用。

验收与文件清单见 `docs/milestone-7-verification.md`。模型输出的意图完整性及通用自然语言准确率尚未经真实 frontier 模型评估，不能把 fixture/mock 结果当作泛化或性能结论。运行时有限几何边界沿用 M5/M6，圆角/倒角最终可行性由原生执行判定。

## Milestone 8 — 可选 Bounded Judge

`IBoundedJudge` 位于纯 State 库，通过 `SemanticEntityBinder` 构造器可选注入。`BindAsync` 先执行既有类型、健康状态、几何、所有权、依赖筛选及确定性排名；0 个候选返回 unresolved，1 个或唯一 preferred-owner 匹配直接绑定，两种情况均不调用 Judge。仅余下 **2–8 个完整合法候选** 时最多调用一次 Judge，超过预算拒绝调用，要求调用方缩小确定性约束。

```csharp
// optionalJudge 可为 null；也可注入实现 IBoundedJudge 的语义选择适配器。
var binder = new SemanticEntityBinder(optionalJudge, TimeSpan.FromSeconds(10));
var result = await binder.BindAsync(state, inputContract, bindingQuery, intent, cancellationToken);
// result.Binding 为绑定结果；JudgeStatus、JudgeCalls、Rationale 为判断来源。
// 同步 binder.Bind(...) 始终只执行确定性绑定。
```

Judge 接收不可变的小候选投影（语义 ID/类型、所属特征 ID/类型、已有几何证据、输入名/角色和有界意图），不接收完整 CADState、原生持久引用、文件路径、IR OperationRegistry 或 CAD 执行回调。响应只能 Select 一个精确候选 ID 并说明理由，或 Abstain；请求 ID、枚举、ID 成员资格、决策形状及 UTF-8 字节上限均严格检查。无 Judge、弃权、非法选择、失败、取消、超时或等待期间状态变化都保留 `BINDING_AMBIGUOUS`，不选首项或自动重试。

```powershell
.\scripts\test-milestone8.ps1
.\scripts\build.ps1
```

M8 专属 **48/48 纯/mock 测试通过**，14 项目 Release 构建 **0 警告、0 错误**。M8 纯测试无需安装 SOLIDWORKS 或读取 COM 注册表。创建/关闭 **0/0 Parts**，真实 Judge 请求 **0**。按 PRD 保留可选 Jev 适配器后续接入，没有配置 Jev 依赖。原生读取/验证保持既有确定性路径，异步结果使用前由调用方在 COM 所属 STA 核对当前文档/revision 和原生引用。详见 `docs/milestone-8-verification.md` 与 `artifacts/milestone8/pure-result.json`。

## M9A — 可注册原生参数 mutation

`IParameterMutationHandler` 将参数的可执行条件、转换限制、验证风险、原生读取/修改、回滚载荷和必需后置条件交给各 handler。`ParameterMutationRegistry` 使用显式 owner/parameter/IR 字段契约注册，拒绝重复描述，支持调用方注入。原生适配器改为按注册表分派；通用 `MutationTransaction`、Binder、ChangeSet/DirtySet、ValidationScope 与 AtomicStateStore 无需改写。

| Handler | 实际默认编辑能力 |
|---|---|
| `PatternScalarMutationHandler` | 迁移已有线性/矩形阵列计数和间距；保留活动方向限制及计数拓扑升级 |
| `ExtrusionDepthMutationHandler` | 当前矩形 blind extrusion 的 `extrusion_depth` |
| `HoleDiameterMutationHandler` | 当前 through-hole 种子的 `hole_diameter`；原生阵列实例同步验证 |

Planner 的 edit catalog 由同一注册表生成，并要求目标/依赖健康、唯一参数绑定和状态值一致。移除 handler 后能力及 schema 随之缩小；未注册的厚度以外轮廓尺寸、盲孔深度/孔径、圆角/倒角编辑不暴露。创建能力仍只有原有矩形轮廓及处理器，没有 Circle/CircularPattern。

```csharp
var registry = ParameterMutationRegistry.Default; // 也可由明确的 handler 集合构造。
var adapter = new TransactionalParameterBackend(context, registry);
var plannerRuntime = SolidWorksPlanningRuntime.ForEdit(context, store.Load(), registry);
var transaction = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter);
var result = transaction.Execute(editOperation);
```

```powershell
.\scripts\test-milestone9a.ps1
# 已完成一次原生验收，通常无需重新创建 Part。
.\scripts\test-milestone9a.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
# 仅复核落盘的原生几何/事务/生命周期证据，不连接 COM 或创建 Part。
.\scripts\test-milestone9a.ps1 -VerifyEvidence
```

M9A **26/26 纯测试通过**；15 项目 Release 构建 **0 警告、0 错误**。同一个 G2 Part 完成厚度 **8→10 mm**、四孔 **Ø6→Ø8 mm**，以及厚度/孔径修改后失败回滚、真实原子提交失败回滚和已注册阵列 handler 的继续使用检查。厚度/孔径各只读 **1/6 参数**，Level 2 验证依赖孔壁、位置和贯穿边界。

创建/关闭 **1/1 Part**，原活动状态恢复；专属账本预算 **1/2**，没有消费剩余尝试。原生末尾曾因精确浮点比较将 `7.999999999999998 mm` 与 8 判为不等；已改用既有 `1e-6 mm` 容差，对全部 8 个已完成原生阶段及最终状态做零-Part 证据复核。原始失败报告保留，成功复核记录在 `artifacts/milestone9a/native-evidence-verification.json`。

本扩展仍使用当前托管构建会话；没有新增组合文件重开/控制器重启恢复、任意拓扑恢复、原生文件与 JSON 联合提交或构造事务回滚。详细记录见 `docs/milestone-9a-verification.md`。

**M9B+ NOT IMPLEMENTED；完整 Milestone 9 泛化评估未执行。**
