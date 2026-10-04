# M9A — Generic Parameter Mutation Extension

状态：**COMPLETE**。依据用户指定 M9A 范围，以及 PRD §11/§16–18/§20、M7 mandatory runtime capability projection。已阅读当前仓库和 M0–M8 verification；没有修改 PRD、读取/复制本地 v0.1 工作区或实现 M9B+。

## 核心 Runtime 是否需要修改

**通用 transaction 核心不需要修改。** `src/CadHarness.State/MutationTransaction.cs`、Binder、ChangeSet/DirtySet、ValidationScope 和 AtomicStateStore 均复用现有实现，没有新增 `if parameter == ...` 或参数 switch。修改的是 SOLIDWORKS 原生参数适配器及后端拥有的 Planner 能力投影。纯 `RelationParameterEditor.Apply` 增加可选字段映射注入，默认旧 M5 入口范围保留；没有扩大 IR 词汇或 schema。

`TransactionalParameterBackend` 原本强制将目标当成 `ILinearPatternFeatureData`，原生捕获、Execute 和 Rollback 全是 Pattern-specific。现在 ResolveInputs 经 registry 确认 handler；NativeEditPreparation 保存该 handler；CaptureRollback 保存 handler 的 opaque payload；Execute/Restore/ValidateNative 分派给 handler。共有会话身份校验、Binder/参数绑定、关系解算和耦合种子传播、ChangeSet/DirtySet、重建、增量/完整验证、元数据暂存、原子提交仍由现有路径协调。

## 注册与处理器

`ParameterMutationRegistry` 从实际 handler 的有限 owner-kind/parameter/IR-field 描述构造只读目录。字段必须匹配 IR 参数类型，重复注册拒绝；静态 Default 包含以下三个 handler。调用方可给原生适配器和 `SolidWorksPlanningRuntime.ForEdit/ForEditSnapshot` 注入同一注册表。新增已存在于有限 IR 词汇中的标量 handler，不需要修改通用 transaction 或增加参数分支。注册表属于受信任的后端代码，注册即表示实现方提供实际 native 方法；测试用 fake handler 不加入 Default。

| Handler | 契约与行为 |
|---|---|
| `PatternScalarMutationHandler` | 迁移已有线性/矩形阵列 scalar mutation；活动方向约束、计数 HighRiskTopology、原生值捕获、ModifyDefinition 和恢复保留 |
| `ExtrusionDepthMutationHandler` | 对当前矩形 blind extrusion 的 `ExtrusionDepth`，读 `IExtrudeFeatureData2.GetDepth`，AccessSelections/SetDepth/ModifyDefinition，捕获实际原值并恢复 |
| `HoleDiameterMutationHandler` | 对当前 `CreateThroughHole` 的 `HoleDiameter`，编辑已绑定圆形孔草图的 native radius；捕获实际直径，恢复草图并重建 |

M9A 默认只注册通孔孔径，未注册盲孔孔径/深度、轮廓宽高/直径、圆角/倒角等编辑。只读 native 参数仍可参与完整验证，不因没有 mutation handler 而被遗漏。

新 handler 的 required postconditions 不止检查 getter。`NativeHoleInstanceVerifier` 只读取受影响 hole/pattern 特征产生的 faces，经直接 native provenance 获得 seed 和 pattern walls，按期望布局唯一匹配各实例，检查实例数、孔位、实际圆柱半径/方向和圆周边界 Z 高度。没有使用完整 body 扫描替代普通编辑的 Level 2 验证；测试运行器另外使用全实体几何作独立断言。厚度和孔径的标准正值修改默认增量验证；不合法宿主布局在修改前拒绝，未启用自动 recovery。原有 pattern count 风险升级保留。

## Planner 能力投影

