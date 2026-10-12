# T4 PARTIAL 复核与修复方案

日期：2026-10-12（Asia/Taipei）  
审查 HEAD：`fcddf2d649e238247d6b0a39f6b746bb62ce74b6`。审查开始时仅 T4A 诊断报告未跟踪。  
性质：独立代码及原始证据复核、T4B 建议；不是实现报告或新的验收结果。

## 1. 结论

**T4 保持 PARTIAL 合理。** 已证明严格计划能进入原生生产链路，并保存来源、编辑及冷恢复；尚未证明配置中的真实模型能产生四类满足几何、来源和后续编辑要求的完整计划。

两轮共八次真实请求均完成，但全部在原生修改前拒绝。主要问题同时来自模型的错误选择、provider 契约的冗余表示责任，以及后续编辑要求未结构化进入规划输入。不能将这些全部归为模型能力不足，也不能用确定性成功覆盖 live 失败。

本轮读取两份报告、Planning/IR/Runtime/Provenance/Session 与测试输入代码，并对八份原始 JSON 做离线字段及有向面积检查。没有重新运行 Planner、测试、provider 或 SOLIDWORKS，没有改生产代码或旧证据。

依据：

- [T4 验证报告](D:/CAD-Harness0.2/docs/m20-preparation-T4-verification.md)。
- [T4A 诊断报告](D:/CAD-Harness0.2/docs/m20-preparation-T4A-diagnosis.md)。
- [v1 结果](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/result.json)、[v2 结果](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/result.json)及各槽原始 request/response。

## 2. 已核实的直接原因

| 家族 | 首轮首个拒绝 | 第二轮首个拒绝 | 首个拒绝之外的重要问题 |
|---|---|---|---|
| C1 | 外轮廓顺时针，有向面积 -960 mm² | 含槽轮廓仍顺时针，面积 -952 mm² | 首轮虚构 head→middle→tip 驱动联动并拆出不支持的后续切除；第二轮仍有错误 binding target 和漏 station |
| C2 | 台阶少拐点，出现斜边 | offset relation 不满足 target=source−offset | 虚构独立孔径/外径之间的联动；第二孔变成 derived driver；半径与偏移来源混淆，frame 法向反向 |
| C3 | 七组重复 geometry binding tuple | world.xz frame 法向与该域定义相反 | 初始几何组成没有保证高度对应规定的 AdditiveBossDepth；还存在包络、坐标及来源矛盾 |
| C4 | 草图 Z=8 却仍绑定 world.xy，无 datum | 同样缺 datum | 局部 Y 槽超出当前局部 X 表达域；第二轮漏消费显式 90°方向来源 |

离线核对还确认：v1 C1/C2/C4 有不存在的 binding target；v2 C1 仍把 operation.id 当 semanticId；两轮 C4 均没有 datum operation。上述错误不是只靠修正绕向或补一个 datum 就能全部解决。

八次实际 model 均记录为 `deepseek-flash`，请求 alias 为 `deepseek-chat`。原结果没有 HTTP、超时或输出截断失败；输入/输出预算不能解释这些已完成但不合法的计划。也没有足够证据比较其他模型的表现。

## 3. 代码层面的根因

### 3.1 provider 边界让模型重复生成表示与证明

[ConstructionPlanner.cs:79](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionPlanner.cs:79) 直接从生产 `ConstructionPlanResponse` 构造模型 Schema。

模型同时承担：operation.id/semanticId、点数组/实体点引用/loop、driver 数值/点-driver 映射/relations、frame 三轴/origin/offset、requirements 回显、逐 metric binding 和派生引用后缀。

这些字段在生产 IR 中有用途，但很多能由相同语义唯一展开。让模型分别生成它们，就增加了互相矛盾、漏项和命名错配的机会。v2 改善了部分目标名、重复 binding 和台阶形状，却没有消除这项责任。

八次样本支持采用编译层的工程判断，不证明提示词长度或 Schema 大小是统计意义上的唯一失败原因。

### 3.2 后续编辑资格未传入请求，也未由计划准入强制验证

[ConstructionPlanRequest](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs:13) 只有 `Input/Rules`。原 fixture 有 `primaryVariable/dependentRelation/postCreationEdit`，但 [T4Cases.All](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4Cases.cs:94) 只读取 intent，再构造尺寸与推导规则。

当前 `VerifyGeometry` 检查数值、来源覆盖和布局，却没有按通用角色核验“这个源尺寸必须对应哪个合格且独立的 native driver”。因此即使未来生成几何正确的另一种历史，也可能直到后续编辑时才暴露不合格。

