# M20 前技术补齐计划

日期：2026-10-11（Asia/Taipei）  
代码审查基线：`b25467d5b3402448f502bf966413213754d600a9`，分支 `v0.3-main-update`  
文档状态：待执行；本文不代表能力已经实现或 M20 已经通过。

## 1. 目标与执行方式

本计划将 M20 前的技术补齐拆成 **五次执行任务**。任务按依赖顺序交给执行 agent，每次只完成指定任务及其必要验证，并留下下一任务可使用的接口说明和证据。

共同目标：让 C1–C4 通过公开生产入口完成原生建模、指定参数编辑、关系验证、可靠保存、新控制器恢复及继续编辑，并让正式验收准确衡量这些能力。

五次任务不是新增 M21–M25，也不改写 M11–M17 的历史结论。M18、M19 按项目决定延后；完成本文后再进入新的正式 M20。

| 任务 | 交付目标 | 前置条件 | 主要影响 |
|---|---|---|---|
| T1 | Generic 持久化、可编辑冷恢复，并定位修复保存失败 | 当前代码审查基线 | C1–C4 的共同基础 |
| T2 | 多段旋转轮廓的参数身份、驱动与关系保持编辑 | T1 完成 | C1、C2 |
| T3 | 有界跨方向加工与通用圆角接入 | T1、T2 完成 | C3 |
| T4 | Planner、需求绑定、推导规则与能力投影升级 | T1–T3 完成 | C1–C4 真实规划 |
| T5 | 修正验收程序、对账证据、完成进入 M20 的检查 | T1–T4 完成 | 正式验收真实性 |

T3 的工作量最大，内部拆为材料验证、圆角集成、完整组合验证三个检查点；仍属于同一次任务。出现真实缺口时如实交接，不能以部分完成充当任务完成。

## 2. 依据与已确认事实

- [v0.3 PRD](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md)：§14–17、§21 和 M20。
- [M20 能力缺口报告](D:/CAD-Harness0.2/docs/milestone-20-capability-gap-audit.md)。
- [冻结数字任务](D:/CAD-Harness0.2/tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json)。
- [已停止的 M20 日程](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_v3/freeze.json)及其原始 slots、停止审计。
- 本计划基于生产实现与 runner 的代码检查，补充报告中尚未展开的参数身份、固定编辑目标、编辑后 Oracle 和需求归一化问题。

| 当前事实 | 补齐含义 |
|---|---|
| C1/C2 固定多段轮廓的初始旋转实体已经成功 | 复用现有旋转能力，重点补参数驱动、关系和恢复 |
| C4 初始实体及 cut-depth 编辑已有成功证据 | 优先用该有限路径定位保存问题及贯通 T1 |
| Generic commit 只更新内存；旧持久化协议支持 legacy/observed 路径 | 将 Generic 接入既有发布及恢复协议 |
| 材料预检拒绝混合扫掠轴；Generic IR 尚无圆角 | C3 需要生产能力扩展 |
| Planner 只验证单圆、局部矩形及局部 X 槽 | 原生能力补齐后扩展规划表达与需求证明 |
| 外部模型限定参数编辑、EditSet、检查点恢复已有实现及真实证据 | 复核相容性与遗漏证据，不重做整个 M14 |

## 3. 全部任务共同遵守的边界

### 3.1 架构与通用性

1. 复用现有 CADState、Semantic Binder、Persistent References、CAD IR、Planner、Validator、Transaction 和 durable revision 协议；允许必要的版本化扩展，不另造独立事务引擎。
2. 生产代码、提示词和 capability 不按 C1–C6、fixture ID、测试文件名或特定尺寸分支。
3. 使用通用轮廓、参数、关系、加工及边处理能力组合零件；不增加 shaft、sleeve、bracket 等零件专用 Tool。
4. 新增操作种类与 native accessor 是必要时的内部扩展；不能为每个参数、区段或零件创建独立 LLM Tool。优先采用有限 descriptor、registry 与同类 handler。
5. 不把 candidate-only、测试私有入口或 contract-only 枚举声明当作公开可执行能力。公开能力投影必须与已验证的实际适用条件一致。
6. 保持 v0.2 wire 行为、legacy managed 恢复和外部 observed/edit 模式兼容；旧包与新包必须明确分派，不能靠猜测反序列化。

