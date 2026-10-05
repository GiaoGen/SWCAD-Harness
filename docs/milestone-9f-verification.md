# M9F — Generalization Revalidation

状态：**COMPLETE**。仅重新评估 M9D 中被阻塞的 G1、G3、G5。当前 production baseline 为 `d51b20c723400934e2ffd4163453bc3f7468da4c`，runtime 为 `solidworks-v0.2-m9e`。未执行 M10、完整历史 regression 或 performance benchmark。

已阅读当前仓库、PRD、M9D/M9E verification 及 `d51b20c`。M9A–M9C、M9E 的 COMPLETE 记录是前提；M9D 的原始 BLOCKED 报告保留，新的 M9 总验收见 [M9 final acceptance](milestone-9-final-acceptance.md)。

## 范围与冻结

**Production code changes：0。** 新增内容只有独立验证项目 `tests/CadHarness.GeneralizationRevalidation.Tests/`、入口 `scripts/test-milestone9f.ps1`、文档和本次证据。未新增 capability、preset、operation、参数 handler 或 case-specific production 分支。

链接读取原 `CaseData.cs`，不修改 M9D fixture。准备阶段对照 M9D `freeze.json` 的 fixture SHA256 和 `cases.json`；对每个 program 作结构比较，证明仅 pattern direction references 的类型由 `LinearEdge` 改为 `ReferenceAxis`，IDs、输入槽、参数、operations、relations 全部保留。没有改变设计目标，也没有执行尺寸修改。

第一案前在 `artifacts/milestone9f/freeze.json` 冻结 production 文件集合/SHA256、历史测试源码/fixture、PRD、历史 milestone verification、M9D/M9E 全部原始证据和相关日志；每案、证据复核及最终汇总均验证一致。M9D/M9E 的原始报告、预算、计划、矩阵及 verification 没有改写。

## Planner 与 capability projection

三案均通过 production `CadPlanner` 的严格 envelope/parser、runtime capability validation 和完整 preflight。响应源为确定性 `FixturePlanSource`，**0 external model calls**；Planned 表示 typed IR 经真实 Planner 接受，不代表外部 LLM 自然语言生成成功率。

construction catalog 的 operations 与实际 backend registry、profiles 与 native extrusion 支持、relations 与 relation engine 支持逐项一致；只暴露 `ReferenceAxis` PatternDirection。旧显式 `LinearEdge` 方向计划被拒绝，新的 typed plans 通过。

每个已提交模型还检查 live edit catalog：每个可见 target/parameter 都有注册 handler、原生读值符合 nominal IR、通过 production Planner、native resolve/preflight，并可捕获 rollback。未投影的 bound edits 均被 catalog 拒绝。G1 的 count/spacing 和 G3/G5 的 X/Y count/spacing 全部可见；seed 的 HoleDiameter 同样完成审计。

本阶段 Editable 的证据范围是 **native read / Planner / preflight / rollback capture**，没有进行参数尺寸 mutation。G1/G5 实际 count/spacing 编辑由 M9E 原始 evidence 支持；G2、G4、held-out 的实际参数编辑由 M9D 原始 evidence 支持。没有将 G3 的只读审计描述为新执行的参数修改测试。

## G1/G3/G5 matrix

| Case | Planned / capability | Native execution | Count / spacing / direction strict readback | Final geometry | CADState / persistent refs | Rollback safe | Production changes |
|---|---|---|---|---|---|---|---|
| G1 | PASS | PASS | PASS | PASS | PASS | PASS | 0 |
| G3 | PASS | PASS | PASS | PASS | PASS | PASS | 0 |
| G5 | PASS | PASS | PASS | PASS | PASS | PASS，含零-Part证据复核 | 0 |

每案仍为四个 Operation IR：Extrude → ThroughHole → Linear/RectangularPattern → Fillet/Chamfer。通过 `RelationBackend.Create(context, program, AtomicStateStore)` 执行；native operations 全部成功、production final validation 成功、revision 0→1 原子提交。COM 测试访问仅作独立观察，没有替代 modeling handler。

| Case | 原几何目标 | Native count / spacing | 最终体积 mm³ |
|---|---|---|---|
| G1 | 80×50×10；2×Ø8；四个外竖边 R3 | 2 / 40 mm | 38917.43368967433 |
| G3 | 120×80×10；2×3 Ø8；四个外竖边 R5 | 2×3 / 70×25 mm | 92769.46921595125 |
| G5 | 90×70×6；2×2 Ø6；四个外竖边 2 mm 等距倒角 | 2×2 / 50×40 mm | 37073.415986824606 |

严格 readback 独立读取 `ILinearPatternFeatureData` 的 D1/D2 count、spacing、uniform/skipped 模式、seed reference、D1Axis/D2Axis feature persistent references 和 reverse flags。datum 端点证明非退化、通过局部原点、与 local X/Y 平行；实际选择 canonicalize 后与 CADState 的真实 RefAxis feature reference **精确相等**，reverse 与实际轴向一致。

