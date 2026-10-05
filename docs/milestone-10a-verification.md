# M10A — LLM Contract Qualification

状态：**BLOCKED**。基线 `d95bb0bfd1bde371bebf2bc5e7dd5665a917e05c`。仅执行 M10A；零 Part gate 失败后立即停止。**未连接或启动 SOLIDWORKS；Parts created/closed = 0/0；未执行 native smoke 或正式 24-Part benchmark。**

## Provider 与本地契约

Harness、Stepwise 共用 `DeepSeekPlanSource`，内部复用 `OpenAiPlanSource` 的 Responses 协议。请求模型保持用户授权的 `deepseek-chat`，endpoint 为 `https://api.deepseek.com/responses`，沿用已有 `CAD_HARNESS_LLM_API_KEY`。temperature=0、max_output_tokens=8192、timeout=120s；无工具、无 JSON object 降级、无重试。

请求使用 `text.format.type=json_schema`、`name=cad_plan`、`strict=true`。schema 直接取自每次 Planner 请求的 `PlannerResponseSchema.Create(current runtime catalog, stepwise)`，没有第二套手写 schema。官方文档：<https://api-docs.deepseek.com/api/create-response/>。首个真实 provider 响应回显 `json_schema` 和 `strict=true`，实际响应模型为 `deepseek-flash`，与之前 M10 中同一请求 alias 的响应一致。

Provider 返回后仍经过严格 envelope、`CadProgramJson`、`RuntimeCapabilityCatalog.Validate`、pure preflight。Planner 增加 failure stage；HTTP 错误、incomplete/refusal/无效输出保留经过 key 脱敏的 raw body 和 provider usage；未知 usage 保持 null。未更改 identifier、关系或 IR 接受规则，也未更改 CAD capability、backend handlers、transactions、Binder、benchmark task definition 或共享最终验证器。

## 零 Part 实际 qualification

| Task | Mode | Intent / flow | Status | Failure stage / reason |
|---|---|---|---|---|
| G2 | Harness | creation | PASS | provider schema → strict envelope → CadProgram parse → capability → pure preflight → task target 全部通过 |
| G2 | Harness | thickness 8→10 | FAIL | provider_structured_output；HTTP 400：`An object with no properties is not allowed` |
| G2 | Harness | through-hole Ø6→Ø8 | NOT_RUN | 首个失败后停止 |
| HeldOut | Harness | creation | NOT_RUN | gate 已失败 |
| HeldOut | Harness | through-hole Ø7→Ø9 | NOT_RUN | gate 已失败 |
| G2 | Stepwise | extrude → hole → rectangular pattern → complete | NOT_RUN | gate 已失败 |
| HeldOut | Stepwise | extrude → through hole → linear pattern → blind hole → complete | NOT_RUN | gate 已失败 |

首个 creation 响应自行选择了 `base_plate`、`hole_seed`、`hole_grid` 等合法 ID，未使用固定答案 ID。ReferenceAxis D1/D2、四个 relation references、几何目标和 operation composition 均通过纯检查。**一个 creation 响应通过不代表全部计划稳定合格或原生建模通过。**

本次真实 identifier rejection **0**、relation contract rejection **0**、semantic-type contract rejection **0**；provider/schema compatibility rejection **1**。其余流程未运行，不能将这些零 rejection 数量解读为完整 qualification 通过。

## 真实失败诊断

第二次请求使用 M9G G2 verified creation state/program 投影 edit catalog，不包含新关系能力。现有 `PlannerResponseSchema` 对该模式生成：

```text
schema.properties.program.anyOf[0].properties.relations:
  type: array
  maxItems: 0
  items:
    type: object
    properties: {}
    additionalProperties: false
    required: []
```

DeepSeek 在 provider schema 校验阶段拒绝空 object，虽然该 array 的 `maxItems=0`。因此没有 edit 模型内容进入本地 parser，也未进入 mutation。Raw rejection 的 request ID 为 `13399e64-1007-4c37-9919-c05ae216fff1`。路径定位与 raw error 记录在 `artifacts/milestone10a/provider-compatibility-diagnostic.json`、`call-02-error.json`。