### 3.2 范围与证据

1. 不实施 M18 查询选择、M19 Playground UI、C5/C6、装配体、多文件事务、STEP 特征恢复或自主工程设计。
2. 不要求本阶段全面支持 `RevolveAxialSpan`、槽宽/长/位置编辑，或任意旋转与棱柱混合历史。仅拓展解决实际阻塞所需的通用能力。
3. 保留 `acceptance_v1/v2/v3` 的请求、结果、冻结文件、停止审计及账本；不得覆盖、修补历史 PASS/FAIL 或恢复旧日程累计成功率。
4. C1–C4 的物理要求、指定编辑、几何容差和正式门槛保持不变。可以修复 runner 的字段传递、目标解析、规则披露和 Oracle 实现，但必须写明原因及前后差异。
5. 不编辑原始 M11 任务文件来绕过能力缺口。新增契约字段或 corrected runner 在新版本、新证据目录中验证。
6. 每项记录执行前后的 commit、工作树差异、源码/二进制标识、测试命令、原始结果和限制。原始未跟踪文件不是可随意清理的临时文件。

### 3.3 验证与原生生命周期

- 先做纯契约、材料计算、规划与事务状态机验证，再运行针对性原生测试。
- 原生验证一次只操作一个 owned Part，登记所有权后才修改；关闭测试文档并恢复用户原活动文档。保持 STA、COM 线程所有权及 mm/m 单位转换规则。
- 保留响应性及 GDI 保护（GDI ≥ 7000 停止）。保存失败、关闭失败、身份漂移或 recovery marker 未处理时停止该批原生运行，保留现场证据。
- 开发验证、live Planner 开发调用与正式 M20 使用不同命名空间和统计。修代码后可以在新开发日程验证，不能重新执行同一冻结失败槽冒充新尝试。
- 每次原生运行提前记录有限的 Part 创建数与打开周期计划，并连续记账；遵守 PRD §17 及用户已有适用授权，不重复申请已经授权的操作。
- 历史 M20 不限额授权如仅针对 M20，不自动解释成五项补齐均不限额。预算不足时按已有规则处理，不能删除账本或静默增额。
- 本文只设计任务，当前不启动 SOLIDWORKS、不调用 provider、不运行 M20。

推荐最小开发日程预算如下，供下发任务时确定。已有相容证据可复用以减少运行；需要增额时说明具体未验证风险。

| 任务 | 新建 Part 上限建议 | 原生打开周期上限建议 | 调用安排 |
|---|---:|---:|---|
| T1 | 2 | 10 | 持久化/重启/故障验证；不调用 LLM |
| T2 | 2 | 8 | C1/C2 驱动、关系、回滚和冷恢复；不调用 LLM |
| T3 | 2 | 8 | 最小跨轴组合与完整 C3；不调用 LLM |
| T4 | 1 | 3 | 使用前项产物做规划接入；开发 live 调用提前固定次数 |
| T5 | 4 | 12 | 开发集成检查；通过前项相容证据可减少新建数 |

测试实现须核算 checkpoint/独立读回实际引入的打开次数。上表是计划建议，不能用预计次数替代实际计数或当作已经批准的预算增额。

## 4. T1 — Generic 持久化与保存恢复

### 4.1 目标

让通用建模产物经过生产事务可靠发布，并由不共享内存的新控制器恢复全部必要绑定、继续执行现有已合格的 scalar edit。首先查清 M20 `Save3=false/errors=1/dirty=true` 的根因。

### 4.2 代码入口

- [GenericFeatureConstruction.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs)：`GenericStore`、`GenericBackend`、Generic session dictionaries。
- [GenericConstructionState.cs](D:/CAD-Harness0.2/src/CadHarness.State/GenericConstructionState.cs)。
- [ManagedRevisionStore.cs](D:/CAD-Harness0.2/src/CadHarness.State/ManagedRevisionStore.cs)。
- [ManagedPartSession.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ManagedPartSession.cs)。
- [ManagedContextRestore.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ManagedContextRestore.cs)。
- [PlanarSketchBackend.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/PlanarSketchBackend.cs)：`PlanarSketchBinding`、dimension ownership/readback。
- 复用 `CadHarness.ManagedRecovery.Tests`、`CadHarness.FeatureConstruction.Tests` 及现有事务故障注入设施。

