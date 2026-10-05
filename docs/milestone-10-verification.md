# Milestone 10 verification

状态：**BLOCKED — ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED**。逐步 agent、共享 provider、benchmark runner 和计量已实现。第一轮冻结测量已完整记录，但 20 个正式运行中，整案创建和可编辑成功均为 **0/20**，没有成功任务的性能对比样本。测量后已补齐通用 Planner 契约，当前构建与 **21 项纯测试 PASS**；修复后的真实模型/原生测量尚未执行。未执行下一阶段或历史 regression。

原始 `artifacts/milestone10/summary.json` 的 `Status=COMPLETE` 仅表示当时 runner 的 24 项测量覆盖完成，**不代表 M10 验收通过**。新的验收记录为 `artifacts/milestone10/m10-acceptance.json`，状态 BLOCKED。当前 summary runner 已分别报告 coverage 与 successful-task comparison，避免混用这两种完成状态。

## 实现与公平性

- Harness 用 `CadPlanner` 请求一个完整 CadProgram，然后确定性执行；逐步模式用 `StepwiseAgent`/`StepwisePlanner` 每次请求一个操作，执行成功后重新观察 committed CADState，再请求下一步。不是拆分预先生成的计划。完成是单独的模型决策，必须再经过共享正确性验证；最多 16 次决策，无隐式重试。
- 两种模式共享 `ChatCompletionPlanSource`、相同请求模型、temperature=0、max_tokens=8192、120 秒超时、相同 user intent、操作/轮廓/关系词汇，以及 `RelationBackend`、`TransactionalParameterBackend` 和现有 native handlers。参数编辑都是相同的一操作 Planner 请求。
- `SolidWorksStepwiseRuntime` 只投影现有能力，合并 prior + addition 后进行 capability/preflight 检查，并通过 Binder 绑定已提交输出。保留 append 不移动/编辑既有操作的约束，方向只接受现有 ReferenceAxis。观察包含已执行程序、revision、实体类型、owner、health、几何；不含未来预期计划。
- 两种模式调用同一个 `BenchmarkValidator.Validate`，检查独立几何目标、原生 count/spacing/seed/direction/reverse、实际 datum/persistent references、CADState/revision/relations/dependencies、绑定参数及编辑 capability。验证器从程序中找 owner/seed，允许任意合法语义 ID。
- G2：100×60×8，四个 Ø6 通孔，60/30 间距；后续 thickness 8→10、Ø6→Ø8。HeldOut：137×91×13，三个 Ø7 通孔，29 间距，另有 (0,27) 的 Ø11 depth4 盲孔；后续通孔 Ø7→Ø9，盲孔保持。二者沿用 M9 独立几何目标，benchmark 没有把 oracle IR 交给真实模型。

核心 runtime 修改限于可选 telemetry 和逐步观察/append projection；未增加 CAD capability、handler、preset 或按 case/尺寸选择的生产分支。事务、rollback、atomic commit 的控制顺序保持。实际所有六处 `ForceRebuild3` 调用均计数，失败调用也计数；恢复与 rollback 单独记录。阶段计时互斥，嵌套 validation/rebuild/LLM 不重复计入父阶段。LLM wall 计 provider 调用，证据写入归于其他阶段。

## 第一轮真实测量

Baseline `c6c3ef6`；SOLIDWORKS 2024 revision `32.0.1`。用户明确同意使用现有 `CAD_HARNESS_LLM_API_KEY`、DeepSeek 官方 endpoint 和 `deepseek-chat`。33 个真实 provider 响应均返回 `model=deepseek-flash`，这是官方接口对请求 alias 的实际响应；两种模式没有更换请求模型。API key 和 Authorization header 未写入 evidence。

冻结为 2 tasks × 2 modes × (1 warm-up attempt + 5 measured attempts) = **24 Parts**。按 manifest 中的交替 task/mode 顺序执行。4 个 warm-up 均排除统计，且均未整案成功。第一项 warm-up 在沙箱 socket 权限处失败，无 provider usage；无凭据 GET 连通检查在沙箱外返回 HTTP 401 后，其余 23 项按原顺序执行，未重试第一项。所有正式运行均在同一个 SOLIDWORKS process `25572`；首次网络失败的 warm-up 使用 process `17152`。

以下都是 **失败运行的中位数**，不能用作成功建模的速度优势：

| Task / mode | 正式运行 | 创建 / 可编辑成功 | LLM calls | Input / output tokens | LLM ms | Runtime ms | Validation ms | Total ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| G2 / Harness | 5 | 0 / 0 | 1 | 18020 / 346 | 2445.295 | 2382.279 | 106.343 | 4946.284 |
| G2 / Stepwise | 5 | 0 / 0 | 1 | 18074 / 71 | 2164.379 | 2482.231 | 66.995 | 4417.963 |
| HeldOut / Harness | 5 | 0 / 0 | 1 | 18040 / 592 | 3058.530 | 2252.083 | 92.109 | 5411.759 |
| HeldOut / Stepwise | 5 | 0 / 0 | 1 | 18094 / 116 | 2017.622 | 2311.606 | 64.502 | 4428.023 |

