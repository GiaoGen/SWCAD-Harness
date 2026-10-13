# Milestone 20 Readiness

后续P1状态（2026-10-13）：**M18 Query／Preview COMPLETE；M19 Local relations / Playground COMPLETE**。M18实际公开批量、漂移／歧义拒绝、回滚及独立cold通过，见[M18验证](D:/CAD-Harness0.2/docs/milestone-18-verification.md)。M19按PRD的0-native pure/mock API及UI验收通过（54项新增API、97项旧回归、22项浏览器检查），见[M19验证](D:/CAD-Harness0.2/docs/milestone-19-verification.md)。独立C5/C6仍未验收；下文M20关闭时的M18/M19 deferred记录保留当时范围，不回填历史。

最新结论（2026-10-13批准增量收尾）：**M20 COMPLETE**。有限判分纠正及遥测证据分配已明确获批；原始两条响应离线回放／补充判分通过，25项增量断言与52项纯回归通过，C3及G2五个公开原生／独立cold阶段全部PASS。没有新provider调用、Prompt／Schema／CAD能力修改或完整M20重跑。本轮1 Part／9 opens／10 closes，原件及8,674份历史身份不变，最终owned=0、无恢复遗留。旧 `acceptance_t5b_v2` 的 **23/25及releaseVerdict=FAIL仍保留**；新结论由 `incremental_closeout_v1/completion.json`支持，不回填历史未知遥测。详见[增量收尾报告](D:/CAD-Harness0.2/docs/milestone-20-incremental-closeout.md)。M18/M19和P1仍未执行。

## 首次正式执行（历史）

最新结论（2026-10-13正式执行）：**M20 PARTIAL；冻结releaseVerdict=FAIL**。预算/执行已获明确人类批准，maintainer_v2的29槽全部完成；确定性4/4、真实完整12/12、两类封存组合、外部事务/回滚/cold及G2全部通过，但两项负向未满足冻结的精确状态/字面问句条件。23/25聚合任务PASS、2 FAIL，另披露Generic/G2全量重建计数/细分时间未实测。见 [正式报告](D:/CAD-Harness0.2/docs/milestone-20-formal-verification.md)。**T5技术准备仍COMPLETE，不能据此标M20 COMPLETE**。以下NOT_RUN/待审批/旧readiness快照保留其历史范围。

日期：2026-10-12至13。T5 技术准备，不是 M20 正式验收。

**NOT_READY。** 用户明确答复“目前没有独立冻结安排”。新的未用于调优的 P0 与外部参数/布局组合尚未由独立任务维护者冻结；不将执行 agent 自己生成并测试的组合称为独立 Held-out。开发闭环最终结果见 [T5 报告](D:/CAD-Harness0.2/docs/m20-preparation-T5-verification.md)。

## T1–T5 与增量

| 阶段 | 能力及证据 | 状态 |
|---|---|---|
| T1 | Generic Format 1、公开会话、Save3/Manifest/Pointer、跨控制器实际编辑、提交点前后恢复；T1 报告、23 项纯测试和原生 fault/recovery | COMPLETE 技术补齐 |
| T2 | Format 2、多 radial driver 精确身份、槽底 relation 联动、C1/C2 原生公开编辑与冷恢复后继续编辑；36 项纯测试 | COMPLETE 技术补齐 |
| T3 | 正交多方向材料证明、共用原 fillet Handler、Format 3、完整 C3/高度联动与故障恢复；31 项纯测试 | COMPLETE 技术补齐 |
| T4/T4C | 通用 semantic v1/v2、有限 external-recess 区段拆分、显式结构/轴/编辑角色、规划 provenance；新四族真实规划 4/4、C1 真实公开原生链、旧包兼容与42项纯验证；独立复核通过 | COMPLETE 技术补齐，不是正式成功率 |
| T5 | corrected runner/Oracle/目标身份、阶段期望、四族完整公开链/恢复后继续编辑、core/manual原子/回滚/cold、只读拒绝及G2通过；52项纯测、资源/历史审计 | PARTIAL仅缺条件6；开发DEVELOPMENT_PASS，readiness NOT_READY |

没有零件专用 Tool：既有 sketch/datum/revolve/extrude/boss/cut 和原 fillet Handler 复用。T1 增 durable session；T2 扩半截面 driver 身份与关系；T3 增有限跨轴材料/边资格；T4 增通用 schema/规则/compiler/provenance。T5 不新增生产操作或编辑资格。未经验证的 root-depth/revolve-axial-span/fillet-radius/任意 profile edit 仍关闭。

## 外部参数与历史对账

所有证据根目录为 `artifacts/milestone14/`；M14 的来源/歧义分配两项已批准修订保持不变。M20 仍要求每个 advertised external edit family 至少两种实际历史、qualified/read-only 分列；两个文件或两组尺寸不是两种历史。

