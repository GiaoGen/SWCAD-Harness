# T5A Verification

日期：2026-10-13。HEAD `a14501ff3a5a50325975b00e78ad84daae7e88c7`，分支 `v0.3-main-update`，锁定当前未提交工作树。

**阶段A：ADAPTER_COMPLETE（离线）。阶段C不依赖封存值的交付完成；阶段B及最终readiness待独立维护者包。T5仍PARTIAL / NOT_READY，M20未执行。**

## Changes

仅修改验收代码、脚本及新增envelope schema；`git diff -- src`为空。未改生产CAD能力、IR、门控、事务、容差或PRD门槛。

- `FormalManifest.cs`：有限版本manifest/package、实际HEAD/dirty源码和binary锁、顺序/来源/未调优/seal核验、明确回归断言及生产相容性。正式Prepare拒绝开发接续环境；开发样例不可Execute。
- `FormalReplay.cs`及`Native.cs`：真实路径读原始provider `response.Json`，不是expanded preview DTO；保存request/envelope/raw/IR/source-map/preview SHA。新native context重新compile/preflight/批准，0新增推理；错误、篡改或旧响应冒充新调用会拒绝。独立抽样还检查provider id、调用时间与冻结时间。
- `TaskPackages.cs`、`CapsuleOracle.cs`、`Tasks.cs`：新C4显式三阶段规格，非核心尺寸、非对称中心、旋转方向及不同初始槽深；测量完整圆柱/环边/直边端点和长度/半圆连接/槽底/体积/bounds。external从冻结typed动作和每步独立expected读取，包含未改目标，不按模型id临时选择新任务值。
- `FormalScoring.cs`：硬条件与2/3、8/12分离，4个受控FAIL可保留且release通过；未实测、provider未响应、缺阶段、错误配对/身份或资源失败不通过。negative按指定critical事实判分，Held-out独立于12分母。开发评分不升级formal release。
- `FormalBudget.cs`及生命周期：新run账本，全局及每槽跨控制器累计；creation/open在API前reservation，rollback计入，超限不调用API。正式路径无无限默认额度；旧账本不覆盖。日志/步骤采用CreateNew。
- `test-milestone20.ps1`：仅接收已封存manifest和已锁binary的Prepare；Execute另需绑定freeze的人类授权与有限批准；成功后才进入cold/read，不重试失败槽。

## Offline Evidence

最终候选目录：[adapter_v6](D:/CAD-Harness0.2/artifacts/m20-preparation/T5A/adapter_v6/result.json)。**95项断言通过，另4项旧真实响应身份/编译SHA证据记录**；原T5定向纯回归 **52/52**，见 `T5/pure/20261013T040015584/result.json`。构建0警告/0错误；`git diff --check`通过。

| 验证 | 结果 |
|---|---|
| 缺包/坏seal/已调优/封存顺序/版本/source/provider漂移/未知权限字段 | fail-closed；开发formal样例可离线冻结 |
| 4 deterministic、12 real、两类新任务、negative、external/G2配对 | 精确固定日程；同族3次prompt/schema相同；开发reuse环境拒绝 |
| 四族T4C live_v2原始JSON | 共用正式解析helper接受；不修改旧响应、不重发、不重建 |
| 原始缺失/篡改、错误DTO/request/version/source-map/IR | 全部拒绝；完整real loader另验证路径、prompt、原始provider正文及新旧时间边界 |
| 新C4及external三阶段 | development样例通过；同体积错误位置、旋转切线/半圆连接、槽底缺失及未改目标变动拒绝 |
| 8/12、每族2/3，保留4个FAIL | release评分PASS；某族1/3或7/12失败；缺Held-out/中断/资源与身份错误不得PASS |
| critical澄清 vs arbitrary/wrong refusal | 按封存缺项区分；正式负向需要真实provider响应，不假设Planner自动澄清 |
| reservation + 实际Lifecycle接口 | worker实例间累计；failure recovery耗用；全局/槽位超限在API回调前拒绝；CreateNew不可覆盖 |

完整real-loader纯测试用归档真实响应及**明确标注的离线metadata**验证接线，只有测试历史freeze时间可接受；实际新freeze拒绝旧调用。该目录 `slots/C1-real-1/offline-test-only.json` 不是正式抽样，不计真实成功率。

历史失败：adapter_v1编译错误（9项验收代码导入/作用域/target-type问题），adapter_v2缺critical自动澄清的错误测试假设；原日志/失败命名空间保留。v3–v5是中间开发验证，**最终锁为v6**，不使用它们的旧锁开展正式任务。

## Lock, Cost And Protection

候选锁：[candidate-lock.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T5A/adapter_v6/candidate-lock.json)，SHA256 `6c99a7ea094831d59f8fdbd5da793e8fceea09a0a2a001768ad257b3eb25ff45`；逐文件SHA含源码、linked tests、脚本/schema及独立bin，不复制历史生成目录。Core prompt/schema捕获在开发freeze中，新任务同一生成器将在正式Prepare离线冻结。

本轮 **0 provider调用、0 Part创建、0原生打开/关闭**。纯预算测试使用独立fake-no-native账本，不能加到原生累计。没有启动/连接SOLIDWORKS，也未宣称实时用户文档状态；既有native账本未改。
`historical-after.json`核验3840份既有T5/T4C/M20文件字节身份保持；旧M20 `native-budget.json` SHA `e4668e843349493a886949d0cf51db4490cd3797d84e59c207e8f56159d26960`，5/15/20不变。原件及Registry指纹由freeze/VerifyFreeze核验，文件哈希读取不构成原生打开。

交接：[维护者规范](D:/CAD-Harness0.2/docs/m20-preparation-T5A-maintainer-package.md)、[日程/一次性成本](D:/CAD-Harness0.2/docs/m20-preparation-T5A-schedule-and-cost.md)。提案18创建/140打开，正常完整日程127打开，最大158关闭，15次provider机会；**budgetApproval=PENDING、executionAuthorized=false**，原16/32未被此提案修改。

## Remaining

独立维护者在该锁之后选择并封存新P0和external组合、独立Oracle及来源/未调优声明；复核完整回归映射。随后仅做既定seal/输入/版本/日程校验和最终readiness，不对新任务试跑调优。技术READY、正式Execute权限/预算和正式release结果分开记录。

既有T1–T4/T5开发原生证据可按相容生产路径复用；本轮新参数化Oracle只获得离线结构验证，不声称已经测量新Held-out原生结果。没有进行T5正式验收或M20，也不重新跑M14–M17。
