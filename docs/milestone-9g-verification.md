# M9G — Current Runtime Compatibility Revalidation

状态：**COMPLETE**。仅执行 G2、Held-out 和 G6_Pattern 的当前 runtime 兼容性重验。已阅读当前仓库、M9E/M9F verification、PRD 和 `a491501`（Add generalization revalidation acceptance）。未执行 M10、历史 regression suite 或性能 benchmark。

production baseline：`a491501`；runtime：`solidworks-v0.2-m9e`。M9G 开始时的全部 production 文件集合/SHA256 与 M9F freeze **完全相同**，因此 M9F 的 G1/G3/G5 与本次案例使用同一 ReferenceAxis production implementation。

## 范围与证据保护

**Production code changes：0。** 仅新增独立测试项目 `tests/CadHarness.CurrentRuntimeCompatibility.Tests/`、入口 `scripts/test-milestone9g.ps1`、本次证据及文档更新。未新增 capability、preset、handler 或 case-specific production 分支。

链接原始 M9D `CaseData.cs`，核对 M9D fixture SHA256 和 `cases.json`。新的计划仅将 Linear/Rectangular Pattern 的方向 references 从 `LinearEdge` 改为 `ReferenceAxis`；输入 IDs、parameters、operations、relations、几何期望保持不变。G2 原来是3个 operations，Held-out 原来是4个 operations，均没有新增 production operation。

独立目录 `artifacts/milestone9g/` 在第一案前冻结 production 源文件、历史 tests/fixtures、PRD、历史 verification/脚本及 **M9D/M9E/M9F 全部 evidence/日志**。准备、每案结束和最终 summary 检查路径集合和 SHA256 未变。未重新调用 M9F runner，也未改写其 G5 raw report 或零-Part复核。

旧版当前汇总文档在更新前保存为 `artifacts/milestone9g/prior-m9-final-acceptance.md`；M9F 原机器汇总仍保留。新的 final acceptance 位于本次目录，并更新当前 [M9 final acceptance](milestone-9-final-acceptance.md)。

## Matrix

| Case | Planner / capability | Native execution | 指定编辑 | Strict direction/count/spacing | Final geometry / CADState / refs | Rollback safety | Parts created/closed |
|---|---|---|---|---|---|---|---|
| G2 | PASS | PASS | PASS，thickness 8→10、四孔 Ø6→Ø8 | PASS，D1/D2 | PASS | PASS，edit＋construction | 1/1 |
| Held-out | PASS | PASS | PASS，三通孔 Ø7→Ø9、盲孔保持 | PASS，D1 | PASS | PASS，edit＋construction | 1/1 |
| G6_Pattern | PASS，预期拒绝 | NO_MUTATION | N/A | N/A | PASS，纯 preflight | PASS_NO_MUTATION | 0/0 |

所有 Planner 流程使用生产 `CadPlanner`、runtime capability projection、strict parser/preflight；响应源为 deterministic typed fixture IR，model calls=0。此处不声称外部 LLM 自然语言生成准确率。

## G2 — ReferenceAxis rectangular pattern 与实际参数事务

原始目标：100×60×8，2×2 Ø6 centered pattern，X/Y spacing=60/30 mm，孔中心 `(±30,±15)`。production `RelationBackend.Create(..., AtomicStateStore)` 完成3个 native operations、full final validation 和 revision0→1 atomic commit。

| Stage | 厚度 / 孔径 mm | Native layout / spacing mm | 体积 mm³ | Revision |
|---|---|---|---|---|
| Creation | 8 / 6 | 2×2 / 60×30 | 47095.22131576613 | 1 |
| ExtrusionDepth edit | 10 / 6 | 2×2 / 60×30 | 58869.02664470767 | 2 |
| HoleDiameter edit | 10 / 8 | 2×2 / 60×30 | 57989.380701702525 | 3 |
| Edit rollback 后 | 10 / 8 | 2×2 / 60×30 | 57989.380701702525 | 3 |
| Construction rollback 后 | 10 / 8 | 2×2 / 60×30 | 57989.380701702525 | 3 |

编辑均先通过动态 edit catalog 和 production Planner，再执行 `MutationTransaction<NativeEditPreparation, NativeEditRollback>` / `TransactionalParameterBackend`。沿用现有 ExtrusionDepthMutationHandler、HoleDiameterMutationHandler，四个阵列实例同步为 Ø8，through boundaries 从 Z=0 到 Z=10。

两次正常编辑的 ChangeSet 各包含1个 changed parameter；DirtySet 包含实际依赖几何及 ReferenceAxis directions，incremental validation `FullModel=false`，parameter reads 为对应的 `stock.extrusion_depth` 或 `seed.hole_diameter`，没有关闭方向检查。

## Held-out — ReferenceAxis linear pattern 与盲孔保留

原始目标：137×91×13，3×Ø7 centered linear pattern，spacing=29 mm、中心 `(-29,0)/(0,0)/(29,0)`；Ø11 blind hole depth4 位于 `(0,27)`。保持原4-operation composition，完成 creation/full final validation/atomic commit，revision1。

| Stage | 三通孔 Ø mm | 盲孔 Ø / depth / Z mm | Native count / spacing | 体积 mm³ | Revision |
|---|---|---|---|---|---|
| Creation | 7 | 11 / 4 / [9,13] | 3 / 29 mm | 160189.97139866307 | 1 |
| HoleDiameter edit | 9 | 11 / 4 / [9,13] | 3 / 29 mm | 159209.79449074305 | 2 |
| 两次 rollback 后 | 9 | 11 / 4 / [9,13] | 3 / 29 mm | 159209.79449074305 | 2 |