| 参数 | 正向 subtype/driver/history | 不同历史与安全结果 | 直接证据及版本 |
|---|---|---|---|
| ExtrusionDepth | 原生矩形 `Extrusion`、`IExtrudeFeatureData2` blind depth、直接 native definition | 同完整几何，真正原生 equation 驱动 `D1@FF_Host=10mm`；可检查但拒绝编辑 | v13 public/cold、v22 equation refusal、v33 equivalent-direct/equation；M14 final `cd5e8e5` |
| HoleDiameter | `Cut/ICE`、唯一 single-circle driving diameter、贯穿孔；手工/Factory均为这一 bounded history | v12 将真实孔尺寸变为 driven，生产 preflight `UNSUPPORTED_PARAMETER_DRIVER`，0 Checkpoint/Mutation，恢复后完整 Oracle | v12 boundaries（内部候选边界，非公开 PASS）；公开正向 v13、v32 manual scalar/cold，最终v33 cold |
| PatternCount | `LPattern`、单方向 active feature pattern、真实 seed/方向引用、`D1TotalInstances` | v12 actual `GeometryPattern`；`UNSUPPORTED_NATIVE_SUBTYPE`、0 Checkpoint/Mutation；不是普通阵列的新尺寸 | v12 boundaries；公开正向 v13、v33 manual scalars/cold |
| PatternSpacing | 同上，`D1Spacing`，世界中心距离与未编辑 Count/Seed核验 | 同 actual GeometryPattern 历史，两参数分别拒绝；双方向/未知方向不授予资格 | v12 boundaries；公开正向 v13、v33 manual scalars/cold |

四份 Factory 是开发 API 生成；人工文件 `M14测试用夹具.SLDPRT` 是用户 GUI 建模、已参与指导/诊断。两者来源可用但不 fresh。`dev_origin` 的原点约束只补兼容性，不自动算另一建模历史。`dev_unknown_descendant` 的盲切下游与无关分支保守整板只读，不宣称局部编辑能力。A1/旧 A1 保留失败，不纳入必须强制支持的正向范围。

当前 T5 没有修改 ExternalPartSession、NativeQualificationCandidates、external Driver/Dependency 检查、事务或原生 Setter。M14 已有实际删除/压制/配置/文件漂移、native rebuild=false、发布与中断恢复证据可以按 unchanged-path 复用；新的正式日程必须明确引用它们，不能把 v12 内部候选负向改称公开入口验证。

## 正式日程方案

尚未执行、不能续用停止的 acceptance_v3。下一次 M20 任务须先满足以下安排：

1. 独立维护者在执行前封存至少一个新 P0 composition 和一个新 external 参数/布局/历史组合，附来源、是否曾调优、任务、数值、依赖、Oracle 与 SHA256。可以使用已披露用户原件；任务独立性与模型来源分别记录。旧 C4-held-out 和17/3/23外部组合均已公开，不能重复算 fresh。
2. 固定核心 C1–C4 不变，四个 typed 确定性完整任务；配置 provider 每族三个独立请求，共12个。不修旧 response，不重采失败槽、不在测量中修改 production/prompt。四族每族2/3、总体8/12门槛保持。
3. 新 P0 Held-out 槽纳入真实规划及原生完整链；外部 fresh任务也有公开 Edit/EditSet、独立 Oracle、持久化及独立控制器读回。未运行结果保持 NOT_RUN。
4. 每外部参数分别列 qualified 与 read-only 历史；直接/方程、驱动/从动孔、普通/geometry pattern 的旧原生证据已存在，但正式复用/新公开拒绝必须预先列槽，不能只把 core/manual 两文件数当 history count。保守未知依赖和错误 driver/reference 拒绝保留。
5. 一次 G2 新构建/编辑与独立 cold；关键负向/故障采用旧有效证据复用加有针对性的新公开回归。所有失败计入本次原生 Ledger，cleanup或ownership失败立即停止并保留现场。
6. 先冻结代码/生产DLL、schema/capability、provider真实配置、全部任务/Oracle、执行顺序和资源槽，再获得新的正式执行指令。本轮正式 `--freeze` 保持 fail-closed，开发 `--development` 不自动升级为正式授权。

### 资源成本

不能沿用旧 runner 的“每任务1次打开”估算：生产 Create 需要 blank reopen、create publish readback、指定 edit publish readback；独立 cold恢复+继续编辑还需要两次打开，最终独立读再一次。完整 T5同级链为每新 Part约6次打开，四族四确定性+12真实槽约96次，尚未含额外held-out、外部及G2。

原PRD M20的16个新Part/32次打开不能覆盖此完整链。用户此前允许无限 preparation次数，本轮仍不是新的正式M20指令。正式前应一次性提交完整成本及对应授权记录，或在不删独立恢复/编辑/拒绝的前提下明确复用证据槽位；不得逐次临时加额度、将其计数为零或默默改PRD上限。

## 不可声称的结果

T4C四族规划4/4不是四族原生4/4；T5开发原生通过不是正式12次模型成功率。历史M14任务级Held-out不等于新M20未调优组合。来源已批准不取消历史、引用、Oracle、事务和冷恢复条件。没有依据宣传普适任意CAD建模或性能提升。

最短后续动作：先完成/复核T5实际开发结果，然后由独立维护者提供并冻结新组合，接入并纯验证其任务/Oracle，明确新正式资源日程，另行发起M20。不要重启第三方搜索或重跑M14–M17全矩阵。