例如把立板高度放进固定草图，让拉伸厚度成为 AdditiveBossDepth，可能表达初始实体，但无法执行已规定的高度 40→44 编辑。这个限制应成为输入与准入条件，不能等 runner 碰巧选对 feature 名称。

### 3.3 数值推导图与 native 设计依赖是不同的图

`diameter→radius` 是同一尺寸的单位/表示换算；两个独立半径恰好相差 2，不表示未来必须保持差值 2。

[RadialProfilePolicy.cs:58](D:/CAD-Harness0.2/src/CadHarness.Ir/RadialProfilePolicy.cs:58) 将 relation 定义为 target=source−offset；[GenericFeatureConstruction.cs:135](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs:135) 将 derived target 标为非独立。

C2 的问题不仅是关系箭头反了。即使反转后数值成立，未声明的联动仍改变编辑含义。必须明确分开 value derivation 与 design dependency，独立 Given 不能凭数值相减建立 native relation。

### 3.4 空间信息披露与生成职责不够明确

[Runtime initialReferences](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/SolidWorksConstructionPlanningRuntime.cs:37) 只有 ID/type，缺少 canonical origin/basis/normal；完整定义藏在 [ConstructionPreflight.cs:40](D:/CAD-Harness0.2/src/CadHarness.Ir/ConstructionPreflight.cs:40) 和 frame 校验代码。

当前域的右手 canonical basis 可明确投影为：

| 平面 | local X | local Y | normal |
|---|---|---|---|
| world.xy | +X | +Y | +Z |
| world.xz | +X | −Z | +Y |
| world.yz | +Y | +Z | +X |

尤其 world.xz 的传统 `(X,Z,−Y)` 虽是右手 frame，却不属于当前正法向资格域。不能把没有明确披露完整 basis 的问题全归罪于模型，也不能因此放宽原生平面验证。

datum/frame、世界方向/局部端点的重复表达可以由有界算法产生；需要证明世界几何、宿主、右手性和加工方向保持。

### 3.5 数值相等与来源覆盖不足以表达全部语义

[VerifyGeometry](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs:170) 保留必要的数值、单位和来源检查，但一个 mm 标量本身无法区分 min/max、world center、local center、native depth 或 offset。

新边界应有明确的坐标/值角色及编辑角色。编译器保留源引用生成 binding，不能按“数值一样”反查哪个 key 可以资助该字段。

## 4. 推荐 T4B：小型语义计划与有界编译

保留现有生产 IR、Binder、Handler、材料域、Transaction、durable Store、Validator。新增范围放在 Planning 边界及必要的批准/provenance 接入。

```text
归一化需求 + 有限规则 + 编辑角色/设计不变量 + 真实能力
  → 一次完整 LLM 语义计划
  → 严格语义检查 + 确定性展开
  → 现有 ConstructionProgram + 完整 bindings/source map
  → 现有几何/材料/资格预检 + 编辑适用性检查
  → 完整预览与一次性批准
  → 现有生产执行、发布、编辑及冷恢复
```

### 4.1 输入补齐

- 增加版本化、可选的 `EditabilityRequirements`：source key、已资格化参数类型、必要的独立性、目标几何角色和保持的不变量。普通旧请求继续分派到兼容路径；有规定编辑的任务必须传递。
- 例如 C1 中段源尺寸对应独立径向 driver，槽深保持；C2 第二孔独立可编辑，其他内外径不联动；C3 立板附加高度对应 AdditiveBossDepth；C4 左槽深度对应 CutDepth。
- 此处描述任务要求，不指定标准 feature tree、不填写生产参数 ID、不在建模时自动执行未来编辑。
- 投影 canonical planes、native profile/driver 条件、材料适用域与数量上限。

### 4.2 provider DTO 降冗余

- 新增 versioned semantic draft；建议使用一个稳定逻辑 ID，值使用 accepted source refs。
- primitive 为矩形、圆、capsule、正交径向区段/边界；操作组合仍由模型选择，包含 host、位置/方向、实体操作、显式设计关系和有限边处理。
- 不要求模型再次写完整 immutable requirements、native 后缀、frame 三轴、点/driver/loop 多套映射和逐项低层 binding。
- `planned/needs_clarification/unsupported` 仍有严格 envelope；缺少关键输入不由 compiler 填补。
- 生产 `ConstructionPlanResponse` 继续作为展开后预览与执行契约。旧 direct IR 路径严格保留，不把旧失败输出自动转换成新格式。

### 4.3 确定性展开的范围

