# M20 Preparation T1 Verification

日期：2026-10-12（Asia/Taipei）。基线：`b25467d5b3402448f502bf966413213754d600a9`，分支 `v0.3-main-update`。

**T1: COMPLETE。** 已完成 Generic 生产持久化、跨控制器恢复后真实编辑及故障隔离/恢复。没有执行 T2–T5、provider 调用或正式 M20；这不是 M20 COMPLETE。

## 实际修改

| 生产文件 | 变化 |
|---|---|
| [GenericPartSession.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPartSession.cs) | 公开 Create/Open/Construct/Edit；拥有文档生命周期、初始 blank 独立重开、真实 Save3、保存后重开及资源保护 |
| [GenericFeatureConstruction.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs) | 接入既有事务的 durable checkpoint/commit；执行阶段写持久 GUID；提交前 stage 完整 Generic 状态 |
| [GenericContextRestore.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericContextRestore.cs) | 按精确引用恢复 profile/plane/primitive/driver/axis/datum/body；检查实际宿主、所有权、定义、依赖、变换及完整几何 |
| [GenericRevisionPackage.cs](D:/CAD-Harness0.2/src/CadHarness.State/GenericRevisionPackage.cs) | 明确 `generic_construction` / FormatVersion 1；严格包关联及绑定验证；版本化 provenance 数据扩展点 |
| [ManagedRevisionStore.cs](D:/CAD-Harness0.2/src/CadHarness.State/ManagedRevisionStore.cs) | 在原 journal/artifact/manifest/atomic pointer 协议内增加 Generic 分派；首次检查点、恢复及路径保护 |

增加专项测试、现有 runner 的 T1 分派及 [执行脚本](D:/CAD-Harness0.2/scripts/test-m20-preparation-T1.ps1)。没有新增建模 operation、Native Subtype、参数资格或模型可见 Tool；原有三个 Generic scalar pair 保持不变。没有改 PRD、Factory 或旧 M20 runner。

## 保存问题与失败记录

- [diag_v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/diag_v1/diagnose/006-save3.json)：NewDocument 首次 SaveAs 后直接构建 plate/cut，Save3 返回 false / errors=1 (`swGenericSaveError`) / warnings=0；仍 dirty，文件哈希未变化。同路径 SaveAs 也失败。
- 文件可写、路径 92 字符、无活动草图、NeedsRebuild=0。不能归因于长路径、只读文件、未退出草图或未重建；没有猜测 SOLIDWORKS 内部错误原因。
- `diag_v2` 保留 clean blank 同路径 SaveAs 的失败。`diag_v3` 证明重新打开 blank 后构建基体可以 Save3。
- [diag_v4](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/diag_v4/diagnose_cut/006-save3.json)：同一 plate/cut 程序，在独立重开 blank 后 Save3 成功、errors/warnings=0、dirty=false，哈希实际更新。
- 根因定位到 Harness 对 NewDocument/首次 SaveAs 生命周期就绪的未经验证假设。修复是在 mutation 前关闭并独立重开 owned blank，校验 clean、可写及原字节不变；正式发布仍使用 Save3，不靠替换 API、覆盖源文件或放宽容差。
- `durable_v1` 揭示 Generic 原来只读 provisional GUID、从未写入持久属性；已在 checkpoint 后 Execute 阶段补写。首次失败内存回滚成功，包无权威 revision，永久隔离。
- `durable_v2/fault_save` 的原生故障语义正确，但测试日志序列化误访问 legacy `ManagedRevision.Program`，控制器因此失败。保留不完整 JSON、失败及 cleanup；修复为显式日志投影，直接恢复已有现场，没有重跑该故障或重标原槽 PASS。

## 原生成功链

主输入为开发 plate/cut：矩形基体、offset datum、一个槽和两个独立圆形盲切除。它不是正式 C4，更不是 held-out。使用生产 `GenericPartSession`、原 Handler/RequestMutationTransaction、原 durable revision store；没有测试专用 Setter。

| 操作 | 独立控制器 | 实际结果及证据 |
|---|---:|---|
| 公开创建、发布、保存后原生重开 | 33712 | [R1](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/create/091-result.json) |
| 冷恢复，再将槽切深 4→5 mm 并发布 | 10080 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/edit/024-result.json)；一个 revision 增量 |
| 第三控制器冷读最终参数、引用及几何 | 28720 | [R2 读回](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/read/008-result.json)；Native SHA256 `052f7db5a51e569a5acbe576c24192bf61f0db94415fcdf08603671d1ba02009` |
| 既有单圆 revolve 创建/发布 | 30700 | [R1](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/torus_v1/create_torus/037-result.json)；持久化圆 driver 和真实 axis line/sketch/datum |
| 新控制器恢复并编辑圆半径 3→4 mm | 32352 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/torus_v1/edit_torus/024-result.json)；真实驱动尺寸 Setter |
| 第三控制器独立冷读 revolve R2 | 31980 | [完整 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/torus_v1/read_torus/007-independent-torus-oracle.json)；体积 4737.410112522892 mm³，差异 0 |

主输入 Oracle 独立使用官方 API 检查单实体、体积、六方向支撑、四个圆柱位置/半径/上下边界及圆弧跨度。增强后的恢复 Oracle 还验证槽的四条直线切线和允许的面类型；两个未编辑切除保持原尺寸和边界。没有调用生产材料函数计算 expected。

保存验证会替换 session.Context；调用方必须使用当前 Context，不保留旧 COM 对象。Generic 状态在新控制器中重建 session-local binding，不复用旧 SessionId。

