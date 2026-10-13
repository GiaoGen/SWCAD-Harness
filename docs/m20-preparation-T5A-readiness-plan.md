# T5A — 正式验收接入、独立 Held-out 与 M20 就绪方案

日期：2026-10-13（Asia/Taipei）。  
审查 HEAD：`a14501f`，分支 `v0.3-main-update`；当前工作树干净。  
状态：待执行方案；本文件不启动正式 M20、不修改历史报告或 PRD 门槛。

## 1. 结论

**T5 的 DEVELOPMENT_PASS 有证据，PARTIAL / NOT_READY 也应保留。** 当前缺口集中在正式评估接入、独立未调优任务及资源安排，没有据此要求新增 CAD 生产能力或重做 T1–T4。

不过，“只缺独立维护者提供一份清单”不够准确：代码里还有尚未开放的正式流程，以及开发日程没有覆盖的真实响应回放和正式判分问题。必须一起补齐，再标 T5 COMPLETE / READY_FOR_M20。

依据：

- [T5 验证报告](D:/CAD-Harness0.2/docs/m20-preparation-T5-verification.md)。
- [现有 M20 就绪检查](D:/CAD-Harness0.2/docs/milestone-20-readiness.md)。
- [T1–T5 补齐计划](D:/CAD-Harness0.2/docs/milestone-20-preparation-plan.md) T5。
- [v0.3 PRD](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md) §14–17、§21、M20。

本轮已核对开发 v5 summary：四族、core/manual、两项只读、G2 共九个聚合任务均 PASS，formalGateMeasured=false、realCompleteTotal=0、heldOutStatus=NOT_RUN。没有把开发结果升级为正式成功率。

## 2. 代码中确认的剩余问题

| 项目 | 当前事实 | 补齐责任 |
|---|---|---|
| 正式冻结没有输入通道 | `Program.Freeze()` 首行要求 Development=true，正式 `--freeze` 无条件拒绝 | 增加受校验的正式 manifest 输入；有效任务/安排允许 Prepare，缺失时仍 fail-closed |
| 正式真实请求未排槽 | Freeze 只创建四个 deterministic 及 external/readonly/G2，没有12个 real 或 negative-planning槽 | 独立正式 schedule，恢复 C1–C4各3次、held-out、负向和证据复用映射 |
| 新 Held-out 无载入/Oracle通道 | `Tasks.Load()` 只取固定 T4C 四例；`Tasks.Geometry()` 与 capsule/Bracket Oracle含核心数字 | 增加外部任务包和参数化独立 expected descriptor，支持新的尺寸/布局 |
| 真实响应回放类型错误 | `Native.cs` 对 real 取 preview.responseJson，它是展开后的 ConstructionPlanResponse；请求 semantic v2 需要原始 SemanticConstructionDraftV2 | 回放 provider 原始 response.Json及其SHA，在新的 native context重新生成批准 |
| 正式成功率被 allPassed 前提覆盖 | `Aggregation.cs` 要所有任务均PASS，才检查2/3与8/12 | 按硬性任务门槛与真实采样率分别判分；受控失败保留但不要求12/12 |
| 负向与 Held-out 未判分 | Summarize 没有 negative-planning专用路径，heldOutStatus固定NOT_RUN | 独立计预期澄清/拒绝与held-out状态，并纳入release criteria |
| provider冻结元数据未对齐 | Freeze/VerifyFreeze 固定8192/120和direct IR全局Schema；成功语义开发使用16384/180、逐请求semantic Schema | 冻结真正使用的逐请求Prompt/Schema/配置，禁止静默降档或用错误Schema快照 |
| 资源声明未执行上限 | formal Native仍使用int.MaxValue，MaximumParts/Opens主要是说明字段 | 正式路径核验批准上限并实际约束reservation；记录完整周期和失败恢复成本 |

代码依据：

- [Program.cs:95](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/Program.cs:95)、[Tasks.cs](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/Tasks.cs)。
- [Native.cs:65](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/Native.cs:65)。
- [Aggregation.cs:61](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/Aggregation.cs:61)。
- [AcceptanceOracle.cs](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/AcceptanceOracle.cs)、[ExternalAndG2.cs](D:/CAD-Harness0.2/tests/CadHarness.CoreAcceptance.Tests/ExternalAndG2.cs)。
- [正式脚本](D:/CAD-Harness0.2/scripts/test-milestone20.ps1)。

