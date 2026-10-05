# M9C — Construction Transaction / Rollback

状态：**COMPLETE**。基于 M9A/M9B，仅执行用户指定 M9C。遵循 PRD §16–18 事务/增量验证思想及 §26 最小测试范围、资源检查、document lifecycle、有限 native budget。没有修改 PRD、读取/复制 v0.1、运行 M9 generalization suite、历史 milestone tests、性能 benchmark 或后续扩展。

## 共享事务与创建入口

原编辑协调器提取为 `RequestMutationTransaction<TRequest,TPrepared,TRollback>`，后端契约为 `IRequestMutationBackend<TRequest,...>`。已有 `IMutationBackend<...>` 是 OperationNode 特化，`MutationTransaction<...>` 保持原公开编辑入口并转发到同一协调器。整 CadProgram 使用同一个 load→resolve→preflight→capture→execute→rebuild→required postconditions→final validation→stage→atomic commit 流程；失败路径仍统一 rollback→rebuild→full restored validation，rollback 失败显式 invalidate。

没有复制第二套事务控制循环或为测试 operation 写核心参数/操作 switch。新的 `TransactionalConstructionBackend` 提供 construction payload、native execution、checkpoint 与验证；任何 registered construction handler 的失败均由相同协调器处理。

ChangeSet 增加显式 `CreatedFeatures`/`CreatedEntities`，允许新 identity 在 baseline 中尚不存在；未声明 foreign mutation 仍拒绝。实际执行 ChangeSet 必须与预检一致。最终 validation scope 根据已验证的新状态刷新，包含所有新参数/实体/关系。创建使用 NewModelFinalization 完整验证；失败恢复使用 Rollback 完整验证。

CADState 允许0个 features，表示空 Part baseline；phantom entity、无 owner binding、非法关系/依赖等原规则保持。空模型的 `CaptureConstructionState` 使用只读 provisional document/configuration GUID，不注册 native properties；`EnsurePersistentIds` 仅在 Execute 内写相同 GUID。失败 checkpoint 删除本次新增 identity properties，恢复原文档/配置自定义属性。已有 GUID 保持不变。

`RelationBackend.Create(context,program,store)` 支持原 AtomicStateStore。旧 no-store Create 入口仍使用相同事务，通过内存 store 发布 live session；CompositionBackend.Execute 也进入此路径，raw per-handler loop 变为 internal。公共 Preflight 与 Execute 都采用完整 construction plan，避免公开 bypass。

可创建新的 pristine Part，或向当前 managed relation session 追加 creation operations。后者合并原 program/relations 后检查；既有 operations 的 serialized definitions 必须保持不变，新增关系不能隐式移动原 seed。重复 IDs、多个 root extrusion、EditParameter 混入 construction、非法 host/layout/spacing/count/depth 均预检拒绝。已有 native 输入再经 Binder 和 persistent reference health 验证后才开始 mutation。

成功构建与编辑统一推进一个 revision：空状态0→初次构建1；成功追加1→2；失败不推进。`StateCommitted=true` 在显式 AtomicStateStore 入口表示 durable JSON 原子提交，在 no-store 入口表示 live session 内存发布。旧 M9A/M9B 的初次构建 revision=0 是当时验收行为，M9C 将其纳入统一事务后调整为1。

## 原生 checkpoint 与恢复验证

`NativeConstructionCheckpoint` 捕获执行前：

- 有限 native feature inventory、其 native object identity、类型和 error/warning；持有原 feature RCW，避免执行中丢失原对象身份。
- solid body 与所有 face 的 persistent references、edge/face counts、质量属性、6个方向的精确 support points、surface kind/area/UV bounds 和中点 position/derivatives。
- 文档及活动 configuration 的自定义属性原始 type/value。
- 内部 outputs、operations、hole profile refs、CreatedFeature、construction root、RelationProgram、dependencies、revision、usable/mutation flags。

恢复按逆序删除 snapshot 之外本次引入的 features，包含 absorbed sketches，每次删除后重新枚举，最多256步；不依赖语义 ID、显示名称、操作 kind 或测试 case。原 feature 永不列入删除候选。随后恢复 properties 和 session，重建后逐项比较 feature identity/status、全部原 body/face refs、拓扑与几何、properties、CADState metadata/parameter/entity geometry。persistent refs 不健康或原模型无法验证时，RollbackSucceeded=false 并 invalidate；不猜测拓扑替代。

native checkpoint 中的 COM identity 只用于同一会话的执行边界，不是 semantic binding 或 stale-reference recovery。语义输入仍使用现有 Binder 和 persistent references。native geometry 比较为有限 readback 与数值容差，不声称 BREP 字节序列比较。