### 4.3 必须实现

1. **独立保存诊断**：用已有支持的 plate/cut 路径记录 SaveAs/Save3 返回值、错误枚举/位、warning、真实路径、只读/dirty/rebuild/active-sketch 状态、owned title 和保存前后文件标识。对照 M16/M17 成功流程，给出可复现根因与修复；不能仅将 Save3 换成另一个调用且不说明发布、身份和原件保护。
2. **明确包类型与版本**：Generic 程序/状态作为现有 revision 协议中受校验的类型扩展或 companion，不伪装为 legacy `CadProgram`。
3. **补完整恢复数据**：保存 feature/profile/axis/datum/host 绑定、native reverse/end condition、primitive 身份、驱动尺寸的定位与所有权证明、草图 transform、参数身份、revision，以及可选但有明确版本的需求/绑定/关系记录。可以使用受校验的确定性重建，不强制逐字段照搬内存对象；不能靠名称或数组位置猜绑定。
4. **生产发布接入**：复用检查点、recovery marker、native save、独立保存读回、状态 flush、artifact hash、manifest 与原子 pointer 发布。事务返回 committed/succeeded 必须反映整个发布结果，不能先报成功再在测试程序外另存文件。
5. **新控制器恢复**：核验文件、文档/配置、程序/状态、引用健康与类型、草图及 driver 所有权、原生定义、依赖和几何；重建新的 session-local binding。原进程 SessionId 不作为新进程可用性的替代证明。
6. **统一拒绝/隔离**：漂移、损坏、歧义或未支持历史在修改前拒绝。保存/发布失败后按照既有协议恢复确定的 authoritative revision 或 quarantine，不能选择“看起来最新”的包。
7. **接口交接**：给 T2/T3 提供参数、关系、边处理恢复信息的版本化扩展点，后续任务新增绑定也必须进入同一发布链路。

### 4.4 验证与完成标准

- 纯测试覆盖包类型分派、严格反序列化、程序/状态关联、损坏 artifact、版本/配置/文件漂移和关键发布边界。
- 最小原生闭环：C4 类 plate/cut 经公开入口创建并发布 → 关闭并结束控制器 A → 控制器 B 恢复 → 改一个切深并发布 → 控制器 C 读回最终参数、其他槽和 reference/revision。
- 至少覆盖两个不同故障边界：native save 与 state publish 之间，以及 pointer publication 前后。纯测试可覆盖完整状态矩阵；针对性原生故障证明文件恢复与重开行为。
- 初始发布没有旧 revision 时，失败应保留证据并隔离，不能声称恢复了不存在的旧模型。
- legacy managed 与 external observed 路径执行必要的契约/分派回归；仅在具体风险要求时扩大原生回归。

**完成条件：保存根因有证据，Generic 成功发布和跨控制器再次编辑通过，失败发布不产生伪 committed 状态，原件与用户文档安全，资源清理通过。**

## 5. T2 — 多段旋转轮廓驱动与关系编辑

### 5.1 目标

复用现有多段轮廓旋转，让 C1/C2 的指定编辑成为有明确原生驱动、可回滚和可冷恢复的生产能力。

### 5.2 代码入口

- `V03Contracts.cs`、`V03ContractValidation.cs`：参数选择及有限关系契约。
- `PlanarSketchBackend.cs`、`PlanarSketchPreflight.cs`：折线/轮廓、原生尺寸和 readback。
- `GenericFeatureConstruction.cs`：`ReadGenericState`、`ApplyGenericEdit`、`ResolveInputs`、rollback。
- `GenericConstructionState.cs`、T1 发布与恢复接口。
- 现有 `DesignRelation`、关系验证及 Dependency Graph；按相容性复用。

### 5.3 必须实现

