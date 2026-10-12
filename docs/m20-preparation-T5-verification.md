# M20 Preparation T5 Verification

日期：2026-10-12 至 13（Asia/Taipei）。HEAD `c7746663070e3ab433384889b3467d8ef6735323`，分支 `v0.3-main-update`；接续并保留未提交的 T4C 工作树。

**T5: PARTIAL；开发实现与集成验证通过，Readiness: NOT_READY。** 唯一就绪缺口是独立维护者尚未冻结新 P0/外部未调优组合及其正式任务/Oracle/资源安排。没有启动正式 M20；开发结果不计入正式 4/4、每族2/3或8/12。

## 验收器修正

- `Tasks.cs` 复用 T4C 通用 v2 输入、规则和确定性完整程序。C3 含跨方向孔和末端圆角，移除旧 `Program=null` / 无条件 native skip。保留生产 preflight 的真实拒绝。
- `Native.cs` 改用 `GenericPartSession` 公开批准执行、Edit、Open 与生产持久化；不再直接创建裸 Context 并将仅读 JSON 当作恢复。创建/指定编辑 R1/R2、第二控制器恢复后再编辑 R3、第三控制器独立读 R3 分别留证。
- 目标从持久化 accepted compilation 的唯一 edit role 解析到生产 `ParameterSemanticId`，再交叉核对 owner/key/qualified；不要求 live 计划使用 `revolve`、`slot0` 或某个 driver 名称。
- 三阶段 expected 参数、体积、圆柱及 bounds 独立存储。C1 主径与槽底联动；C2 仅大孔径变化；C3 编辑最高 Z52、继续编辑最高 Z50；C4 只有目标槽底变化。Oracle 不调用生产材料积分器生成 expected。
- `AcceptanceOracle.cs` 检查圆柱完整角跨度、位置、轴及环边界，C4 四条直线切线的长度/方向/端点、半圆弧连接与半径、槽底面及全部槽形。`BracketOracle.cs` 复用已通过的独立原生测量：安装面法向/面积、孔轴/中心/跨度、圆角 patch/切线、体积与 bounds；另逐项解析持久特征引用。
- `Aggregation.cs` 按同一个 package、Revision、native SHA、pointer SHA 和不同控制器配对 external/G2；缺阶段为 NOT_RUN/PARTIAL，显式区分 STOPPED/REFUSED/FAIL/PASS。完整新模型链必须有冷恢复后实际编辑及最终独立读回；记录全部耗时，成功耗时单列。
- 脚本删除无效 `Set-Content -NoClobber`，启动标记和每版 summary 使用 CreateNew。新冻结记录实际 HEAD、源码/二进制、任务、schema/capability、Oracle、原件与开发槽位；不复制历史生成目录，不覆盖旧 summary。
- 最终收尾补充：任何 phase 有 started 而无 result 时为 STOPPED；未开始仍 NOT_RUN。安全清理完成的原生失败保留 FAIL 并允许继续下一个独立槽；资源/所有权失败仍停止。失败任务不再盲目进入 cold/read；控制器日志也使用原子 CreateNew。该测试调度/汇总差异通过专项纯测，未改生产或重复原生成功链。
- 正式 Freeze 明确拒绝未安排独立 Held-out 的运行，开发冻结有显式 `--development` 身份。本轮没有恢复旧 acceptance 或调用 provider。

没有新增生产 Handler、Tool、Native Subtype、Scalar 资格或容差变化。本轮新增/修改仅验收代码和脚本；既有 T4C 生产差异不是 T5 新改动。

## 开发日程与失败保护

首次 `development_v2/C1-deterministic` 已通过公开创建/发布 R1，但 Oracle 对完整圆环调用空的 start/end vertex，发生测试空引用。保留 native-result FAIL、全部步骤与 R1 快照；不得改写为完整任务 PASS。修正为完整圆环允许无顶点，但 capsule 半圆仍必须有两个正确连接点。

`development_v4/freeze.json` 独立冻结既有 R1 native/pointer 和公开创建证据，原生续验前重新核验。新槽独立证明 R1 完整几何，再公开编辑 R2；不重建 C1，也不将磁盘 R1 直接转换为旧槽 PASS。`development_v1/v3` 及预检失败保留，未消耗原生打开。

`development_v4/core-external` 的四公开 Scalar、双孔 EditSet、真实 first-Setter 故障回滚、非法 old value 拒绝、完整 Oracle及保存均完成；收尾临时元数据控制器使用 managed 模式读取 external manifest，正确触发 `MODE_MISMATCH`。该原始 controller 27908 的 **FAIL 保留**，不是生产缺陷。v5仅更正测试读取器接口，按旧四Scalar/batch/fault原始步骤、R5 Native/Pointer/expected SHA对账，生成明确标注 `reconciled-successful-steps.json`，再由21964独立公开冷读R5补齐。没有重复原生编辑、改写旧 FAIL、或将该旧失败控制器计为完整成功耗时。

| 家族 | 指定编辑 | 恢复后继续编辑 | 本轮完整链 |
|---|---|---|---|
| C1 | middle radius 12→13；槽底 10→11，depth=2 | 13→12.5；槽底 10.5 | PASS；26404/22364/25708 三控制器、R2/R3/R3 |
| C2 | stepped bore radius 12→13，其他内外径保持 | 13→11 | PASS；8412/25036/30712，R2/R3/R3 |
| C3 | leg height 40→44，孔/安装面/圆角保持 | 44→42 | PASS；1936/29336/18772，R2/R3/R3；最高Z52→50 |
| C4 | 一个槽 depth 3→4，其他两个仍 3 | 4→3.5 | PASS；30892/19508/5556，R2/R3/R3；完整capsule连接/直边/底面通过 |