这些是实际代码推导出的正式接入缺口，不声称已经新运行得到这些失败。开发 typed 链未触发 real 分支，也没有测试8/12的正式汇总，故九个开发任务PASS不能证明上述路径已经完成。

## 3. 执行分工与顺序

| 阶段 | 角色 | 交付 |
|---|---|---|
| A | 执行 agent | 修正式 runner/任务包接口/判分/预算；用开发可见样例做纯验证，锁定待测代码及二进制 |
| B | 独立维护者 | 按锁定能力设计新P0与外部任务，独立算Oracle并封存；不调优生产代码 |
| C | 执行 agent / 审阅者 | 接入封存包、验证完整性与执行安排，更新T5/readiness，输出具体预算与正式执行指令 |

“独立维护者”可以是另一模型任务或人，不要求第三方公司，也不要求找到新第三方模型。独立性指其任务没有被用于待测实现/提示词调优，并有可审计的来源与冻结顺序。

优先完成A后锁代码，再让B选择真实数值；不要将B的封存任务提前用于A的开发测试。当前对话可在后续承担独立维护者角色，但本方案不直接公布新的held-out数值或创建其他任务。

## 4. 阶段A：正式 Runner 接入

### 4.1 正式配置与日程

新增有限、versioned formal manifest接口，建议包含：

- run ID、manifest版本、任务包标识及hash。
- 实际HEAD、源码/二进制/脚本/Oracle指纹；有未提交差异时明确记录，不能只记commit名。
- provider endpoint、requested alias、format、temperature、输出/超时设置和凭据环境引用；不得写凭据正文。
- 每槽身份、任务类别、是否要求真实模型调用、原生阶段、配对关系、计入哪个gate、证据复用引用。
- 独立held-out包指纹、维护者/封存时间、未调优声明及待测版本。
- proposed/approved资源上限、reservation策略、实际执行授权记录。

development与formal明确分派；保留现有开发接续/对账，但formal禁止通过环境变量读取`ReusedCore`、旧R1 Continuation或`CompletedExternal`来计新的核心测量PASS。获准复用的历史证据只能满足预先指定的回归条款，不能替换12次真实抽样或新的held-out。

正式Prepare只构建/纯验证/冻结，不调用provider或打开Part。Execute须另有适用的明确指令及预算授权；不能因manifest有效就自动执行。

### 4.2 最低正式槽位

| 组别 | 安排 | 判分 |
|---|---|---|
| 核心确定性 | C1–C4各一次新完整链 | 4/4必须通过 |
| 核心真实模型 | 每族同一核心任务3次，共12次 | 每族至少2/3、总计至少8/12 |
| 新P0 held-out | 至少一个新参数/布局组合，一次真实规划及完整原生链 | 单独必须通过，不混入核心12次分母 |
| 新external held-out | 新未调优参数/布局组合；公开Edit/EditSet、独立Oracle与cold | 单独必须通过 |
| core/manual外部回归 | 四项Scalar、same-Part Batch、受控失败恢复、cold；历史复用仅限明确批准映射 | 每个必需条款有证据 |
| 不支持历史/只读 | qualification矩阵所需public拒绝或预先注明的相容历史证据 | 拒绝前无不安全修改，inventory可检查 |
| G2 | 一次新legacy构建/编辑及cold | 必须通过 |
| 输入缺失 | 至少两个缺critical输入意图或按PRD预先明确引用有效证据 | 新执行的槽验证期望澄清/拒绝、无原生修改 |
| 默认/规则与故障 | M17/T1/T3等有效证据逐条相容映射；有共享变更风险才补新槽 | 不以planning-only冒充native通过 |

核心数字和实际物理要求保持不变。held-out不能被换成缺关键尺寸后“正确拒绝”的容易任务来满足生成能力。

### 4.3 真实计划到原生执行

