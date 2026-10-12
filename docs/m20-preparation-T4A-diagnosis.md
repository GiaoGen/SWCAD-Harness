# T4A — LLM Planning Failure Diagnosis

日期：2026-10-12（Asia/Taipei）。HEAD `fcddf2d649e238247d6b0a39f6b746bb62ce74b6`；执行前工作树干净。

**结论：MIXED。建议“小范围 Prompt/Schema/输入修正 + 有界确定性编译”，不建议继续只加长提示词。** 本轮仅离线读取、JSON 字段对照和算术检查，没有调用 Planner/LLM、运行测试、连接 SOLIDWORKS、修改生产代码或重新执行失败槽。T4 仍为 PARTIAL，不启动 T4B/T5/M20。

## 1. 证据与判断方法

依据 [T4 报告](D:/CAD-Harness0.2/docs/m20-preparation-T4-verification.md)、[v1 冻结](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/freeze.json)、[v2 冻结](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/freeze.json)、各槽的原始请求/响应/拒绝及基线源码。下文的“首个拒绝”是已经执行并记录的事实；“遮住的问题”是原 JSON 与源码的静态对照，不声称执行过新的 Validator 或得到了新的拒绝结果。

- 八次请求 alias 均为 `deepseek-chat`，实际返回均为 `deepseek-flash`。Provider `status=completed`、`incomplete_details=null`；输出 2,487–6,180 tokens，均低于 16,384。没有 HTTP/超时/截断失败证据，也不能据此断言另一模型一定成功。
- 八份返回 requirements 与请求中的 immutable record 逐字段一致。问题不是模型修改了原始输入，而是将这些输入映射到几何/身份/依赖时出错。
- 两轮 operation Schema 相同，7 branches，21,452 UTF-8 bytes，SHA256 `d61b2efb2edcfb370d47fa8986414c055bfdf759b715b21f6f7d40194ba1a69f`。改变的是提示词/规则披露，不是扩大支持范围。
- 所有记录都是 mutation 前拒绝。这八次不是 SOLIDWORKS Setter、Save、事务或冷恢复失败；T4 的确定性公开原生成功链不能代替真实模型规划成功。
- 根因标签可以并存：`LLM_ERROR` 是违反已披露规则/任务；`CONTRACT_DESIGN` 是信息遗漏、表达矛盾或冗余生成责任；`DETERMINISTIC_WORK` 是可由有界算法从明确语义和来源唯一求出的表示。标签不是错误率统计；八次不足以证明“提示太长”就是失败的统计原因。

## 2. 八次失败

### 1. C1 / live_v1

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/shaft/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/shaft/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/shaft/result.json)。

**首个拒绝：`INVALID_SKETCH`，外轮廓不是 CCW。** `shaft_profile` 从 `(0,0)→(0,15)→…→(80,0)`，有向面积 **-960 mm²**；另一个 groove 矩形面积 **-8 mm²**。[绕向检查](D:/CAD-Harness0.2/src/CadHarness.Ir/PlanarSketchPreflight.cs:101) 拒绝正确。

**遮住的问题：** 环槽被拆成旋转后的普通 ExtrudedCut；[材料域](D:/CAD-Harness0.2/src/CadHarness.Ir/FeatureConstructionPreflight.cs:26) 不支持该旋转/棱柱混合历史，普通切除也不能当成环槽。`shaft_revolve/groove_cut` binding 使用 operation.id 而非 `shaft_body/groove_feature` semanticId；漏 p1/p3/p5/p7 的 station。新增 `head→middle offset=3`、`middle→tip offset=3`，不来自声明的设计关系；中段因此成为 derived target，不能作为规定的独立编辑 driver。两个 radial_offset 又错误绑定半径15/12，而非偏移3。

**归因：** `LLM_ERROR`：绕向、混合历史、目标名及来源错误；`CONTRACT_DESIGN`：v1 同时说 radial 不用 bounds center，却又泛称所有 sketches 要 center，且未披露规则输入 DAG；`DETERMINISTIC_WORK`：从明确径向区间生成正交半剖面、绕向、点/driver 身份和全部站位绑定。不能仅反转 polyline 就宣称此响应已正确。

### 2. C2 / live_v1

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/sleeve/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/sleeve/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/sleeve/result.json)。

**首个拒绝：`V03_CONTRACT_INVALID`，radial 边非正交。** `p_bore_step=(20,10)` 直接连 `p_bore_end=(36,12)`，ΔX=16、ΔY=2，缺 `(20,12)` 台阶角；[正交检查](D:/CAD-Harness0.2/src/CadHarness.Ir/RadialProfilePolicy.cs:43) 不应放宽。

