# Milestone 9 — Final acceptance

最终状态：**COMPLETE**。M9D 中唯一被阻塞的 G1/G3/G5 已在 M9F 使用 `d51b20c` production runtime 和 ReferenceAxis typed plans 通过重新验收；满足 M10 prerequisite，**未执行 M10**。

此文是新的最终汇总，不改写历史 M9D/M9E verification 或原始 evidence。

| Case | 最终结果 | 验收依据 |
|---|---|---|
| G1：2-hole linear pattern + R3 | PASS | M9F：Planner/capability、4 native operations、strict count/spacing/direction、geometry/state/refs、rollback |
| G2：thickness 8→10、hole Ø6→Ø8 | PASS | M9D：创建、native edits/array propagation、state、rollback |
| G3：2×3 rectangular pattern + R5 | PASS | M9F：原尺寸组合、六孔几何、D1/D2 strict readback、state、rollback |
| G4：circular disk / bolt circle | PASS | M9D：圆盘/中心孔/六孔阵列、孔径编辑、native readback、state、rollback |
| G5：2×2 rectangular pattern + 2 mm chamfer | PASS | M9F：创建/readback/geometry/state PASS；rollback 原始证据通过零-Part容差复核 |
| G6：impossible fillet | PASS（预期失败） | M9D：mutation 后失败并完整恢复 model/state |
| G6：invalid pattern / placement | PASS（预期拒绝） | M9D：mutation 前拒绝、model/state 不变 |
| G7：unsupported | PASS（UNSUPPORTED） | M9D：不调用 modeling backend、model/state 不变 |
| Held-out composition | PASS | M9D：137×91×13、三通孔＋偏置盲孔、独立几何、通孔编辑、rollback；无生产代码修改 |

G2/G4/G6/G7/held-out 是 **M9D 历史证据 carry-forward**，本次未在 M9E runtime 上重跑。M9E 提供通用方向修复、真实 RefAxis 和 G1/G5 count/spacing 事务编辑证据；M9F 只关闭三个既知阻塞。

M9F 保持 original fixture targets、dimensions、four-operation compositions 和 relations，唯一 plan contract 改动为 PatternDirection references 使用 ReferenceAxis。production code changes **0**，未新增 capability、preset 或 case-specific production 分支；新独立 budget 下创建/关闭 **3/3 Parts**，每案立即关闭并恢复原活动文档。M9D/M9E 原始 evidence 的路径集合与 SHA256 全部保持一致。

G5 原始 test report 保留 BLOCKED，原因是测试误用浮点逐字相等；生产 rollback 已成功。体积差 `7.275957614183426e-12 mm³` 在既有 `0.001 mm³` 容差内，模型结构、state 字节、session 及 persistent identities 不变。零-Part复核及有效验收来源明确保存在新证据中，未修改 raw native reports，也未增加 native run。

Planner 使用 deterministic typed fixture responses，通过真实 production CadPlanner/capability/parser/preflight；本汇总不声称外部 LLM 生成准确率或 benchmark 结果。M9F edit projection 只做 native read/preflight/rollback capture，没有改变 nominal dimensions。状态与引用验证属于受控 live session，不声称 native file reopen/controller restart 恢复。

机器汇总：`artifacts/milestone9f/m9-final-acceptance.json`，包含 M9 status、M10 prerequisite、G1/G3/G5 有效报告及 G5 raw-status provenance、历史 carry-forward 和独立 native budget。

详细证据与命令见 [M9F verification](milestone-9f-verification.md)、[M9E verification](milestone-9e-verification.md)、[M9D historical verification](milestone-9d-verification.md)。
