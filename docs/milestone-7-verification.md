# Milestone 7 验证

**COMPLETE — MILESTONE 7 COMPLETE**。

依据 PRD §7/§20/§26/§27 M7 和用户追加的运行时能力要求，实现自然语言→一次严格 CadProgram 的规划路径，先支持确定性 fixture/结构化 mock，再加入一个可配置 frontier LLM 适配器。遵守默认原生预算：0 Parts；没有可选原生 smoke、迭代工具代理、Jev 或大规模 live benchmark。

## 用户追加的硬约束

以下内容已加入 PRD 的 M7，而非仅保留在提示里：

> Planner MUST NOT consume the whole IR OperationRegistry
> as if every IR operation were executable.
>
> Introduce a runtime capability projection/catalog.
>
> The planner may only receive operations, parameter edits,
> profiles and relations that the current SOLIDWORKS runtime
> can actually execute.

`SolidWorksPlanningRuntime` 先枚举 `FeatureBackendRegistry.SupportedKinds` 的实际原生处理器，再只为这些类型读取 IR 契约并缩小输入类型、参数界限、轮廓与关系。规划层从 `RuntimeCapabilityCatalog` 得到投影 registry；提示、schema、解析和能力检查都使用它。没有调用完整 `ProgramJsonSchema.Create()` 或在规划层访问 `OperationRegistry.Default`。IR 本身没有删除未来操作；它们对当前 Planner 不可见。

| 模式/能力 | 当前投影 |
|---|---|
| 创建操作 | create_extrude、create_through_hole、create_blind_hole、create_linear_pattern、create_rectangular_pattern、apply_fillet、apply_chamfer |
| 创建轮廓 | centered_rectangle；不暴露圆盘拉伸或独立 circle_profile |
| 创建关系 | hosted_on、pattern_seed、equal_spacing、centered_about、symmetric_about_axis |
| 编辑操作 | 仅 edit_parameter；每个计划一个事务；当前健康、唯一绑定、与程序值一致的目标/参数配对 |
| 线性阵列编辑 | pattern_count、pattern_spacing |
| 矩形阵列编辑 | 活动 X/Y 方向上的 count/spacing；不暴露停用方向，计数下限保持原生活动方向数 |
| 不暴露 | create_circular_pattern、circle 拉伸轮廓、厚度/孔径/圆角/倒角编辑、through_all/aligned_with/depends_on 关系 |

创建与编辑分开投影，不能混合。宿主、方向、轮廓、实例数、盲孔深度、重复方向明确间距等限制同时进入提示和确定性预检。创建只允许计划内前序输出，孔宿主限定初始矩形 top_face；外部模型引用不作为可执行创建能力。完整原生几何可行性仍由后端在实际执行时判定，能力目录不声称任意正值圆角/倒角必定成功。

编辑投影复用 M6 的 `IsExecutableParameter` 活动标量访问规则和关系编辑器实际使用的字段映射；不是从 `EditableParameter` 全枚举推断。失效实体、缺失/非唯一绑定、与程序值漂移的参数不提供编辑能力；程序/状态特征、关系、依赖不匹配时拒绝投影。live 入口还核对会话可用性、原生文档/配置身份和 revision。纯 snapshot 入口便于离线规划/测试，不意味着已经重新连接或恢复 CAD 文件。

## 文件变更

