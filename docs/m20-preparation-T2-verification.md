# M20 Preparation T2 Verification

日期：2026-10-12（Asia/Taipei）。基线：`145534d`，分支 `v0.3-main-update`。

**T2: COMPLETE。** 候选及公开生产入口的原生创建、指定编辑、冷恢复后继续编辑、关系传播、回滚和独立恢复均通过；没有启动 T3–T5、真实 provider 调用或正式 M20。这不代表 M20 COMPLETE。

## 实际实现与边界

- [RadialProfilePolicy.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/RadialProfilePolicy.cs)：版本 1 正交半剖面、稳定 driver ID、完整点归属及有向无环正偏移关系；检查闭合、绕向、自交、非负半径和正材料/壁厚。编辑前证明各中间形状，优先增长时大半径先改、缩小时小半径先改；无可证明顺序则拒绝。
- [RadialSketchNative.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/RadialSketchNative.cs)：真实完全约束草图、固定原点、原生水平/垂直线及点关系、轴向位置尺寸和径向尺寸；`IDimension.SetSystemValue3`，没有直接移动原生点。按精确点/线/草图/原点与原生尺寸关系绑定，不按名称、数值或近似几何猜测。
- [GenericParameterSelection.cs](D:/CAD-Harness0.2/src/CadHarness.State/GenericParameterSelection.cs) 与 [GenericFeatureConstruction.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs)：`GenericScalarCommand.ParameterSemanticId` 精确选择同类参数；旧 `Target+ParameterKey` 仅唯一匹配时兼容。主参数和派生参数共用原事务、一个 aggregate checkpoint/revision；回滚恢复实际先前读回值及整个程序/绑定。ChangedSet 比较声明驱动意图，不把未改参数的浮点噪声算成修改。
- [GenericRevisionPackage.cs](D:/CAD-Harness0.2/src/CadHarness.State/GenericRevisionPackage.cs)、[GenericContextRestore.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericContextRestore.cs)：带 radial 数据时明确 FormatVersion **2**，保存 driver/point/primitive/axis 绑定、原点、尺寸方向、transform、规则及完整状态；不含 radial 数据的旧包仍为 **1**。继续复用 T1 的 journal、save、manifest、hash、pointer 和冷恢复协议。
- `ClosedSketch.radialProfile` 是可省略的版本化扩展；内部字段仍严格。当前 program/planning-response Schema 与字段表同步生成，原 M11 数字任务及历史冻结记录未修改。旧 JSON 不要求补该字段。
- [ConstructionPlanner.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionPlanner.cs) 只修复严格 provider Schema 回归：当前投影的 `radialProfile` 为显式 null，不广告新径向规划能力。真正 Planner/需求绑定升级留给 T4。

适用域：单一闭合正交 polyline 半剖面、正径向 driver、固定轴向 station、单体整周旋转、精确 native ownership、单配置及独立尺寸；草图坐标轴须与实际 native sketch axes 对齐。**不开放轴向跨度、任意节点、任意角度/斜向轮廓、多体或外部未知历史编辑。** 偏移规则由 Harness 在同一事务内显式传播并验证，不伪称 SOLIDWORKS Equation，也不承诺手工修改源尺寸后原生自动联动。

## 原生证据

输入来自 [T2Cases.cs](D:/CAD-Harness0.2/tests/CadHarness.FeatureConstruction.Tests/T2Cases.cs)，均为开发数据，不是 held-out。独立 Oracle 直接测量官方 API 的圆柱半径/轴、完整圆周、各圆环轴向边界、六向极值、面类型及实体体积，不调用生产材料计算或 Binder。

