# M20 Incremental Closeout

2026-10-13（Asia/Taipei）。**M20 COMPLETE，依据明确批准的有限判分纠正与遥测证据分配。** 未重跑整个 M20、未新增 CAD 能力、未改 Prompt、Schema、任务或 PRD。M18/M19、C5/C6/P1仍未执行。

## Authority And Evidence

旧 `acceptance_t5b_v2` 的冻结判分仍为 **23 PASS / 2 FAIL、releaseVerdict=FAIL**，原报告和失败不变。本结论是新命名空间 `artifacts/milestone20/incremental_closeout_v1` 的批准增量结论，不把旧总结改成25/25。

- 原确定性4/4、真实任务12/12（每族3/3）、封存组合、外部公开Scalar/EditSet/回滚/cold、只读拒绝、G2和三组14项安全断言按原范围复用。
- 新授权及证据分配：`human-authorization.json`；新源码/二进制/Oracle/任务/日程冻结：`freeze.json`、`execution-approval.json`。
- 新freeze SHA256：`5dbb11f4ec5eda30e21d6554fb57e89ea46ea89f1ed2b60845cf1e01ca90e35d`。
- 8,674份历史文件身份最终逐一校验；旧freeze、维护者包、原始provider响应、旧账本及锁定二进制保持不变。
- `source-compatibility.json` 核验113份生产源码：111份字节不变，2份仅增加既有遥测wrapper，逆向还原后精确匹配旧源码SHA。**不声称修改后的源码仍符合旧freeze路径哈希。** 新源码及二进制由新freeze绑定，旧成功证据保留其原版本身份。

## Negative Adjudication

新policy v2只在补充判分中使用；`FormalManifest.NegativePassed` 的旧严格v1路径不改。两条旧原始响应经严格生产Planner离线回放，状态、错误码、消息、问题一致，0新provider调用。

| 槽 | 原判分 | 原始行为及补充判分 |
|---|---|---|
| missing_shaft | FAIL，缺字面`axial_lengths` | 确实询问轴向长度；仅规范化单词分隔并核对真实critical缺项，PASS |
| missing_fit | FAIL，非NeedsClarification | `CAPABILITY_UNAVAILABLE`明确拒绝保证配合／公差；闭合能力词汇无相应建模／验证能力，无preview或修改，PASS |

无关问句、部分词匹配、错误错误码、泛泛Unsupported、缺少能力证明均拒绝。不是“任意拒绝都通过”，不是增加配合功能。机器证据：`negative-adjudication-v2.json`、`pure-result.json`。原题、响应和源记录没有修补或重新抽样。

## Targeted Native Regression

使用生产公开入口、原独立Oracle和持久化机制；仅C3新建一个Part，G2通过生产`MigrateCopy`复用旧不可变原件，未重新创建G2。各关键步骤立即落盘到 `slots/<slot>/steps/<phase>/`。

| 槽／阶段 | 控制器PID | 公开操作及结果 | 实际重建API次数 |
|---|---:|---|---:|
| C3-telemetry / native | 28236 | 创建R1；指定高度40→44、保存R2、完整几何PASS | 14 |
| C3-telemetry / cold | 19104 | 独立恢复R2；44→42、保存R3、完整几何PASS | 1 |
| C3-telemetry / read | 4236 | 第三控制器最终读取R3／42，引用、几何、持久化PASS | 0 |
| G2-regression / native | 15960 | 原件深8；新迁移副本公开编辑11.25、提交R2、孔／关系／体积PASS | 1 |
| G2-cold / native | 18600 | 独立恢复R2／11.25、生产绑定和完整几何PASS | 0 |

对应 `slots/C3-telemetry/{native,cold,read}-result.json`、`slots/G2-regression/native-result.json`、`slots/G2-cold/native-result.json` 均PASS。所有阶段cleanup/integrity通过，Native/State/Manifest一致，无未处理Recovery Marker。

## Telemetry Allocation

生产仅修改 `GenericFeatureConstruction.cs` 与 `PlanarSketchBackend.cs` 两处 `ForceRebuild3`，接入已有 `ExecutionTelemetry.Rebuild`。调用参数、返回值、异常、重建后的检查和安全门控不变。显式原生重建调用均经既有collector计数；测试runner启用collector并逐步骤／最终持久化快照。

新5阶段合计实测：**16次重建、重建1,651.58ms、事务Validation 42,233.14ms、exclusive Runtime 457,098.13ms**；阶段总耗时约501.17秒。Runtime包含未另行计时的恢复打开、独立测试Oracle和发布／生命周期工作；Validation仅表示被生产验证span覆盖的时间，不冒充所有测试Oracle耗时。内部嵌套span为exclusive，不能把Runtime再当总时长。

新正常路径未调用纠正Recovery或Rollback，两者次数／耗时为0，不把cold reopen算作纠正Recovery。旧故障证据继续证明安全；旧Generic/G2缺少的完整计数／细分耗时仍为 **null / UNMEASURED**，没有回填或按源码估算。按照批准分配，由新受控路径验证遥测而不补测全部旧槽，不作效率或总体可靠性宣传。

## Tests And Safety

- 增量纯断言25/25；原核心纯回归52/52，结果 `artifacts/m20-preparation/T5/pure/20261013T120524938/result.json`。新编译0警告／0错误，`git diff --check`通过。旧M14–M17完整矩阵未重跑。
- 本轮1创建尝试／1 Part、9 opens、10 closes；M20累计24创建尝试／24 Part、151 opens、175 closes。新账本与逐次reservation独立保存，旧账本字节不变；0新LLM调用。
- 最终SOLIDWORKS PID3364响应正常，GDI6036<7000，documents=[]、owned=0、无未解决恢复标记；没有启动／终止用户应用或接管工程师文档。
- 冻结原件SHA不变，人工原件仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`。最终审计 `ownership-inspect-result.json`，机器完成记录 `completion.json`。
- 冻结前沙箱编译和兼容性预检失败另保留 `pre-freeze-diagnostics.json`；修正后才冻结，没有失败槽重采或盲目重跑。

**结论：批准范围内的全部增量条件通过，M20 COMPLETE。** 旧正式FAIL、来源披露、历史遥测缺测及M18/M19的待办不被本结论掩盖。
