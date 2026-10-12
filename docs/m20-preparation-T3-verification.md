# M20 Preparation T3 Verification

日期：2026-10-12（Asia/Taipei）。基线 `145534d`，分支 `v0.3-main-update`；T2 的未提交实现完整保留。

**T3: COMPLETE。** 三个检查点、公开完整组合、高度编辑、独立 Oracle、保存/冷恢复后继续编辑及实际 Setter 后故障恢复均通过。没有启动 T4/T5、provider 调用或正式 M20；本报告不代表 M20 COMPLETE。源码保留在工作树，未主动提交或推送。

## 实现与适用域

- [OrthogonalMaterialProof.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/OrthogonalMaterialProof.cs)：显式 root-local 正交矩形体域，分割积分计算矩形体并集；圆孔按实际材料区间裁剪。分离圆柱及同轴相同半径的重叠区间可证明，完全重复切除在 mutation 前拒绝。接合区域被切、部分圆盘/矩形相交、未证明孔相交、斜向加工、空/分离实体及切除后追加正实体均拒绝。不是通用 B-rep 布尔算法。
- [FeatureConstructionPreflight.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/FeatureConstructionPreflight.cs) / [ConstructionPreflight.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/ConstructionPreflight.cs)：跨轴域显式分派；旧平行分层及旋转证明保留，没有删除 mixed-axis guard。
- [ConvexFilletProof.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/ConvexFilletProof.cs)、[V03Contracts.cs](D:/CAD-Harness0.2/src/CadHarness.Ir/V03Contracts.cs)、`V03ContractValidation.cs`：可省略的 version 1 `ConstructionOperation.edgeTreatment`，仅用于 `ApplyFillet`。声明精确正实体宿主、root-local 边轴、其余两个轴按升序排列的 min/max 侧及正半径；圆角必须位于历史末尾，区域不受其他体/孔/圆角干扰。计算完整凸角材料损失，不仅比较原生 DefaultRadius。
- [GenericFilletNative.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFilletNative.cs)：选择完整线边，核对端点、两个外向平面法向、宿主及精确正实体祖先；候选数量必须为 1，再解析 Persistent Reference 验证。共面合并面可保留基体所有者，但不能接受无关或未知所有者。创建时先证明 typed prior output 几何，宿主实际生成后、fillet API 前绑定；对已有宿主追加时在 Checkpoint 前绑定并在执行时复核。
- [EdgeTreatmentHandlers.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/EdgeTreatmentHandlers.cs)：Generic 与旧 Handler 共用原来的 `FeatureFillet3` uniform/simple 调用，没有第二套圆角引擎。原生定义/引用及圆柱 patch 的轴、中心、半径、90-degree sweep 和面积均验证。
- `GenericFeatureConstruction.cs`、`GenericContextRestore.cs`、[GenericRevisionPackage.cs](D:/CAD-Harness0.2/src/CadHarness.State/GenericRevisionPackage.cs)：边、宿主和两个面所有者进入明确 **FormatVersion 3**；旧普通包仍为 1，radial 包仍为 2。复用原事务、ChangeSet/Dependency、检查点、journal、Save3、独立保存读回、manifest 和 pointer 发布；没有改写事务/持久化协议。
- `GenericFeatureCapabilities.CrossAxisExecutable/ConvexFilletExecutable` 在候选原生资格及恢复通过后才开放；每请求仍须通过上述有限材料、绑定、driver 和完整原生验证。圆角半径是声明/核验的固定值，**未新增半径 Scalar 编辑资格**。M15 固定坐标/圆尺寸驱动的边界保持，不宣称所有几何约束已原生关联。
- `ConstructionPlanner.cs` 仅保持 provider schema 中 `edgeTreatment=required null`，不提前广告圆角规划；contract schemas/字段表同步生成。现有旧 Registry 仍为 9 个操作、Planner 投影仍为原 6 家族；没有新增零件专用 Tool、Fixture Factory 或 PRD 改动。

## 原生资格与公开闭环

日程及追加诊断记录：`artifacts/m20-preparation/T3/development-plan*.json`。所有原生步骤使用 STA、新控制器、独立工作包和 CreateNew 即时 JSON，记录源码/二进制 SHA、Setter、检查点、发布、Oracle、生命周期及耗时。下列输入均是开发数据，不是 held-out。