| 要求 | 已完成证据 | 结果 |
|---|---|---|
| 三段轴径及 35–39 mm 环槽；主半径 12→13、槽底 10→11 | [candidate 编辑](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/candidate_v4/shaft/candidate_edit/029-result.json) | R1→R2，2 Setter / 1 checkpoint / 1 save；位置、其余半径和槽深 2 mm 保持 |
| 台阶内孔半径 12→13，外径/法兰保持 | [candidate 编辑](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/candidate_v4/sleeve/candidate_edit/027-result.json) | R1→R2，1 Setter / 1 checkpoint / 1 save，最小对应壁厚 4→3 mm |
| 两个新控制器恢复后再次编辑 | [shaft](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/candidate_v4/shaft/candidate_cold_edit/024-result.json)、[sleeve](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/candidate_v4/sleeve/candidate_cold_edit/022-result.json) | 两者 R2→R3，主半径 13→14；保存后独立恢复/几何通过 |
| 避免 source-first 造成零长度中间台阶的安全减小 | [新资格槽](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/qualification_v2/shaft/candidate_reverse/024-result.json) | R3→R4，14→12 / 12→10；groove→middle 顺序通过 |
| 首个实际 Setter 后故障及原子回滚 | [实际事务结果](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/qualification_v2/shaft/candidate_fault/013-edit.json) | 1 Setter 后中断，rollback=true、commit=false、Pointer/R4 不变，完整 Oracle 通过 |
| 新控制器 durable recovery | [恢复](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/qualification_v2/shaft/recover/011-result.json) | 精确 R4/native SHA 保持，原引用/参数/完整 Oracle 通过，marker 清除 |
| 独立冷读未受故障影响的 sleeve | [冷读](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/qualification_v2/sleeve/read/008-result.json) | R3、半径 14、完整 Oracle 通过 |

公开入口日程：[public-plan.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public-plan.json)。全部 10 槽通过；没有调用 Candidate API。每个控制器分别记录源码/二进制 SHA、请求、Setter 状态/立即读回、重建、提交、Oracle、生命周期和耗时。

| 公开操作 | controller / 直接证据 | 实际结果 |
|---|---|---|
| 创建两类模型 | [shaft 28544](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/shaft/create/079-result.json)、[sleeve 23416](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/create/061-result.json) | `GenericPartSession.Create/Construct`，R1，真实 Save3/保存读回/独立 Oracle |
| 指定径向主参数编辑 | [shaft 14396](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/shaft/edit/027-result.json)、[sleeve 19704](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/edit/024-result.json) | 两者 12→13，R1→R2；shaft 槽底 10→11，sleeve 对应壁厚 4→3；各 1 checkpoint / 1 revision / 1 save，ChangedParameters 精确 |
| 新控制器恢复后再编辑 | [shaft 23188](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/shaft/cold_edit/024-result.json)、[sleeve 9732](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/cold_edit/022-result.json) | 两者 13→11，R2→R3；shaft 槽底 11→9、安全 dependent-first；其余区段/轴向位置保持；保存后再次 native 重开与 Oracle |
| 公开入口 first-Setter 故障 | [controller 1272](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/shaft/fault/013-edit.json) | 11→11.5 的首个实际 Setter 后注入；仅 1 Setter/1 checkpoint、0 save，完整回滚，R3 与 Pointer 不变，完整 Oracle 通过；会话正确 quarantine |
| 独立控制器恢复与最终冷读 | [shaft 10040](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/shaft/recover/011-result.json)、[sleeve 28524](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/read/008-result.json) | 两者 R3/半径 11，参数/持久引用/完整几何通过，成功包无 recovery marker |
| 旧 Generic Format 1 原生回归 | [controller 1224](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/shaft/legacy_read/008-result.json) | T1 原 torus 包仍 R2，半径 4、独立 Oracle 通过；SHA256 `4d056391094bfee4ee01f3942fb195df060057e595118a2e81afbf9ace8f851d` 不变，未编辑 |

公开最终 native SHA256：shaft `068e40cd0d199269056812403c0e4f476d71e61eda4a57985cbb247915e4e1a9`；sleeve `d14f2501b91c3df96532f546a7e4912021d7f2d13ea0f9dff36e566d8f1af703`。源身份、轴/草图/原点/驱动引用及完整程序进入相同 manifest/pointer；不是测试程序另存文件冒充 commit。

## 失败保留与安全拒绝

