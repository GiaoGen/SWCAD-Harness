# T4C — 外径局部凹槽语义、结构准入与 T4 完成方案

日期：2026-10-12（Asia/Taipei）。  
审查 HEAD：`fcddf2d649e238247d6b0a39f6b746bb62ce74b6`；T4B 实现在当前未提交工作树。  
状态：执行方案，尚未实施；不代表 T4 已 COMPLETE。

## 1. 目标与执行任务

本任务完成 T4 的剩余能力及证据闭环：让真实 Planner 准确表达外径局部凹槽，将其确定性编译为受支持的径向半剖面，验证其满足结构和编辑意图，再通过公开生产入口完成创建、指定编辑、来源发布与跨控制器恢复。

T4C 包含必要的实现、针对性验证和最终 T4 完成审计。不能只写接口、修提示词、通过编译单测或创建确定性 candidate 就停止并声称完成。

本任务不实施 T5、M18/M19 或正式 M20。T4 的 COMPLETE 与正式 M20 的 4/4、每族 2/3、8/12 是不同结论。

依据：

- [T4B 验证报告](D:/CAD-Harness0.2/docs/m20-preparation-T4B-verification.md)。
- [T4 复核与修复方案](D:/CAD-Harness0.2/docs/m20-preparation-T4-review-and-fix-plan.md)。
- [M20 前补齐计划](D:/CAD-Harness0.2/docs/milestone-20-preparation-plan.md) T4 的完成条件。
- [v0.3 PRD](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md)及原冻结数字任务。

## 2. 本次需要解决的两个实际问题

### 2.1 模型仍承担区间拆分

T4B v3 已将槽放到 outer，但输出“0–20、20–60、60–80 三个基础区段，再追加 35–39 槽”。现有 `Radial()` 要求最终区段连续且不重叠，因此拒绝。

外径局部凹槽的含义已经明确时，从基础区段减去槽范围属于机械展开。新语义契约应让模型声明修改及宿主，交由编译器拆分，而不要求模型手写最终五个区段。

### 2.2 合法计划没有保证结构符合需求

T4B v2 将外槽放进 inner，得到合法的内部空腔，来源与编辑角色检查却通过；独立任务复核才发现体积及坐标基准不符。

因此需要将明确的结构要求纳入生产计划准入。绑定完整只证明数值有来源，不能证明数值用于正确的外/内边界。

修复这两点应保留旧 raw IR/draft 的严格拒绝，不把历史错误当成可自动修复的合法输入。

## 3. 当前代码入口

| 入口 | 接入责任 |
|---|---|
| [SemanticConstructionCompiler.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/SemanticConstructionCompiler.cs) | semantic DTO、`Radial()`、版本分派、lowering/source map、编辑角色、schema/instructions |
| [ConstructionRequirements.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs) | 可选的新语义版本、typed geometry requirements、来源与规则验证 |
| [ConstructionPlanner.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionPlanner.cs) | 编译前语义检查、编译后结构准入、preview/approval 绑定 |
| [GenericPlanningProvenance.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPlanningProvenance.cs) | 新旧 compilation 回放、来源及历史 override 验证 |
| [SolidWorksConstructionPlanningRuntime.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/SolidWorksConstructionPlanningRuntime.cs) | 模式明确的 capability 投影；保留现有 native qualification |
| [T4BCases.cs](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4BCases.cs)、[T4BTests.cs](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4BTests.cs) | 新请求的规范化、旧响应拒绝回归、任务结构检查 |
| [T4Native.cs](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4Native.cs) | 真实 raw response 重放与公开生产执行；独立原生 Oracle |

可以新增少量职责清晰的 Planning 文件和 T4C 测试文件；无需把所有逻辑继续堆入一个 compiler 文件，也不建立另一套 Native executor/事务/通用测试框架。

## 4. 实现一：新的有限径向语义表达

### 4.1 版本与数据职责

建议引入 semantic draft v2 及对应 compiler 版本，名称可调整，但必须明确版本分派。

新径向表达至少包含：

