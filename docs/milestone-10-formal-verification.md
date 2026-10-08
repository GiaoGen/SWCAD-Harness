# Milestone 10 — Formal Stepwise Baseline + Comparative Benchmark

状态：**M10 COMPLETE**。冻结生产基线 `a2edad2b22f8bd0a53b53ae8c484bda03a5d20b7`；独立 evidence：`artifacts/milestone10-formal-a2edad2`。按 PRD §24、§26、M10、§30–31 执行原 schedule，完成 **24/24 slots、创建/关闭 24/24 Parts**，最大并发 test-owned Parts=1。

**GENERALIZED CAD HARNESS v0.2 COMPLETE**。这是有限支持范围的验收，不代表任意自然语言 CAD 或模型规划零失败。正式 measured attempts 中记录了三次真实 Stepwise 规划失败，保留为 benchmark 数据。

## Prerequisite、freeze 与范围

只读核验 M10D final acceptance：五项 Harness qualification、两项 Stepwise qualification、4/4 native smoke 全部 PASS，provider/schema/identifier/relation/type rejection 和 qualification planning/infrastructure failures 为 0。本轮没有重复 M10A–M10D qualification/smoke，没有 connectivity/debug Parts。

生产代码、原 `BenchmarkData`、`CaseData`、`BenchmarkValidator`/`SharedValidation`、native lifecycle/resource guard 与 `a2edad2` 一致。**生产代码修改 0，benchmark-time tuning 0**。新增内容仅为独立 formal runner、审计/统计测试、脚本、evidence 和本文；没有修改 capability、backend、Binder、transaction、rollback、prompt/schema、任务或统计次数。

首次 benchmark Part 前冻结 **192 个 source files、7 个 binaries、880 个 historical files**，包含 M10/M10A/M10B/M10C/M10D 原始 evidence 与 verification、M9 acceptance 和 PRD。manifest/sha256、schedule、provider-config、独立 native-budget ledger 均在测量前写入。每 slot 前后复核 source/binary/history/tasks/schedule；终局 preservation audit 全部不变。原失败 M10 测量与 qualification 数据均未混入本次统计。

## 测量前 tests/build

.NET SDK 8.0.425；最终 Release build **0 warnings / 0 errors**；**23 项 benchmark 专属 pure tests PASS**，0 Parts。覆盖 fair vocabulary/ReferenceAxis、strict identifiers/relations/envelopes、单操作决策/真实执行后 observation、Binder、committed protection、bounded decisions/cancellation、无 hidden retry、实际 rebuild/recovery/rollback telemetry、atomic transaction、24-slot warm-up/measurement 结构、null usage/median、独立 edit oracle、原记录器，以及本次 failure continuation 和 successful-only statistics。

没有完整历史 regression。只读检查旧验证证据用于最终验收，不宣称本次重跑了它们。

## Fair baseline 与实际 provider

Harness 使用现有 `CadPlanner` 请求一个 bounded 高层 CadProgram 后确定性创建。Stepwise 复用 M10D 的真实 `StepwisePlanner` bounded loop，每次仅新增一个 operation，执行成功并提交后调用 `SolidWorksStepwiseRuntime.ForSession` 获取实际 observation，再请求下一 decision；completion 决策之后必须通过共享最终 validator。没有拆分完整 oracle program，没有未来 operations/IDs/relations 注入 prompt。

两模式共享：原 task intents、当前 executable runtime vocabulary、DeepSeek adapter/model/settings、RelationBackend、transaction/edit backend 与 BenchmarkValidator。G2 创建后执行 thickness 8→10 和 hole Ø6→Ø8；Held-out 创建后执行 through-hole Ø7→Ø9，独立 Ø11、depth4 盲孔保持不变。尺寸和 compositions 沿用原 benchmark task definition。

请求模型 `deepseek-chat`，实际响应 alias **`deepseek-flash`**；官方 `https://api.deepseek.com/responses`，strict `json_schema` 直接来自既有 PlannerResponseSchema，temperature=0、max_output_tokens=8192、timeout=120s、store=false、无工具/response repair/rename/retry。既有环境 key 未写入 evidence。96 次真实 provider requests/responses/usage 与 schema audits 全部保留，schema audits 均 PASS；pure recording mock 单独保留并排除于真实模型统计。

## Schedule 与成功率

原 `BenchmarkData.Schedule()` 保持不变：2 tasks × 2 modes ×（1 warm-up + 5 measured）=24。前四个 warm-ups 均成功，全部排除于 measured statistics。其余20个 measured slots 按原 counterbalanced 顺序执行，没有重跑或跳过失败 slot。

| Task | Mode | Measured attempts | Creation success | Editable-model success | Successful tasks |
|---|---|---:|---:|---:|---:|
| G2 | Harness | 5 | 5/5 (100%) | 5/5 (100%) | 5 |
| G2 | Stepwise | 5 | 5/5 (100%) | 5/5 (100%) | 5 |
| Held-out | Harness | 5 | 5/5 (100%) | 5/5 (100%) | 5 |
| Held-out | Stepwise | 5 | 2/5 (40%) | 2/5 (40%) | 2 |