| 文件 | 内容 |
|---|---|
| `src/CadHarness.Planning/CadHarness.Planning.csproj`（新增） | 纯 .NET 8 规划库，仅引用 IR，不依赖 COM |
| `RuntimeCapabilityCatalog.cs`（新增，同目录） | 创建/编辑能力目录、参数配对/范围、投影校验、纯预检接口 |
| `PlannerResponseSchema.cs`（新增，同目录） | 仅从目录生成 provider-compatible 严格 schema，closed variants 表达可选 IR 字段 |
| `CadPlanner.cs`（新增，同目录） | 一次响应、strict envelope、IR/能力/运行时三层检查、取消/结构化失败、通用 fixture source |
| `OpenAiPlanSource.cs`（新增，同目录） | 配置化 Responses HTTP 适配器、token/timeout/byte bounds、拒绝工具输出/拒答/截断、凭据隔离 |
| `src/CadHarness.SolidWorks/SolidWorksPlanningRuntime.cs`（新增） | 后端拥有的能力投影、snapshot/live 模式、现有关系/原生处理器纯预检 |
| `src/CadHarness.SolidWorks/TransactionalParameterBackend.cs` | 共享活动参数访问规则，并由实际 M6 输入解析使用 |
| `src/CadHarness.SolidWorks/CreateExtrudeHandler.cs` | 明确当前原生轮廓集合，由预检与投影共用 |
| `src/CadHarness.State/RelationParameterEditor.cs` | 实际编辑字段映射改为只读共享表，由执行与投影共用 |
| `src/CadHarness.State/DesignRelationEngine.cs` | 从实际关系处理器表提供 SupportedKinds |
| `src/CadHarness.SolidWorks/CadHarness.SolidWorks.csproj` | 引用纯规划接口，仍由后端负责 COM |
| `src/CadHarness.Planner.Cli/*`（新增） | 创建/编辑规划 CLI、fixture/LLM source、严格程序输出；没有 native execute |
| `tests/CadHarness.Planning.Tests/*`（新增） | M7 项目、47 项纯/mock 测试、精确自然语言响应 fixture |
| `scripts/test-milestone7.ps1`、`scripts/plan.ps1`（新增） | 专属验收与用户规划入口 |
| `scripts/build.ps1`、`CadHarness.sln` | M7 scope 与 3 个新增项目，总计 13 项目 |
| `Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` | 仅按用户明确要求追加 M7 capability projection 小节 |
| `README.md`、本记录 | 使用方式、验收与边界 |

没有读取/复制本地 v0.1 工作区，没有生产零件预设、任意 C#/VBA 生成或执行入口。

## 规划与 HTTP 路径

`CadPlanner.PlanAsync` 检查意图非空且 ≤8192 UTF-8 bytes，构造能力受限提示/schema，向 source 请求一次响应。Envelope 固定为 outcome、program、reason；planned 要求严格 program 且 reason 为空，unsupported 要求 program=null 和明确原因。递归拒绝重复字段、未知/缺失 envelope 字段、非法 JSON、代码围栏、超限响应及矛盾结果；IR 解析器继续校验封闭字段、有限枚举/数值、所有权、引用顺序及 1–12 操作。随后做能力检查和现有运行时纯预检。失败返回 null Program，没有自动修复/第二次模型请求，也不把不支持意图替换为另一模型。

`FixturePlanSource` 接收调用方的精确意图映射；JSON fixture 位于本次专属测试目录。生产类没有测试尺寸或预设流程，未匹配的文本返回 unsupported。创建 fixture 保留用户给出的 80×50×10、Ø8、2 个孔和 40 mm 居中间距；关系引擎独立求得种子 X=-20。编辑 fixture 生成 holes.pattern_spacing=50 的 typed edit；预检可验证既有关系传播。

`OpenAiPlanSource` 只发送一条 HTTPS Responses POST，使用部署方配置的 model；`text.format` 为 json_schema/strict，store=false，不发送 tools。工厂禁止自动重定向，以配置的 linked cancellation timeout 限制请求；不自动重试、轮询或启动工具代理。API key 只进入每次请求的 Authorization header，不进入提示、schema、规划产物或错误结果。HTTP 错误只报告状态码，不回显 provider body。响应有 512 KiB 字节上限；未完成状态、refusal、工具调用、多段计划或不合法内容都明确失败。取消/超时返回 cancelled。输入/输出 token 若 provider 提供则记录，不进行性能比较。

OpenAI Docs 官方页面已实际读取，用于核对 Responses `text.format`、strict/all-fields-required/closed-object 规则：[Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)。Schema 的可选 IR 输入/参数采用有限 closed variants；不让 nullable optional 字段进入不接受 null 的既有 IR parser。未改用完整 IR schema，因为它同时暴露当前未实现词汇，且包含与该 provider 子集不同的 schema 结构。

| 环境设置 | 用途 |
|---|---|
| CAD_HARNESS_PLANNER_MODEL | 必需，部署方显式选择 frontier 模型；不自动替换或猜测 |
| OPENAI_API_KEY | 必需，只用于 HTTP Authorization |
| CAD_HARNESS_PLANNER_ENDPOINT | 可选 HTTPS Responses URL，默认 https://api.openai.com/v1/responses；拒绝 URL 内 credentials/query/fragment |
| CAD_HARNESS_PLANNER_MAX_OUTPUT_TOKENS | 默认 8192，范围 256–32768 |
| CAD_HARNESS_PLANNER_TIMEOUT_SECONDS | 默认 120，范围 1–300 |

本次未使用真实凭据或发出真实模型请求；用注入 HttpMessageHandler 验证请求构造、输出解析及错误边界。实际模型访问权限、联网连通性和自然语言规划准确率尚未 live 验证，不能将 mock 成功描述为真实 frontier 模型验收。

