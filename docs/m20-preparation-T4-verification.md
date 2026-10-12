# M20 Preparation T4 Verification

日期：2026-10-12（Asia/Taipei）。基线 `571851e`，分支 `v0.3-main-update`。本轮源码在工作树，未主动提交或推送。

**T4: PARTIAL。** 表达、规则、Schema、严格批准、来源持久化和公开原生接入已验证；配置中真实 provider 的四类完整规划均未通过，不能凭确定性计划宣布 T4 完成或 M20 就绪。没有执行 T5、新正式 M20、Factory 扩展或第三方模型搜索。

## 实际实现

| 生产文件 | 变化及边界 |
|---|---|
| [ConstructionRequirements.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs) | `Element/EndElement` 精确绑定轴向点/跨度、径向 driver/offset；新增 fillet/slot-end 半径。逐点/逐 driver 证明台阶，不用包络代替内部几何。 |
| 同上 | 保留旧 `InputKey`，新增可选 `Inputs`；有限版本 1 的和、差、取负、完整旋转及 capsule 半宽规则。原始 Given 通过推导 DAG 追溯；循环/前向/单位错误先拒绝，共享祖先去重。 |
| 同上 | `bracket_cosmetic_corner` 是通用 `cosmetic_convex_radius` 的兼容别名，非关键默认仅允许 1 mm 外观圆角。即使数值相同或经过 copy/derived 链，也不能资助孔径、深度等功能尺寸。 |
| [ConstructionPlanner.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionPlanner.cs) | Circle/Slot/Polyline 分支收紧；公开 T2 radial、T3 fillet 字段；plane 只允许 reference_plane。一次完整规划，向模型披露正常规则图，不注入标准树，不修补失败响应。批准绑定 request/response/capability/context/revision 的 SHA，消费前重新预检。 |
| [V03ContractCapabilities.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/V03ContractCapabilities.cs)、[SolidWorksConstructionPlanningRuntime.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/SolidWorksConstructionPlanningRuntime.cs) | 原六家族加已资格化的有界末端凸圆角；广告 T1 冷恢复、T2 正交半剖面、T3 材料域。实际仍由 Native qualification 过滤。旋转/末端圆角之后不广告追加建模；外部模型模式不开放。旧 OperationRegistry 仍为 9。 |
| [GenericPlanningProvenance.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPlanningProvenance.cs) | `construction_planning` / Version 1：结构化 accepted request、response/bindings/rules、capability、context/hash、baseline、revision events。历史回放必须复现权威 program/revision；后续 scalar 是明确 override，不重写原 Given。 |
| [GenericPartSession.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPartSession.cs)、[GenericContextRestore.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericContextRestore.cs) | 新公开 `GenericPartSession.ExecuteConstructionPlan(approval)` 接原 Construct/Transaction/Store。变更前验证来源，提交后保存；失败按实际 StateCommitted 恢复来源。捕获/冷恢复验证历史。不另造事务引擎。旧 Context 执行入口在 durable session 上明确拒绝，避免丢失来源。 |
| [GenericRevisionPackage.cs](D:/CAD-Harness0.2/src/CadHarness.State/GenericRevisionPackage.cs)、[V03ContractJson.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/V03ContractJson.cs) | 严格 reader/Schema 用标准字段注解兑现原有 provenance 65,536 字符上限；其他字符串默认仍为 10,924。没有全局放宽。规划历史另有 65,536-byte 严格读取和最多 64 events；超限在 mutation 前拒绝，不删减来源。 |

Generic 包 FormatVersion 仍按普通/radial/edge 分别为 1/2/3；可选 provenance 内部自行版本化。旧包无规划历史时保持旧恢复路径。没有新增 native accessor、几何容差、零件专用 Tool 或生产 fixture 分支；PRD、原 C1–C4 数字任务和旧 M20 runner 未修改。

## 确定性与纯验证

[T4 40/40](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/result.json)，Release 编译零 warning/error。

| 完整开发计划 | 已证明内容 | 证据（不是 LLM 成功） |
|---|---|---|
| C1 | 三段径向 driver、35–39 槽的两个站位、槽底与槽深关系，所有 Given 的有限推导来源 | [完整计划](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/shaft-plan.json) |
| C2 | 外径/法兰/两段孔的精确 driver、肩部与孔台阶站位、第二孔 span、两条累计长度链 | [完整计划](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/sleeve-plan.json) |
| C3 | 原任务空间范围、三个孔位、Z/X 加工、立板和披露的 1 mm 圆角默认；12 operations | [完整计划](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/bracket-plan.json) |
| C4 | 实际 top datum、90-degree frame、三条世界 Y 槽、局部 X capsule 及端半径来源 | [完整计划](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/slots-plan.json) |