1. **参数身份**：同一旋转 feature 内存在多个同类径向参数，必须能精确选择一个 driver。设计稳定的参数 semantic identity/selector；旧 `Target + ParameterKey` 调用仅在唯一匹配时兼容，非唯一必须拒绝。
2. **有界轮廓表达**：为轴向/径向正交台阶轮廓声明区段及驱动来源；继续支持 C1/C2 所需内外轮廓。检查顺序、连续闭合、不自交、非负半径、正壁厚、参数范围和关系无环。
3. **真正的原生驱动**：驱动维度通过原生尺寸/已验证约束表达。不能将所有线固定后直接挪点，再宣称其具备驱动尺寸。采用部分固定与驱动组合时，记录每项实际参数化边界。
4. **有限关系**：至少支持 C1 的槽底半径 = 中段半径 − 槽深，并保持同轴和不相关区段。通过类型化规则执行和验证，不让 LLM 生成任意表达式或代码。
5. **原子修改**：一个主参数编辑及其关系传播作为同一事务、一次 aggregate revision 提交。回滚恢复全部相关 driver、程序、关系、引用和原生几何。
6. **持久化集成**：T1 包保存具体参数身份、区段绑定和关系；新控制器恢复后仍选择同一 driver、保持联动。
7. **资格范围**：只广告已验证的 driver/profile 条件；不把多段径向编辑扩大成任意轮廓节点/轴向编辑资格。

### 5.4 验证与完成标准

| 开发任务 | 初始结构 | 指定编辑与不变量 |
|---|---|---|
| C1 | 三段轴径及 35–39 mm 环槽 | 中段半径 12→13；槽底 10→11；槽深仍为 2；其他径向区段与轴向边界不变 |
| C2 | 外径、法兰及两段同轴内孔 | 20–36 mm 内孔半径 12→13；其他内外径、肩部与法兰不变；相应最小壁厚 4→3 |

- 纯测试覆盖同类参数歧义、错误 driver/owner、关系环、非法半径/壁厚、事务传播及完整 rollback payload。
- 原生 readback 验证 driving dimension、轴向边界、各圆柱半径、体积及未修改区段；关系传播中失败时恢复全部受影响参数。
- C1/C2 均通过保存和新控制器恢复，在恢复后的模型上再次执行一个有效编辑并读回；后续测试值放在开发 fixture 中，不嵌入生产代码。
- 增加至少一个不同台阶数量或区段布局的纯/开发案例，证明实现没有依赖固定顶点下标。

**完成条件：两类模型均具备公开、精确、多 driver 的编辑入口及关系闭环，不依赖测试特供参数名称，T1 恢复兼容新绑定。**

## 6. T3 — 跨方向加工与通用圆角

### 6.1 目标

让同一个原生实体支持有界的不同主轴加工，并将已有 fillet 接入 Generic，使完整 C3 可执行及保持编辑关系。

### 6.2 代码入口

- `FeatureConstructionPreflight.cs`、`ConstructionPreflight.cs`：材料、坐标与 host 验证。
- `GenericFeatureConstruction.cs`、`NativeConstructionCheckpoint.cs`。
- `EdgeTreatmentHandlers.cs`、`FeatureBackendRegistry.cs`、Binder/NativeTopology。
- `V03Contracts.cs`、`V03ContractValidation.cs`、T1/T2 状态与恢复扩展。

### 6.3 检查点 A：跨方向材料验证

1. 实现一个明确且有界的几何适用域，覆盖矩形基体/附加矩形体及主轴方向圆形切除。支持 C3 的 Z 轴底板孔与 X 轴立板孔，适用规则不按支架名称分派。
2. 根据真实 host 材料核验切除起止、贯穿范围、重叠/相交处理、体积变化、外包络与单体连通性。不能仅累加 `孔面积 × 深度` 而忽略重复切除或没有材料的范围。
3. 可以采用多种可组合的有限材料证明策略；若某种组合缺少可靠证明，应在 mutation 前拒绝。无需实现任意 B-rep 布尔证明器。
4. 保留旧平行分层算法及其已支持路径；新策略对适用域显式分派。不得只删除 mixed-axis guard 或把 ForceRebuild 成功当材料证明。

### 6.4 检查点 B：圆角接入

