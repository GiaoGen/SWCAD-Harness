# M10C — Stepwise State Contract Closure

状态：**BLOCKED — BLOCKED_RUNTIME**。基线 `890c493c36086c07198124e29762e9d87f9ff404`。Stepwise 通用 committed-state observation/prompt 契约已实现；零 Part 实测发现现有 construction preflight 对数值等价的 `+0/-0` 使用 JSON 字符串比较，误判为修改已提交 operation。此 runtime infrastructure 修复超出本阶段范围，未执行修复或重试。

**未启动或连接 SOLIDWORKS，Parts created/closed=0/0；四案 smoke 均 NOT_RUN；正式 M10 24-Part benchmark 未执行。**

## Stepwise state contract

`SolidWorksStepwiseRuntime` 的 observation 增加四个明确字段，全部由构造器接收的 defensive committed program/CADState 派生：

- `committedOperationIds`：已提交 operation IDs。
- `committedSemanticIds`：已提交 feature/entity/parameter 的 semantic IDs，去重。
- `committedOperations`：已存在 operations 的 id、semanticId、kind。
- `healthySemanticOutputs`：entity 与 owner feature 均 Healthy 的可引用 typed outputs。

原有 revision、committed program 和 entities/geometry 保留。未更改 runtime capability projection、preflight、Binder、native handlers、transaction coordinator 或 rollback semantics。

`StepwisePlanner.CommittedStateInstructions` 明确：observation 是已存在模型的事实；下一 planned decision 必须新增 exactly one operation，operation/semantic IDs 不得冲突，不得通过新 ID 重建已有 feature，不得修改/重命名/重定义已提交 operations；已有 healthy outputs 应作为 input/reference；只有整个 intent 的几何与关系满足时才 complete。

没有下一步操作提示、G2/HeldOut 专用指导、future IDs/relations、oracle sequence 或 case examples；没有自动 rename、response repair、retry。Harness prompt/schema/catalog 保持原内容，两模式仍共用同一 DeepSeek adapter/model/settings。

`prompt-contract-diff.json` 保存纯 snapshot 的前后 observation 与新增规则。真实 `real-prompt-diff.json` 比较 M10B/M10C 的首个 Stepwise 请求，确认 intent、capability、schema 全部相同，instructions 只增加上述通用规则，observation 只增加当前 committed-state 字段。构造 observation 的生产代码没有 task/oracle/future 参数。

## Pure tests / build

**28 项 M10C-only pure tests PASS**；Release build **0 warnings / 0 errors**。覆盖本次全部状态约束及原 provider/schema qualification 边界：

- IDs/kinds/healthy outputs/revision 明确进入 observation 与真实 Planner prompt。
- 两类 ID 冲突仍被本地 validation 拒绝，不自动重命名。
- 无未来 operations、semantic IDs 或 relations；任意合法模型 IDs 可工作；已有 outputs 可绑定。
- unhealthy entity/owner 不投影为 reusable output；已有定义不能被 append 修改或重命名。
- 提前 complete 不会绕过最终 task-target validator。
- 通用规则无 case/future guidance；Harness prompt 与 M10B 真实记录逐字相同。
- 两模式共享 provider/model/settings；重复 feature/ID 的诊断保留实际 parse stage。
- 继承的 provider schema compatibility、strict envelope、capability/preflight、prefix projection 和 4-Part gate 验证。

## Harness qualification matrix

| Task / intent | Result |
|---|---|
| G2 creation | PASS |
| G2 thickness 8→10 | PASS |
| G2 hole Ø6→Ø8 | PASS |
| HeldOut creation | PASS |
| HeldOut through-hole Ø7→Ø9 | PASS |

五项均经过真实 DeepSeek json_schema、strict envelope、CadProgram parse、runtime capability validation、pure preflight 与独立 task-target validation。编辑使用对应 M9G verified snapshots，G2 第二个 edit 使用 thickness-10 snapshot。此处是零 Part 契约证据，未宣称原生创建/编辑成功。

## Stepwise decision sequences

| Task | Real decision sequence | Result |
|---|---|---|
| G2 | create_extrude → create_through_hole → create_rectangular_pattern → complete | PASS；4 calls |
| HeldOut | create_extrude → create_through_hole → create_linear_pattern（preflight rejected） | BLOCKED_RUNTIME；3 calls |

每次请求采用前一次 accepted committed observation；未将验收 composition 写成 future plan。G2 complete 后重新 parse/validate 已提交 program，再执行完整 task-target validation。HeldOut pattern 的 ID 为新的 `through_hole_pattern`，seed 引用已有 `through_hole_seed`，direction 为现有 `plate_extrude.direction_x` ReferenceAxis；三个 relations 指向正确 seed/local_frame。没有重复 operation 或 semantic ID。因本次 rejection，blind-hole/complete 决策未请求，不能对未执行的后续模型表现作结论。

## Runtime infrastructure blocker：signed zero

第三个 HeldOut response 通过 provider/schema、strict envelope、单决策 parse 和 merged capability validation，在 **pure_preflight** 返回：

```text
OPERATION_UNSUPPORTED
Construction extensions cannot move or edit existing operation definitions.
```