**遮住的问题：** 每个 radial_driver_radius 都被 diameter/radius 两个 key 重复绑定；直径24不能直接证明半径12。漏五个 station、angle 绑定不存在的 `revolve_flanged_sleeve`。frame 使用 world.xz 的 -Y 法向，而现有域要求 +Y。关系 `large=small-2` 实际要求12=8，另两个同样倒向；这些独立 Given 半径本来也不应凭数值差被联动。

**归因：** `LLM_ERROR`：明确正交条件、减法方向和数值映射不满足；`CONTRACT_DESIGN`：要求模型重复写点、driver、point-driver map、requirements、bindings，且 v1 缺 DAG/唯一 tuple 说明；`DETERMINISTIC_WORK`：区间转台阶、半径换算和逐段证明。不能给任意斜边自动插点，因为可能改变原意；只允许新的 typed 正交区间表达按定义展开。

### 3. C3 / live_v1

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/bracket/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/bracket/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/bracket/result.json)。

**首个拒绝：`V03_CONTRACT_INVALID`，7 组重复 geometry tuple。** base centerX 两项、centerY 三项、width 三项；三个孔的 radius 各两项；upright height 两项。[唯一性检查](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs:74) 是必要的一值一来源证明。

**遮住的问题：** `base_min_x=0` 被拿去证明实际 centerX=30，`min_y=-20` 证明 centerY=0，Ø6 直接证明 R3，均是数值矛盾；23 项 placement metric 无精确 binding。立板使用 YZ 上 `y=-20..20,z=8..48` 矩形、沿 X 拉伸8：从参数布局看能表达所需立板区域，但高度40在固定坐标草图中，native AdditiveBossDepth 是8，不是规定的40→44。

**归因：** `CONTRACT_DESIGN`：v1 未明说 tuple 唯一、没给输入 DAG，反而要求所有 Given 被消费；请求也漏主变量/后续编辑资格。`LLM_ERROR`：以上实际数值错配，不能用信息缺口免责。`DETERMINISTIC_WORK`：根据源字段生成完整唯一 source map、圆直径/半径双几何证明和 placement。删除重复 binding 或选择“看上去对”的同值 key 不足以修复。

### 4. C4 / live_v1

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/slots/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/slots/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v1/slots/result.json)。

**首个拒绝：`SPATIAL_FRAME_MISMATCH`。** 三个 slot sketch 的 `plane=world.xy`、origin Z=8、offset=0，且没有 datum operation；距父平面8 mm。[平面所属检查](D:/CAD-Harness0.2/src/CadHarness.Ir/PlanarSketchPreflight.cs:266) 必须保留。

**遮住的问题：** 单位 frame 中 slot 两端沿局部 Y，ΔX=0、ΔY=14；世界 Y 意图正确，但违反 Planner 只接受局部 X slot 的表达域。大量 placement binding 使用 operation.id（如 `plate_sketch/slot0_sketch`），不是 semanticId；base depth 也是该问题。不能单独补 datum 就算通过。

**归因：** `LLM_ERROR`：capability 已要求 tracked datum/offset=0；`CONTRACT_DESIGN`：同一放置要求 origin、frame 和 offset 双重表达，又要求模型手动创建其宿主；`DETERMINISTIC_WORK`：由明确 top plane/世界中心/方向生成 datum、局部 capsule 和旋转 frame，再证明 world transform 不变。

### 5. C1 / live_v2

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/shaft/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/shaft/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/shaft/result.json)。

**首个拒绝仍为 `INVALID_SKETCH`。** 槽已整合进12点半剖面，但有向面积 **-952 mm²**；v2 明说“下边界左→右，外边界右→左”，仍未遵守。

**遮住的问题：** `shaft_sketch` 五项 placement、`shaft_revolve` angle 指向 operation.id，而 semanticId 实际为 `shaft_profile/shaft_body`；漏 p1/p3/p5/p7/p9/p11 六个 station。加一个 overall span 不能代替每个精确点的 station。槽的 middle→groove offset2 本身正确。

**归因：** `LLM_ERROR` 明确；`CONTRACT_DESIGN` 的两套名字与重复 station 枚举责任仍在；`DETERMINISTIC_WORK` 可消除绕向、名称后缀与逐点账单。v2 说明已改善语义选择，但不能据此把失败升格 PASS。

### 6. C2 / live_v2

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/sleeve/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/sleeve/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/sleeve/result.json)。