| 语义输入 | compiler 的机械工作 | 必须拒绝的情况 |
|---|---|---|
| 明确的正交外/内径区段 | 合并站位、生成台阶角、闭合 CCW 半剖面、driver/point map | 斜边意图、区段冲突/空缺、不正壁厚、无法唯一确定区域 |
| 已给 capsule 宽长/世界中心/方向 | 生成 local-X 端点及端半径、canonical frame | 未给关键尺寸、方向冲突、非法长度/宽度 |
| 有来源的平面/位置/方向 | 显式 datum、plane ownership、坐标变换与 sweep direction | 不能保持世界几何或落在未支持宿主/材料域 |
| 唯一逻辑 symbol | 分配 IR ID/semanticId、typed outputs 和引用 | 重复/未知/前向/错误类型的依赖 |
| source refs 与有限派生链 | 完整且唯一的 binding 与 source map | 未消费必要输入、按数值猜来源、默认尺寸洗白 |
| 已声明设计关系 | native driver dependency | 凭半径差或同值创造未声明联动 |

中间坐标计算可属于 primitive 的固定展开规则，例如 capsule 的 `(length−width)/2`。每步仍记录输入及规则版本，不偷偷改写原 accepted Given，也不要求模型手填这些中间值。

不能把 C2 旧斜边插角后判成功，不能反转旧 relation 来掩盖设计错误，不能只改一个法向符号；新的语义计划按明确含义展开，并重新通过旧严格预检。

### 4.4 编辑适用性与来源发布

- 将 source role 映射到展开后的具体 parameter identity；验证 owner、独立 driver、qualified accessor 与保持关系。
- 对任务已给的后续编辑，用现有有限编辑/几何证明做 mutation 前适用性检查；这不能替代真实原生编辑和引用验证。
- 批准 hash 关联 normalized input、raw draft、compiler/schema/rule/capability 版本或指纹、expanded program/bindings 和当前 context/revision。
- 扩展 [GenericPlanningProvenance.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPlanningProvenance.cs)，明确区分旧 direct-plan 与新 compiled-plan。回放验证 source map、展开结果、历史 override 和权威 revision。
- 不改写历史 Given，不复用旧批准，不另造 Native executor 或事务。先量化来源 payload，遵守现有 65,536 边界，不静默扩容或丢失批准依据。

## 5. 验证安排及完成条件

1. **纯验证先行**：C1–C4 语义计划、不同区段数量/尺寸/名字、多个合法主平面与方向；独立 expected 几何/关系，不用 compiler 自己产生的输出证明自己。
2. **关键负向**：未知 symbol/source、角色错配、伪造联动、derived target 充当独立编辑目标、区段重叠/空缺、缺关键尺寸、错误坐标空间、材料不支持、来源/版本/hash 漂移和 payload 超限。
3. **保留旧错误拒绝**：原八份 raw IR 只读回归；保留旧 live 0/8，不称为 compiler 修正后的成功。
4. **新 live 开发冻结**：纯验证稳定后每族一次，固定 provider 设置、调用数、raw draft、compiler 版本及源代码。四族任一仍有明确规划阻塞，T4 继续 PARTIAL；修复后新日程，不能覆盖失败或统计挑选。
5. **公开原生接入**：对新编译且通过的真实计划做必要的公开批准创建、指定编辑、独立 Oracle、来源发布及跨控制器恢复。至少径向关系与跨方向/圆角两条关键链，其他家族列明与旧有效原生证据的相容映射。
6. **单独交付报告**：记录能力、未执行项、计数与限制；新补齐结果不能计入正式 M20 的 4/4 或 8/12。

只有真实四族规划证据、展开/来源正确性、编辑适用性及公开接入条件满足后，才可将 T4 改为 COMPLETE 并进入 T5。编译器单测通过不等于模型规划通过；结构化输入识别准确也不等于已具备任意自然语言工程设计能力。

## 6. 不建议的处理

- 继续仅加长 prompt 或无限重试；提示/Schema 修正应保留，但不是唯一方案。
- 放宽绕向、法向、正交、driver、来源或材料检查来让旧响应通过。
- 编码四个 fixture 标准树、按 case 名称选择编译结果，或按数值猜修正意图。
- 在确定性 primitive compiler 中接管整件零件的语义规划，制造“真实模型成功”。
- 直接归因某模型不具备 CAD 能力或保证换模型解决；现有数据只能证明当前模型与当前边界组合的八次开发失败。

有限编译是把重复展开责任移入 Harness，原生可靠性架构保持。LLM 仍负责组件/特征组合与设计关系选择；若新的语义计划仍选错，这些错误应继续拒绝并如实计入结果。
