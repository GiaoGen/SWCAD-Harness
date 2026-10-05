# Milestone 9 — Final acceptance

最终状态：**COMPLETE**。M9G 已使用 `a491501` 下当前 production runtime、ReferenceAxis typed plans 重新验证 G2、Held-out 和 G6_Pattern。production 与 M9F baseline 完全相同，G1/G3/G5 沿用 M9F。**所有使用 Linear/Rectangular Pattern 的 supported/negative cases 均已在当前 ReferenceAxis runtime 下验证。M10 prerequisite satisfied=true，可以直接进入 M10；未执行 M10。**

此文为更新后的当前最终汇总。M9D/M9E/M9F verification 和全部原始 evidence 保持不变；本次更新前的文档保存于 `artifacts/milestone9g/prior-m9-final-acceptance.md`。

| Case | 最终结果 | 验收依据 |
|---|---|---|
| G1：2-hole linear pattern + R3 | PASS | M9F：Planner/capability、4 native operations、strict count/spacing/direction、geometry/state/refs、rollback |
| G2：thickness 8→10、hole Ø6→Ø8 | PASS | M9G：当前 D1/D2 ReferenceAxis、创建/实际参数编辑、四孔同步、strict readback、geometry/state/refs、edit＋construction rollback |
| G3：2×3 rectangular pattern + R5 | PASS | M9F：原尺寸组合、六孔几何、D1/D2 strict readback、state、rollback |
| G4：circular disk / bolt circle | PASS | M9D：圆盘/中心孔/六孔阵列、孔径编辑、native readback、state、rollback |
| G5：2×2 rectangular pattern + 2 mm chamfer | PASS | M9F：创建/readback/geometry/state PASS；rollback 原始证据通过零-Part容差复核 |
| G6：impossible fillet | PASS（预期失败） | M9D：mutation 后失败并完整恢复 model/state |
| G6：invalid pattern count=1025 | PASS（预期拒绝） | M9G：当前 ReferenceAxis typed plan；Planner/runtime/backend preflight 因 count 上限拒绝，0 native calls / 0 Parts |
| G6：invalid placement | PASS（预期拒绝） | M9D：mutation 前拒绝、model/state 不变；未受方向契约变化影响 |
| G7：unsupported | PASS（UNSUPPORTED） | M9D：不调用 modeling backend、model/state 不变 |
| Held-out composition | PASS | M9G：137×91×13、ReferenceAxis、三通孔 Ø7→Ø9、Ø11 depth4盲孔保持、strict readback、state/refs、edit＋construction rollback |

G1/G3/G5 使用 **M9F ReferenceAxis evidence carry-forward**，本次没有重跑；M9G freeze 证明当前 production 文件集合和 SHA256 与 M9F 相同。G4、G6_Fillet、G6_Placement、G7 使用 **M9D 历史证据 carry-forward**，这些案例未受 Linear/Rectangular PatternDirection contract 变化影响。G2、Held-out、G6_Pattern 已由 M9G 当前 runtime evidence 替代旧契约下的历史验收依据。M9E 真实 RefAxis 和 G1/G5 count/spacing 编辑证据仍保留。

M9F 保持 original fixture targets、dimensions、four-operation compositions 和 relations，唯一 plan contract 改动为 PatternDirection references 使用 ReferenceAxis。production code changes **0**，未新增 capability、preset 或 case-specific production 分支；新独立 budget 下创建/关闭 **3/3 Parts**，每案立即关闭并恢复原活动文档。M9D/M9E 原始 evidence 的路径集合与 SHA256 全部保持一致。

M9G 同样保持原 fixture/参数/relations，唯一 construction-plan 改动为方向 reference types；仅执行用户指定 thickness/diameter edits。production code changes **0**，独立 budget 下创建/关闭 **2/2 Parts**，最大并发1；G6_Pattern **0 Part**。每案立即关闭、恢复原活动文档；M9D/M9E/M9F 原 evidence 的集合与 SHA256 未变。

G5 原始 test report 保留 BLOCKED，原因是测试误用浮点逐字相等；生产 rollback 已成功。体积差 `7.275957614183426e-12 mm³` 在既有 `0.001 mm³` 容差内，模型结构、state 字节、session 及 persistent identities 不变。零-Part复核及有效验收来源明确保存在新证据中，未修改 raw native reports，也未增加 native run。

Planner 使用 deterministic typed fixture responses，通过真实 production CadPlanner/capability/parser/preflight；本汇总不声称外部 LLM 生成准确率或 benchmark 结果。M9F edit projection 只做 native read/preflight/rollback capture；M9G 对 G2、Held-out 执行指定的实际 native parameter transactions，完成增量 validation、atomic commit 和真实提交失败回滚。状态与引用验证属于受控 live session，不声称 native file reopen/controller restart 恢复。

当前机器汇总：`artifacts/milestone9g/m9-final-acceptance.json`，包含 M9 status、M10 prerequisite、全部受影响案例的当前 ReferenceAxis runtime 验证标志、G2/Held-out/G6_Pattern 原始报告、M9F 有效报告及 G5 raw-status provenance、未受影响的 M9D carry-forward 和 M9G budget。旧 `artifacts/milestone9f/m9-final-acceptance.json` 保留。

详细证据与命令见 [M9G verification](milestone-9g-verification.md)、[M9F verification](milestone-9f-verification.md)、[M9E verification](milestone-9e-verification.md)、[M9D historical verification](milestone-9d-verification.md)。