1. Plan保存实际请求、原始provider response.Json、完整provider envelope、expanded IR、source map及preview。
2. 原生phase读取该槽原始语义JSON，核对冻结请求及raw SHA；不要把展开后的responseJson再送到semantic parser。
3. 在新Generic session重新做完整compile/admission/preflight，建立与该native context/revision绑定的新preview/批准。
4. 比较展开结果/来源/任务要求与离线已接受结果一致，执行公开入口；该回放明确记0个新增模型调用。
5. 同一个真实结果继续执行指定编辑、冷恢复后再次编辑及最终独立读回，保留原始请求和独立override。
6. 未planned的真实槽明确记录规划失败/拒绝，0 Part；不得回退typed fixture。

汇总核心real成功还须核对冻结槽的实际provider调用、planningPassed、live provenance、请求/raw hash与原生链身份对应。不能仅凭slot.Mode=real和native PASS就把fixture回放计为真实模型成功。

增加最小离线回放测试：使用既有T4C成功真实响应原字节，经过正式real分支的公共解析函数，证明不会因错DTO被拒绝。再覆盖原始JSON缺失/篡改、错误request/version/source和IR漂移。不要重发该开发请求或重建四族旧模型。

### 4.4 任务包与独立Oracle参数化

不要为某个held-out文件新增特殊分支。新增外部Task/Expected descriptor，核心任务也可投影到该descriptor，但无需重写全部测试框架。

- P0建议先支持C4类已资格范围的三个独立槽的新尺寸/布局，包含非对称中心或不同槽深组合；选值由阶段B维护者完成。
- 任务输入、指定编辑、恢复后编辑与三阶段几何expected分开存储；live任务无需提交标准feature tree或deterministic draft。
- capsule Oracle由descriptor的宽、长、中心、宿主厚度、各槽深度及世界方向计算和检查实际圆弧/直线/槽底连接；不能保留100/60/8/3/±30等固定核心数值用于新任务。
- independent expected来源于维护者公式/规格，不调用production material/compiler生成expected，也不向模型发送Oracle答案。
- external动作从封存的typed scalar/batch描述读取，不使用`id==manual ? valueA : valueB`或固定increment代替新组合。
- 外部按明确native/semantic身份选目标，记录未编辑目标及影响关系；不同文件名不能成为不同历史的证明。

### 4.5 正式汇总修正

正式release判断分开：

```text
核心确定性4/4
AND 每族核心real完整任务数>=2（分母固定3）
AND 核心real总数>=8（分母固定12）
AND 两类新held-out通过
AND 外部/G2/只读/必要负向与复用证据条款齐全
AND 失败受控、所有权/原件/资源保护通过
```

真实采样可以有受控FAIL/REFUSED，保持原行失败并计入分母；只要门槛达到且失败安全，不能再用`statuses.All(PASS)`强制12/12。

negative-planning单独读取planning-result；正确且符合预期的NeedsClarification/Unsupported按冻结任务预期评分，不能无条件把任何拒绝算PASS，也不能要求不存在的native/cold/read结果。

heldOutStatus从实际槽及配对结果计算。缺失阶段保持NOT_RUN/PARTIAL，started无结果为STOPPED；新/旧controller、package、revision与native/pointer SHA检查继续保留。

开发DEVELOPMENT_PASS、技术READY、正式release verdict和执行状态分开表达。发生用户停止/资源中断时先报告STOPPED及未测量条件，不用局部8次成功替代完整冻结日程。

## 5. 阶段B：独立 Held-out 封存

### 5.1 维护者输入与禁用项

维护者可以读取已声明的资格域、任务包Schema、原件规格和历史公开任务，避开已用于调优的组合。不得根据待测模型试跑结果选题、重采易通过任务或修改提示词/Handler。

维护者独立核对尺寸合法、来源/依赖一致、正壁厚/相离槽等几何条件；可以做独立算术校验，不调用待测LLM、Planner/compiler/native backend对真正held-out试跑。

不是保密文件路径就自动独立。必须记录谁选择、何时选择、是否调优，以及锁定的代码/提示词版本。

### 5.2 封存包最少内容