这是通用 schema/provider integration gap，尚不是 G2 CAD capability 缺口。按照“若 zero-Part qualification 失败，M10A BLOCKED，停止”的要求，未在冻结后修改 schema、重试请求、跳过编辑或降级协议。后续需要在现有 `PlannerResponseSchema` 内解决零关系数组的 provider-compatible 表达，再使用新的独立 qualification evidence 验证。

## Stepwise observation 与 smoke gate 实现

资格 runner 内部读取 M9G verified programs/states；每个已接受实际 Stepwise decision 合并后经过关系求解与纯检查，再根据已提交 owner 前缀筛选 Features、Entities、Parameters、Bindings，并映射模型选择的 ID。Relations/dependencies 来自实际已提交 program，不注入未来 oracle relations。模型只看到当前 committed program、revision 和有限 semantic observation；完整 oracle 不进入 provider prompt。历史 persistent references 仅标注为模拟来源，不宣称它们是新 live Part 的有效引用。

新 native 入口在 `SolidWorksConnection.Connect` 前验证冻结 source/binary/history hashes、零 Part result hash 和全部七项 PASS matrix。独立 ledger 最大 4 Parts；四个 slots 使用原始 benchmark intents/oracles、共享 provider、现有创建/编辑 backend，以及相同 `BenchmarkValidator.Validate`。每案创建和每次编辑后均需最终验证；前案失败/cleanup 失败阻止后案；每 Part 关闭并恢复原活动文档。本次没有执行该入口。

| Native smoke | Creation | Requested edits | Strict geometry/state/refs | Status |
|---|---|---|---|---|
| G2 Harness | NOT_RUN | NOT_RUN | NOT_RUN | gate blocked |
| G2 Stepwise | NOT_RUN | NOT_RUN | NOT_RUN | gate blocked |
| HeldOut Harness | NOT_RUN | NOT_RUN | NOT_RUN | gate blocked |
| HeldOut Stepwise | NOT_RUN | NOT_RUN | NOT_RUN | gate blocked |

## Calls、tokens 与验证

真实 provider 请求 **2**；收到 completed 模型响应 **1**、HTTP schema error **1**。

| Call | Stage | Input tokens | Output tokens | Result |
|---|---|---:|---:|---|
| 1 | G2 Harness creation | 29406 | 318 | PASS |
| 2 | G2 Harness thickness edit | null | null | HTTP 400 schema rejection；provider 未提供 usage |

已知 usage 为 input **29406** / output **318**，total **29724**；包含第二次请求的完整总 usage 保持 **null/unknown**，不把缺失值写成 0。LLM wall 仅记录审计，不构成 performance benchmark。

M10A-only **10 项纯测试 PASS**；Release build **0 warnings / 0 errors**。验证共享 Responses 请求准确发送现有 schema、两种模式均使用相同 adapter、temperature/limits、raw/usage 保留及 key 脱敏、失败无 fallback/retry、严格 envelope 与本地 identifier/relation rejection、任意合法 IDs 的 prefix projection、无未来观察泄漏、实际 StepwisePlanner 的合并 capability/preflight、M9G 三个 edit snapshots、提前 complete/缺关系不能通过、无 gate 时 native 入口先拒绝、仅四 smoke slots。

```powershell
.\scripts\test-milestone10a.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone10a.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 随后使用冻结 DLL --qualify；真实网络调用使用已授权 key 和网络权限。
# 此轮 qualification 已消耗，runner 拒绝覆盖或重试；不执行 Smoke。
```

独立 evidence：`artifacts/milestone10a/manifest.json`/`.sha256`、`pure-results.json`、`qualification-attempt.json`、`call-01/02-request.json`、原始 completed response/error、各次 decision/boundaries、`qualification-matrix.json`、`qualification-result.json`/`.sha256`、`provider-compatibility-diagnostic.json`、`native-budget.json`、`final-acceptance.json` 和 `preservation-audit.json`。

M9D/E/F/G、原 M10 第一轮及 contract-fix evidence 的路径/内容哈希全部不变。独立 M10A ledger 为 creation attempts **0**、created **0**、closed **0**、open owned Parts **0**。没有历史 regression 或 performance benchmark。

**M10A BLOCKED；正式 M10 24-Part benchmark 安全重新授权条件尚未具备。** 必须先解决上述共享 schema 兼容问题，重新通过完整零 Part gate，再通过全部四案 native smoke。