- `BaseOuter`：连续、无重叠的基础外径区段；轴向边界与半径均引用 accepted source keys。
- `BaseInner`：基础内径区段；实心区段用明确的 zero baseline，不用外槽半径充当内孔。
- `ExternalRecesses`：外径局部凹槽声明，每项有稳定 ID、明确的宿主外径来源/区段选择、start/end 来源、floor radius 来源及其设计关系。
- `Dependencies`：仅包含输入已经声明的设计依赖，复用现有 source/target/offset 规则。

该表达是通用径向边界修改，不叫 shaft 模板，不创建专用 Tool，也不新增原生环槽 accessor。最后仍生成现有 `RadialProfile` 加一个受支持的 revolve。

### 4.2 语义示例

下面仅说明新表达的职责，不规定最终 DTO 拼写；数值只在 fixture 中解析：

```json
{
  "baseOuter": [
    {"start": "zero", "end": "head_length", "radius": "head_radius"},
    {"start": "head_length", "end": "middle_end", "radius": "middle_radius"},
    {"start": "middle_end", "end": "overall", "radius": "tip_radius"}
  ],
  "baseInner": [{"start": "zero", "end": "overall", "radius": null}],
  "externalRecesses": [{
    "id": "recess_a",
    "hostRadius": "middle_radius",
    "start": "groove_start",
    "end": "groove_end",
    "floorRadius": "groove_radius"
  }]
}
```

LLM 仍负责选择外槽而非内孔、引用正确来源、确定宿主和几何组成。compiler 不从 C1、文件名、尺寸或旧失败文本识别标准答案。

### 4.3 边界条件

本次有限资格范围：每项 recess 明确落在一个基础外径区段内；多个 recess 允许在不同位置存在，但彼此不能重叠。跨越多个基础台阶、重叠修改或需要额外优先级的组合先明确拒绝，不靠 last-write-wins 解决。

核验：

1. source 存在、单位正确、来源合法；所有基础区段连续且域一致。
2. host 在给定范围内唯一，所选 host radius 来源与实际基础区段相符；不按半径数值相等猜宿主。
3. start < end，槽范围被宿主包含，floor radius > 0 且小于宿主外径。
4. 槽底高于该范围内的内径，保留正壁厚；不生成空材料、分体或未证明拓扑。
5. floor 及 depth 的真实联动匹配已声明依赖；未声明联动不能由两个值之差创造。
6. 各项声明都有唯一身份，展开后 primitive/driver/point/binding 数量继续满足现有边界。

必要时收紧到槽范围严格位于宿主区段内部。资格条件须明示，不能为了支持端部特殊情况扩大本任务。

## 5. 实现二：确定性区间拆分及来源保持

### 5.1 算法

1. 验证基础区段及 recess 适用条件。
2. 收集宿主区段边界与 recess 的 start/end 事件，使用明确规则按轴向排序。
3. 将宿主拆为剩余段、槽底段、剩余段；槽内选择显式 floor radius，其他范围保留原 radius 来源。
4. 多槽按位置展开；不靠声明顺序决定覆盖优先级。
5. 合并时只合并同一来源/driver 且没有需要保留的语义边界的相邻段，不按数值相等合并独立参数。
6. 生成最终连续 outer，保留 base inner，交给现有 CCW half-section、driver 与 binding 展开。
7. 执行原有 `RadialProfilePolicy`、requirements、材料、能力及编辑适用性验证。

坐标相等但来源 key 不同的事件必须保留来源证据，并由有限推导图证明关系；不能通过任意同值替换满足 source coverage。无法证明唯一身份/来源时拒绝，不默默选一个 key。

### 5.2 C1 开发验收结果

| 轴向区间 mm | 外半径 mm | 来源 |
|---|---:|---|
| 0–20 | 15 | head radius |
| 20–35 | 12 | middle radius |
| 35–39 | 10 | explicit recess floor radius |
| 39–60 | 12 | middle radius |
| 60–80 | 9 | tip radius |

内部全程为 zero baseline。编辑中段半径 12→13 时，槽底 10→11，槽深仍 2；其余径向及轴向区段不变。