| 检查点 / 操作 | 直接证据 | 实际结果 |
|---|---|---|
| A：两方向圆孔创建 | [candidate cross](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/candidate_v1/cross/candidate_create/097-result.json) | R1，Z/X 孔实际材料跨度均 8 mm，完整独立 Oracle/保存重开通过 |
| A：高度 40→44 | [candidate edit](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/candidate_v1/cross/candidate_edit/020-result.json) | R1→R2，孔位/贯穿/法向/体积/包络通过 |
| B：原生 R1 圆角、精确边及归属 | [fillet continuation](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/fillet_v2/cross/candidate_fillet/030-result.json) | R2→R3，Format 3，保存重开及独立 patch/tangent Oracle 通过 |
| B/C：候选高度联动与冷恢复后继续编辑 | [edit](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/qualification_v2/cross/candidate_edit/030-result.json)、[cold edit](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/qualification_v2/cross/candidate_cold_edit/028-result.json) | 高度 44→48→46，R3→R4→R5；边引用稳定，圆角跟随，其他几何保持 |
| C：公开完整创建 | [controller 28316](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/public_v1/full/create/109-result.json) | `GenericPartSession.Create/Construct`，R1，高度 40、最高 Z48；12-operation 完整树，真实 Save3 和独立读回 |
| C：公开指定高度编辑 | [controller 22048](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/public_v1/full/edit/030-result.json) | 40→44，R1→R2；**1 Setter / 1 Checkpoint / 1 Save / 1 Revision**，ChangedParameters 仅 upright.depth、Dirty 包含 corner |
| C：全新控制器恢复并继续编辑 | [controller 26424](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/public_v1/full/cold_edit/028-result.json) | 44→42，R2→R3；保存并再独立重开，最高 Z50、孔位/法向/R1 保持 |
| 当前源码最终独立冷读 | [final reader](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/final_reader_v2/full/read/009-result.json) | R3，高度 42，完整 Oracle/精确引用/Format 3 通过，0 Setter / 0 Save |

独立 Oracle 位于 [T3Tests.cs](D:/CAD-Harness0.2/tests/CadHarness.FeatureConstruction.Tests/T3Tests.cs)，直接测官方 API：两个 Z 轴孔中心 `(30,-10)/(30,10)`、一个 X 轴孔中心 `(y=0,z=30)`，均 R3 且实际贯穿跨度 8；两个安装面外法向；fillet Y 轴/R1/90-degree patch、两条完整切线边；全部允许面类型、六向极值和实体体积。Expected 来自冻结测试尺寸及公式，不调用生产材料算法、Binder 或 State 的 expected 值。

[公开编辑 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/public_v1/full/edit/029-independent-oracle.json)：实际体积 `32592.8319133605 mm3`，独立 expected `32592.831913360504`，差 `-3.638e-12`；包络 `[0,-20,0,60,20,52]`。体积/包络、孔轴、孔中心、圆角面/边和安装面关系均通过；没有放宽容差。

## 故障、安全拒绝与失败保留

| 要求 | 证据 / 结果 |
|---|---|
| 实际高度 Setter 后故障 | [候选](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/fault_v3/cross/candidate_fault/017-result.json)、[公开](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/public_v1/full/fault/017-result.json)：前向 1 Setter、恢复 1 Setter、1 Checkpoint、0 Save；完整原生回滚，Revision/Pointer/native SHA 不变，随后正确 quarantine |
| 独立 durable recovery | [候选 R5](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/fault_v3/cross/recover/012-result.json)、[公开 R3](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/public_v1/full/recover/012-result.json)：新控制器恢复权威版本、完整 Oracle/引用通过、活动 marker 清除 |
| 非法高度 | public `edit/008-preflight-negative.json`：孔已不在受支持实际材料中；mutation=false、0 Setter / Checkpoint / Save；候选入口亦通过 |
| 有限域负向 / 非唯一选择器 | [31 项纯测试](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/final_pure_v2/result.json)：部分相交、接合切断、空/分离体、斜向、完全重复切除、近同轴重叠、过大/被覆盖/重叠圆角、selector 0/2/3 候选、错误宿主/版本/持久化所有权及旧包伪装拒绝 |
| 非世界坐标特例 | 同一纯集验证刚性旋转/平移后的材料、体积和宿主局部 corner selector；这是数学/frame 证据，不宣称任意 native datum 旋转均已资格化 |

历史 FAIL 均保留：首次完整创建要求两个面均归立板，实际合并侧面仍归基体，安全拒绝并回滚；只读 `binding_probe_v1` 给出两个精确原生所有者后修正。`qualification_v2/candidate_fault` 的测试注入器在回滚 Setter 上再次抛错，保持 FAIL；先以 `fault_recovery_v1` 恢复 R5，再以一次性注入通过 `fault_v3`，没有把失败改写为 PASS。新增 package 纯测试首次编译的 Vector3 类型歧义摘要保留在 `pre_public_pure/build-failure.json`，修正仅涉及测试 alias。