各组 rebuild/recovery/rollback 的中位数均为 0。五个逐步运行成功执行了 extrusion + through-hole 两个操作，每个实际 rebuild=5，随后 pattern 预检失败；这五项包括一个 warm-up、四个 measured runs。总共记录 **34 次 LLM 请求尝试**，33 次有真实 provider 返回；单个缺失 usage 保持 null。所有成功任务条件统计为空。未实际进入参数编辑阶段，因此本轮没有原生可编辑成功、成功最终模型、dirty/full validation speedup 或端到端加速证据。既有 M9 编辑验证不能代替 M10 benchmark 样本。

创建/关闭 **24/24 Parts**；最大同时打开的 test-owned Part 为 1；每次都恢复原活动文档，cleanup error=0。每项创建前/结束后检查 resource guard，GDI 最大 2253，低于 7000。未保存 native Part，也未关闭其他用户文档。

## 失败诊断与测量后修复

24 次运行包含 18 次 schema rejection、3 次 centered relation 引用失败、2 次 equal_spacing 引用失败、1 次网络失败。正式运行中分别为 16/2/2/0。

1. 真实输出使用 `extrude1`、`throughHoleSeed` 等 ID。现有 IR 的 native-name deny rule 拒绝 `extrude1`，而 camelCase 不符合 lowercase identifier；当时模型 schema 的 Identifier 仅给出长度，未投影已有 lexical pattern。
2. 真实输出将 `centered_about.reference` 指向 `plate.axis_y`，而该 handler 要求 LocalFrame；另有 `equal_spacing.reference` 指向方向而非种子 FeatureRef。当时 catalog 只列关系名称，没有投影关系的 subject/reference 契约。

测量期间未修改提示、目标、验证器或源码；失败预检未被绕过。测量结束后，原始源码与 DLL 先归档到 `measurement-freeze-snapshot.zip`，再做通用修复：

- `PlannerResponseSchema` 从现有 `Identifiers.SchemaPattern` 投影 root/reference lexical pattern，不改变 IR 的接受规则。
- `RuntimeCapabilityCatalog` 为已投影的五类关系补充 `relationContracts`，说明可用 subject kinds、reference type 和确定性语义。
- 两种 Planner 使用相同的 identifier/relation instructions；没有提供 case plan、尺寸分支或新 modeling capability。
- runner 支持明确指定独立 evidence 目录，拒绝覆盖既有 manifest/已尝试运行；保留真实 failure code 与 stages；summary 区分覆盖完成和成功任务对比可用。

**上述修复仅有纯测试证据，不能宣称真实 DeepSeek/native 已验证。** 第一轮 source/binary 与当前实现不同，原始 manifest、reports 和 summary 保留；当前 runner 会拒绝把修复后的源码用于原冻结目录。

## 测试、命令与 evidence

初始 M10-only 纯测试 18 PASS；修复后 21 PASS，包括两模式词汇相同、ReferenceAxis、单操作/完成 schema、identifier schema 与 IR validator 一致、关系投影、执行后观察、Binder、不可移动既有 seed、严格 envelope、失败/取消/决策上限、telemetry、原子提交失败 rollback、warm-up/schedule/median/null usage、独立编辑 oracle、共享 HTTP adapter、失败无 retry、原始响应/usage 保存。修复后 Release build 为 0 warnings / 0 errors，纯测试 0 Part。未运行完整历史测试或另一轮 native benchmark。

```powershell
.\scripts\test-milestone10.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone10.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 第一轮 native 使用冻结 DLL，逐项 --live <manifest-run-id>；需具备官方 endpoint 的网络权限。
# 第一轮 summary 已在修复前生成，不能用当前不同源码重算或改写。
.\scripts\test-milestone10.ps1 -Mode Pure -EvidenceName milestone10-contract-fix -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
```

原始 evidence：`artifacts/milestone10/manifest.json`/`.sha256`、每项 attempt、原始 prompt/schema/provider response/usage、decision、native transaction、observation、CADState、result、`native-budget.json`、`summary.json`/`.log`、`pre-benchmark-pure-results.json`、`measurement-freeze-snapshot.zip`。快照 SHA256：`658EC145255A3C1422E549FDCED9D0DE0FDA38DD414C1EC67251DAE6315F4367`。M9D/M9E/M9F/M9G 原始 evidence、historical tests、verification、M9 final acceptance 和 PRD 的路径/内容 SHA256 复核全部不变。

当前修复证据：`artifacts/milestone10-contract-fix/pure-results.json`、`pure.log`、当前 source hashes，以及新的 `artifacts/milestone10/m10-acceptance.json`。该目录尚未创建 native ledger 或冻结新的 live schedule。

要验证当前修复并完成有成功任务的对比，需要新的独立 **24 Parts maximum**，仍为两项任务/两模式各 1 warm-up + 5 measured runs；原始 evidence 不覆盖、不补写 PASS。PRD §26.5：**“Do not run it without explicit user approval.”** 当前 24 Parts 已耗尽，额外原生验证没有执行。