- `candidate_v1`：错误点间关系标识导致真实欠约束，回滚成功，无权威 revision。连接阶段 sandbox `MK_E_UNAVAILABLE` 独立记录，0 创建/打开；正常用户会话连接后未自动启动应用。
- `candidate_v2`：备选 `ISketchRelationManager.AddRelation` 调用发生 `RPC_E_SERVERFAULT`，完整回滚；不猜测内部 COM 原因。根据本机官方 API 文档使用二维 `sgHORIZONTALPOINTS2D/sgVERTICALPOINTS2D`。
- `candidate_v3`：过强的即时检查错误要求已共享同一原生点的端点另有 coincidence；现在仅在**精确 native references 相同**时免重复关系，不按坐标猜测共享。
- `candidate_v4/shaft/candidate_fault`：实际 Setter、回滚与 R3/Pointer 保持均通过，但测试尾部错误复用已 quarantine 的生产 context。失败保留，没有补写原槽 PASS；由 [新的恢复控制器](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/candidate_v4/shaft/recover/011-result.json) 恢复 R3，再在 `qualification_v2` 验证修正测试。
- 未指定同类 driver、旧值不匹配、非法值/壁厚及未资格轴向参数在候选生产事务预检中拒绝，Setter/checkpoint/save 均为 0。公开 `shaft/edit/007–009-preflight-negative.json` 与 `sleeve/edit/007–008-preflight-negative.json` 再验证真实入口的歧义、未资格轴向参数及派生参数拒绝，全部 mutation=false、Setter/checkpoint/save=0。
- 纯测试还覆盖派生 target 直接编辑拒绝、关系环/未知 endpoint、错误 point/driver、损坏/缺失 binding、错误版本和 owner；六个点的新布局及不同参数名证明不依赖固定顶点数量或名称。

## 回归与最终审计

| 回归 | 结果 / 原始记录 |
|---|---|
| T2 | [36/36](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/pure_final/result.json) |
| T1 shared publish/恢复 | [23/23](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/t2_final_compat/result.json) |
| M11 严格契约 | [77/77](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/regression-results/m11-final.json) |
| M15 草图 | `artifacts/milestone15/pure/20261012T035301965/result.json`，25/25 |
| M16 feature/材料 | `artifacts/milestone16/pure/20261012T0337005360/result.json`，20/20 |
| M17 Planner | `artifacts/milestone17/pure/20261012T035310521/results.json`，36/36 |
| legacy recovery | `artifacts/milestone12/pure/20261012T014305363/results.json`，48/48 |
| external editing | `artifacts/milestone14/pure/20261012T014208355/result.json`，86/86 |

Release 编译零 warning/error。最后的生产变动仅为 `PreparedPlanarSketch.CoordinateDriverPolicy` 描述，纠正非圆固定坐标与原生径向尺寸的区分；不参与材料、Setter、状态或包的计算/序列化。公开 native 证据与最终源码相容，最终 T2/M15/M17 纯测试再验证该描述及资格范围，没有为文案重跑原生槽。

[最终只读审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/final_audit/shaft/audit/009-result.json)：**7 创建尝试/7 Part、41 打开、48 关闭、0 owned 文档**；失败及诊断累计，不重置其他账本。实际 SOLIDWORKS 文档为空、process 16516 持续响应、审计 GDI 2915，已记录 cleanup 最大 GDI 2922，均低于 7000；没有启动/终止用户应用。

公开成功包 authority 均为 R3，没有活动恢复 marker；候选成功包为 shaft R4 / sleeve R3，同样无 pending marker。三份首次失败包 marker 有意保留，各有 `candidate_v1/v2/v3/shaft/quarantine-disposition.json`：无权威 revision、永久隔离、禁止编辑/自动恢复，无持有 lease 或未关闭文档。**不是磁盘 marker 数量为零。**

[保护审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/final_audit/shaft/audit/003-preservation.json)：四份 Factory 原件全部符合 Registry，人工原件 SHA256 仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`；已停止 M20 的 1477 条证据全部 hash 不变。原 M20/T1 账本未重置，旧 acceptance 不恢复。M11/M17 初次纯回归失败另有诚实 [摘要](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/regression-failures.json)，不冒充 raw/native 证据。

## 接口交接与停止点

`GenericScalarCommand(Target, Parameter, ExpectedOldValue, Value, ExpectedRevision, ParameterSemanticId)`；多 driver 参数身份为 `<owner>.radius.<declared-driver-id>`，从生产 `State.Parameters` 读取，不要求测试特供名称。Derived target 只读；只有受支持源 driver 可直接修改。

T3 新 binding 必须显式升级包版本并进入同一协议。T4 再接入 typed requirements/bindings/规则 provenance 和真实 capability/provider 投影；不能从当前 null provider 字段推断 Planner 已支持本功能。

**T2 完成，可继续 T3；本轮停在这里。** T3–T5 与正式 M20 未开始。源码及报告保留为当前工作区差异，尚未主动提交或推送 Git。
