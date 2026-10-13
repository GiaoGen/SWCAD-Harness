# T5C V2 Readiness Verification

日期：2026-10-13（Asia/Taipei）。**阶段C COMPLETE；T5 COMPLETE（技术准备）；READY_FOR_M20。** 正式执行未授权，预算审批 PENDING，M20 正式结果 NOT_RUN。

## Versioned Correction

用户明确授权修订封存包。维护者在未改变的 `T5A/adapter_v6` 候选下生成 `T5B/maintainer_v2`，保留 v1 包及 `T5C/intake_v1` 的真实失败。

P0 仅将 `slot_angle` 从毫米型 `preservedSources` 移出；72° 的 Given/Critical 需求、正常意图和三阶段独立 expected 不变。它继续使用已有 degree 来源及 semantic `placement.angle` / `RequirementMetric.InPlaneAngle` 契约，不添加不存在的角色字段、不授予角度编辑能力。冻结 Schema 的四个 placement 分支接受该角度键，毫米坐标字段不接受该键。独立 Oracle 的 -18°（从 +Y 表达，等价于从 +X 的72°）保持，圆弧中心、端点、切线、槽底和体积检查均未放宽。

全部数值、布局、指定编辑及冷恢复编辑保持；external payload 和独立算术 `oracle-audit.json` 与 v1 **字节一致**。新包来源明确写明：同对话、候选锁后维护者；任务已经在失败的离线接入中揭示，**不是重新选择的 unseen 任务**，也不是独立第三方模型。此前未进行 provider/native 性能筛选，没有用任务调优待测实现或提示词。本次按用户授权修订契约声明，不将旧失败改为 PASS。符合方案 §3、§5 对任务未调优的边界；无相关实现修复触发重新选题。

## Completion Matrix

| 条件 | 结果及证据 |
|---|---|
| 候选锁不变 | 427项源码/脚本/Schema/bin指纹一致；HEAD a14501f，沿用明确记录的未提交候选 |
| 任务包、来源、Oracle及封存顺序 | v2 package/manifest/seal/bundle核验通过；v1历史保留；正常需求不附标准program/draft |
| 真实离线正式接入 | 使用锁定binary执行一次 `Prepare`，通过严格读取、Normalize、角色/几何/源身份及回归映射校验；没有重建candidate |
| 角度不变量 | `angle-review.json` PASS；修改范围精确为一个列表项，冻结任务等于v2；三阶段expected不变 |
| 新纯回归 | `T5/pure/20261013T054839458/result.json` **52/52 PASS** |
| 正式adapter验证复用 | 既有 `T5A/adapter_v6/result.json` 95项断言及4条真实响应身份记录，候选未改；不冒充新任务真实规划 |
| 开发完整链与历史故障 | T5 DEVELOPMENT_PASS及T1/T3/M14相容证据按原范围复用；3组14条显式断言通过，不把内部native拒绝升级为公开入口 |
| 正式日程 | 29槽/25聚合行，4 deterministic、12 core real（每族3次）、P0/external各一新未调优组合槽、2负向、4 independent cold配对；同族3份prompt相同 |
| 固定provider设置 | deepseek-chat、responses/json_schema、temperature=0、16384 tokens、180s；逐请求Prompt/Schema已离线冻结，未连接provider |
| 资源安排 | 18创建机会、正常127/最大140 opens、正常145/最大158 closes、15 provider机会；有限全局/槽位执行门控沿用已验证candidate |
| 历史与原件保护 | 4218份先前文件未变，含v1封存包及失败；482项formal冻结身份匹配（含受保护Registry/原件）；旧M20 Ledger未改 |
| 权限/测量 | `executionAuthorized=false`，`budgetApproval=PENDING`；无execution-authorized/started、slots或新native-budget；不声称正式M20通过 |

证据：[intake_v2/verification.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T5C/intake_v2/verification.json)、[angle-review.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T5C/intake_v2/angle-review.json)、同目录 `prepare.log`、`historical-before.json`。历史层次复用说明继续适用 [T5C v1报告](D:/CAD-Harness0.2/docs/m20-preparation-T5C-readiness-verification.md) 的 Evidence Reuse Boundaries，不重复其日志。

## Fingerprints

| 文件 | SHA256 |
|---|---|
| candidate-lock.json（不变） | `6c99a7ea094831d59f8fdbd5da793e8fceea09a0a2a001768ad257b3eb25ff45` |
| maintainer_v2/package.json | `a5c056f1029ca51490b7dbaffa0f65a618180549429cf3426364a6d1ae7b0a10` |
| maintainer_v2/manifest.json | `07c2ca7a8c624200e88d58b6d2e3b5c2f22be059d2db4ef7835e1fb18cee5682` |
| maintainer_v2/bundle-seal.json | `538132242965174bcb884b230012b11cffed10027673537082858875c61d7cf2` |
| acceptance_t5b_v2/freeze.json | `5112065b0d7e300805350cac22669113f993ff40370acacc91c6ee5a96b2d46e` |

## Execution Boundary

本轮只新增维护者/审计产物与文档，候选代码、二进制、提示词生成器、Schema和PRD均未修改。**0 LLM调用、0 Part创建/打开/关闭**，未连接SOLIDWORKS；账本owned=0不是实时应用文档检查。旧M20仍5创建/15打开/20关闭。

新正式冻结目录：[acceptance_t5b_v2](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_t5b_v2/freeze.json)。下一次执行必须另有绑定此freeze SHA的人类执行、发送范围及完整资源批准。一次性提案见 [execution-request-pending.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T5C/intake_v2/execution-request-pending.json)，**不是授权文件**。原16/32限额不被准备完成覆盖，不执行 `Execute` 或正式判分。

T5 COMPLETE 仅说明接口、未调优任务包、独立Oracle、正式日程与成本准备齐全。新任务原生结果、核心4/4及真实2/3、8/12成功率全部仍待正式M20，不从离线Prepare推断。