1. 增加或复用通用 typed edge-treatment 表达与原生 fillet 调用，避免写一个支架专用 API。
2. 在计划中表达有限的 edge query/selector 或 typed prior output，并由 Binder 在预检中绑定精确原生边。这里仅支持显式几何/拓扑条件的建模边选择，不开展 M18 的任意自然语言查询系统。
3. selector 必须含必要 host、几何/方向及局部位置条件；多个候选不任选第一条。运行时绑定后的 reference、边类型和 owner 必须再验证。
4. 接入圆角半径、feature definition、边与宿主关系、材料验证、ChangeSet、发布、rollback 和冷恢复。
5. 对支持的简单凸边圆角建立独立几何验证与边界条件；不能仅读取 `DefaultRadius` 就宣称最终形体正确，也不能关闭整个 Generic 材料验证。

### 6.5 检查点 C：完整组合与编辑

- 基体：`x=0..60, y=-20..20, z=0..8` mm。
- 立板：`x=0..8, y=-20..20, z=8..48` mm。
- 底板两个 Ø6 孔：`x=30, y=-10/+10`，沿 Z 贯穿。
- 立板 Ø6 孔：`y=0, z=30`，沿 X 贯穿。
- 圆角：立板外侧顶部沿 Y 的边，初始 `x=0,z=48`，披露的半径为 1 mm。
- 后续编辑：立板附加高度 40→44；总体最高 Z 为 48→52；底板、全局孔位、垂直关系和圆角半径保持，圆角跟随立板顶部。

以上数值只属于开发/验收任务。生产边选择、材料算法和编辑关系不能硬编码这些坐标。

### 6.6 验证与完成标准

- 纯验证覆盖 frame 转换、跨轴组合材料、未证明相交拒绝、detached/empty body、圆角 selector 非唯一和过大半径。
- 完整 C3 通过公开入口：创建 → 原生定义及独立几何验证 → 附加高度修改 → 再次验证 → 发布 → 新控制器恢复并继续编辑。
- 独立 Oracle 检查安装面法向、孔轴/中心/贯穿范围、圆角面/半径/边及修改后的包络；不要继续套用只认识轴向圆柱的 C1/C2 Oracle。
- 至少证明一次跨轴加工或圆角失败的事务恢复。若 native 重建不能恢复精确身份，使用 T1 检查点恢复或明确隔离，不以近似几何替代健康绑定。
- 以旋转/平移后的同类开发形体或纯案例验证 selector 与几何策略没有依赖世界坐标特例。

**完成条件：完整 C3 的两个方向孔、圆角、指定编辑及持久化恢复全部通过；三个内部检查点任一个未通过，T3 都未完成。**

## 7. T4 — Planner 与需求绑定升级

### 7.1 目标

让 Planner 使用已验证的 T1–T3 能力，一次生成可组合、全部尺寸有来源的计划。提高真实计划可靠性，同时保留严格预检和批准机制。

### 7.2 代码入口

- `ConstructionPlanner.cs`、`ConstructionRequirements.cs`。
- `SolidWorksConstructionPlanningRuntime.cs`、`V03ContractCapabilities.cs`。
- `V03ContractValidation.cs`、相关 schemas 与 `ProviderSchemaCompatibility`。
- `CadHarness.ConstructionPlanning.Tests` 的 Cases、PureTests/Program、LiveTests、NativeTests。

### 7.3 必须实现

1. **同步能力投影**：profile、操作、参数身份、关系、材料适用域、恢复后的模式和边处理只广告经过前项资格验证的范围。保持外部模型与 Harness 新建模式隔离；不得因此开放任意旋转后的追加建模。
2. **逐段需求证明**：扩展绑定以验证轴向 station/span、径向区段、槽深/槽底及圆角。单个 profile bounding box 不能证明全部内部台阶。
3. **有限推导规则**：支持必要的 diameter→radius、累计长度/坐标、已给几何尺寸间的有限关系、完整旋转的结构性角度及布局默认。规则有版本、单位、输入键、输出键、来源及误差检查；不引入任意表达式执行。
4. **原始输入来源闭环**：原始 Given 可通过有记录的派生链绑定到几何；不能因最终绑定在 Derived 节点上就误判原 Given 未使用，也不能容许无关 Given 被静默忽略。
5. **圆角规则**：对账 fixture 的 `bracket_cosmetic_corner`。可以保留版本化兼容标识并由通用规则实现，但不得分派到 C3 模板。半径 1 的默认必须披露且限于已验证的非关键外观处理；不能将默认尺寸权限扩大到孔径、配合、壁厚或强度。
6. **Schema 收紧**：按 primitive 分支约束 points 数量、radius/width/length 等字段。Circle 必须有中心点；不合法的空 points 应在结构化输出约束或严格解析阶段暴露。
7. **typed outputs 清晰化**：operation semanticId 与 `.profile/.body/.plane/.axis_x` 输出分开表达，合法 plane 类型收紧。禁止 Body 作为草图平面、禁止错误后缀引用和尚未产生的输出。
8. **空间与槽形来源**：C4 世界 Y 槽由局部 X 槽及披露 frame 表达；顶部草图需真实支持的基准，不接受没有 plane ownership 的 frame Z 偏移。
9. **完整规划及批准**：继续一次完整计划优先、immutable requirements/prefix、context/revision/hash、一次性批准及执行前 fresh preflight。不要靠逐 feature LLM 调用或自动重试隐藏规划失败。
10. **持久化 provenance**：生产批准执行后保存 accepted requirements、bindings、规则版本及实际程序，恢复后仍可检查尺寸来源和后续编辑关系。