各后续槽的 `002-code-version.json` 留有源码/二进制哈希。早期主输入证据复用的相容性依据：之后仅增加恢复时的精确宿主/轴所有权检查、创建路径/关闭/活动文档保护及测试 Oracle；未改变切除 Setter、材料算法、事务序列或容差。增强宿主检查及 Oracle 已由真实恢复覆盖；最终轴/driver 路径由 torus 三控制器链覆盖。

## 故障矩阵

| 边界 | 原生真实步骤及提交语义 | 新控制器结果 |
|---|---|---|
| AfterNativeSave / state flush 前 | [实际保存后失败](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/fault_save/015-edit.json)：未提交，pointer 留 R2，内存回滚成功，会话隔离 | [22508 恢复 R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/recover_save/011-result.json)，恢复精确原 SHA256，完整增强 Oracle 通过，marker 清除 |
| BeforePointerPublish | [未提交](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/fault_before/020-edit.json)；native/state 已保存并独立重开，旧 context 的原地回滚失败，安全隔离；不把它写成回滚 PASS | [31136 恢复 R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/recover_before/011-result.json)，文件快照/状态/引用/Oracle 通过，marker 清除 |
| AfterPointerPublish | [R3 已提交](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/fault_after/021-edit.json)，不执行普通回滚；保留维护 marker，暂停编辑 | [25896 保持 R3](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/durable_v2/recover_after/011-result.json)，切深 6 mm、完整 Oracle 通过，marker 清除 |
| 首次发布 AfterNativeSave | [真实初始失败](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/initial_failure_v1/fault_initial/082-construction.json)，没有 previous revision 或 pointer，内存回滚成功 | `INCOMPLETE_DURABLE_PUBLISH` / Quarantined；不得从已保存 candidate 虚构权威状态 |

这些故障是实际生产发布边界的受控注入，不是将 Mock 返回值当作真实 Save3 或冷重开结果。无需终止 SOLIDWORKS 或用户进程。

## 回归与安全

- T1 专项 **23/23**：[结果](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/pure_final/result.json)。包含 strict missing/unknown fields、版本/包类型、关联/transform、artifact drift、Generic/legacy 分派、六个发布边界、首次隔离、document/configuration/revision/path/envelope 拒绝。
- FeatureConstruction **20/20**：`artifacts/milestone16/pure/20261011T1642478877/result.json`，包括原有未证明材料及未资格参数继续拒绝。
- legacy managed recovery **48/48**：`artifacts/milestone12/pure/20261011T164255947/results.json`。
- external observed / EditSet **86/86**：`artifacts/milestone14/pure/20261011T164316571/result.json`。未重跑旧完整原生矩阵；旧分派、原 SaveNative/RestoreManagedState/ExternalPartSession 和资格门控未修改。
- Release 编译零 warning/error。没有放宽几何容差、Driver/Dependency/Qualification 或使用名称/近似几何猜引用。
- [T1 累计账本](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/native-budget.json)：**5 次创建尝试/5 个 Part、26 次打开、31 次关闭、0 owned 文档**，失败及诊断均计入。不限额授权不是无穷重试；有限槽位及追加诊断目的分别留档。
- [实时只读审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/final_audit/audit/007-result.json)：控制器 26052，实际 SOLIDWORKS 文档为空、响应正常；0 新建/0 打开。最大已记录 GDI **3609**，低于 7000。
- 活跃成功包 `durable_v2` 权威 **R3**、`torus_v1` 权威 **R2**，没有待恢复 marker。两份首次失败包的 marker **有意保留**，分别有不可变 `quarantine-disposition.json`，无 authority、禁止编辑/自动恢复、无未关闭文档或持有 lease；不能声称磁盘上 marker 数量为零。
- 原 M20 账本仍为 5 创建/15 打开/20 关闭。停止审计中的 **1477 条历史证据哈希全部不变**；旧 freeze 元数据不变，live source 中仅本轮列出的两个旧生产文件有意改变，不能恢复旧 acceptance。
- 四个 Factory 原件与 Registry 哈希全部匹配；人工原件仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`。未打开或修改工程师原件。

## T2/T3 接口交接及停止点

公开 API：`GenericPartSession.Create/Open/Construct/Edit`；参数请求仍为 `GenericScalarCommand`。`CurrentGeneric.State` 是完整状态，`Store.ReadCurrent().Generic` 是已发布包；`ManagedRevision.State` 的空 v0.2 envelope 仅承载共享协议的 identity/revision，**不会**持久化为旧 CADState，也不代表 legacy program。

包由 state/program artifact 的明确 `0.3/0.3` 分派，内部再校验 PackageType/FormatVersion；旧 managed `0.2/0.2` 与 external observed 分支保持隔离。`GenericSketchImage` 保存精确 primitive refs、独立圆 driver/owner、native transform/revision；`GenericFeatureImage` 保存 profile、reverse 与轴辅助引用。几何配对来自持久化 specification 的确定性 primitive 定义和精确 native refs，不从当前原生数组顺序或名称反推目标。

T2 的多 driver/segment/relation、T3 的 edge-treatment 必须为结构变更显式升级包版本并增加严格 reader/恢复验证，继续使用同一 journal/pointer 发布链；不能伪装成现有圆 driver 或自动猜测旧包。`GenericProvenance` 提供 Version 1、Kind 和有界 JSON object 数据容器，当前只保存/校验、不会执行 payload；T4 再接入其 typed accepted requirements/bindings/rule 校验。

**停在 T1 验收点。** T2–T5 和正式 M20 均未开始。当前工作树保留本轮差异及所有此前未跟踪文件；后续先核验本报告、当前源码和两个成功包的实际 revision，不能假定 R1。