**首个拒绝：`V03_CONTRACT_INVALID`，offset relation 不符实际半径。** `small10→large12 offset2` 要求12=10−2；`large12→outside16 offset4` 要求16=12−4；`outside16→flange24 offset8` 要求24=16−8。[关系公式](D:/CAD-Harness0.2/src/CadHarness.Ir/RadialProfilePolicy.cs:58) 与冻结 capability 都明确是 target=source−offset。

**遮住的问题：** radial_offset 的 source key 选的是 large/outside/flange 半径12/16/24，不是偏移2/4/8；法向仍 -Y。这些独立直径之间没有声明恒差关系，不能反转 relation 的箭头来“修正”：那会联动本应不变的孔段/外径。large bore 被设为 derived target，也不能完成规定的独立半径编辑。

**归因：** `LLM_ERROR`：明确的关系公式和源量错用；`CONTRACT_DESIGN`：当前数值派生与 native 驱动依赖都是模型要重复描述的图，容易混淆，但没有证据表明 Schema 强迫关系非空；`DETERMINISTIC_WORK`：仅从已声明设计关系建立 native offsets，普通 diameter→radius 不意味着两个 native drivers 应互相依赖。本轮台阶拐点、全部 station、目标拼写已改善。

### 7. C3 / live_v2

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/bracket/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/bracket/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/bracket/result.json)。

**首个拒绝：`SPATIAL_FRAME_MISMATCH`，world.xz normal 反向。** `sk_upright` 的 X=(1,0,0)、Y=(0,0,1)、Z=(0,-1,0) 是右手 frame，却与 [该域 world.xz 的 +Y 定义](D:/CAD-Harness0.2/src/CadHarness.Ir/ConstructionPreflight.cs:40) 点积-1，不满足 [法向检查](D:/CAD-Harness0.2/src/CadHarness.Ir/PlanarSketchPreflight.cs:265)。这不是 SOLIDWORKS API 报错。

**遮住的问题：** 从所写 frame/direction/depth20 推算，立板沿 Y 只覆盖 -20..0，不是 -20..20。base originY=0 却绑定 min_y=-20；upright 的 profile centerY=28 却绑定 center_y=0。leg_height Given40没有被正确消费；AdditiveBossDepth20也不是规定的40。即使绕过 normal，仍不能完成任务。

**归因：** `LLM_ERROR`：正主轴规则、包络和数值映射未遵守；`CONTRACT_DESIGN`：initialReferences 只有名称/type，无 canonical basis/normal，且未传 required-edit role；`DETERMINISTIC_WORK`：canonical plane basis、world/local 转换、datum 所属和 driver role 校验。不能只翻 Z 向量：原 frame 的右手性、实体位置、切除方向必须一起得到证明。

### 8. C4 / live_v2

证据：[请求](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/slots/call-01-request.json)、[响应](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/slots/call-01-response.json)、[拒绝](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/live_v2/slots/result.json)。

**首个拒绝仍为 `SPATIAL_FRAME_MISMATCH`。** origin=(-30/0/30,0,8)、world.xy、offset0，无 datum；v2 已给出明确 datum 说明，仍忽略。

**遮住的问题：** 三个 slot 仍用局部 Y 两端 `(0,-7)/(0,7)` 配单位 frame；binding 使用 zero_angle=0，未消费明确 Given `slot_angle=90`。应由局部 X capsule + 90° frame 表达同一世界 Y 几何。目标名字和 end-radius 来源本轮已改善。

**归因：** `LLM_ERROR` 明确；`CONTRACT_DESIGN`：模型仍须为同一方向写端点、三轴、angle binding 和 datum 历史；`DETERMINISTIC_WORK`：端点间距 length−width=14、端半径 width/2=3、canonical orientation、宿主平面及 source map。缺 plane ownership 的 frame 不能作为合法表示接受。

## 3. 跨案例契约问题