### 7.4 验证与完成标准

- 为 C1–C4 各提供至少一个通过纯预检的完整计划，覆盖新增绑定；使用 fixture 的结果明确标为确定性，不作为真实 LLM 成功。
- 回放旧 C2/C4 错误输出，确认拒绝原因具体且没有变成宽松接受。修正后的通用格式通过；不能建立旧输出文本→标准答案的修补表。
- 覆盖漏绑定、单位错误、错误区段、非法引用、丢失 critical input、旧 preview 重用、漂移的 context/revision、未合格能力等负向案例。
- C1/C2 请求必须补布局/派生来源，C3 必须传递原任务中的孔位、空间范围、圆角默认规则；规则属于正常输入归一化，不直接给 Planner 注入标准 feature tree。
- 先冻结开发 live 日程，再对配置中的实际模型做有限冒烟。保存实际 provider/model、原始 request/response、accepted record、调用量/耗时/拒绝原因。失败后修复使用新开发日程；不将重复尝试计入正式成功率。
- 用公开批准执行入口证明规划产物可进入现有生产链路；优先复用前项可编辑产物和原生资格，不重跑完整旧矩阵。

**完成条件：Planner 的表达、规则、native qualification、validator 和持久化内容一致；四类完整计划可验证，真实开发调用有诚实结果。真实模型未证明某家族可规划时，必须保留该阻塞，不能只凭 fixture 宣布就绪。**

## 8. T5 — 验收修正与 M20 就绪检查

### 8.1 目标

修复 runner 的误判与漏判，复核相容旧证据，完成独立于正式成功率的开发集成检查，输出进入 M20 的准确结论。

### 8.2 代码入口

- `tests/CadHarness.CoreAcceptance.Tests/Program.cs`。
- `Tasks.cs`、`Native.cs`、`ExternalAndG2.cs`、`Recovery.cs`。
- `scripts/test-milestone20.ps1`。
- M14 final acceptance、M15–M17 报告、现有资源 guard 和生命周期账本。

### 8.3 必须修正的验收问题

