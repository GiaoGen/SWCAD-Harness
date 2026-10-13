# M20 Formal Verification

日期：2026-10-13（Asia/Taipei）。Run：`acceptance_t5b_v2`。

**M20 PARTIAL；冻结正式 releaseVerdict=FAIL；日程全部执行，23/25 聚合任务 PASS、2/25 FAIL。** 不重采失败，不修改判分或封存任务，不宣称 v0.3 CORE COMPLETE。

## Freeze And Authority

基线 `a14501ff3a5a50325975b00e78ad84daae7e88c7` + 明确记录的未提交 `T5A/adapter_v6` 候选；不是只以Git HEAD代替实际代码身份。

- candidate SHA：`6c99a7ea094831d59f8fdbd5da793e8fceea09a0a2a001768ad257b3eb25ff45`。
- maintainer_v2 package SHA：`a5c056f1029ca51490b7dbaffa0f65a618180549429cf3426364a6d1ae7b0a10`。
- freeze SHA：`5112065b0d7e300805350cac22669113f993ff40370acacc91c6ee5a96b2d46e`。
- 用户本轮明确批准18创建/140打开/15 provider机会及合理恢复追加；[human-authorization.json](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_t5b_v2/human-authorization.json)、`execution-approval.json`绑定freeze及每槽上限。本轮无需追加，统计槽均只运行一次。

29个冻结槽位全部完成；同族三次使用同一题目、Prompt、Schema、provider配置。原始响应用于公开原生入口，没有修补响应、退回typed草案或benchmark-time调优。候选427项、冻结482项、封存bundle 10项及4248份先前记录最终核验不变。v1失败、停止的旧M20及T4/T5记录保留。

## Core Results

| 族 | 确定性完整链 | 真实规划 | 真实完整任务 | 几何/参数/指定编辑/冷恢复后继续编辑/最终读回 | 成功-only原生全链均值 |
|---|---|---|---|---|---|
| C1 | 1/1 | 3/3 | 3/3 | PASS | 13.90分钟 |
| C2 | 1/1 | 3/3 | 3/3 | PASS | 8.28分钟 |
| C3 | 1/1 | 3/3 | 3/3 | PASS | 5.25分钟 |
| C4 | 1/1 | 3/3 | 3/3 | PASS | 5.54分钟 |

§16的确定性 **4/4**、真实每族≥2/3及总体≥8/12均满足，实际 **12/12**。每条完整新建任务分别保存R1、指定编辑R2、独立控制器恢复后编辑R3、第三控制器最终读取R3；Native/State/Manifest及引用一致。所有原生阶段61/61 PASS，包含外部、只读和G2，不将只读结果当可编辑成功。

逐槽原始请求、响应、调用身份、source map/preview/编译关联、API journal、Independent Oracle、Revision及三个阶段结果位于 `artifacts/milestone20/acceptance_t5b_v2/slots/<slot>/`。机器索引：[final-verification.json](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_t5b_v2/final-verification.json)。正式原始判分：[summary-20261013T092703354.json](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_t5b_v2/summary-20261013T092703354.json)。

## Held-out And External

| 项目 | 实际操作与结果 |
|---|---|
| 封存P0 | 126×84×11.5板，三个独立盲胶囊槽，72°布局；左深3.2→4.35→3.85。真实原始响应、公开创建/编辑、参数化完整Oracle、独立cold/read全部PASS |
| core四Scalar | 深10→11.5；孔B Ø10→10.5；阵列3→4；间距25→22，各单Checkpoint/单Revision，完整Oracle通过 |
| 人工模型四Scalar | 深14→15.5；孔B Ø11→11.5；阵列4→3；间距26→24.5，全部PASS |
| core双目标 | B Ø10.5→11.25、C Ø6→7.25，一次真正EditSet，1 Checkpoint、R4→R5，独立cold保留R5 |
| 人工模型双目标 | B Ø11.5→12.25、C Ø5→6.25，1 Checkpoint、R4→R5，独立cold保留R5 |
| 两组首Setter后故障 | `M20_CONTROLLED_FIRST_EDIT_FAILURE`，真实mutationStarted=true；两个目标全部回滚，stateCommitted=false、R5/Pointer不变，完整Oracle及后续cold PASS |
| 封存external组合 | 原件上新工作副本，间距26→27.8（R1）；同事务B Ø11→12.8、C Ø5→6.2（R2）。1 Checkpoint/1 Revision，公开入口、完整Oracle与独立cold PASS |
| Equation / Unknown | 两个冻结只读源的真实生产拒绝及原生Oracle PASS，未授予编辑权限 |
| G2 | 新建v0.2原生源，迁移工作副本、深8→11.25、关系/孔/体积验证及独立cold R2全部PASS |

本轮external共12次成功事务（10 Scalar/批量回归动作 + 2封存动作），外加2次**期望失败且正确回滚**，并非2次意外原生失败。证据分别见各槽 `external-frozen-action`、`external-first-edit-fault` journal和cold结果。

来源边界不变：Factory是开发夹具；人工模型已参与诊断，不是新独立工程师模型。封存组合是锁后未用于实现/Prompt调优的任务组合；v1曾揭示并被离线契约拒绝、v2由用户授权改角色声明的事实保留，不称为人员盲测或未经揭示题目。角度需求/Oracle及数值未改变，满足所批准的任务级来源安排，不扩张其来源声明。

