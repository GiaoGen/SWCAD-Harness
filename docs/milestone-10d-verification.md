# M10D — Typed IR Equivalence Repair

基线：`41b93e52f203252594cbe9e5bded06a459ecbea5`。只执行 M10D；正式 M10 24-Part benchmark 未执行。

状态：**M10D COMPLETE**。独立 zero-Part qualification 全部 PASS，四案 native smoke 全部 PASS，**Parts created/closed=4/4**，原活动文档全部恢复。已经满足重新授权正式 M10 24-Part benchmark 的前置条件；本轮未执行正式 benchmark。

最终状态与计数见独立 `artifacts/milestone10d/final-acceptance.json`；本轮使用独立 maximum 4 Parts budget，不覆盖 M10/M10A/M10B/M10C evidence。

## 根因与修复范围

M10C Held-out 的第三个真实 Stepwise decision 正确新增 linear pattern，使用既有 seed、ReferenceAxis 和正确 relations。`CenteredAbout` 求解将既有 seed 的 `placement.yMm` 从 `+0` 转为 `-0`。数值精确相等，但 `ConstructionPrograms.Plan` 原先比较单 operation 的序列化 JSON 字符串，错误拒绝了 append。

新增通用 `CadHarness.Ir.OperationSemanticComparer`，比较 operation Id、Kind、SemanticId；按 ordinal input name 比较 input 集合，并比较每个有序 semantic reference 的 ID/type；按 ordinal parameter key set 比较参数，递归比较 ParameterKind 和当前 typed values。输入/参数映射的插入顺序不改变定义，reference 顺序仍受保护，重复 input name fail closed。

当前参数族全部覆盖：Length、Angle、Placement/Point2D、Profile（CenteredRectangle/Circle）、Count、ParameterName、递归 EditValue。有限 double 采用 `double.IsFinite(a) && double.IsFinite(b) && a == b`，没有 geometry tolerance：signed zero 相等，`1` 与 `1.0000000001` 不等；NaN/Infinity 即便与同一实例比较也返回 false。未知 parameter/profile shapes 返回 false，没有 reference-equality shortcut。

`ConstructionPrograms.Plan` 的 committed-definition protection 改用 typed comparer。**该保护不再使用 JSON-string equality**；原拒绝错误码和消息保持。现有 serializer 仍用于 defensive request copy，未更改 wire 格式，未全局规范化 `-0`。M10C fixture 的 before `0` / after `-0` wire 差异仍保留在新 regression evidence。

生产修改仅两处：新增 IR comparer，以及 construction committed-definition 比较。CAD capability、SOLIDWORKS handlers、Binder、transaction coordinator、rollback、BenchmarkValidator、任务定义、sample count 均未改变。Harness prompt/schema/provider 和 Stepwise prompt/state/future guidance 均未改变。

## Pure tests / build

Release build：**0 warnings / 0 errors**。**46 项 pure tests PASS**：继承 28 项相关 contract coverage，新增 18 项 typed equivalence regressions。没有运行完整历史 regression。

新增 coverage 包括 signed-zero placement/nested profile/EditValue、精确数值差异、真实 placement/diameter/depth/profile/count/spacing/angle 变化、operation ID/kind/semantic ID、input name/reference/type/reference order、parameter key set、递归 EditValue、未知 shapes、nonfinite、null/未知 operation/type 和重复 input 的 fail closed。

实际 M10C committed snapshot + call-12 response 已通过新的 `RelationBackend.Preflight`，同时证明原 wire 字符串仍不同而 typed definition 相等。将真实 committed seed 的 Y 从 0 移至 1 后，relation append 仍返回 `OPERATION_UNSUPPORTED: Construction extensions cannot move or edit existing operation definitions.`。G2 append 仍通过且 prior typed definitions 保持不变。

Harness G2 请求与 M10B 记录逐字一致；Held-out Stepwise 的真实 M10C call-12 prompt、intent、capability、schema 与当前使用相同 committed snapshot 构造的请求逐字一致。保留 provider schema compatibility、identifier uniqueness、relation validation、no-future-oracle、duplicate feature protection 和 4-Part gate coverage。

## Zero-Part real qualification

使用官方 DeepSeek Responses endpoint、请求模型 `deepseek-chat`、既有环境 key；Harness/Stepwise 共用原 adapter、`PlannerResponseSchema`、strict json_schema、temperature=0、max_output_tokens=8192、120s timeout。没有 fallback、response repair、rename 或 retry。实际响应 model alias 记录在 acceptance evidence。

| Harness intent | Result |
|---|---|
| G2 creation | PASS |
| G2 thickness 8→10 | PASS |
| G2 through-hole Ø6→Ø8 | PASS |
| Held-out creation | PASS |
| Held-out through-hole Ø7→Ø9 | PASS |