这张表属于独立开发/验收 expected，不能作为生产分支或返回模板。

### 5.3 来源与批准

- source map 记录基础区段、modifier、拆分区段、实际边界、driver 及对应原始来源的关联。
- 编译生成的中间数据是表示展开，不把它伪装为新的用户 Given，不反写原输入。
- 本任务可以新增明确的逻辑 source-map descriptor；必须接入批准哈希与 provenance 校验，不增加槽专用 LLM Tool。

## 6. 实现三：结构要求的生产准入

### 6.1 输入中的通用要求

新增可选、版本化的 typed geometry requirements/obligations，最少覆盖本次需要：

- 外径局部凹槽：结构类别、宿主来源、start/end、floor/depth 来源以及关系。
- 实心内部要求或明确内孔区段要求。
- 原任务的基础径向区段及轴向长度职责。
- 已明确或经披露布局规则选定的世界轴向/坐标基准。

这些要求来自正常输入归一化中的明确任务事实。测试构造可以如实传入原意图已经给出的“外槽、实心轴”等条件，但不能传入 Expected draft、最终五段轮廓、native feature tree、oracle 体积或运行标准答案。

当前 T4 使用结构化尺寸锚点，并未交付任意自由文本工程设计解析。本任务不扩展为完整 NLP 抽取系统；如有结构语义歧义，按原有最小澄清规则处理，不能从测试 ID 推导。

### 6.2 必须检查的语义及几何

1. 计划确实包含要求的 external recess，且关联正确 host、span 与 floor 来源。
2. external recess 不能由 inner cavity、额外通孔或未支持的后续 cut 替代。
3. 展开后的边界在槽内达到指定半径，槽外保留基础外轮廓；内部符合 solid/bore 要求。
4. 检验全部轴向区段、原始长度及径向尺寸，不只查整体体积/包络或标记了 recess 标签。
5. 世界旋转轴及布局符合披露的坐标要求。未显式给定的轴向只能来自已披露的合法布局默认，不能伪称为用户 Given。
6. 编辑角色仍对应精确独立 driver，槽深不变量与其他 preserved sources 在模拟编辑及最终结果中保持。

以上检查在返回可执行 preview 前进行，并在 approval consumption/provenance replay 中重复核验。输入声明与 compiler 输出应有独立可检查的对应关系，不能只比较 compiler 自己生成的两个对象。

### 6.3 诊断输出

为语义错误提供具体位置、role/source/modifier ID 及阶段；至少能区分：

- 需求要求 external，但计划使用 inner。
- recess 缺失、host 错误或 span/floor 来源错配。
- 基础区段有 gap/overlap，或 modifiers 相互冲突。
- 已给几何/布局不一致。
- 后续编辑 driver 不合格或破坏不变量。

诊断可用于开发修复，不建立自动把错误 draft 改为正确 draft 的执行循环。

## 7. 新旧版本、Schema 与持久化

1. direct IR、semantic v1、semantic v2 与对应 compiler 明确分派。不要只将全局 `Version` 常量改成 2，导致 T4B v1 包无法恢复。
2. 已保存的 v1 compilation 按原规则严格回放；旧 v3 的 overlap 仍是 FAIL。新的 modifier 含义只在新请求/Schema 中开放。
3. request/draft/schema/source map/compiled evidence/approval/provenance 的版本检查同步；变更来源、modifier、compiler 或 expanded IR 时拒绝旧批准。
4. provider Schema 显式表达基础边界与 external recess 的不同角色，并继续仅枚举已接受来源、有限集合和合格操作。
5. capability 投影只描述语义 node ID，避免重新出现 `.body` 引用冲突；完整运行资格仍绑定批准。
6. 保留原 65,536 provenance 边界。核算 expanded map 对 payload 的影响，精简冗余与空白但不丢字段；超限在 mutation 前拒绝，不静默扩容。
7. 原生层仍用现有 `GenericPartSession.ExecuteConstructionPlan`、Edit、Handler、Transaction、Store 和恢复协议。没有新 native qualification 时不能广告新平面、参数或材料域。