| 问题 | 证据及最小处理 |
|---|---|
| v1 的矛盾/未披露信息 | 原请求一边豁免 radial bounds，一边说所有 sketches 要 center；没有 derivation input DAG、唯一 tuple 明文。v2 已修正，重复 binding 也减少；不能据 v2 的改善声称四族已经通过。 |
| 把表示和证明都交给模型重复生成 | Schema 要两个名称、点数组+实体点 ID+loop membership、driver 半径+point-driver map+relations、12个 frame 数值+offset、requirements 回显及逐 metric binding。C1/C4 的 binding 直接用了另一个合法名字（operation.id）；多个数值表彼此矛盾。生产 IR 可以保留，provider 边界不必承担全部重复字段。 |
| Schema 只约束形状，未表达全部适用域 | Slot 两个 endpoint，不能结构上保证 local X；frame 是自由数字，不能保证选中 datum；radial version 未限制1、drivers/relations maxItems4096而 native policy最多24；entity polyline与radial/null没有完整联动。是应收紧的投影，不代表八次都在这些 limits 上失败。独立关系、来源、材料依然必须由 Validator 检查，不能企图用 JSON Schema 代替。 |
| 独立尺寸与 native dependency 混淆 | C1 v1/C2两轮自行建立尺寸恒差；正确算出两个半径的差并不授权编辑联动。[native 参数读取](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs:135) 将 derived target 标为不独立。numeric Given/Derived DAG 与设计关联必须分开。 |
| 后续编辑资格没有成为 Planner 输入 | [冻结任务](D:/CAD-Harness0.2/tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json) 有 primaryVariable/dependentRelation/postCreationEdit；[T4Cases.All](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4Cases.cs:94) 仅取 intent，再构造数字 requirements/Rules；[ConstructionPlanRequest](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs:13) 只有 Input/Rules。实际没有 required native driver role；capability 提到 scalar separate，但不把这些要求绑定到 source key。C3 v1 说明几何正确不等于后续编辑合格。应传通用“哪个尺寸须有哪个已资格化 driver/哪些不变量须保持”，不能传测试专用 tree spelling。 |
| Source key 的语义职责不够明确 | min/max、center、world origin、局部 center、offset都用 mm 标量；C3 把 min_y 用作 originY，C2把半径用作差。单位相同不等于含义相同。新的语义 draft 应有 typed coordinate/value-source roles，lowering生成正确 metric，原逐项数值/source验证保持。 |

保持必要安全条件：唯一身份、正交/非自交/正壁厚、固定原生宿主、driver独立性、未知来源拒绝、材料证明、qualified subtype、fresh native preflight、原件保护及事务恢复。没有发现放宽这些条件能解决任务的证据。也没有证据说明默认圆角规则、事务引擎或 provider 网络是八次首要故障。

## 4. 最小修复清单

| 优先级 | T4B 应做什么 | 明确不做什么 |
|---|---|---|
| P0 | 归一化输入携带 required-edit source/role、允许的已资格化参数及必要不变量；投影真实 plane basis、scalar/profile适用域；保留v2已修正的规则披露 | 不根据 C3 名字强制“leg”树；不把40恰好等于另一个 span 当作可替代身份 |
| P0 | 引入小型 versioned semantic draft，值来自 accepted requirement refs；图节点只需一个稳定逻辑 ID，明确 host、profile语义、方向和设计关系 | 不让模型提交代码/表达式；不引入 CAD Tool per family；不把 free-text 输入全交给模板匹配 |
| P0 | 确定性 lowering：正交径向区间/边界→一个带driver的half-section；矩形/圆/capsule→现有 sketch；world位置/方向→已支持 datum+frame；symbol→typed outputs；源字段→完整唯一 binding | 不把“错误 raw IR”自动修好；不凭数字差增加依赖；不填 missing critical input |
| P0 | 编译后验证 required driver/owner/独立性及编辑不变量的表示条件，再跑原 strict IR/requirements/material preflight | 不新增固定 polyline station/草图高度等 Scalar 资格；不以几何等价忽略可编辑性 |
| P1 | Provider Schema 按语义 primitive 分支收紧，runtime枚举可用 source/已知symbols；避免 requirements整份回显、14-field null填表和重复双名称，最终预览仍有原严格记录及程序 | 不删除生产 validator 的 source coverage、版本、引用、边界/数量上限 |
| P1 | 记录原请求、raw draft、compiler/rule版本、expanded IR、source map、批准hash；接原 Generic provenance/persistence/replay | 不另写事务或Native执行器，不丢历史，不顺手增大65,536 provenance边界 |

## 5. 有界编译层是否必要

**建议需要，但不是通用 CAD 编译器，更不是失败响应修补器。** 只用 Prompt/Schema 可修信息遗漏，却无法从结构上消除“一个 station 两个顶点、同一位置 world/local两种坐标、一个driver三处半径、一份 source多套名字”的互相不一致。v2 已披露这些规则却仍0/4，支持把机械展开交给程序的工程选择；并不证明编译层实施后模型必定4/4。

推荐流程：

