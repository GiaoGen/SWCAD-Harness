# M9E — Topology-stable Pattern Direction

状态：**COMPLETE**。仅执行M9E；阅读当前仓库、M9D verification及`225f37b`（Add generalization acceptance tests）。没有新增G1/G3/G5生产分支、preset、尺寸特判、跳过方向验证、修改M9D原始证据、运行完整泛化suite或执行M10。

## 最小诊断与根因

`225f37b` 中矩形extrusion的 `.direction_x/.direction_y` 是顶部BREP linear edges。`RelationNativeReadback.Verify` 把D1Axis persistent reference和D1ReverseDirection合在同一个布尔判断。

先只拆分诊断信息，仍执行同一严格检查，再在一个新M9E Part中复现原G1组合：Extrude→Hole→LinearPattern→Fillet。四个native operations全部成功，required postconditions随后失败：

```text
RELATION_VIOLATED: Native first pattern direction differs.
axisReferenceMatches=False
reverseMatches=True
actualReverse=False
expectedReverse=False
```

**直接证实的根因是D1Axis持久引用不一致，reverse-direction没有不一致。** `diagnosis.json` 保留actual/expected两个不同的完整persistent references。后续边处理改变了BREP方向边的拓扑身份；最终模型所解析的边与pattern selection readback所得引用不相同。不能把实体边身份当成设计局部方向的稳定定义。诊断只复现一个代表case，没有宣称分别完成G3/G5旧后端根因实验。

诊断仍由production construction transaction执行，failure四flags为true/true/true/false；原始pristine Part和state文件恢复，原state字节相同，Part关闭。没有通过关闭验证绕过失败。

## 通用方向抽象与native representation

矩形extrusion新增实际native datum outputs：

| Output | Semantic type / role | Native representation |
|---|---|---|
| `.direction_x` | ReferenceAxis / PatternDirection | origin XY与XZ reference planes交线生成的RefAxis feature |
| `.direction_y` | ReferenceAxis / PatternDirection | origin XY与YZ reference planes交线生成的RefAxis feature |
| `.axis_x/.axis_y` | ReferenceAxis / symmetry reference | 分别与上述datum同一真实native feature的语义alias |

`NativePatternDirection.Create` 按origin plane transforms/法向选择基准面，无显示名称、BREP edge、零件尺寸或case name。调用native `InsertAxis2`，取得该调用产生的唯一RefAxis feature。datums只有origin-plane依赖；后续Fillet/Chamfer不改变这些特征的定义来源。两个辅助native features作为extrusion-owned semantic outputs，不增加CadProgram operations、OperationKind或parameter mutation handlers。

持久引用统一使用RefAxis **feature** 的persistent reference。SOLIDWORKS pattern definition可能返回specific `IRefAxis` interface；`CanonicalFeature` 将其映射到唯一owner feature后做严格reference比较。COM identity仅用于同次native接口/owner查找，不用于语义绑定、选择任意候选或替换失效persistent refs。

native axis的端点顺序可能反向，因此每次读取native `GetRefAxisParams`，验证非临时datum、feature error/warning、有效非退化参数、穿过局部原点且平行指定X/Y方向，再根据native vector生成reverse flag。CADState保存经native验证的正向局部轴几何；实体health始终来自实际persistent reference解析。

## 架构与语义一致性

**修改了通用方向表示和backend abstraction；未修改transaction coordinator、Binder算法或AtomicStateStore。**

- IR extrusion direction outputs改为ReferenceAxis；已有PatternDirection role仍可在广义IR表示轴/边，默认shorthand现在继承ReferenceAxis，当前SOLIDWORKS runtime只接受ReferenceAxis方向。
- ProfileOutputs、strict parser/contract、RelationContext.Layout、默认direction引用、pattern creation和native readback使用一致类型与同一local X/Y含义。
- `LinearPatternHandler` 通过Binder解析native RefAxis feature，选择datum作为D1/D2 axis；不选择BREP边。
- native readback继续检查seed、count、spacing、uniform/skipped modes、datum persistent identity和reverse flag。检查没有放宽为“只要平行就算同一个引用”。
- `SemanticStateCapture`从真实datum读回几何并保存persistent refs/owner/health；Planner construction projection和schema只暴露这些实际输出/输入，runtime id为m9e。
- Circle/CircularPattern能力保持原范围，Circle不因此新增linear-pattern方向输出。圆形rotational_reference仍是真实外圆柱面。

边处理仍会消费原outer edges。沿用M9C“失效边仍保存真实health、Binder拒绝再次使用”的规则，并将原来按整个owner豁免缩小为**程序实际fillet/chamfer输入的LinearEdge IDs**。construction final与parameter full validation共用该有限判定；datums及其他引用仍必须健康。这样count的完整验证可以检查合法含圆角/倒角的已提交模型，且不会误称旧边仍可绑定。

原 `.direction_x/.direction_y` 的类型发生明确契约变更。旧计划显式声明`linear_edge`时当前runtime预检拒绝；新计划必须使用`reference_axis`，没有静默把边转成轴或迁移旧CADState。旧M9D fixture/state/evidence保持原样。

## 专属测试

