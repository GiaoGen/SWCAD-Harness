# M10B — Provider Schema Compatibility Repair

状态：**BLOCKED**。基线 `2c2c68b99be09ebb28768ebd5df04261568358ca`。已修复并真实验证 M10A 的空 object provider schema 问题；重新 qualification 在 HeldOut Stepwise 的第三个 decision 发生 identifier uniqueness contract rejection，未达到完整 gate。

**零 Part gate 失败后立即停止；未启动或连接 SOLIDWORKS，Parts created/closed = 0/0；四案 native smoke 均未运行；未执行正式 M10 24-Part benchmark。**

## Schema 根因与修复

M10A 的 edit catalog 没有 relation capability。原 schema 虽然限制 `relations.maxItems=0`，仍给 `items` 生成 `type=object, properties={}, required=[]`。DeepSeek 在编译 provider schema 时拒绝这个无法构造的空 object，发生于 inference 前。

通用 `PlannerResponseSchema` 现在生成：

```json
"relations": {
  "type": "array",
  "maxItems": 0,
  "items": { "type": "string" }
}
```

string items 是 provider-compatible 的合法 schema；`maxItems=0` 使它无法被实际实例化。因此仍只接受空数组，未增加 relation capability。所有有关系能力的 catalog 继续投影原有 typed relation variants，identifier patterns 仍来自 `Identifiers.SchemaPattern`，未放宽本地 `CadProgramJson`、relation validation 或 capability/preflight。

新增通用 `ProviderSchemaCompatibility` 递归审计 schema construction：非空 object properties、严格 required 与 properties 一致、additionalProperties=false、非空 anyOf/enum、合法 array items/边界。它只检查 provider schema 结构，**不验证或执行模型响应，不替代本地 strict parser**。共享 `DeepSeekPlanSource` 在 HTTP 前调用审计；实际 qualification/smoke 请求还逐次保存独立 schema-audit evidence。

Harness 与 Stepwise 继续共用同一 `PlannerResponseSchema` 和 DeepSeek Responses adapter。endpoint `https://api.deepseek.com/responses`，`text.format.type=json_schema`、`strict=true`；请求模型 `deepseek-chat`，沿用已授权 key；temperature=0、max_output_tokens=8192、120s timeout。没有 JSON object fallback、固定答案、case prompt、provider retry 或 operation ID 自动改写。

## Pure tests 与 build

M10B-only **15 项纯测试 PASS**；Release build **0 warnings / 0 errors**。没有运行完整历史 regression。

其中 provider compatibility 检查覆盖 **14 个实际 Planner → shared adapter → captured HTTP 请求路径**：G2 的 Harness construction、两个 edit snapshots、Stepwise initial、两个 append 和 complete；HeldOut 的 Harness construction、edit snapshot、Stepwise initial、三个 append 和 complete。

每条路径递归审计实际发送的 schema，并核对它与当前 generator/catalog 一致；检查严格 identifier patterns、operation kinds、edit target/parameter pairs、relation kinds 与 projected relation contracts，保证 complete envelope 保留。还验证空关系数组继续由 schema 与本地 capability 禁止非空内容、坏 schema 在 HTTP 前被拒绝，以及原有 prefix observation/提前 complete/本地 parser/preflight/gate 的行为。

## Zero-Part real LLM qualification matrix

| Task | Mode | Intent / flow | Result |
|---|---|---|---|
| G2 | Harness | creation | PASS |
| G2 | Harness | thickness 8→10 | PASS |
| G2 | Harness | hole Ø6→Ø8 | PASS |
| HeldOut | Harness | creation | PASS |
| HeldOut | Harness | through-hole Ø7→Ø9 | PASS |
| G2 | Stepwise | extrude → through hole → rectangular pattern → complete | PASS（4 calls） |
| HeldOut | Stepwise | extrude → through hole → linear pattern → blind hole → complete | FAIL（第三个 decision 重复 through-hole ID；后续未运行） |

五个 Harness 响应和 G2 的三个 operation decisions 均通过 `provider json_schema → strict envelope → CadProgram parse → capability validation → pure preflight → task-target validation`。G2 complete 使用合法 null complete envelope，随后重新解析和验证已提交完整 CadProgram，再验证最终 task target。

Stepwise 每次请求使用上次已提交 observation。模拟 state 根据 M9G 已验证 state/program 筛选已提交 owner 前缀，并映射模型自行选择的 IDs；程序、relations、dependencies 来自真实已接受决策。未来 oracle operations 不进入 prompt。Historical native persistent references 仅作为模拟 provenance，未宣称是新 live Part 的有效 refs。三个 edit intents 使用对应的 M9G verified snapshots；G2 第二个 edit 使用 thickness-10 state/program。

## 首个失败的真实原因

HeldOut Stepwise 已提交两个 operation IDs：`plate_extrude`、`center_hole_seed`。第三次响应再次返回：