| Stepwise task | Actual decisions | Result |
|---|---|---|
| G2 | create_extrude → create_through_hole → create_rectangular_pattern → complete | PASS，4 calls |
| Held-out | create_extrude → create_through_hole → create_linear_pattern → create_blind_hole → complete | PASS，5 calls |

五项 Harness + 两项 Stepwise qualification 均经过 provider structured output、strict envelope、CadProgram parse、merged runtime capability validation、pure preflight、committed observation/task-target validation。complete 使用 null program envelope，另对累计 committed program 重新 parse/capability/preflight/final task-target validation。

每个 accepted Stepwise decision 后，使用 M9G 已验证 state 的 committed-prefix projection 构造下一 observation；模型自由生成 IDs，内部映射不作为未来计划进入 prompt。未来 operations、IDs、relations 不暴露给模型。零 Part 阶段没有启动或连接 SOLIDWORKS。

**14 calls，input tokens=349022，output tokens=1767，总 tokens=350789。** Genuine planning failures、infrastructure failures、provider/schema rejection、identifier/relation/type rejection、duplicate/recreated features 均为 **0**。全部七项 PASS 后才开启 native smoke。

## Native smoke 与证据

独立 schedule：G2 Harness、G2 Stepwise、Held-out Harness、Held-out Stepwise。每案 fresh real LLM creation，然后共享 `BenchmarkValidator`；G2 的 thickness/diameter 两项 edit、Held-out 的 through-hole diameter edit 均由 fresh real LLM 规划，通过现有原生事务执行，再运行同一最终 validator。

验证严格 geometry（含独立盲孔尺寸/深度不变）、native pattern count/spacing/direction/seed/reverse、design relations、真实 ReferenceAxis/persistent refs、CADState/revision/dependencies/parameter bindings、state commit 和 cleanup。保留每次 construction/edit execution result；未改 validator correctness oracle，没有新增 case-specific production branch。

每 Part 完成即 CloseDoc/discard，恢复原 active document；原 GDI/resource guard 保留，每案创建前要求没有 test-owned open Parts。没有额外 connectivity Part、retry 或 budget 扩展。

| Case | Creation | Editable | Strict geometry | Relations | ReferenceAxis/persistent refs | CADState | Cleanup |
|---|---|---|---|---|---|---|---|
| G2 Harness | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| G2 Stepwise | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Held-out Harness | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Held-out Stepwise | PASS | PASS | PASS | PASS | PASS | PASS | PASS |

原生 Stepwise 的实际 sequences 与零 Part 一致：G2 extrude → through hole → rectangular pattern → complete；Held-out extrude → through hole → linear pattern → blind hole → complete。每次新请求使用前一次原生事务成功提交的实时 observation，未拆分完整 future plan。

四个 creation attempts 创建并关闭四个 Parts；open test-owned Parts=0，各案 original active restored=true，cleanup error=null。所有源文件/二进制与历史证据 freeze 复核 PASS。

| Flow | LLM calls | Input tokens | Output tokens |
|---|---:|---:|---:|
| Zero-Part qualification | 14 | 349022 | 1767 |
| G2 Harness smoke | 3 | 36698 | 456 |
| G2 Stepwise smoke | 6 | 131112 | 526 |
| Held-out Harness smoke | 2 | 32445 | 444 |
| Held-out Stepwise smoke | 6 | 159260 | 568 |
| Total | **31** | **708537** | **3761** |

总 tokens=712298，missing usage=0。实际 provider response model 为 `deepseek-flash`（请求 `deepseek-chat` alias），两模式共享设置。全程 genuine planning failures=0、runtime infrastructure failures=0、provider/schema/identifier/relation/type/local contract rejections=0、duplicate operation/semantic IDs=0、recreated committed features=0。记录的 latency 仅用于审计，不作性能比较或速度结论。

## Preservation / reproduction

qualification 前冻结 **184 个 source files、7 个 binaries、619 个 historical files**，包括 M10/M10A/M10B/M10C verification 与 artifacts。每轮资格/原生测试前后复核 freeze；`preservation-audit.json` 记录原始 history hashes 不变。

新 evidence 位于 `artifacts/milestone10d`：manifest/hash、scope audit、pure results、schema audit matrices、signed-zero regression、每次真实 request/schema/raw response/usage、committed observations/snapshots、decisions/boundaries/audits/sequences、qualification result/hash、四案 native execution/validation、native-budget ledger、final acceptance 和 preservation audit。key 不写入 evidence。

```powershell
.\scripts\test-milestone10d.ps1 -Mode Pure -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone10d.ps1 -Mode Prepare -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
# 本轮实测使用 frozen Release DLL 的 --qualify / --smoke / --summary。
# 已消耗的资格/原生 slot 不允许重复执行，正式 benchmark 需要另行授权。
```

验收记录：[final-acceptance.json](../artifacts/milestone10d/final-acceptance.json)。