`SolidWorksPlanningRuntime` 从 mutation registry Descriptors 生成编辑配对，要求：Binder 唯一健康目标，相关依赖特征/实体健康，唯一参数 binding，已提交值与程序一致。Preflight 同样使用 registry.ApplyProgram/ValidateTransition，再执行关系求解和既有后端预检。native Execute 仍重新检查 live identity/revision、绑定和读回，snapshot 投影不能冒充已恢复真实 CAD 文档。

新增可投影项只有实际注册的 `extrusion_depth` 和 through-hole `hole_diameter`。移除 handler 时 catalog/schema 同步移除。没有向 Planner 提供完整 IR OperationRegistry、未实现的 Circle/CircularPattern、通用 COM 方法或任意代码执行回调。规划层 `RuntimeCapabilityCatalog` 类本身不用增加参数特判，其后端生成内容已更新。

## 测试与原生结果

```powershell
.\scripts\test-milestone9a.ps1
.\scripts\test-milestone9a.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone9a.ps1 -VerifyEvidence
.\scripts\build.ps1
```

SDK 8.0.425。**26/26 专属纯测试 PASS**；**15 项目 Release 编译 0 警告、0 错误**。仅运行 M9A 功能测试，M0–M8 只编译。纯测试覆盖 handler 注册/移除/重复拒绝、扩展注册、只读参数验证、严格 Planner fixture/schema、owner/parameter 配对、绑定缺失和漂移拒绝、依赖健康、DirtySet、活动方向及计数风险策略、未实现能力不暴露。

原生 SOLIDWORKS revision **32.0.1**，本次连接进程 **1984**。G2 fixture 为 **100×60×8 mm** 矩形板、**4×Ø6** 通孔、**2×2** 居中布局，X/Y spacing=60/30 mm；5 项显式关系、3 个 feature、17 个 semantic entity、6 个 parameter。仅一个 Part。

| 原生阶段 | 结果 |
|---|---|
| 厚度 8→10 | 原生实体深度 10 mm；4 个 Ø6 孔均贯穿 Z=0..10，位置 X=±30/Y=±15；revision 0→1、原子提交 |
| 孔径 Ø6→Ø8 | 四个实际孔壁均 Ø8（读回约 7.999999999999998）；位置/贯穿高度保持，revision 1→2、原子提交 |
| Ø40 拒绝 | 关系宿主边界/相交检查返回 OPERATION_PRECONDITION_FAILED，修改前拒绝、文件不变 |
| 厚度 0 拒绝 | PROGRAM_SCHEMA_INVALID，修改前拒绝、文件不变 |
| 厚度 10→11 后注入失败 | 真实修改/重建/原生孔实例验证之后，由测试装饰器抛 required-postcondition failure；生产 handler 恢复 10，完整恢复验证通过，文件逐字节不变 |
| 孔径 8→9 后注入失败 | 同样发生在真实 native propagation 验证后；恢复 8，全部四孔恢复，完整恢复验证通过，文件逐字节不变 |
| 实际原子提交失败 | 真实 Ø8→Ø8.5 修改/验证完成，FileShare.Read 锁使 File.Replace 失败；恢复 Ø8 和 staged session metadata/revision，文件逐字节不变 |
| 已注册 pattern handler 可继续使用 | 原有 spacingX=60 再次设置为 60，原生 ModifyDefinition 与提交成功；revision 2→3。这是同 Part 的 dispatch/session 检查，不声称新的阵列几何变更或全面旧回归 |

厚度编辑 preflight/final 均读 **1/6 参数、17/17 实体、5/5 关系**；孔径编辑读 **1/6 参数、10/17 实体、5/5 关系**。所有 3 个托管特征始终执行 Level 0 原生 error/warning/rebuild 检查。新 handler 的 dirty instance face 检查补充 Level 2 的 native provenance 与几何证据。回滚执行 FullValidationReason.Rollback，完整参数/实体/关系读回。这里没有计时比较或效率结论。

原生质量属性体积：厚度修改后 **58869.02664470767 mm³**，孔径修改后 **57989.380701702525 mm³**，均与解析几何期望一致。最终原生 feature refs、document/configuration identity 及关系保持，提交状态 revision=3；参数为 thickness=10、hole diameter≈8。