## Frozen Negative Failures

| 槽 | 冻结期望 | 实际真实结果 | 冻结判分及根因 |
|---|---|---|---|
| missing_shaft | NeedsClarification，问题含字面串 `axial_lengths` | NeedsClarification；问题为“What are the axial lengths (mm) of each shaft section…”；无preview/原生修改 | **FAIL**：`axial lengths`自然语言不匹配带下划线字面片段。`NEEDS_CLARIFICATION`；问题内容确实询问了缺失轴向长度 |
| missing_fit | NeedsClarification，问题含 `fit_tolerance` | Unsupported；`CAPABILITY_UNAVAILABLE`，明确当前能力不提供保证配合/公差建模验证；无preview/原生修改 | **FAIL**：结果类型不符冻结期望，且无所要求问题片段 |

每项只调用一次provider，原始 `plan.json`、`provider-request/response.json`、`planning-result.json`及seal保持。两项均0 Part、0 native open、0 Setter、0 Checkpoint，没有把拒绝当成建模成功。

**PRD与冻结条件必须分开。** §13/§16/§21.8允许缺关键约束时澄清或Unsupported、禁止原生修改；两项实际行为是安全的。`NegativePassed`（[FormalManifest.cs:149](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/FormalManifest.cs:149)）另外要求精确状态与问题片段，`LoadFormal`限定这两题的ExpectedStatus必须是NeedsClarification。固定oracle比PRD安全表述更窄，不能观察结果后改成任何拒绝均PASS。

证据支持**验收契约/任务与期望设计问题**，不支持“需要新配合建模功能”或“存在CAD原生编辑缺陷”的推断。没有发现模型违反“必须输出该下划线片段”的明确提示；更自然的轴长问句仍被字面匹配拒绝。下一步应在独立审阅中核对负向需求、Prompt及oracle的合同，再取得明确的新冻结安排；**本轮不修生产代码、不改题/期望、不重新抽样凑PASS、不重跑已成功的原生链。**

## Reused Safety Evidence

按照冻结manifest的3组14项断言与相容生产源码，复用默认/规则、保存/Pointer前后故障恢复、外部不同历史及引用/配置/漂移等证据。确切路径见 `freeze.formal.manifest.regression` 和 [T5C v2](D:/CAD-Harness0.2/docs/m20-preparation-T5C-v2-readiness-verification.md)。驱动/从动孔及普通/GeometryPattern的v12拒绝仍标**内部原生证据**，不升级为公开入口。

两次M20公开批量回滚补新证；BeforePointer隔离后恢复与AfterPointer已提交不可普通回滚等旧语义不变。Generic任务34次ExpectedOldValue不符均在Setter/Checkpoint前拒绝。未为本次报告重跑M14–M17或扩展测试能力。

## Telemetry And Cleanup

- provider：15次，temperature=0、16384 tokens、180s、responses/json_schema；alias deepseek-chat，实际全部deepseek-flash。169155 input / 6140 output tokens；规划合计50.11秒，0自动重试。仅发送已授权合成任务/能力/schema，无原件、原生引用或凭据正文。
- 正式执行起止14:57:10–17:27:03，约149.88分钟（不含前后审计）。主要耗时在原生完整链，具体native/cold/read及每步骤累计时间均保留；均值只取成功任务，不作效率或总体可靠性比较。
- 重建遥测：27条external真实`rebuild-return`，均成功、合计2352.10ms；64条成功事务级重建标志。**后者不是实际API总调用数。** Generic/G2没有完整物理重建调用计数或单独rebuild/validation/recovery时长，因此全局字段为null，保留为§16遥测证据缺口，不从源码推算成实测。
- 本轮18创建尝试/18 Part、127 opens、145 closes，0 owned、0当前未解决recovery marker。旧M20 5/15/20账本字节不变；M20累计23创建/142 opens/165 closes，不重置旧账本。
- 最终SOLIDWORKS PID3364响应正常、GDI5746<7000；阶段结束GDI最高5748；documents=[]。未启动或终止用户应用、未接管工程师文档，所有阶段活动文档恢复与cleanup通过。
- 五个受保护原件SHA不变；人工原件SHA仍 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`。全部具体身份见 [native-audit.json](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_t5b_v2/native-audit.json)。G2生成源的三文件哈希亦由原生阶段验证保留。

首次沙箱COM预检不可见桌面ROT，改用沙箱外**只读**预检后通过，未消耗Part/open。离线收尾helper的PowerShell别名/字典取值错误仅修在本轮artifact helper，诊断保留；不属于测量槽或候选修复，不重跑provider/native。

## Delivery And Remaining

本轮0生产源码/验收候选/Prompt/Schema/PRD修改；只新增授权、原始正式证据、审计产物及本报告，更新readiness。源工作树已有T5A差异保持，不主动提交改变受测HEAD；`git diff --check`通过。

**仍未满足：两项冻结负向硬条件，以及完整Generic/G2重建计数/细分遥测证据。** 不改变23项成功结果；T5技术准备COMPLETE不被等同于M20 release通过。可复用的CAD能力已有本轮真实正向证据，当前不需为了负向结果新增建模能力。

M18/M19、C5/C6/P1仍单列未执行；原生身份歧义未验证分支继续按已批准M14分配保留UNVERIFIED，不冒称全部v0.3 milestones完成。