负向覆盖漏 driver/station、错误 selector/单位/引用、未消费 Given、缺 critical input、默认洗成 Derived、循环/共享 DAG、未资格操作、Body 当 plane、隐式顶部 offset、旧 approval/context/capability、来源篡改/缺事件/错误 old value/版本/哈希及超限。

原 `acceptance_v3` C2/C4 provider 响应原字节回放仍拒绝，记录具体错误及源 SHA：[C2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/legacy-C2-replay.json)、[C4](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_pure_v3/legacy-C4-replay.json)。没有响应文本到标准答案的修补表。

## 真实开发规划

执行前分别冻结 [live_v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/freeze.json)、[live_v2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/freeze.json)。每轮四次、每族一次，temperature=0、max output=16,384、timeout=180s、无自动重试。只发合成任务/正常规则/capability/schema，不发原件、路径、原生引用或凭据正文。

请求 alias `deepseek-chat`；原始返回 model 为 **`deepseek-flash`**，不能将 alias 当作实际型号。所有 `call-01-request.json` / `call-01-response.json`、调用量、token、耗时和拒绝原因保留。

| 家族 | live_v1 | live_v2 |
|---|---|---|
| C1 | clockwise 外轮廓；另拆 groove cut，不是受支持的单一 half-section | 已整合 groove，但仍 clockwise，绕向拒绝 |
| C2 | 内孔台阶缺拐点，形成斜边 | offset relation 与实际 driver 半径不一致 |
| C3 | 重复几何 binding，原始 Given/Derived 祖先重复资助同一 metric | 使用反向 world.xz normal，原生平面/方向拒绝 |
| C4 | origin Z=8 直接挂 world.xy，没有 tracked top datum | 仍缺 datum；还没有用披露的 frame 表达 Y 槽 |

两轮均 **0/4 accepted**，[结果 v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/result.json)、[结果 v2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/result.json)。v1 后只增加通用规则图、唯一绑定、正交绕向和 datum 语义说明；没有改变任务、标准、几何或响应。v2 后停止该规划路径，不用继续随机重试累积表面通过率。共 8 次，输入 95,926 / 输出 36,943 tokens；不计正式 M20 成功率。后续源码仅加强来源验证/持久化，没有未记录的第三轮调用。

## 公开批准执行、保存与冷恢复

实机使用确定性完整计划，经真实 Planner fresh preflight 和批准，进入生产 Handler、原事务及 durable publication。没有直接测试脚本 Setter。为证明首次 accepted provenance 发布而使用两个新独立包，不给旧 T2/T3 R3 包补造历史；旧包未修改。未执行无必要的 C2/C4 全套旧原生矩阵。

| 模型 / 步骤 | 控制器 | 结果与直接证据 |
|---|---:|---|
| C1 公共批准创建、保存后独立原生重开 | 28160 | [R1/半径12](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/shaft/create/080-result.json)；[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/shaft/create/079-independent-oracle.json)；批准复用拒绝 |
| C1 冷恢复后公共 Edit | 18948 | [R2/半径13](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/shaft/edit/022-result.json)；精确 parameter ID 从 accepted binding 解析；槽底10→11、深度仍2、不相关区段不变 |
| C1 第三控制器冷读 | 5916 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/shaft/read/008-result.json)；[完整 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/shaft/read/007-independent-oracle.json)，体积39860.52758874732，差2.18e-11 mm³ |
| C3 公共批准完整创建、保存后原生重开 | 2476 | [R1/高度40](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/bracket/create/111-result.json)；[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/bracket/create/110-independent-oracle.json)；批准复用拒绝 |
| C3 冷恢复后公共 Edit | 26032 | [R2/高度44](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/bracket/edit/030-result.json)；[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/bracket/edit/029-independent-oracle.json)，最高Z=52，孔位/安装面/圆角跟随通过 |
| C3 第三控制器冷读 | 10748 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/bracket/read/009-result.json)；[完整 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v3/bracket/read/008-independent-oracle.json)，体积32592.8319133605，差-3.64e-12 mm³ |