| 问题 | 修正要求 |
|---|---|
| C3 `Program=null` 且 native 分支无条件跳过 | 在新版本中给出完整确定性程序，移除已过时 skip；能力不满足时仍明确拒绝 |
| C1/C2 编辑后仍用初始 cylinder radii | 单独存储/读取 post-edit expected geometry，按冻结关系检验编辑后半径与未修改段 |
| 所有阶段都使用初始 bounds | 使用 phase-specific bounds；C3 编辑后最高 Z=52，不能继续与 48 比较 |
| 编辑目标硬编码 `revolve/slot0` | 从 accepted requirements/bindings 与 production parameter identity 解析实际目标，唯一绑定后编辑；fixture 可固定名字，live 不能要求相同命名 |
| 同几何不同合法 feature history | 允许不同计划历史；Oracle 衡量几何、参数化、关系、编辑与恢复，不要求 feature tree spelling 一致 |
| C1/C2 `Owner+Key` 无法选择某个区段 | runner 使用 T2 参数身份，保留对精确 owner/driver 的检查 |
| C1/C2/C3 请求锚点/规则不完整 | 通过 T4 通用归一化传递来源，不放宽全尺寸覆盖；记录与旧 runner 的差异 |
| cold 分支仅读取 JSON/引用，未恢复 | 调用 T1 公开生产 session；新控制器恢复后实际编辑、保存并独立读回 |
| cold 后的新编辑与原生阶段混在一个 expected state | 明确阶段和每步 expected revision/value/geometry；保存后读回匹配对应 authoritative revision |
| C4 Oracle 缺 capsule 直线切线检查 | 验证直边长度、方向、端点与圆弧连接、圆弧半径和完整槽形；同体积不代替同几何 |
| C3 无完整 Oracle | 独立量安装面、孔、fillet、host、编辑后包络/体积与 reference health |
| external/g2 的 native/cold 分槽被按单槽完整任务判分 | 按相同 fixture/package/revision 严格配对汇总，缺失阶段保持 NOT_RUN/PARTIAL |
| `Set-Content -NoClobber` 无效 | 使用真实 CreateNew/安全原子写入与明确的新汇总版本，禁止覆盖旧结果 |
| summary 固定 PARTIAL/布尔默认掩盖未运行 | 根据完整日程计算状态；分清 STOPPED、NOT_RUN、REFUSED、FAIL、PASS，不补造未执行结果 |

独立 Oracle 可以使用任务定义中的公式与原生测量 API，但不得调用生产材料计算函数得到自身 expected 值，也不能用生产 binder 的成功结果替代独立 target/几何核验。

### 8.4 旧证据与 held-out 对账

1. 建立外部四项参数（depth、hole diameter、pattern count、spacing）的逐项矩阵：来源、native subtype、driver、建模历史、qualified/read-only 原因、直接证据及适用代码版本。
2. 区分几何等价和编辑资格等价。两个文件、两组尺寸或两个不同来源本身不能证明两种建模历史。
3. 复用 M14 已批准来源及相容证据，不重启第三方模型搜索；未执行的 manual cold、read-only、G2 槽如实保留。
4. 已公开的 `C4-held-out` 数值/布局和已运行外部任务不能在新调优后继续声称为未见 held-out。作为历史/开发回归保留。
5. 新正式验收需有新的未用于调优的 P0 组合和外部参数/布局或历史组合。由独立任务维护者在执行前冻结；若执行 agent 已用这些值调优，它们就是 development-visible，需另选组合。
6. 新 held-out 不改变 C1–C4 固定核心任务、门槛或已批准外部来源规则，不嵌入生产 prompt/capability/handler。缺少真正未调优组合时，标记 readiness 条件未满足。

### 8.5 开发集成检查

- 所有相关纯测试通过，新增能力未泄露到不支持的上下文。
- C1–C4 通过 corrected runner 或同等公开入口的开发流程：完整创建、独立 Oracle、参数/关系编辑、持久化、跨控制器恢复和恢复后继续编辑。
- 生产错误引用/非法 driver/未证明几何在 native mutation 前拒绝；已开始执行的失败可回滚或隔离并保留证据。
- 不按生产 case 名称或冻结尺寸分支；Tool、operation、handler 和 capability 的增量及理由有清单。
- 针对 T1–T4 对共享代码的实际影响，复核 legacy managed、external scalar/EditSet 与恢复。能复用的旧结果列明版本相容性理由；不能证明相容的关键路径运行针对性验证。
- 所有 owned 文档已关闭、原活动文档恢复、原件 hash 不变、账本与 recovery marker 可解释。

开发集成通过并不计入 M20 的 4/4 或 8/12。进入正式测量前仍须冻结源码/二进制、provider、prompt、capability、任务、Oracle、顺序与资源计划。

### 8.6 完成标准与停止位置

输出 `READY_FOR_M20` 或 `NOT_READY`。后者必须列出具体未实现/未验证项、代码位置、证据和最小后续动作。

`READY_FOR_M20` 必须同时满足：

1. T1–T4 的实现与公开原生资格完成。
2. corrected runner/Oracle 的误判与漏判已修复并验证。
3. 开发 C1–C4 完整闭环通过，至少关键失败恢复有证据。
4. 配置模型的开发规划证据没有尚未解决的系统性表达阻塞。
5. 外部历史/参数矩阵对账完成，正式日程遗漏项已安排。
6. 新 held-out、资源计划及冻结机制具备可执行安排。