- 新P0任务：用户意图、正常结构化锚点/规则/编辑角色、允许的布局来源；初始/指定编辑/恢复后编辑的独立几何expected。
- 新external任务：实际原件来源/hash/配置、明确目标身份、scalar和至少一个same-Part多目标动作、未改特征、独立规格/geometry expected、cold expected。
- 每任务的名称/类别、维护者、封存时间、未调优声明、source/Oracle来源；区分新任务组合与已公开原件。
- payload文件逐项SHA256、manifest与seal；公共摘要可以只含类型、数量和hash，不提前暴露待测数值。
- 与阶段A锁定的source/prompt/schema版本关系和允许的能力域。

不需要新第三方Part。使用已批准人工或Factory文件时如实注明模型已公开，只有该参数/动作组合未用于调优；旧C4 held-out、旧17/3/23组合不能重新标fresh。

任务自身要求记录原始尺寸与结构事实，不能把Expected program、最终轮廓、feature顺序、模型答案或Oracle结果塞到production prompt/capability。

### 5.3 揭示后的规则

正式Prepare可验证文件hash、Schema和必要的确定输入完整性，并产生已冻结请求；不得用真正held-out进行试跑调优。

揭示后待测版本和提示词锁定。若需要任何相关修复，当前任务变为development-visible；保留该轮结果，在新版本/新正式run中重新取得未用于调优组合。不能修完继续将同题称为fresh。

接口开发和adapter验证使用另行注明的development样例，封存前完成，避免依赖真实hidden payload进行最后一轮实现。

## 6. 阶段C：成本、授权与就绪

### 6.1 预算需要重新对账

原PRD M20最多16个新Part/32次打开不能直接容纳现有完整链。采用不复用核心测量、额外一个held-out和一次G2时，至少需要计划18次新Part创建机会：16核心＋1P0 held-out＋1G2。

参考现有每个核心完整链约6次打开：核心16项约96；P0 held-out约6。使用当前外部槽上限，每组external native/cold为12＋1；core/manual两组26，新external held-out另13，两个只读2，G2约3＋1。合计是 **147次规划打开上限示例**，尚需按最终槽位验证恢复余量与额外history negatives。

这不是建议盲目重跑147次，更不是预算授权。执行者应输出由实际API生命周期/槽位逐项计算的表：创建尝试、正常打开、publish readback、独立cold、故障恢复打开、关闭和最坏上限。合法复用的回归槽可减少成本，但不能删核心12次抽样、fresh-held-out和必要冷恢复。

不要临时逐项加额度。先完成所有受影响接口和成本方案，再向用户提交一次具体正式预算/执行请求；预算修订依据PRD §17和M20条款，批准后留可引用记录。不将历史preparation不限额解释成新正式M20授权。

### 6.2 实际资源门控

- formal路径不能继续把int.MaxValue当默认预算；每次reservation前校验本轮和槽位批准上限。
- 合并同一个任务的native/cold/read成本，不能每个controller把允许值重新计成0。
- 正常失败/rollback也消耗资源；资源/owned异常停止；禁止自动重试或追加新Part。
- 生产源码、输入、封存包或冻结配置发生漂移时停止正式日程并标明未测量项；这种完整性失败不能当作普通受控规划失败后继续采样。
- 新正式run可有独立授权、独立ledger及历史账本引用，并在累计成本中汇总；不能删除/重置旧ledger或覆盖旧停止证据。修正当前formal Ledger路径时保留旧M20 5/15/20记录。
- 保持STA、GDI≥7000停止、原件工作副本、owned登记和恢复原活动文档。

### 6.3 Readiness与执行授权分开

所有技术接口、独立任务包、正式日程、预算计算及纯验证完成后，可以报告T5 COMPLETE / 技术READY_FOR_M20，同时单列`executionAuthorized=false`或预算审批状态。READY不等于已经允许Execute，也不等于正式门槛通过。

最终readiness同时列出技术矩阵与执行条件。预算批准尚缺时明确写`budgetApproval=PENDING`、`executionAuthorized=false`；技术READY只表示可提交正式执行请求，不意味着可以启动Execute。旧16/32预算没有取得新批准前继续有效，不能以技术READY覆盖它。

## 7. 本阶段纯验证与验收条件

### 7.1 必须增加的纯验证