只读诊断使用当前 frozen IR/state DLLs 解析实际已提交 program 与实际 provider response，调用现有纯 `DesignRelationEngine.Solve`，逐项比较 prior operation 的 wire serialization。只有 seed 的一个字段不同：

| Field | Before | After relation solve |
|---|---:|---:|
| placement.xMm | -29 | -29 |
| placement.yMm | 0 | -0 |
| y IEEE bits | 0 | -9223372036854775808 |

`CenteredAbout` 对 local-Y 的零 span 求负值产生 IEEE negative zero，两个坐标数值相等。现有 `ConstructionPrograms.Plan` 对 prior operation 做 JSON 字符串比较，因 `yMm:0` 与 `yMm:-0` 不同而触发“修改已提交 operation”拒绝；其余 definition 全部一致。证据保存为 `signed-zero-preflight-diagnostic.json`，包含完整 before/after wire 和 bit pattern。

这是可复现的 runtime infrastructure false positive，**不是 genuine Stepwise planning failure**。未通过修改 prompt 要求 `-0`、改写模型 response、跳过 preflight 或更改 runtime 来让它通过。依照本阶段 scope freeze，报告 BLOCKED_RUNTIME 并停止。

## Failure counts

| Category | Count |
|---|---:|
| Duplicate operation IDs | 0 |
| Duplicate semantic IDs | 0 |
| Recreated identical committed features | 0 |
| Provider/schema rejection | 0 |
| Relation contract rejection | 0 |
| Semantic-type rejection | 0 |
| Genuine Stepwise planning failure | **0** |
| Confirmed runtime infrastructure failure | **1** |
| Local preflight rejection caused by that bug | 1 |

原始自动 audit 对任何非 provider 的模型响应后拒绝给出 preliminary planning-failure 分类，无法知道 preflight 内部的 signed-zero false positive。只读诊断之后，**最终 `final-acceptance.json` 将本次归类为 infrastructure failure=1、genuine planning failure=0**，并同步纠正最后一个 decision 的分类；原 qualification result、decision audit、raw response 与未复核的 `runner-summary.json` 均保留，未改写为 PASS。

## Native smoke matrix

| Case | Creation | Editable | Geometry | Relations | ReferenceAxis/persistent refs | CADState | Cleanup |
|---|---|---|---|---|---|---|---|
| G2 Harness | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |
| G2 Stepwise | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |
| HeldOut Harness | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |
| HeldOut Stepwise | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |

独立 native budget 最大 4 Parts，creation attempts=0、created=0、closed=0、open owned Parts=0。Native runner 的 Connect 前 gate 要求全部七项 qualification PASS；未满足，所以未进入原生阶段。Runner 使用同一真实 StepwisePlanner、同一 shared provider、原 task definitions/backends 和共享 `BenchmarkValidator`，无自动 retry。

## LLM calls / usage

| Flow | Calls | Input tokens | Output tokens |
|---|---:|---:|---:|
| Harness creation/edits | 5 | 68992 | 887 |
| G2 Stepwise 至 complete | 4 | 123820 | 387 |
| HeldOut Stepwise 至 blocker | 3 | 92263 | 357 |
| Total | **12** | **285075** | **1631** |

12 次均有 completed provider response/usage；missing usage=0，总 tokens=286706。请求模型 deepseek-chat，实际响应模型 deepseek-flash，官方 Responses endpoint、temperature=0、max_output_tokens=8192、120s timeout 与前两轮相同。所有实际 schema audits PASS；没有 provider error。Latency 仅作审计，不形成性能 benchmark 结论。

## Scope / preservation

生产修改仅为 Stepwise 通用 prompt 和 `SolidWorksStepwiseRuntime` 中的 observation projection。**CAD 执行 runtime 未改变**；该文件的 Preflight implementation 与基线逐字相同。Capabilities、SOLIDWORKS modeling handlers、Binder、transaction、rollback、benchmark task definitions/sample count、BenchmarkValidator 均未更改。

冻结后 **171 个 source files、7 个 binaries、522 个 historical files** 哈希复核不变，包括原 M10/M10A/M10B verification/evidence。没有 key 值进入新 evidence。

独立 `artifacts/milestone10c` 保存 manifest/hash、纯测试与 schema matrices、prompt diff、12 次真实 request/schema/response/usage、committed observations/snapshots、decisions/boundaries/audits/sequences、qualification result/hash、signed-zero diagnostic、0-Part ledger、runner summary、最终 acceptance 和 preservation audit。没有重复请求、完整历史 regression 或正式 benchmark。

```powershell
.\scripts\test-milestone10c.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone10c.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 使用 frozen DLL --qualify 执行授权的真实请求；本轮已失败，不 retry 或执行 Smoke。
# runtime signed-zero 诊断仅使用 frozen managed DLLs 的纯解析/求解，不连接 native。
```

**M10C BLOCKED — BLOCKED_RUNTIME。安全重新授权正式 M10 24-Part benchmark 的条件未满足。** 下一步需要另行处理 construction preflight 对数值等价 signed zero 的比较，再在新的独立 evidence 中重新 qualification；本阶段不修复该 runtime，也未运行正式 benchmark。