T5 交付就绪报告与新正式日程方案，**不自动调用正式 `Execute`，不恢复旧 acceptance run**。用户另行下发 M20 执行任务时启动新冻结；已停止的历史 M20 执行授权不能作为本任务自动启动新正式运行的依据。

正式门槛仍为：确定性 C1–C4 **4/4 完整任务**；真实模型每族至少 **2/3**，总计至少 **8/12**。`READY_FOR_M20` 不保证真实成功率，只有新正式结果能够证明门槛达成。

## 9. 每次任务的交付与交接格式

每项提交以下内容，证据目录使用唯一 run ID：

| 交付 | 要求 |
|---|---|
| 生产实现 | 可 review 的差异；无测试数据分支；公开入口与资格条件清楚 |
| 验证代码 | 针对真实风险的纯/原生测试与独立 Oracle；不以实现镜像测试代替验证 |
| 原始证据 | `artifacts/m20-preparation/Tn/<run-id>/`，记录请求、结果、故障、生命周期及文件 hash |
| 验证报告 | `docs/m20-preparation-Tn-verification.md`，注明代码基线、实际结果、限制和未执行项 |
| 交接说明 | 新增/修改的契约、API、包 schema、parameter/selector 身份和后续接入方式 |
| 就绪状态 | `COMPLETE`、`PARTIAL` 或 `BLOCKED`，说明哪些条件未满足；不把未运行项填成 PASS |

T5 另输出 `docs/milestone-20-readiness.md`，含 T1–T5 矩阵、外部参数/历史矩阵、Tool/handler 增量、回归映射、新正式日程和最终 readiness 判断。

不要求为每项主动推送或创建 PR；按用户给执行 agent 的 Git 要求保存工作。新任务接手时先核验前项结果及当前工作树，不覆盖别人未提交的修改。

## 10. 可直接交给执行 agent 的五次任务指令

### 第一次

> 执行 `docs/milestone-20-preparation-plan.md` 的 T1。先核对当前代码与已有证据，定位 Save3 失败，再将 Generic 持久化及可编辑冷恢复接入既有协议。完成针对性验证和 T1 报告，交接参数/关系/边绑定的持久化扩展点。不要继续 T2–T5 或启动正式 M20；未满足原生/恢复条件时如实报告。

### 第二次

> 执行同一文档的 T2，先验证 T1 完成条件。实现多段旋转轮廓的精确参数身份、原生驱动与关系保持编辑；证明 C1/C2 指定编辑、回滚和新控制器恢复后继续编辑。保持同一发布协议，输出 T2 报告与接口交接，不启动正式 M20。

### 第三次

> 执行同一文档的 T3，先核验 T1/T2。按跨方向材料验证、通用圆角、完整组合三个检查点推进，完成完整 C3 的创建、指定高度编辑、独立 Oracle、失败恢复和冷恢复。复用现有 fillet，不删除可靠性 guard 或加入支架专用模板；输出 T3 报告，不启动正式 M20。

### 第四次

> 执行同一文档的 T4，先核验 T1–T3 原生资格。扩展 Planner 的多段需求绑定、有限规则、typed outputs、primitive Schema 和真实能力投影，保留一次完整规划与严格批准预检。完成纯验证、固定日程的开发 live 冒烟及公开入口接入验证；输出 T4 报告，不把开发调用计入 M20。

### 第五次

> 执行同一文档的 T5，先核验 T1–T4。修正 M20 runner/Oracle/汇总与实际编辑目标解析，完成开发完整闭环和外部证据对账，安排新的未调优 held-out 与正式冻结日程。输出 `milestone-20-readiness.md` 的 READY_FOR_M20 或具体 NOT_READY 结论，保留所有历史结果，默认不启动正式 Execute。

## 11. 判断

五次拆分合理：T1 提供共用可靠性基础，T2/T3 补齐原生能力，T4 对齐 LLM 可用表达，T5 保证测量准确且可复现。每次都具有具体结果及完成条件，能够逐项交接；不能只完成接口或测试报告就提前宣布能力就绪。