正常 fillet/chamfer 可以消费其宿主 LinearEdges。最终 state 保留这些实体的实际失效 health，不能为此错误拒绝合法构建，也不能虚报它们仍可绑定；其他 feature/body/frame/host refs 必须保持健康。已有失效 input 在 mutation 前拒绝。Planner catalog runtime id 更新至 m9c，保留实际 backend 的有限 capabilities，增加 construction 事务约束；没有新增未注册 parameter mutation handler 或任意模型追加 Planner 模式。

## 失败结果

`CompositionExecutionResult` 的顶层四个字段由完整 Transaction 决定，不再始终返回 rollback=false。Transaction 还包含 stage、revision、ChangeSet/DirtySet、validation scope、rollback failure code/message。每个 operation 的子结果描述该 handler 当时的执行情况，整体 rollback/commit 以顶层结果为准。

| 情况 | MutationStarted | RollbackAttempted | RollbackSucceeded | StateCommitted |
|---|---|---|---|---|
| 纯 program/布局错误或已有 input 失效 | false | false | false | false |
| 原生修改后失败且恢复验证通过 | true | true | true | false |
| 原生修改后失败且恢复/验证失败 | true | true | false | false，附 failure detail，session unusable |
| 全部执行/验证/原子提交成功 | true | false | false | true |

## 专属纯/mock 与编译

```powershell
.\scripts\test-milestone9c.ps1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\build.ps1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
```

SDK 8.0.425，**22/22 M9C pure/mock tests PASS**。验证空 baseline/phantom entity 拒绝、Created identities/foreign mutation、impossible fillet 结构合法且交给 native feasibility、invalid pattern/placement、追加语义输出、重复 ID/宿主范围/隐式旧 seed 修改拒绝；共享 coordinator 的 resolve/preflight/capture/execute/postcondition/final/stage failure boundaries；commit failure 恢复 native 与 staged metadata；success full scope 和调用顺序；failed rollback invalidation 与明确 flags。纯测试创建0 Parts。

**17 项目 Release compile PASS，零警告、零错误**。全解决方案仅编译；M0–M9B tests 未执行。新增 ConstructionTransactions.Tests、Milestone9C build scope 和 test-milestone9c.ps1。

## 定向原生验收

两次运行都附加到 SOLIDWORKS revision **32.0.1**，process **1984**；创建前 responding=true，GDI分别 **550/551**，打开 test-owned Parts=0，阈值7000。专属耐久 budget 总上限2，最大并发1。原执行命令如下，**预算已用完，不应自动再次运行**：