```text
用户意图 + trusted normalized requirements/rules + editable roles + capability
  -> 一次完整 LLM semantic draft（不执行 CAD）
  -> strict draft validation + bounded deterministic lowering
  -> 现有 ConstructionProgram + requirements bindings + source map
  -> 现有 IR/几何/材料/资格 preflight + 完整预览/明确批准
  -> 现有 Binder/Handler/Transaction/ManagedRevisionStore + 独立 Oracle/冷恢复
```

LLM 仍负责需求与几何组成的语义选择、明确关联和完整操作意图，不负责数值换算或 native 字符串后缀。Compiler只允许已有矩形/单圆/capsule、正交径向半剖面、principal plane/datum、已资格化扫掠及terminal convex fillet的组合；不按shaft/sleeve/bracket、fixture ID、known dimensions分派。范围/数量/单位和源规则有限，不执行任意表达式、代码或工程尺寸推断。

关键边界：

- contour生成必须依据已声明区间/拓扑；不为任意斜边插一个“猜测正确”的角。绕向属于明确外边界的表示归一化，不能借机改变实体区域或反转设计含义。
- frame/datum lowering必须保持 world几何、右手性、plane ownership、加工方向和 driver语义；不只把向量符号翻过来。不能证明唯一表示/正确依赖时拒绝。
- bindings从编译时携带的 source refs生成；禁止按值相等反查 key，不能丢 Given、换source、默认洗成Derived或给未声明关系找数值理由。
- 旧 direct IR路径仍严格：本次8份输出保留原FAIL，不作为 compiler“修正后PASS”。新的draft是新版本/新证据，不是对旧批准或旧 formal槽的隐式repair。
- 公开审批hash必须包含或受校验地关联 accepted request、raw draft、compiler/rule/capability版本与expanded IR；scalar override仍不修改初始Given。恢复不能重用历史批准作为当前执行许可；量化新provenance大小，不裁掉来源或静默扩大上限。

PRD §5.1/§13允许有界归一化、有限规则及一次完整规划；§5 的可复用组合编译原则允许这种 lowering。对外 `planned` 的最终预览仍须包含严格 typed program/requirements，保持唯一 execute approval；内部 provider DTO 若改版，应明确版本/兼容分派，不偷偷改变旧wire。§14/§16的几何、指定编辑、held-out、4/4、每族2/3及8/12标准完全不变。**不是把标准答案编码进确定性程序来接替模型选择。**

## 6. T4B 具体实施与验收边界

1. 仅修改 Planning 边界及必要的 capability/provenance接入：versioned draft、required-edit role输入、finite lowering、source map及批准关联；复用 ConstructionRequirements 已有 rule DAG。Native Handler、材料域、Scalar资格、事务与恢复引擎不扩展。
2. 完成上述四类可组合 primitive 的 lowering，不增加零件专用 operation。Direct IR仍拒绝本次错误。现有代码和8次失败只作只读根因/回归材料；不能制作响应文本→正确fixture的映射。
3. 先纯验证：完整C1–C4语义计划、不同段数/名字/区间、不同principal平面及world方向；正确source trace、独立driver、不支持上下文、缺失/冲突/环/非法单位/未知symbol/额外Given、无安全lowering、source或compiler hash漂移及payload边界。Expected几何与SourceMap不能仅由compiler自己重算自己来证明。
4. 纯验证稳定后，新开发日程固定每族一次真实调用、无自动重试；记录实际alias/model与原始draft/编译IR/拒绝。同一失败没有新诊断就停止，不继续加提示词碰运气。某族未证明合法完整规划时仍PARTIAL；这不是正式M20成功率。
5. 对通过的编译计划，经公开批准入口做最小必要的原生创建/指定编辑/来源发布/新控制器冷恢复，至少覆盖径向driver关联和跨方向/边处理这两种关键路径；所有打开累计、owned副本和独立Oracle规则保持。旧T4/T1–T3有效证据列明相容性，不重做整个矩阵。
6. 不修T5的CoreAcceptance runner/评分/正式held-out，不实施M18/M19，不搜索模型、不扩展Factory。T4B需先达到原T4 §7.4的真实表达和公开接入条件，再交给T5；不能仅有compiler单测就标T4 COMPLETE。

本次保护了 live_v1/v2 所有文件、T4旧报告和账本共30个文件的SHA；结束前重新核对。原生账本仍为 **3创建/11打开/14关闭/0 owned记录**，本轮增量全为0；这是磁盘历史状态，本次没有查询应用实时文档。仅新增本报告，不改变旧FAIL或生产源码。

**最终：MIXED。停止于T4A诊断交付，T4B/T5尚未执行。**