Oracle 复用 T2/T3 独立测试实现，通过官方 API 测量圆柱轴/半径/边界、面法向、极值、体积及圆角 patch；expected 来自冻结数字任务的独立公式，不调用生产材料函数或 Factory 自检。圆角、孔位置、未编辑段与引用健康没有被 volume-only 检查替代。

两个权威包均为 R2、两条 history events（accepted plan + explicit scalar override），初始 Given 未改。C1 native SHA `274a8df50408e250a0dcc86013b849e068a4c10c4a7dda4eec2ab7850a49b4ef`；C3 `64fbbba8b5b7c7d499abb2f3f0a2e7e83382feca9549c83316b44435a171220c`。冷控制器核验 Native/State/Program/Manifest/Pointer，成功包无 recovery marker。

各槽 `002-source.json` 保存源码及真实加载 DLL SHA。最后的版本检查、默认祖先去重没有改变 Setter、几何、恢复数据或协议；最终编译版本再次冷读两个先前包，证明相容，不重跑已充分通过的创建。

## 历史失败、回归与资源

- `provenance_pure_v1` 保留 8 个失败：JSON 字符串嵌套的 response 超过 reader 默认字符串上限。改为结构化 request/response，批准哈希仍可重现。
- [public_v2 发布失败](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v2/shaft/create/073-publish.json)：原生建模/Rebuild 已成功，外层 provenance.payload 的通用 reader 10,924 与容器已声明 65,536 冲突；StateCommitted=false，内存回滚成功，R0，无权威 pointer。修复字段级 reader/Schema 不一致后仅用新包复测。
- 首次失败包无可恢复的旧 Revision；[隔离记录](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/public_v2/shaft/package/quarantine-disposition.json) 有意保留 marker/candidate/快照，禁止再编辑或自动提升为权威包。不存在未处理的权威恢复，也不声称磁盘上 marker 总数为零。
- 早期脚本遗漏默认 PackageRun 时的空字符串判定，在 COM 连接前拒绝（0 原生成本）；已改为显式空值默认。早期编译/脚本诊断保留于执行会话，不计成原生成功或覆盖上述文件。
- 回归：M11 **77/77** `artifacts/m20-preparation/T4/m11_regression/pure-results.json`；M12 **48/48** `artifacts/milestone12/pure/20261012T080430683/results.json`；M14 **86/86** `artifacts/milestone14/pure/20261012T080535294/result.json`；最终 M17 **36/36** `artifacts/milestone17/pure/20261012T081827627/results.json`。
- T1 **23/23**、T2 **36/36**、T3 **31/31** 的 `from_t4_final/result.json` 均通过。之后只修改规划来源检查，三项的 Handler/材料/关系/协议不变；没有重跑旧完整原生矩阵。M11 的第七描述符断言是已资格化 fillet 的投影同步，原数字任务未改。
- [T4 账本](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/native-budget.json)：**3 次创建/3 个 Part、11 次打开、14 次关闭、0 owned**，包括一次首次发布失败。有效两模型完整链预计10 opens，失败另计1；无限额授权不是无限重试。
- [最终只读审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/final_audit/native-audit.json)：实际文档为空、应用响应正常、GDI **4319 < 7000**，原活动文档恢复。四个 Factory 原件 SHA 与 Registry 一致；人工原件 SHA 仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`；未打开/修改工程师原件。停止审计中的 **1477 条旧证据 SHA 不变**。

## 交接与停止点

T5 应使用可选 `RequirementRule.Inputs`、精确 binding selectors 和正常归一化输入；从 accepted binding 解析 driver，而非固定 feature spelling。批准生产执行使用 `GenericPartSession.ExecuteConstructionPlan`；后续编辑使用该 session 的 Edit，显式保留 override。读取 `CurrentGeneric.Provenance` 可检查来源，但不能把历史批准或 offline snapshot 当作当前执行许可。

**本次停在 T4 的 PARTIAL 验收点，不自动进入 T5。** 不缺 T1–T3 原生能力证据，当前阻塞是配置模型对完整合法表达的实际规划可靠性：绕向、native normal、relation 以及 datum/frame。需要独立后续的有限 T4 开发验证解决并重新证明这些家族，不能交给 T5 用固定答案、逐 feature 调用、自动修补或降低标准遮掩。任何新开发调用必须新冻结，保留本次 0/8 结果；正式 M20 门槛未测量。