公开日程结束后的最终静态检查增加“新增切除量为正”拒绝条件，未改 Setter、事务、保存、恢复或已有分离孔路径的数学结果。只重跑针对性纯测试及当前 Reader 冷读，没有重新跑成功 create/edit 槽。完整公开源码/二进制保存在 `public-source-freeze`；最终两个源文件和变更 DLL 作为 `final-source-delta` 保存，`development-plan-amendment-v4.json` 说明相容性，旧冻结未覆盖。

## 回归与保护审计

| 回归 | 结果 / 证据 |
|---|---|
| T3 | 31/31，`artifacts/m20-preparation/T3/final_pure_v2/result.json` |
| T1 / T2 | 23/23、36/36，两个阶段的 `from_t3_final_v2/result.json` |
| M11 | 77/77，`artifacts/milestone11/pure-results.json`；SHA `5bb1778a78690c7727ca491b3e6511b8203021b5a3fe3e1c411c1ea4a15f7fb0`，与 T2 保留的 m11-final.json 字节一致 |
| M15 / M16 / M17 | 25/25 `milestone15/pure/20261012T050206192/result.json`；20/20 `milestone16/pure/20261012T0531190484/result.json`；36/36 `milestone17/pure/20261012T053128206/results.json` |
| 旧持久化 / 外部 Scalar/EditSet | 48/48 `milestone12/pure/20261012T050505541/results.json`；86/86 `milestone14/pure/20261012T050500345/result.json` |
| 旧包原生冷恢复 | Format 2 [shaft](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/from_t3_cold/shaft/read/008-result.json)，R3/主半径11；Format 1 [torus](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/from_t3_cold/shaft/legacy_read/008-result.json)，R2/半径4；完整独立 Oracle、原 SHA 不变，均无编辑/保存 |

Release 编译 0 warning/error；`git diff --check` 无内容错误。最终预检收紧仅影响新 orthogonal 域，旧平行/旋转及 external/legacy 路径不变；最终 T1/T2/M16/M17 纯测试再验证，旧原生包无需重做完整建模矩阵。

[最终实时审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/final_audit/full/audit/007-result.json)：T3 账本 **3 创建尝试/3 Part、29 打开、32 关闭、0 owned 文档**。另两次旧包兼容冷读累计进原 T2 账本，T2 现为 7 Part/43 打开/50 关闭；本轮总增量为 3 Part/31 打开/34 关闭。T1 仍为 5/26/31；历史 M20 账本未重置、日程未恢复。Recovery 实际含两次打开，原计划的一次估计已在 amendment-v3 追加纠正，真实 ledger 从未少计。

SOLIDWORKS process 16516 持续响应、最终 GDI **3908 <7000**、实际 documents=[]。各控制器关闭 owned 文档并恢复原活动状态；成功包 lease 均已释放，candidate cross 权威 R5/public full 权威 R3 无活动恢复 marker。首次失败 full 包没有 authority，按 [quarantine disposition](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/candidate_v1/full/quarantine-disposition.json) 永久隔离，保留 marker/失败，不允许自动恢复或编辑；不是声称全磁盘 marker 数量为零。

[原件/历史保护](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/final_audit/full/audit/003-preservation.json)：四个 Factory 原件全部匹配 Registry；人工原件 SHA 仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`；停止 M20 的 1477 条证据 drift=0。public R3 native SHA 为 `c8a116abfab753a62d20c8f40f44c5515ed3b7fb987993826a28589cb36a3256`。

## T4 交接与停止点

`ApplyFillet` 的操作字段仅 `HostBody` 和 version 1 `EdgeTreatment` 非空，selector 的 HostFeature 必须是先前正实体 feature；其他 sweep 字段为空。Host 的局部边界关系持久化，Native Edge/Host/FaceOwners 精确引用进入 Format 3。现有 `GenericScalarCommand` 精确选择 AdditiveBossDepth，不新增编辑半径/任意 B-rep 能力。

T4 须升级真实 capability/provider fillet 分支、radial/edge 参数与逐段需求绑定、默认规则/provenance 和完整 prefix 表达。当前 Planner **未资格化完整 C3 规划**，不能将 null provider 字段当作可调用能力。T3 三个检查点完成，可进入 T4；本轮停在这里，T4/T5 与新正式 M20 未执行。