```powershell
.\scripts\test-milestone9c.ps1 -Live -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9c.ps1 -LiveTopology -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

### Part 1 — pristine 与既有模型

使用通用测试组合：80×50×8 extrusion，中心 Ø6 through hole；追加孔 Ø4 位于(20,10)。测试 impossible fillet 的 R1000 作用于4条外竖边。没有生产尺寸/ID 分支。

| 原生场景 | 结果 |
|---|---|
| invalid pattern count=1025 | OPERATION_PRECONDITION_FAILED，四个顶层 flags 均false，无原生 operation 执行，模型/文件/session不变 |
| invalid placement=(40,25) | 同样修改前拒绝，无 native identity property 注册，模型/文件/session不变 |
| pristine Part 完成 extrusion+hole 后 impossible fillet | 两步真实 native operation 成功，fillet API 返回无 feature，GEOMETRY_IMPOSSIBLE；MutationStarted/RollbackAttempted/RollbackSucceeded=true，StateCommitted=false；恢复为原0-body Part、template feature inventory、原自定义属性与空 CADState，新增GUID被删除 |
| 正常 baseline construction | shared transaction 成功提交，revision0→1；1 body、2 managed features、16 entities、2 parameters，Ø6孔/板尺寸及质量属性通过独立 native 检查 |
| 已有模型 added hole 成功后 impossible fillet | 第一追加 operation 实际成功，第二失败；删除新孔及 absorbed sketch，保留原板/Ø6孔/refs/properties/session/revision1，state文件逐字节不变 |
| 已有模型非法追加 placement=(100,0) | mutation 前拒绝，三种状态不变 |
| 完整追加 native execution/validation 后注入 required postcondition failure | TEST_POSTCONDITION_FAILED；测试仅装饰 ValidatePostconditions，Rollback 完全由 production adapter 提供；原模型/文件/session不变 |
| 真正 atomic commit failure | 新孔已创建/重建/验证并 stage revision2，用 FileShare.Read 锁使 File.Replace 失败；STATE_COMMIT_FAILED，统一 rollback 恢复原 topology、session及revision1，原state字节保留，无 temporary file |
| rollback 后正常追加 | 同一 session 可继续使用，成功原子提交，revision1→2；板与原孔保持，第二 Ø4孔正确，3 features/19 entities/3 parameters，final scope完整 |

本运行报告7个 failure stages，全部 `ModelUnchanged=true`、`StateFileUnchanged=true`、`SessionStateUnchanged=true`；两次成功构建的详细结果保留在 native-run.log。没有为 impossible fillet 写专用恢复函数。

### Part 2 — 正常圆角与真实拓扑恢复

这是 M9C 中对正常拓扑消费的定向验证，不是 G1/G4 或 generalization suite。先构建相同板/中心孔，再用实际 ApplyFillet R1 对四条外竖边成功提交。

圆角后原模型：1 body、11 faces、26 edges，体积 **31766.9380701703 mm³**，与 `(80×50−π×3²−(4−π)×1²)×8` 一致；3 managed features、17 entities、3 parameters、revision2。四个已消费外边真实失效 health 保留，所有健康实体的 Binder/persistent refs 可重新解析。

| 场景 | 结果 |
|---|---|
| 合法 R1 fillet | transaction 成功提交；不会把已消费边误当成必须仍健康的 feature output，也不会把它们虚报为 healthy |
| 再次请求原 outer_edge_1..4 | Binder 返回 BINDING_UNRESOLVED，preflight 修改前拒绝，原 rounded 模型/文件/session不变 |
| rounded 模型追加孔后 required postcondition failure | native hole 创建与 production native validation 完成后测试注入失败；相同 checkpoint 删除新增 topology 并恢复原圆角模型。原11 faces/26 edges、全部原 face/body refs、surface samples、mass、properties、CADState 与 revision2 验证通过 |

本运行3 stages，其中一个正常提交、两个 failure；两项 failure 的模型/文件/session unchanged 标记均true。没有额外 benchmark、native loops、参数 editing 测试或创建 recovery。

## 状态证据与生命周期

恢复前后 JSON SHA256：

| 模型 | 原状态与恢复状态共同 SHA256 |
|---|---|
| 原矩形板/中心孔 | `879850FEDCFA626DF7DE6A935D05F8E6319151FEC573A74DA360D00D3AF22A01` |
| 原圆角板/中心孔 | `7894E225B38770C511C892389A551D582D619B03814EA263911EA2BAB3CD33EC` |

共 **2次创建尝试、创建2、关闭/丢弃2，预算2/2**，最终 open test-owned Parts=0；两次原活动状态恢复true、cleanup error=null。每个 Part 在 geometry 操作前注册为 test-owned；没有关闭其他文档、删除旧 milestone ledger 或重用旧测试模型。native-result.json 两个报告均 COMPLETE，没有失败豁免或 offline 改判。

证据目录 `artifacts/milestone9c/`：pure-result/pure-run、native-run/native-topology-run、native-result、native-budget、empty-state、original/restored-model-state、native-state、original/restored-topology-state、topology-state、solution-build.log。没有保存 native Part；savedPath为空，这些是已关闭 Part 的会话证据。

## 实现文件与范围

| 文件 | 内容 |
|---|---|
| State/MutationTransaction.cs | request-generic shared coordinator、原编辑薄包装、最终创建 scope |
| State/IncrementalValidation.cs、StateValidation.cs | declared created identities、空模型 baseline |
| SolidWorks/ConstructionPrograms.cs | 完整/追加计划预检，既有 operation 不可隐式修改 |
| SolidWorks/TransactionalConstructionBackend.cs | 通用 creation adapter、native 输入 preflight、full final/restored validation |
| SolidWorks/NativeConstructionCheckpoint.cs | 通用 native feature boundary/geometry/property checkpoint 与恢复 |
| SolidWorks/ConstructionSession.cs、DocumentIdentityAdapter.cs | read-only baseline identity、全部 session snapshot、Execute 内GUID注册 |
| SolidWorks/RelationBackend.cs、FeatureBackendRegistry.cs | 公共创建路径接入 transaction、真实 result flags、internal raw loop |
| SolidWorks/SolidWorksPlanningRuntime.cs | m9c runtime/transaction约束，保留有限可执行能力 |
| tests/CadHarness.ConstructionTransactions.Tests、scripts、solution | M9C 专属入口和证据 |

范围仍是当前受控 construction session 和有限 registered feature-creation backend；追加不编辑旧 feature definitions，若关系要求移动旧 seed则预检拒绝。rollback 恢复模型几何/拓扑、native refs、properties 与 managed state，未承诺复原 GUI selection、undo history 或 native save-dirty flag。没有任意已打开非托管 Part 的 topology adoption、native Part/JSON 联合磁盘原子提交、组合重开/controller restart、未知 native effects 的自动 recovery 或 broader generalization evaluation。

**M9C COMPLETE；M9 generalization suite、performance benchmark 与后续扩展未执行。**