1. 无held-out/坏seal/已调优标识/版本不符时正式Prepare拒绝；合法formal manifest可冻结，0 provider/native。
2. Formal槽位确切包含核心4 deterministic、每族3 real、fresh两类、负向/回归/复用映射；开发env接续不能渗透formal。
3. 既有真实semantic响应从正式real回放入口合法解析，错误IR DTO、篡改或丢失响应拒绝。
4. 开发可见新尺寸/布局的C4和external descriptor三阶段Oracle正确；同体积但错误槽位/弧线/未改特征拒绝。
5. 聚合：每族2成功1受控失败，总8/12且全部硬条件通过，应release PASS并保留4个失败行；总8但某族1/3应FAIL；总7/12应FAIL；缺阶段/中断不得PASS。
6. 正确critical澄清与错误/无依据拒绝区分；held-out缺失、错误配对、错误hash、资源失败阻止release。
7. 预算上限、跨controller累计、失败恢复、超限0新增打开/创建、旧ledger不可覆盖；阶段顺序与日志CreateNew保持。
8. Prompt/Schema/实际provider设置来自同一冻结来源；默认配置变化或源文件变化拒绝。

先以这些验证证明正式接口可执行，不重跑四族旧原生链或发出新的正式请求来调试控制流。没有明确共享生产风险时，旧T1–T4资格及T5开发结果复用。

### 7.2 完成矩阵

| 项目 | COMPLETE所需结果 |
|---|---|
| 开发CAD/外部/G2链 | 已有有效T5 DEVELOPMENT_PASS及相容生产版本，失败历史保持 |
| 正式runner入口 | manifest载入/任务构造/真实回放/调度/评分/预算均有代码与针对性纯证据 |
| 两类未调优组合 | 独立维护者封存，来源/版本/Oracle/hash可核验，不是作者自己试跑后选出的组合 |
| 正式日程 | 核心门槛、held-out、外部history及负向/复用映射齐全，顺序和provider配置固定 |
| 资源安排 | 完整成本表、有限上限、批准需求和旧账本保护；Execute授权单列 |
| 最终readiness | 清楚区分T5 COMPLETE、技术READY、执行批准、正式结果；无未解释NOT_RUN |

若阶段B尚无维护者，可将A完整交付为ADAPTER_COMPLETE，准确写T5仍待held-out，不停下能够独立完成的代码工作。不能仅删除Development guard就标READY，也不能因为dev52/52而忽略新增正式路径。

## 8. 交付物与可复制指令

新增报告：`docs/m20-preparation-T5A-verification.md`；readiness以新版本或明确增补更新，保留原T5 PARTIAL记录及所有旧summary。证据目录`artifacts/m20-preparation/T5A/<run-id>/`。

建议新增held-out包Schema/示例（示例均标development）、正式schedule draft、成本表、loader/Oracle/real replay/aggregation/budget纯验证。真实封存数值由阶段B另行提供，本方案不生成。

执行agent指令：

> 按 `docs/m20-preparation-T5A-readiness-plan.md` 完成阶段A及不依赖封存值的阶段C工作。复用现有生产能力，修正式manifest接入、核心12次日程、原始semantic响应回放、held-out/negative/2-of-3评分及实际预算门控；参数化新任务与独立Oracle，用development样例完成针对性纯验证。锁定候选代码/二进制并交付维护者任务包规范、正式日程和一次性成本表。保留旧T5/T4/M20证据，不调用正式provider、不打开原生Part调试正式路径、不执行M20。独立维护者包到位后，在不调优该任务的前提下完成封存校验与最终readiness，执行权限与预算批准单列。

独立维护者指令（阶段A完成后下发）：

> 按本文件阶段B及已锁定的能力/Schema，独立设计一个新P0参数布局组合和一个已批准原件上的新external动作组合，写正常需求与独立三阶段Oracle，核对未用于调优并封存hash及来源声明。不要运行待测LLM/Planner/compiler/native backend来选题，不修改实现或提示词；不给执行者标准feature tree或最终计划。公开封存摘要和包指纹，待正式任务接入时按既定流程揭示。

本轮停止于方案交付。正式M20将在技术准备和资源授权明确后作为独立任务执行；开发通过和技术READY都不能提前写成正式PASS。