## 8. 验证计划

### 8.1 纯测试先行

正向至少覆盖：

- C1 三个基础区段加一个 external recess 的正确展开、独立来源和编辑关系。
- 不同台阶数量、名字、尺寸与槽位置；同一轴多个不重叠 recess；修改声明顺序不改变几何与来源意义。
- 单槽不存在时的无槽旋转件，以及有内孔但槽底仍保留正壁厚的有限变体。
- 将槽落在另一基础区段，验证 host 选择不依赖 middle 等固定 key 名称。
- C2/C3/C4 新 provider 契约的正确编译与 source/role 保持；仅实际受影响路径需要新增测试。

负向至少覆盖：

- 外槽误放 inner；要求实心却有内部空腔；遗漏 external recess。
- 错误宿主、槽超出范围、跨台阶未资格化、多个槽重叠、错误 floor/depth、壁厚为零或负。
- 符号未知/重复、错误单位/来源、默认洗白、同值独立源替换、循环或虚构依赖。
- 修改源参数破坏槽深/其他独立区段，或 primary 成为 derived target。
- 新旧版本错误分派、modifier/source map/expanded IR/approval 漂移与 payload 超限。

几何 expected 独立从测试声明或手算关系获得。不能拿 compiler 输出的五段结果重新计算后就声称证明了语义正确。

### 8.2 历史错误回归

- 旧 T4B 三次 C1 原始响应与结果保留，v1 host、v2 内空腔及 v3 overlap 都不升格 PASS。
- 历史 live_v2 的结构合法但任务错误案例，另构造保持同一错误空腔含义的新版本 draft，在新的 typed requirement 准入下应在 preview 前明确拒绝。此负向必须命中外槽/实心结构检查，不能只命中 draft 版本不相容。
- v3 旧 outer 追加区间不得被自动当成新 ExternalRecesses。只有 model 使用新契约明确声明 modifier，才允许确定性拆分。
- 保留旧包正确恢复和旧批准拒绝行为；相容性用真实 v1 provenance 和关键旧包核验，不要求重做所有旧原生矩阵。

### 8.3 新真实规划开发冻结

纯验证稳定后，冻结新的 T4C 日程：C1–C4 每族一次实际 provider 调用，无自动重试；每个计划同时记录 `plannerAccepted`、`taskGeometryPassed`、`editabilityPassed` 与后续 native 证据。

这样既补齐 C1，也确认 provider/version/Schema 改动没有破坏已有三族真实规划。旧 T4B C2/C3/C4 的成功可作为回归依据，但不能掩盖新接口上的失败。

记录 request、原始 draft、compiler/schema/rule/capability 版本、expanded IR、source map、批准哈希、实际 model、调用/token/耗时及所有拒绝。`deepseek-chat` alias 与实际返回型号分别记录。

如果出现新错误，执行 agent 应分析剩余原因并继续完成有依据的实现/纯验证修复；在已有适用授权内用新的有限开发冻结验证。不能只记录第一个错误就停止全部可推进的实现工作，也不能重复抽样直到凑出一次成功。每轮事先固定范围和次数，版本与历史失败保留；没有新诊断或超出适用授权时停止对应外部运行并如实交接。

至少增加一个不同尺寸/布局的 C1 开发任务作为泛化检查，可优先纯验证；若决定加入真实调用，必须在该轮执行前列入冻结日程，不能失败后临时挑更容易的任务。

### 8.4 C1 真实计划的公开原生完整链

只有 task/role/preflight 全部通过的实际模型响应可用于该链：

1. 保存 live raw bytes 和 approved draft/program 标识；新 native context 重新验证同一真实响应，不重新生成 candidate 答案。
2. 经公开 preview/一次批准创建、发布、关闭 controller A。
3. controller B 冷恢复该真实产物；从 accepted edit target 解析精确参数，执行中段半径 12→13，独立验证槽底 10→11、槽深 2、其他区段不变并发布。
4. controller C 冷读最终模型、native definitions、driver/references、program/state/revision/provenance；核验初始 Given 未改，后续 override 独立记录。

