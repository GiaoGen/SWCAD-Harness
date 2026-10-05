# M9D — Generalization Evaluation

验收状态：**BLOCKED**。评估已完整执行，机器结果为 `EVALUATION_COMPLETE_WITH_BLOCKERS`：G2、G4、held-out 和全部预期负例通过；G1/G3/G5 为 **BLOCKED_CAPABILITY**。未修补生产代码来改变结果。

前提：M9A、M9B、M9C verification 均为 COMPLETE。按 PRD §22–23、§26、Milestone 9 和用户指定 M9D 执行；没有修改 PRD、运行旧 milestone tests、读取 v0.1、增加生产 preset、扩展 backend 或执行 benchmark/M10。

## 评估方式与冻结

新增内容仅在 `tests/CadHarness.Generalization.Tests/`、`scripts/test-milestone9d.ps1` 和文档。`src/` 的源码/项目/配置文件在第一案前记录 SHA256，每案及最终矩阵均检查集合与哈希完全相同；**生产代码变更为0**。

`CaseData.cs` 和全部案例的 IR、relations、独立几何期望在 native 执行前冻结。`freeze.json` 还记录此前测试源码和 fixture 的哈希。此前测试 corpus 中没有137这个新轮廓宽度；held-out 是在 M9A–M9C handlers 完成后新定义、此前未用于开发的混合 through/blind composition。执行后没有修改其定义。

所有计划都经生产 `CadPlanner` 的严格 envelope/parser、runtime capability validation 和完整 preflight。计划响应由测试提供确定性 `FixturePlanSource`，模型调用数为0。**Planned 表示预先定义的 IR 经生产 Planner 接受或预期拒绝，不是外部 LLM 从自然语言生成计划的准确率**。G7 同样使用显式 unsupported 响应并验证 Planner 不返回 program；本评估没有测量模型语义识别率。

成功案例通过 `RelationBackend.Create(context, program, AtomicStateStore)` 执行；编辑通过动态 edit catalog、生产 CadPlanner 和 `MutationTransaction`/`TransactionalParameterBackend` 执行。没有直接调用 native modeling API 替代 handler。测试 COM 访问只用于独立观察及读回。

## Generalization matrix

| Case | Planned | Executed | Validated | Editable | Rollback safe | Production-code changes required |
|---|---|---|---|---|---|---|
| G1 | PASS | 4 native operations PASS，整体回滚 | BLOCKED_CAPABILITY | BLOCKED_CAPABILITY，未提交可编辑模型 | PASS，runtime full-restored validation | 需要通用组合/引用验证修复；未实施 |
| G2 | PASS | PASS | PASS | PASS，thickness 8→10、四孔 Ø6→Ø8 | PASS | NONE |
| G3 | PASS | 4 native operations PASS，整体回滚 | BLOCKED_CAPABILITY | BLOCKED_CAPABILITY，未提交可编辑模型 | PASS | 需要通用组合/引用验证修复；未实施 |
| G4 | PASS | PASS | PASS | PASS，bolt holes Ø8→Ø9→Ø8 | PASS | NONE |
| G5 | PASS | 4 native operations PASS，整体回滚 | BLOCKED_CAPABILITY | BLOCKED_CAPABILITY，未提交可编辑模型 | PASS | 需要通用组合/引用验证修复；未实施 |
| G6 impossible fillet | PASS，结构合法 | 先产生实际拉伸/孔修改，再预期失败 | PASS，恢复为空原模型 | N/A | PASS | NONE |
| G6 invalid pattern | 预期拒绝 | 无 mutation | PASS，原模型/state不变 | N/A | 无需 rollback，状态不变 | NONE |
| G6 invalid placement | 预期拒绝 | 无 mutation | PASS，原模型/state不变 | N/A | 无需 rollback，状态不变 | NONE |
| G7 unsupported | UNSUPPORTED | 不调用建模后端 | PASS，原模型/state不变 | N/A | 无需 rollback，状态不变 | NONE |
| Held-out | PASS | PASS | PASS | PASS，三孔 Ø7→Ø9，盲孔保持 | PASS | NONE |

`Production-code changes required` 是缺口判定，不代表本阶段已修改生产代码。G1/G3/G5 不是成功模型；原生子操作成功不能替代最终验证/状态提交。