## 验证

```powershell
.\scripts\test-milestone7.ps1
.\scripts\plan.ps1 -Intent '创建一块80×50×10毫米的矩形板，两个直径8毫米的通孔，沿X轴居中间距40毫米' `
  -Fixtures tests/CadHarness.Planning.Tests/Fixtures/intent-responses.json `
  -Output artifacts/milestone7/cli-create-program.json
.\scripts\plan.ps1 -Intent '把现有阵列holes的孔间距改为50毫米，保持居中' `
  -Fixtures tests/CadHarness.Planning.Tests/Fixtures/intent-responses.json `
  -ModelProgram artifacts/milestone7/planned-program.json -State artifacts/milestone7/mock-model-state.json `
  -Output artifacts/milestone7/cli-edit-program.json
.\scripts\plan.ps1 -Intent '设计带内部冷却通道的优化涡轮叶片' `
  -Fixtures tests/CadHarness.Planning.Tests/Fixtures/intent-responses.json
.\scripts\build.ps1
```

SDK 8.0.425，M7 与 13 项目 Release 解决方案构建 **0 警告、0 错误**。只运行 M7 测试；构建其他项目不运行它们的功能测试。

**47/47 PASS**：实际 handler→catalog 一致、未实现词汇不进入提示/schema、输入类型缩小、provider strict schema 结构、中文创建/unsupported fixture、单响应提示；IR 合法但不可执行的圆盘/圆周阵列/关系/参数编辑拒绝；宿主、范围、缺间距拒绝；严格 envelope/JSON/参数/代码字段/字节界限；创建与编辑隔离、一个编辑事务、健康目标、唯一绑定、参数漂移、依赖快照一致、停用方向/计数转换；取消与意图上限；mock HTTP 的 model/endpoint/header/schema、store=false、无工具、单请求、token 读取、HTTP 拒绝、工具调用、refusal、截断、流界限、取消及错误配置。

| CLI 场景 | 结果 |
|---|---|
| 中文创建 | planned；严格程序 3 个创建操作、5 个关系；成功导出程序文件 |
| 中文编辑 | planned；一个 edit_parameter，目标 holes，pattern_spacing=50；成功导出文件 |
| 涡轮叶片 unsupported | unsupported，Program=null；没有创建 Part 或降级模型 |

CLI fixture 模式 ModelCalls=0；模型适配器模式记录一次模型请求尝试，mock HTTP 不代表真实网络请求。CLI 仅生成/验证计划；不会执行、保存或重开原生 Part。CLI 编辑使用的是本次测试产生的合成状态，原生引用为测试载荷，不声称能对真实 SOLIDWORKS 会话执行该合成状态。

## 生命周期、证据与限制

创建 **0**、关闭 **0** native Parts；默认预算 0，未启用允许的最多 1 Part smoke。没有连接/启动 SOLIDWORKS，没有打开测试文档或需要恢复的活动文档，也没有修改之前里程碑的 native-budget 账本。真实付费模型请求 **0**。没有 M0–M6 功能/原生重跑、历史基准、大规模 live 测试、迭代工具 agent、Jev 或 baseline。

保留 `artifacts/milestone7/pure-result.json`、创建/编辑 capability JSON、两份输出 schema、planned-program.json、合成 mock-model-state.json 和 CLI 导出的 create/edit 程序。文档读取缓存不作为性能或模型正确性证据。由于没有创建 Part，不新建/消耗 native budget ledger。

当前限制：

- deterministic fixture 只覆盖精确登记意图；开放自然语言依赖配置的 frontier 模型。本次未评价真实模型准确率或 held-out 泛化。
- 计划必须符合当前有限运行时的模式、输入/宿主/方向、轮廓、关系和编辑边界；目录不会扩大后端能力。
- 原生 topology/圆角/倒角几何可行性仍需执行时验证，不能由纯规划预检保证。
- 编辑需要当前可用构建会话与匹配状态；snapshot CLI 不实现完整组合重开/控制器重启恢复。实际执行仍走 M5/M6 的身份、绑定、验证和回滚。
- 目前一个编辑事务/计划，不支持创建后混合编辑或同计划连续多参数事务。
- 无自动 retry/repair、Jev、性能/效率比较或 baseline；上述测试不证明 PRD §25 的效率假设。

本记录保留 M7 的验收范围；后续 M8 的实现与验收见 [milestone-8-verification.md](milestone-8-verification.md)。