## T5A Addendum — 2026-10-13

以上T5 PARTIAL及旧汇总保留为历史。本次阶段A及不依赖封存值的阶段C已交付 **ADAPTER_COMPLETE（离线）**；正式manifest接入、原始semantic回放、2-of-3/negative/Held-out评分、参数化Oracle和实际预算门控均有针对性纯验证。旧文中的“formal freeze无条件拒绝”已由有效封存manifest入口替代；缺包或开发包仍fail-closed。

当前技术readiness仍 **NOT_READY**：独立维护者尚未封存新的两类任务及最终复用映射，不能把开发样例当Held-out。候选为 `T5A/adapter_v6`，95项新断言及52项旧定向纯回归通过。没有新provider/native执行，也未计入正式4/4、2/3或8/12。

一次性成本提案18创建/140打开（正常127打开），15次provider请求；原PRD16/32没有被改动。`budgetApproval=PENDING`、`executionAuthorized=false`。阶段B包到位后按既定协议只做封存校验/最终readiness，再单列正式执行授权，不调优该任务。

详见 [T5A验证](D:/CAD-Harness0.2/docs/m20-preparation-T5A-verification.md)、[维护者任务包](D:/CAD-Harness0.2/docs/m20-preparation-T5A-maintainer-package.md)、[日程及成本](D:/CAD-Harness0.2/docs/m20-preparation-T5A-schedule-and-cost.md)。

## T5C Intake — 2026-10-13

阶段B包已到位，旧“维护者尚未封存”的条件现已解决；但实际formal Prepare未通过。错误 `V03_CONTRACT_INVALID` 来自P0 `preservedSources` 中的 `slot_angle`（degree），而 `ValidateRequestRoles.Source` 只允许Millimeter。没有修改候选、输入/Oracle/提示词来迎合测试，也未重复抽样。原封存报告、包和失败全部保留。

427项候选指纹、4194份历史文件及三组14项回归断言保持；已锁binary本轮纯回归52/52。尚需新合格P0维护者任务包及成功的正式freeze，技术readiness仍 **NOT_READY**。external未参与相关调优，可以在后继包中如实重新封存。

18创建/140打开/15provider成本提案不变；**预算未批准、executionAuthorized=false、正式测量NOT_RUN**，没有native/provider增量或新formal freeze。下一步是替换维护者任务包的独立阶段B，不是修改生产门控或启动M20。见 [T5C报告](D:/CAD-Harness0.2/docs/m20-preparation-T5C-readiness-verification.md)。

## T5C V2 Completion — 2026-10-13

v1真实失败不变。明确人类授权后，维护者新封存 `maintainer_v2`：仅移出毫米型preservedSources中的slot_angle，保留Given/Critical degree来源、受支持placement.angle和独立三阶段旋转几何Oracle。任务已揭示的事实明确记录；未调优candidate、未做性能选题，不冒称新盲测题。

`acceptance_t5b_v2`一次离线Prepare通过：29槽/25聚合行、核心4 deterministic/12 real及两类未调优组合、2 negative、4 cold配对、482项冻结文件；52/52新纯回归、3组14项相容断言、427项candidate及4218份历史身份通过。阶段A既有95项断言和4条响应记录继续按原范围复用。无provider/native增量，无生产代码/提示词/Schema修订。

技术准备 **T5 COMPLETE / READY_FOR_M20**。freeze SHA `5112065b0d7e300805350cac22669113f993ff40370acacc91c6ee5a96b2d46e`；package SHA `a5c056f1029ca51490b7dbaffa0f65a618180549429cf3426364a6d1ae7b0a10`。正式Execute仍需绑定freeze的独立人类执行/发送及18创建、140打开、15provider完整批准；正常127打开，最大158关闭。原16/32预算不变，`executionAuthorized=false`、`budgetApproval=PENDING`，不启动M20。

## Formal Execution — 2026-10-13

用户随后明确批准前述完整预算和冻结包执行；授权绑定`acceptance_t5b_v2` freeze及各槽，并允许合理恢复追加。本轮实际18创建/127打开/145关闭、15次provider，未需追加或重采样。原件、427项candidate、482项freeze文件与4248份历史记录保持；最终文档为空、owned=0、权威恢复遗留=0。

正式结果23/25 PASS、2/25 FAIL，所有native阶段61/61 PASS。C1–C4确定性4/4、真实每族3/3（总12/12），P0/external新组合和其cold均PASS。两条负向都安全且无native修改，但`missing_shaft`自然语言轴长问题没有字面`axial_lengths`，`missing_fit`为Unsupported而非冻结NeedsClarification。保留原始FAIL，不事后放宽oracle或增加配合能力。Generic/G2的精确物理重建计数及独立细分时长另列未实测，不将事务标志冒充计数。

**M20 PARTIAL**。当前下一步是独立审阅负向判分契约和遥测缺项，不再盲目重跑正向建模、搜索第三方、修补封存响应或调优候选。任何后续安排均保留本轮正式失败，另行明确冻结与证据复用范围。详见[正式报告](D:/CAD-Harness0.2/docs/milestone-20-formal-verification.md)。