## 几何与编辑结果

### G1/G3/G5 — 共同阻塞

- G1：80×50×10、两孔 Ø8、居中40 spacing、四个外竖边 R3。
- G3：120×80×10、2×3 个 Ø8、居中70×25 spacing、四个外竖边 R5。PRD未指定 spacing，本评估显式选70×25。
- G5：90×70×6、四个 Ø6、居中50×40 spacing、四个外竖边2 mm等距倒角。PRD未指定孔径/spacing/边集合，本评估显式选上述合法组合。

三案都是原有 extrusion→through-hole→linear/rectangular-pattern→fillet/chamfer handlers；每案四个 native 子操作全部成功，随后 production required postconditions 返回：

```text
RELATION_VIOLATED
Native first pattern direction differs.
```

失败发生在 `RelationNativeReadback.Verify` 的方向引用/反向标志一致性检查。该检查同时比较 native `D1Axis` persistent reference 和 reverse direction，现有日志没有区分哪个子比较失败，因此不把更细的根因当作已证明。确定的缺口是：当前后端不能最终验证并提交这些**阵列后外边处理组合**。

预检与 catalog 接受这三种组合，实际最终验证失败，所以 operation/profile/relations 注册集合虽然一致，**组合层的 capability projection 与可成功完成的 runtime 能力不完全一致**。后续修复应是通用方向引用/拓扑消费处理，或投影明确拒绝不支持的组合；M9D不做修复，也未调整案例顺序/参数绕开失败。

三案四个 flags 均为 `MutationStarted=true, RollbackAttempted=true, RollbackSucceeded=true, StateCommitted=false`，共享 coordinator 的 full restored validation 通过。G3/G5 另外保存独立 before/after 模型、文件和 session 比较，全部unchanged。G1第一案的初版 reporter 未在早退路径保存独立 before/after；保留原始 report，不重跑，矩阵仅根据生产事务的 full-restored verification 标为 `PASS_RUNTIME_VERIFIED`，并在离线汇总确认其磁盘 CADState 仍是空模型/revision0。没有补造G1独立几何快照。

### G2 — 创建与必要编辑通过

100×60×8、2×2 Ø6，显式60×30居中布局；relations 使用 hosted_on、pattern_seed、equal_spacing、centered_about。初次提交revision1；深度8→10提交revision2；种子孔径6→8提交revision3。

独立 body readback 检查100×60的extents、厚度、四个圆柱壁的实际Ø6/Ø8、中心(±30,±15)、Z边界0/8或0/10、整体体积。编辑保留relations和persistent bindings；增量scope/read-set保存在各 `edit-N.json`，不是用整个body扫描代替生产增量验证。

### G4 — circular disk / bolt circle通过

Ø100×12、中心Ø20 through bore、Ø70 PCD六个Ø8 through holes；四个原有操作为Circle extrusion、center bore、bolt seed、CircularPattern，轴为 `stock.rotational_reference` 的真实native CylindricalFace。

独立native readback检查外圆柱、中心孔、六个bolt cylinders的半径/坐标/数量/Z边界以及体积。种子孔径8→9→8两次真实提交并同步六实例。动态catalog只暴露两个through-hole HoleDiameter配对，未暴露Circle extrusion depth/profile diameter或circular count/angle编辑。

### Held-out — 混合through/blind组合通过

```text
137 × 91 × 13 rectangular extrusion
3 × Ø7 through holes, centered linear pattern along X, spacing29
seed/instances centers = (-29,0), (0,0), (29,0)
Ø11 blind hole at (0,27), depth4, bottomZ9/topZ13
later edit through-hole diameter7→9; blind hole unchanged
```

四个操作组合：CreateExtrude、CreateThroughHole、CreateLinearPattern、CreateBlindHole。沿用hosted_on、pattern_seed、equal_spacing、centered_about；没有new operation kind、preset或新handler。

创建完整提交并保存state后，重新加载JSON、通过Binder解析健康persistent refs，读取动态catalog后再规划并执行通孔孔径编辑。独立native验证三孔一起变化，盲孔Ø11、位置和depth4保持。创建/编辑状态均落盘；原生Part不保存，编辑发生在同一受控live session。这不证明native Part重开/controller restart后的恢复能力。