Measured creation/editable success=**17/20**；Harness=10/10，Stepwise=7/10。M10 的统计最小要求是每 task/mode 五次 measured attempts，并未要求五次均成功；本轮要求满足。成功率差异是该有限样本的观测，不作总体可靠性统计推断。

## All-attempt statistics

下表均为 measured attempts 的中位数，每列 N=5。时间单位为秒；完整原始毫秒值在 summary.json。**包含失败 attempts 的统计不能用于效率结论。**

| Metric | G2 Harness | G2 Stepwise | Held-out Harness | Held-out Stepwise |
|---|---:|---:|---:|---:|
| LLM calls | 3 | 6 | 2 | 4 |
| Input tokens | 36698 | 131112 | 32436 | 124200 |
| Output tokens | 457 | 525 | 439 | 474 |
| Total tokens | 37155 | 131637 | 32875 | 124674 |
| LLM wall | 5.120 | 9.544 | 3.797 | 7.627 |
| Runtime wall | 16.824 | 17.890 | 16.418 | 14.987 |
| Validation wall | 25.702 | 24.853 | 17.731 | 5.232 |
| Rebuild wall | 2.304 | 2.888 | 2.353 | 2.222 |
| Observation wall | 0 | 0.191 | 0 | 0.144 |
| Rebuilds | 7 | 9 | 7 | 7 |
| Recoveries / recovery wall | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| Rollbacks / rollback wall | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| Total wall | 49.287 | 55.317 | 40.132 | 30.138 |

Held-out Stepwise all-attempt total median 30.138s 比 Harness 短，是三次失败提前结束造成的，**不能解释为 Stepwise 更快**。

## Successful-task statistics

仅包含 creation 和全部 requested edits 均通过共享 validator 的 runs；warm-ups 和 failure-shortened attempts 全部排除。

| Metric | G2 Harness | G2 Stepwise | Held-out Harness | Held-out Stepwise |
|---|---:|---:|---:|---:|
| Successful N | 5 | 5 | 5 | **2** |
| LLM calls | 3 | 6 | 2 | 6 |
| Input tokens | 36698 | 131112 | 32436 | 159234 |
| Output tokens | 457 | 525 | 439 | 563.5 |
| Total tokens | 37155 | 131637 | 32875 | 159797.5 |
| LLM wall (s) | 5.120 | 9.544 | 3.797 | 10.361 |
| Runtime wall (s) | 16.824 | 17.890 | 16.418 | 18.689 |
| Validation wall (s) | 25.702 | 24.853 | 17.731 | 25.977 |
| Rebuild wall (s) | 2.304 | 2.888 | 2.353 | 3.342 |
| Observation wall (s) | 0 | 0.191 | 0 | 0.178 |
| Rebuilds | 7 | 9 | 7 | 10 |
| Recoveries / recovery wall | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| Rollbacks / rollback wall | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| Total wall (s) | 49.287 | 55.317 | 40.132 | 58.546 |

偶数样本中位数可为非整数（例如 output tokens 563.5），不是伪造 usage；每次实际整数 usage 原样保留。各 phase 独立取中位数，不能把分项中位数相加当成 total 中位数。Phase times 是 exclusive；Total 包含 connection、Part lifecycle、执行期间 evidence overhead，pre/post freeze hash audit 位于 stopwatch 外，两模式定义相同。

## 失败的真实原因与继续策略

失败 slots：`14-HeldOut-Stepwise-measured-3`、`19-HeldOut-Stepwise-measured-4`、`22-HeldOut-Stepwise-measured-5`。

三案实际 decisions 相同：extrude → Ø7 through-hole seed at (-29,0) → **额外 standalone Ø7 through hole at (0,0)** → centered 3-hole linear pattern。模型第四个 decision 的阵列会在 (0,0) 生成与已提交独立孔重叠的实例，原 production preflight 返回：

```text
failureStage = pure_preflight
failureCode = OPERATION_PRECONDITION_FAILED
Managed hole instances must not intersect or touch.
```

这是 **3 次 genuine model/planning failures**，不是 signed-zero infrastructure regression。第四个 decision 通过 schema/envelope/IR/merged capability，在 mutation 前被拒绝；拒绝决策没有 mutation、rollback 或 state commit。之前三次成功 operation 的 committed program/state 和实时 observation 保留在对应目录，随后关闭/discard Part。未调用 blind-hole/complete 或 requested edit 的后续模型决策，未修复/重命名/重试。

| Failed slot | LLM calls | Input tokens | Output tokens | Actual total wall (s) |
|---|---:|---:|---:|---:|
| 14 | 4 | 124200 | 474 | 30.138 |
| 19 | 4 | 124200 | 474 | 29.008 |
| 22 | 4 | 124200 | 474 | 27.955 |

三案各 created/closed=1/1、active restored=true、cleanup error=null、freeze/resource/integrity PASS。依照用户授权继续原 schedule，未因合法 benchmark task failure 停止实验。Provider/schema/identifier/relation/semantic-type rejection=0；geometry preflight rejection=3；infrastructure/cleanup/integrity failures=0。原 decision audits 保留 `local_contract_rejection` 的边界标签与 genuine planning failure=true，run classification 为 task_attempt_failure；补充 failure-classification.json 明确模型原因，不改写原报告。