新入口：`tests/CadHarness.PatternDirections.Tests`及`scripts/test-milestone9e.ps1`。复用现有resource guard、NativeTestBudget与TestPartScope；测试只读取链接的历史fixture定义，生成新的typed计划，不修改其文件。没有运行M9D runner。

纯测试 **10/10 PASS，0 Parts**：typed datum outputs、circle范围、catalog类型、linear+fillet/rectangular+chamfer严格预检、旧edge输入拒绝、隐式方向、shorthand、居中关系和Planner schema。

### 原生linear-pattern + fillet

80×50×10、两孔Ø8、40 mm居中spacing、四个外竖边R3。四步native操作通过，完整final validation和atomic state commit通过，revision0→1。

| 阶段 | Native count / spacing | 结果 |
|---|---|---|
| 初次finalized construction | 2 / 40 | PASS |
| PatternSpacing→24 | 2 / 24 | PASS，增量validation，revision2 |
| PatternCount→3 | 3 / 24 | PASS，full validation，revision3 |
| PatternCount→2 | 2 / 24 | PASS，full validation，revision4 |
| PatternSpacing→40 | 2 / 40 | PASS，增量validation，revision5 |
| 追加孔后真实atomic commit failure | 保持2 / 40 | PASS，通用construction rollback |

每个阶段独立readback验证D1Axis reference equality、reverse equality、count/spacing、实际孔中心/Ø8/Z边界、最终extents和解析体积。nominal体积38917.43368967433 mm³；三孔阶段38414.778865099965 mm³。datum refs跨spacing/count edits和rollback保持完全相同。

### 原生rectangular-pattern + chamfer

90×70×6、2×2 Ø6、50×40居中spacing、四个外竖边2 mm等距倒角。四步native操作通过，完整final validation和commit通过。

| 阶段 | Native layout / spacing | 结果 |
|---|---|---|
| 初次finalized construction | 2×2 / 50×40 | PASS |
| PatternSpacingX→30 | 2×2 / 30×40 | PASS，增量validation |
| PatternCountX→3 | 3×2 / 30×40 | PASS，full validation |
| PatternCountX→2 | 2×2 / 30×40 | PASS，full validation |
| PatternSpacingX→50 | 2×2 / 50×40 | PASS，增量validation，最终revision5 |
| 追加孔后真实atomic commit failure | 保持2×2 / 50×40 | PASS，通用construction rollback |

所有阶段D1/D2 native datum refs与reverse flags均匹配；X/Y count与spacing独立读取，最终孔位/孔径/through boundaries与解析体积正确。nominal体积37073.415986824606 mm³；六孔阶段36734.12398023691 mm³。

两案的CADState均commit→load→Binder重新解析；健康实体的persistent refs可解析，已消耗边仍拒绝绑定。native datums由真实IRefAxis证明，未借用extrusion feature假装datum。额外通用孔的native创建成功后，用真实FileShare.Read锁使File.Replace失败，production rollback保持state字节、session snapshot、datum references和几何不变。没有专用case rollback。

## 生命周期、构建与原证据保护

M9E独立耐久budget为3：诊断1＋linear验收1＋rectangular验收1；最大并发1。实际**创建尝试3、创建3、关闭/丢弃3、open test-owned Parts=0**，预算3/3已用完。每个Part都在geometry前注册、结束后立即关闭，原活动状态恢复true，cleanup error=null。

SOLIDWORKS32.0.1，process1984；创建前responding=true、test-owned Parts=0、GDI562/563/564，低于7000。没有关闭其他文档或保留native Part文件；persistent state证据是同一受控live session的JSON与native readback，不声称重开恢复。

M9E开始时冻结M9D evidence全部文件的SHA256；诊断、纯测试、两案native结束均比较路径集合和哈希完全相同。`m9d-evidence-hashes.json`保存原值。M9D原始verification、fixture、raw reports、matrix和预算没有改写。

最终M9E专属project和既有solution Release编译零警告、零错误；既有milestone tests只编译，不执行。两次native之后只补充datum error/warning拒绝检查与diagnostic-mode防重复保护，并重新完成专属pure/build检查，没有增加live次数。

```powershell
.\scripts\test-milestone9e.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 以下为已执行的原生验收命令记录，预算已用完，入口拒绝再次运行。
.\scripts\test-milestone9e.ps1 -Mode Linear -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9e.ps1 -Mode Rectangular -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

Diagnose只适用于修复前edge backend加拆分诊断的阶段；诊断已留存，当前datum runtime在创建Part前拒绝再次执行baseline诊断。

证据：`artifacts/milestone9e/diagnosis.json`、`pure-result.json`、`native-budget.json`、M9D evidence hashes，以及linear/rectangular下的program、catalog、initial/current CADState、independent observations和result/stage traces；构建/runner logs在`artifacts/milestone9e-*.log`。

## 是否可以重新评估G1/G3/G5

**可以**，应使用新typed ReferenceAxis plans、新评估baseline/budget/evidence目录。原M9D入口冻结的是旧production和显式edge plans，不能覆盖其freeze或报告来冒充重新评估。

本次已验证G1等价linear+fillet和G5等价rectangular+chamfer组合，并验证count/spacing edits；G3的2×3+R5完整case尚未单独重评。M9D旧验收仍BLOCKED，M9E修复COMPLETE不替代新的G1/G3/G5评估结论。未执行M10或性能benchmark。