## 负例与每个成功模型的rollback

G6使用独立83×57×9板：

- impossible fillet：原生extrusion+Ø6 hole成功，R1000失败为GEOMETRY_IMPOSSIBLE；true/true/true/false四flags，恢复执行前pristine Part。
- invalid pattern：1025实例。Planner按runtime计数上限拒绝，直接backend防御性入口也在mutation前拒绝。
- invalid placement：Ø6孔位(60,40)在宿主之外，Planner/backend都在mutation前拒绝。
- G7：优化涡轮叶片及内部冷却通道不在catalog，返回INTENT_UNSUPPORTED；没有将其替换成支持的设计。

G2/G4/Held-out正常创建与编辑完成后，各再请求一个通用Ø3附加through hole。在真实`FileShare.Read`锁持有期间，该hole native创建、重建、生产后置条件和final validation均完成，`File.Replace`提交失败为STATE_COMMIT_FAILED。共享production construction rollback恢复执行前模型、refs/properties/session、CADState/revision；独立模型比较与state文件SHA256完全相同，未留下atomic临时文件。测试没有专用rollback函数或新增fault-injection production分支。

9个具备独立before/after证据的失败/拒绝场景全部 `ModelUnchanged=true, FileUnchanged=true, SessionUnchanged=true`，文件前后SHA256相同。另有G1 production full-restored verification通过，证据区别已说明。

## Runtime capability audit

纯准备阶段检查：construction operations集合等于实际FeatureBackendRegistry八个handlers；profiles集合等于实际CreateExtrudeHandler支持的矩形和Circle；relations集合等于DesignRelationEngine支持集合；construction不暴露EditParameter。

三个成功模型的动态edit catalog共12个配对：G2六个、G4两个、held-out四个。每个配对检查注册handler存在、native Read值匹配committed state、production ResolveInputs/Preflight/CaptureRollback可执行；未注册/未投影的bound edits通过catalog validation全部拒绝。**这12项是native访问与无mutation预检审计，不等同于12项都执行了值变化。** 实际值变化执行五次：G2两次、G4两次、held-out一次。未运行额外性能/编辑枚举suite。

组合能力一致性总结果为false，原因仅上述三案最终方向验证失败；没有将registry集合相同误报为整个runtime projection正确。

## 执行、资源和证据

专属test项目Release编译通过，**零警告、零错误**。未加入历史test runner或运行旧tests。

```powershell
.\scripts\test-milestone9d.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 原执行方式：每案调用一次，严格串行。10案已运行，不应自动重跑。
.\scripts\test-milestone9d.ps1 -Mode Live -Case G2 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
# 汇总为离线只读验证，不创建Part。
.\scripts\test-milestone9d.ps1 -Mode Matrix -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
```

SOLIDWORKS32.0.1；十案创建前响应正常、test-owned Part数0、GDI依次552–561，低于7000。M9 PRD只列并发上限1；本次据明确案例范围将耐久总budget固定为10（G1–G5五案、G6三案、G7一案、held-out一案），每案只执行一次，无修复重试。

最终：**10次创建尝试、创建10、关闭/丢弃10、open test-owned Parts=0**。每案完成后立即cleanup并恢复原活动状态，所有restored=true、cleanup error=null。G7和预检拒绝案也使用独立空Part证明无原生mutation；没有关闭无关文档。

证据目录 `artifacts/milestone9d/`：

- `freeze.json`、`cases.json`、`plans.json`、`construction-capabilities.json`、`planner-schema.json`、`prepare-result.json`。
- 各case的原始 `result.json`、`state.json`；成功模型另有initial/edited states、edit catalog、hidden edits和edit validation traces。
- 各案 `*-run.log`、耐久 `native-budget.json`、最终 `generalization-matrix.json/.md` 和 `matrix-run.log`。

汇总工具曾因直接反序列化含abstract IR参数的原始结果失败，已仅修正**测试汇总读取**为JSON DOM；native原始报告未改写、未重跑。G1早期reporter的离线列补全只依据已有transaction flags/full restored validation，不升级失败结论。

评估阶段已完成；**M9全案例通过验收仍BLOCKED**。缺口、负例成功与held-out成功分别记录，没有宣称PRD的整体效率假设或任意CAD能力已证明。