原生 Oracle 至少独立测量：所有轴向边界和圆柱半径、外槽的实际凹入边界、无内部空腔、世界轴线/包络、体积及编辑不变量。体积为初始 `11704π`、编辑后 `12688π` mm³，仅作辅助，不能代替区域/拓扑检查。

复用 T2/T3 的官方 API 测量和生命周期设施；不让新 compiler/source map 生成自己的 oracle expected。candidate 原生成功仍是 candidate，不换标签。

## 9. T4 COMPLETE 的最终条件

执行 agent 必须逐项填表并提供直接证据：

| 条件 | 必需证据 |
|---|---|
| 通用 modifier 展开及任务结构准入实现 | 公开生产路径、独立纯正负向、source map 与角色验证 |
| 四族新真实完整计划满足任务 | 新冻结 C1–C4 raw response、结构/来源/几何/编辑资格结果；不是只看 preview |
| C1 真实径向计划完整原生链 | 实际模型响应 → 公开创建 → 指定编辑 → 多控制器恢复与独立 Oracle |
| C3 跨方向/圆角关键原生链 | 相容的 T4B 实际模型链证据；共享代码有风险变更时补针对性验证 |
| C2/C4 生产接入映射 | 新编译产物与已验证 subtype/driver/material/provenance 的明确映射；不把旧证据说成新完整原生执行 |
| 旧契约/包兼容与来源完整 | v1 replay/关键恢复、hash/version/payload 拒绝，以及必要 legacy/external 回归 |
| 无零件模板或安全门控放宽 | 生产差异审查；无 fixture ID/尺寸分支，无新专用 Tool |
| 生命周期与证据保护 | 连续账本、原件 hash、owned 清理、原活动文档恢复、recovery disposition |

如果 C2/C4 的映射无法证明新路径相容，补最小公开创建/编辑/冷恢复验证，不能仅靠文字说明跨过缺口。不强制四族重复所有旧原生矩阵。

全部满足后，可在新报告明确写 **T4C COMPLETE；T4 COMPLETE**，并注明真实开发通过不构成 M20 成功率证明。

若实际模型仍未满足某族任务，或公开原生链没有通过，则如实报告具体未满足项，不改变门槛、不改标签。方案不预先保证模型输出，但要求实现责任和证据责任都处理完整。

## 10. 资源、交付与执行指令

遵守 PRD 的 owned 副本、STA、单位、响应性/GDI 保护和原件保护。沿用 T4 连续账本，不能重置预算；开始每轮实际原生/LLM 操作前核对已有授权适用范围，已经覆盖的操作无需重复询问。

新证据使用 `artifacts/m20-preparation/T4C/<run-id>/`；新增报告 `docs/m20-preparation-T4C-verification.md`，内容包括实现边界、所有真实调用、独立 Oracle、兼容性、未执行项及最终 T4 矩阵。

保留 T4B 未提交工作树并在其上接续，先核对来源和实际加载 DLL；不 reset/覆盖前项源码，不修改旧报告或旧失败统计。提交/推送按用户的 Git 指令处理。

本方案交付不运行任何测试或 provider。后续执行任务默认在完成 T4 审计后停止，不启动 T5/正式 M20。

可直接交给执行 agent：

> 按 `docs/m20-preparation-T4C-completion-plan.md` 完整执行 T4C。接续当前 T4B 工作树，实现 versioned external-recess 语义、确定性区段拆分及任务结构准入，保持 v1 包和旧严格路径相容。完成针对性纯验证、新冻结四族真实规划，以及 C1 真实响应经公开入口创建、指定编辑和跨控制器冷恢复。复核旧有效证据的相容性，补无法证明的关键项，输出完整 T4 完成矩阵与 T4C 报告。不要用 candidate、preview-only、来源覆盖或旧响应修补代替真实任务通过；保留所有历史失败。遇到错误先定位并推进有依据的修复，在适用授权内完成有限新验证，不重复抽样凑成功。全部完成条件满足才标 T4 COMPLETE，不进入 T5 或正式 M20。