```text
id = center_hole_seed
kind = create_through_hole
semanticId = center_hole_seed
```

它通过 provider schema、strict envelope、单决策 CadProgram parse，但合并 prior + addition 时 `RuntimeCapabilityCatalog.Validate` 通过原有 ProgramValidator 拒绝：

```text
failureStage = runtime_capability_validation
failureCode  = PROGRAM_SCHEMA_INVALID
reason       = Duplicate operation ID.
```

本次 **provider schema rejection = 0**，**identifier uniqueness rejection = 1**，identifier lexical rejection = 0、relation contract rejection = 0、semantic-type rejection = 0。这个响应没有进入 pure preflight、task-target commit 或 native mutation。它同时重复了已提交 semantic ID，但最先被 operation ID 唯一性检查拒绝，计为一个 identifier rejection，不重复计数。

这是剩余的 Stepwise committed-observation/identifier contract qualification 失败，不是空 object schema 失败；没有证据表明需要修改 CAD runtime。按本阶段任一 zero-Part 失败必须停止的要求，没有修改 frozen planner、runtime 或 observation 来消除本次失败，没有自动重试或继续请求 pattern/blind hole/complete。

完整 raw response、前一 committed observation、decision 和 `identifier-collision-diagnostic.json` 保留。记录器继承的 lexical-only counter 未将 `Duplicate operation ID` 计入 identifier，且 rejected StepwiseDecision 不携带已解析 Program，使原 boundaries 的 CadProgramParse 字段显示 NOT_PASSED；failure stage 明确发生于 parse 之后。**最终 `final-acceptance.json` 与 diagnostic 按真实阶段将其归类为单决策 parse PASS、合并 capability FAIL、identifier uniqueness rejection=1。** 原 qualification result/boundaries 未改写；未经分类补充的 runner summary 另存为 `runner-summary.json`。

## Native smoke matrix

| Case | Creation | Editable | Strict geometry | Relations | ReferenceAxis / persistent refs | CADState | Cleanup |
|---|---|---|---|---|---|---|---|
| G2 Harness | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |
| G2 Stepwise | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |
| HeldOut Harness | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |
| HeldOut Stepwise | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN | NOT_RUN |

独立 M10B native ledger 的 maximum=4，creation attempts=0、created=0、closed=0、open owned Parts=0。Native 入口在 Connect 前检查完整 zero-Part gate 与 source/binary/history freeze；仅四个 slots，使用未改动的 task definitions、backend 和共享 `BenchmarkValidator`，每次 creation/edit 后验证；前案 failure/cleanup failure 阻止下一案。此次没有执行该入口。

## 真实 calls / usage

| Flow | Calls | Input tokens | Output tokens |
|---|---:|---:|---:|
| Harness：两项 creation + 三项 edits | 5 | 68992 | 909 |
| G2 Stepwise 至 complete | 4 | 121395 | 387 |
| HeldOut Stepwise 至首个拒绝 | 3 | 90644 | 309 |
| Total | **12** | **281031** | **1605** |

12 次请求均有 completed provider response 和 usage；missing usage=0，total tokens=282636。所有实际 response models 为 `deepseek-flash`，没有更换请求 alias `deepseek-chat`。12 份 schema audit 均 PASS，raw provider responses 均回显 json_schema/strict。没有 provider HTTP error，失败来自保留的本地 contract validation。Latency 仅记录审计，不用于 benchmark 性能结论。

## Scope 与 evidence

Production CAD runtime **未改变**：CAD capabilities、SOLIDWORKS modeling handlers、Binder、transaction coordinator、rollback semantics、BenchmarkValidator、G2/HeldOut task definitions 和 M10 sample count 全部保持基线内容。生产改动仅限 `PlannerResponseSchema`、schema construction auditor 和 shared DeepSeek adapter 的发送前审计。

新 runner/project/script 使用独立 `artifacts/milestone10b`。保留 manifest/source/binary/history freeze、纯测试与 schema-path matrices、12 次 request schemas、raw provider responses/usage、schema-audits、local decisions/boundaries、Stepwise pre-decision observations/committed snapshots、qualification matrix/result/hash、identifier diagnostic、native ledger、runner summary、最终 acceptance、preservation audit 和 provider metadata。

冻结后的 **160 个 source files、7 个 binaries、434 个 historical evidence/document files** 复核不变，包括 M10/M10A 原始 evidence 和 verification；没有 key 值进入新 evidence。

```powershell
.\scripts\test-milestone10b.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone10b.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 使用冻结 DLL --qualify，真实 provider 请求使用已授权的网络权限和 key。
# 本轮已失败；不 retry、不运行 Smoke。
# --summary 仅汇总已存在证据，不调用 provider 或 native。
```

**M10B BLOCKED。空 object provider schema 兼容修复已验证；完整 qualification 与四案 smoke 仍未全部通过。安全重新授权正式 M10 24-Part benchmark 的条件未满足，正式 benchmark 未执行。**