最终几何对照原冻结的 analytic targets：一个 solid body、width/height/thickness、孔数、孔中心/孔径、Z=0 到 thickness 的贯穿边界和体积；G1/G3 另验证四个正确位置/半径的外圆角柱面及 native radius，G5 验证 native 两侧 chamfer distance 和四个45°竖直倒角面。长度容差 `1e-6 mm`、体积容差 `0.001 mm³`，与 M9D 一致。

CADState 经 commit→load→StateValidation→Binder/native resolution 验证；feature refs、relations、dependencies、参数绑定和值正确，direction/axis 是真实 IRefAxis。已被 treatment 消耗的指定 outer edges 保存真实 unavailable health，并被 Binder 拒绝；未豁免 datum 或其他引用。

## Rollback safety 与 G5 证据复核

每案在已提交 nominal 四步模型上，通过通用 Operation IR 临时追加 Ø3 通孔 `(0,-18)`，再用真实 `FileShare.Read` 锁使 atomic `File.Replace` 失败。探针没有改变已提交设计目标或操作组合。

三案追加孔的 native operation 均成功；failure code 均为 `STATE_COMMIT_FAILED`，四个 flags 均为：

```text
MutationStarted = true
RollbackAttempted = true
RollbackSucceeded = true
StateCommitted = false
```

production rollback 使用已有 checkpoint/transaction，恢复 original feature inventory、body/face/edge structure、properties、strict relation native readback 和 native captured-state comparison。state 字节和 session snapshot 均完全相同；原 committed state 保持 revision1，无临时 state 文件泄漏。G1/G3 在 rollback 后还完成独立 datum readback 和 Binder/几何复查。

G5 的原始 test `result.json` 保留 **BLOCKED**：测试比较器误将 geometry JSON 的浮点体积逐字比较，导致在生产 rollback 已成功后停止。实际体积分别为 `37073.41598682461`、`37073.415986824606`，仅差 **7.275957614183426×10⁻¹² mm³**。feature/property/topology signature、孔位/孔径/边界、state 字节、session 均相同，没有 runtime capability gap。

保留全部 raw native reports/logs、原测试比较器快照和第一次 BLOCKED summary log，再冻结 `native-evidence-hashes.json`。新增 **0 Parts / 0 native calls** 的 `VerifyEvidence` 对原始数据重新检查既有容差、完整 transaction flags、production restored validation 成功、state hashes/metadata、datum identities、projection 及 cleanup；结果在 `G5/evidence-verification.json`，为 PASS。测试比较器修正为已有几何容差，没有放宽 persistent identity/state 比较，也没有重跑 G5。

G5 rollback 后的 strict native readback/persistent-state 验证依据是原生执行期间已完成的 production `TransactionalConstructionBackend.ValidateRestored`：checkpoint.Verify → RelationNativeReadback.Verify → captured-state comparison；独立测试在精确浮点断言处停止，**没有额外生成独立 post-rollback COM observations**。最终 summary 明确保留 `RawStatus/RawMessage` 和复核依据，不覆盖原始失败报告。

## 生命周期、构建与证据

M9F 独立耐久 budget 为 **3**，最大并发1。实际 **CreationAttempts=3、PartsCreated=3、PartsClosed=3、OpenTestOwnedTitles=[]**，预算3/3已用完。每案独立 Part，结束后立即关闭/丢弃，OriginalActiveRestored=true、CleanupError=null。

SOLIDWORKS revision `32.0.1`、process1984；创建前 responding=true，test-owned Parts=0，GDI分别565/566/567，低于7000。没有保存 Part 文件或关闭其他文档。persistent-state 证据属于受控 live session，不宣称组合文件重开或 controller restart 恢复。

仅构建本次专属 project 及它依赖的四个 production projects，Release **0 warnings / 0 errors**。没有运行历史 tests 或 benchmark。

```powershell
# 已执行；Prepare/VerifyEvidence/Summary 均为零-Part模式。
.\scripts\test-milestone9f.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 下列三次Live已耗尽本次预算，入口拒绝重复案例。
.\scripts\test-milestone9f.ps1 -Mode Live -Case G1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9f.ps1 -Mode Live -Case G3 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9f.ps1 -Mode Live -Case G5 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9f.ps1 -Mode VerifyEvidence -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone9f.ps1 -Mode Summary -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
```

证据：`artifacts/milestone9f/` 下 freeze、prepare、catalog/schema、每案 typed program/plan/expected、state、final geometry、strict readback、projection audit、raw result/rollback、G5 evidence-verification、native-evidence-hashes、budget、revalidation matrix 和 `m9-final-acceptance.json`；日志为 `artifacts/milestone9f-*.log`。

结论：三案均 PASS，原 direction capability blocker 已消除，**M9 final status COMPLETE，满足 M10 prerequisite**。M10 尚未执行。