每个关键步骤即时写入 `artifacts/m20-preparation/T5/development_v4/slots/<slot>/steps/<phase>/`；每阶段结果保留 controller、package、Revision、参数、Native/Pointer SHA、资源和 cleanup。来源为开发 typed/semantic fixture，不冒充新真实模型抽样。

上述四链在v5按SHA/相同package/Revision独立配对复用，不重复执行。完整开发矩阵：[v5 summary](D:/CAD-Harness0.2/artifacts/m20-preparation/T5/development_v5/summary-20261012T170205091.json)，四族+core/manual+两readonly+G2九个聚合任务均通过；v4包含真实FAIL的旧汇总仍保留。C3额外只读宿主探针 [012-result.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T5/host_final_v1/012-result.json)再次公开恢复R3，官方GetParents/IGetParentCount、消费草图/子特征和transitive native引用证明立板与圆角宿主；0 Setter/Checkpoint，Native/Pointer不变。

| 回归 | 公开链及独立controller | 结果 |
|---|---|---|
| dev_core external | v4原始操作27908 + v5步骤对账 + 21964 cold | R0→R5；四Scalar、双孔一个Checkpoint/Revision、实际FirstEdit回滚、非法old value零Checkpoint、全几何/持久化通过；旧收尾FAIL保留 |
| manual external | v5 17328 / 27472 | R0→R5；相同公开/回滚完整链通过，Oracle使用144×96的手工规格，不用Factory尺寸 |
| equation / unknown readonly | v5 17680 / 22916 | 真正生产intake拒绝、保留可检查inventory，0 Setter/Checkpoint；不是Factory自检PASS |
| G2 legacy | v5 25520 / 18016 | 新构建、MigrateCopy、公开旧Edit 8→11.25、R1→R2及独立cold，四孔/体积/关系/原始三文件hash通过 |

## 相容证据与回归

- T4C 独立复核、42/42 纯测试及 `live_v2` 四族真实规划保留；C2/C4 mapping 只计映射，不当作此前的完整原生任务。
- T5 最终纯测 `pure/20261012T170425642/result.json`：52/52；包括原48项、两种Ledger格式、真实started/result文件的STOPPED/NOT_RUN。v4/v5原生版本48/50项亦通过；最终新增仅调度/日志/汇总逻辑，相容既有实机证据。
- 当前构建 legacy 恢复 48/48：`artifacts/milestone12/pure/20261012T154508302/results.json`；external Scalar/EditSet 86/86：`artifacts/milestone14/pure/20261012T154518350/result.json`。独立 regression-bin，不覆盖冻结 native runner。
- 复用 T4C 已跑 T1/T2/T3 23/36/31、M17 36、T4 40 和 T4B 32 项纯回归；T5 不改变其生产实现或契约。当前四族新公开循环验证共享 Generic 恢复/provenance 接入。
- 复用 T1 `durable_v2/fault_save/recover_save`、`fault_before/recover_before`、`fault_after/recover_after` 和 T3 `public_v1/full/fault/recover`：真实保存/Setter 后失败、提交点前后正确权威语义、独立恢复和 Oracle。事务/持久化/Handler 生产文件未在 T5 修改；旧失败和故意隔离的初始无权威 marker 保留。新增 external/G2 定向回归结果另记，不重跑 M14A/B 全矩阵。

## 资源与停止点

初始只读 COM 审计 `preflight_v4/native-audit.json`：进程 3028 responding、GDI 2133、空文档、所有历史 ledger owned 为空；四 Factory 原件和人工原件哈希一致、停止 M20 的 1477 个文件未变。审计明确区分纯测试的文本假 native marker 和真实初始失败隔离，未删除 marker。

最终只读审计：[final_audit_v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T5/final_audit_v1/native-audit.json)。进程3028 responding，GDI3072<7000，实际文档空、0 owned、0未解决权威恢复；初始无权威失败marker与纯测试假native marker分列保留，不声称磁盘marker为零。四Factory和人工原件hash不变，1477份停止M20记录未变；[另68份T4/T4B历史](D:/CAD-Harness0.2/artifacts/m20-preparation/T5/historical-integrity-v1.json)亦不变。

T5累计 **5创建尝试/5 Part、47 opens、52 closes、0 owned**，包括两次测试报告失败和额外只读补证。T1–T5合计 **26 Part、173 opens、199 closes**；旧M20 **5/15/20** 与M14 **0/145/145** 分账保持原值，未重置。稳定v4 168个源/DLL/必要定义按执行前hash归档；v5以后每run独立bin。早期T5共用bin的失败版本只有原始hash/步骤/堆栈，旧测试DLL曾被修复构建替换，未伪造回填；不影响旧M20/T4记录或成功稳定版本的可复核性。

生产源码与v4 native冻结仍一致；最终测试调度变化不改Handler/Native/Oracle/事务/持久化。源码与报告保留工作树，未主动commit/push，未覆盖接手的T4C差异。

**停止位置：T5 PARTIAL / NOT_READY，开发验证DEVELOPMENT_PASS。** 需要独立维护者封存新P0与外部参数组合，并在正式前接入其明确任务/Oracle和新资源冻结安排；当前formal freeze有意fail-closed。没有新生产能力缺口、没有新生产资格开放，也没有开始正式M20。详见 [readiness](D:/CAD-Harness0.2/docs/milestone-20-readiness.md)。