## 比较结论与统计限制

**G2** 每模式均有5次成功 measured runs。本样本中 Harness 中位数为：calls 少50%、total tokens 少71.77%、LLM wall 少46.36%、total wall 少10.90%（55.317→49.287s，median ratio 1.122）。这支持该任务/模型/机器上的观测优势，不证明任意 CAD 或模型的速度优势。

**Held-out** Stepwise 只有2个成功样本。其成功任务中位数可作为描述性结果保留，但不足以支持可靠的 timing advantage 或总体效率结论；不将它与五成功样本组混合制造总体 speedup。Calls/tokens 的实际记录仍可审计，不能把三次失败的少调用/短时间视为完成任务更省资源。

只有两种任务、一次 SOLIDWORKS 实例、一个 provider alias 和每组五 measured attempts；native/runtime/validation times 随 schedule 出现变化，counterbalanced 顺序保持原定义，但不消除环境、缓存和服务变化。没有统计显著性或全模型族结论。No recovery/rollback events，因此本次不能比较恢复性能。两模式编辑均执行 targeted transaction validation 后附加相同 full correctness oracle，本次 **没有测量 single-parameter incremental-versus-full validation speedup**，不声称满足该工程速度目标。PRD §25 的工程目标不是保证结果或强制验收数值。

## Lifecycle、usage 与完整性

24 creation attempts、created24、closed24、open test-owned Parts=0；每 Part 立即关闭/discard并恢复原活动文档。24个报告均 before/after owned count=0、响应正常、cleanup/integrity/continuation=true。SOLIDWORKS 2024 revision32.0.1，同一 PID33940；最大 observed GDI=2330，低于7000。没有额外 Part、budget 扩展或 later milestone。

| Usage | Calls | Input tokens | Output tokens |
|---|---:|---:|---:|
| Measured only | 79 | 1692307 | 9662 |
| Warm-ups only | 17 | 359558 | 1998 |
| All formal scheduled runs | **96** | **2051865** | **11660** |

missing usage=0；完整 raw provider evidence、每次 decision、execution、Stepwise observation、validation、resources、per-run result、失败分类与 aggregate statistics 均保存。M10D 的31 calls和历史第一轮 M10全部排除于本次统计。

## PRD final completion criteria

| §31 criterion | Result | Evidence |
|---|---|---|
| 1. One IR supports multiple unpreset compositions | PASS | M9 final acceptance |
| 2. G1–G5 execute without presets | PASS | M9F/M9G + unaffected M9D carry-forward |
| 3. Held-out executes without production modification | PASS | M9G；本轮 production changes0 |
| 4. Same-model edits preserve relations | PASS | M9G + 本轮成功 creation/edit validations |
| 5. Binding works on supported types | PASS | M5/M9 + 本轮 state/reference validation |
| 6. Persistent identity survives supported edits | PASS | M9G + 本轮；有限 live-session 范围 |
| 7. Incremental validation detects injected failures | PASS | M6 pure/native injection + M9C |
| 8. Failed mutations rollback safely | PASS | M6/M9C/M9 generalization evidence |
| 9. Bounded judge optional | PASS | M8；本轮无需 judge |
| 10. Fair stepwise baseline exists | PASS | 冻结真实模型/committed observations/shared backend/oracle |
| 11. Comparative metrics recorded | PASS | 本轮24 slots，五 measured attempts/task/mode |
| 12. No unsupported efficiency claim | PASS | 成功样本分离、G2有限结论、Held-out N2限制 |

历史功能证据明确 carry-forward，未为最终状态运行历史 native regression。**12/12 criteria PASS，未满足项为空；GENERALIZED CAD HARNESS v0.2 COMPLETE。** M10 COMPLETE 表示 PRD要求的公平测量、统计覆盖、证据和结论约束完成，不代表全部 baseline attempts 成功。

## 文件与复现

新增 `tests/CadHarness.FormalBenchmark.Tests` 独立 runner；原 benchmark task/data/validator/recording source 与 lifecycle helpers 通过 linked compile 共用。新增 `scripts/test-milestone10-formal.ps1`、`scripts/run-milestone10-formal.ps1`。只有测量前执行 build/pure/prepare；实际24 slots由 frozen Release DLL 执行，无测量中 rebuild。

```powershell
.\scripts\test-milestone10-formal.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone10-formal.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\run-milestone10-formal.ps1
# 本轮已完成，controller/slot attempt guards 禁止重跑；24-Part budget已耗尽。
```

主证据：`manifest.json/.sha256`、`schedule.json`、`provider-config.json`、`premeasurement-audit.json`、`pure-results.json`、24个 slot目录、`native-budget.json`、`summary.json`、`comparison.json`、`failure-classification.json`、`usage-and-environment.json`、`measurement-integrity.json`、`preservation-audit.json`、`v02-completion-criteria.json`、`final-acceptance.json`。没有覆盖原 M10/M10A–D verification 或 evidence；没有实现后续 milestone。