### 浮点断言修正与原始证据

原生运行的全部 8 个阶段和 cleanup 已成功，但最末综合断言曾使用 `HoleDiameter == 8` 精确比较，因原生浮点读回 **7.999999999999998** 而抛出 `Persisted final state or identity differs`，原始 runner 返回失败。不是原生编辑/rollback/state commit 失败。断言已改为沿用 `1e-6 mm` 容差，未修改生产算法来隐藏偏差。

保留原 `native-result.json` 的 BLOCKED 状态、原始 message、每个 stage 的 transaction/read/geometry 和 cleanup 证据。新增 `-VerifyEvidence` 用修正后的容差严格复核：初始及全部 8 个阶段的独立 native geometry；成功提交与 revision；两次修改前拒绝；两种 handler 的修改后回滚；真实 commit failure；状态逐字节不变标记；最终磁盘参数/健康实体/feature refs/relations/document identity；临时文件清理和 budget ledger。仅允许已知末尾断言缺陷，其他 native failure 不可由该模式转为成功。复核结果 **COMPLETE**，保存在 `native-evidence-verification.json`。没有重写原始失败报告，也没有为此修正再创建 Part。

## 生命周期、文件与限制

创建前 responding=true、GDI=**417**（阈值7000）、open test-owned Parts=0。Part 在几何操作前注册；最终创建 **1**、关闭/丢弃 **1**，原活动状态恢复 true，cleanup error=null，打开测试 Part=0。M9A 独立耐久账本上限2次创建尝试，本次 **1/2**，未消费第二次尝试，不修改或删除旧里程碑账本。附加/启动所有权由已有 SolidWorksConnection 管理，不强制终止任何进程。

| 变更文件 | 内容 |
|---|---|
| `src/CadHarness.SolidWorks/ParameterMutationRegistry.cs` | 描述、接口、只读注册表、统一纯程序编辑分派 |
| `src/CadHarness.SolidWorks/ParameterMutationHandlers.cs` | 三个 handler 与 dirty hole instance native readback |
| `src/CadHarness.SolidWorks/TransactionalParameterBackend.cs` | handler dispatch 和 opaque rollback payload，移除核心参数 switch/Pattern-only capture |
| `src/CadHarness.SolidWorks/SemanticStateCapture.cs` | 绑定孔草图的 native SetDiameter |
| `src/CadHarness.SolidWorks/SolidWorksPlanningRuntime.cs` | registry 驱动 capability projection/preflight |
| `src/CadHarness.State/RelationParameterEditor.cs` | 可选纯字段映射；旧 M5 默认范围保留 |
| `tests/CadHarness.ParameterMutations.Tests/*` | 专属纯/原生/evidence audit runner、G2 fixture、测试失败装饰器 |
| `scripts/test-milestone9a.ps1`、`scripts/build.ps1`、`CadHarness.sln` | M9A 入口/scope、第15个项目 |
| `README.md`、本记录 | 使用、证据与范围 |

证据位于 `artifacts/milestone9a/`：pure-result、edit catalog/schema、native-run.log、native-result、initial/native state、native live catalog、native budget、native-evidence-verification。没有保存 native Part 文件，状态 savedPath 为空，属于已关闭测试 Part 的会话证据，不能作为可重开执行的模型文件。

限制：仍为当前托管构建会话和 XY 矩形宿主；没有组合重开/控制器重启恢复、任意拓扑重解析、创建阶段 transaction rollback、native Part/JSON 联合原子提交、Circle/CircularPattern、blind-hole diameter mutation、M9 broad generalization suite、held-out evaluation、baseline 或 benchmark。注册扩展的测试 handler 仅证明架构可扩展，不声称相应 native 能力已实现。

**M9B+ NOT IMPLEMENTED；完整 M9 泛化评估未执行。**