通孔 Ø7→Ø9 通过真实 Planner/edit transaction 提交，三个实例贯穿边界仍为 Z=0/13。独立 native cylinder/edge observations 在 creation、edit、两次 rollback 后均验证盲孔 center `(0,27)`、Ø11 和 Z=9/13 不变；CADState 参数绑定同时验证 blind-hole depth4。正常编辑保持增量验证，只改变 `seed.hole_diameter`。

## 严格 native readback、持久状态与 capability 一致性

每个阶段及 rollback 后均独立检查：

- `ILinearPatternFeatureData` D1/D2 counts/spacings、uniform/skipped modes、seed persistent reference；
- 原生 D1Axis/D2Axis canonical feature reference 与 CADState 的 RefAxis feature reference **精确相等**；
- `GetRefAxisParams` 端点非退化，穿过 local origin、平行指定 X/Y，并核对 reverse flag；
- datum `.direction_x/.direction_y` 与 `.axis_x/.axis_y` refs 跨 thickness/diameter edits 及 rollback 保持完全相同；
- 一个 solid body，原轮廓/厚度、实际孔数/中心/孔径、贯穿/盲孔边界和独立解析体积；
- commit→load→StateValidation→Binder/native persistent resolution，feature/entity health、relations/dependencies、全部参数绑定和值。

长度容差 `1e-6 mm`、体积容差 `0.001 mm³`，沿用 M9D/M9F；reference identities、state bytes、session 比较保持精确。

construction catalog 与实际 backend operations/profiles/relations 一致，只暴露 ReferenceAxis pattern directions。各阶段 live edit catalog 的可见 target/parameter 都有现有 registered handler、正确 native read、通过 resolve/preflight 且可捕获 rollback；active pattern count/spacing 投影完整。未投影的 bound edits 被拒绝，Held-out 的 blind-hole 编辑没有被错误暴露。

## Rollback safety

每个 live Part 各执行两个真实 atomic commit failure probes：

1. 在最终已提交模型上临时将 seed HoleDiameter 增加1 mm；native Apply、rebuild、validation、stage state 成功后，真实 `FileShare.Read` 锁使 atomic replace 失败。
2. 用通用 IR 临时追加 Ø3 through hole `(0,-18)`，native creation 成功后同样触发真实 atomic commit failure。

四个失败均为 `STATE_COMMIT_FAILED`，flags 均为 `MutationStarted=true / RollbackAttempted=true / RollbackSucceeded=true / StateCommitted=false`。使用已有通用事务和 backend rollback，没有测试专用 restore。

每次恢复后独立复查 model structure/features/properties、全部孔几何、state bytes/hash、live captured-state snapshot、strict directions/count/spacing 和 persistent refs。**ModelUnchanged/FileUnchanged/SessionUnchanged 全部 true**。G2 revision保持3，Held-out保持2；临时 state 文件无泄漏。

## G6_Pattern — Typed count=1025，零-Part预检拒绝

原始83×57×9 host、Ø6 seed 和 invalid 1025-instance linear plan 保持不变；方向为 `stock.direction_x: ReferenceAxis`。广义 IR `ProgramValidator` 接受其结构/类型，排除因为旧 `LinearEdge` 类型而失败。

- production Planner：`PlanningStatus.Rejected`，Program=null，`PROGRAM_SCHEMA_INVALID`，唯一 issue 为 `$.operations[2].count`；
- current runtime preflight：同一 count contract issue；
- production backend preflight：`OPERATION_PRECONDITION_FAILED: Pattern exceeds 1024 instances.`。

没有连接 SOLIDWORKS、没有调用建模或 state commit，native calls=0、Parts=0、MutationStarted=false；无需 rollback。这是当前 executable catalog 的1024实例上限拒绝，不是方向能力阻塞。

## 生命周期、构建、执行记录

M9G 独立耐久 budget=**2**，最大并发1。实际 **CreationAttempts=2 / PartsCreated=2 / PartsClosed=2 / OpenTestOwnedTitles=[]**，预算2/2已用完。G2、Held-out 各独立 Part，每案结束立即关闭/丢弃，OriginalActiveRestored=true、CleanupError=null。

SOLIDWORKS revision32.0.1，process1984；创建前 responding=true、open test-owned Parts=0、GDI568/569，低于7000。没有保存 native Part 文件或关闭其他文档；persistent-state/native refs 证据属于受控 live session，不声称文件重开或 controller restart 恢复。

只构建 M9G 专属 project 和4个依赖的 production projects，Release **0 warnings / 0 errors**。没有执行旧阶段 tests、G1/G3/G5、G4、其他负例或 benchmark。

```powershell
.\scripts\test-milestone9g.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 以下Live为已执行记录；预算已用完，入口拒绝重复案例。
.\scripts\test-milestone9g.ps1 -Mode Live -Case G2 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9g.ps1 -Mode Live -Case HeldOut -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9g.ps1 -Mode Summary -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
```

证据：`artifacts/milestone9g/` 中 freeze、catalog/schema、typed programs/plans/expected、G6_Pattern pure rejection result、每案 raw result、stage observations/CADState/current programs、正常 mutation traces、edit/construction rollback evidence、budget、compatibility matrix 和 `m9-final-acceptance.json`；日志为 `artifacts/milestone9g-*.log`。

**所有受 PatternDirection contract 变化影响的 Linear/Rectangular Pattern supported/negative cases 均已在当前 ReferenceAxis runtime 下验证**：G1/G3/G5沿用相同 production 源码下的M9F；G2/Held-out/G6_Pattern使用本次M9G。G4、G6_Fillet、G6_Placement、G7不受该contract变化影响，按用户要求沿用M9D。

结论：**M9 COMPLETE，M10 prerequisite satisfied=true，可以直接进入 M10；本次未执行 M10。**
