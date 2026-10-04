# Milestone 6 验证

**COMPLETE — MILESTONE 6 COMPLETE**。

依据 `Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` §16–18、§26、§27 M6，实现 ChangeSet、DirtySet、通用修改事务、定向验证、完整验证升级与回滚。四项必需验证均通过，累计创建并关闭 1 个 Part，上限为 3。PRD 未修改，没有读取/复制本地 v0.1 工作区。

## 文件变更

| 文件 | 实现 |
|---|---|
| `src/CadHarness.State/IncrementalValidation.cs`（新增） | ChangeSet、依赖/所有权闭包 DirtySet、有限完整升级原因、ValidationScope、验证读取集合 |
| `src/CadHarness.State/MutationTransaction.cs`（新增） | 泛型事务、后端/状态存储接口、阶段顺序、一次允许的恢复、失败回滚、明确修改/回滚/提交标记 |
| `src/CadHarness.State/AtomicStateStore.cs` | 实现事务状态存储接口，复用同目录 Flush/Replace 原子提交 |
| `src/CadHarness.State/CadState.cs` | 共享结构化错误码接口 |
| `src/CadHarness.SolidWorks/TransactionalParameterBackend.cs`（新增） | 绑定/预检、实际回滚数据、原生编辑适配器、定向/完整读回、恢复几何/metadata、不可恢复会话隔离 |
| `src/CadHarness.SolidWorks/SemanticStateCapture.cs` | 按范围读取参数/实体，与已提交快照合并；完整捕获入口保留 |
| `src/CadHarness.SolidWorks/RelationNativeReadback.cs` | 按范围验证受影响布局，定向参数 getter 受读取集合限制 |
| `src/CadHarness.SolidWorks/NativePatternEditor.cs` | M6 仅设置请求标量；恢复时可设置已捕获的原值 |
| `src/CadHarness.SolidWorks/RelationBackend.cs` | 新增接收 AtomicStateStore 的 M6 编辑入口 |
| `src/CadHarness.SolidWorks/BackendContracts.cs` | 原生异常实现统一错误码接口 |
| `tests/CadHarness.Transactions.Tests/*`（新增） | 项目、TestData、PureTests、原生运行器、单个 transaction.json 组合 |
| `scripts/test-milestone6.ps1`（新增） | 只运行 M6 纯验证或单次原生验收 |
| `scripts/build.ps1`、`CadHarness.sln` | M6 范围与第十个项目 |
| `README.md`、本文件 | 入口、结果、限制与后续边界 |

共享本项目已有资源守卫、耐久预算账本和 TestPartScope；没有引入历史测试基础设施或生产零件预设。

## 事务与验证

纯状态层不包含 COM 或耦合布局公式。事务加载状态，解析绑定，预检，捕获回滚数据，执行，重建，验证必需后置条件；后端允许时最多一次恢复，恢复后强制完整验证；最终验证后的候选状态必须保持文档/配置身份并恰好增加一个 revision。先暂存会话 metadata，再原子提交 JSON，成功提交之后没有可能失败的后端操作。提交失败仍恢复原生值与会话 metadata。

执行返回的 ChangeSet 必须与预检边界一致，差异触发完整回滚。嵌套事务在第二次加载前拒绝。失败结果同时包含原失败阶段/错误码、MutationStarted、RollbackAttempted、RollbackSucceeded、StateCommitted 与独立回滚错误。恢复后的验证失败不算成功回滚，且会隔离原生上下文。预检拒绝没有修改，不需要回滚。

DirtySet 从修改特征、参数所有者、失效关系引用开始，沿依赖图求闭包；加入受影响特征拥有的实体与关系读取依赖。共享宿主只作为读取依赖时，不扩展到宿主上的独立布局。普通编辑保持 Level 1/2 定向范围；Level 0 始终检查所有托管特征状态、API 返回、重建/草图状态及事务身份。

完整升级枚举覆盖 PRD 的七个原因：新模型最终化、拓扑风险、恢复、引用重解析、状态漂移怀疑、显式完整验证、基准故障注入。恢复原状态另使用 Rollback 原因。原生计数编辑自动标记拓扑高风险；预检定向读回发现漂移会尝试完整检查后拒绝修改。普通间距编辑通过原生直接引用读回受影响参数、草图、孔壁、宿主、方向与关系，不调用完整状态捕获。最终参数/实体合并入既有快照。

## 验证结果

```powershell
.\scripts\test-milestone6.ps1
.\scripts\test-milestone6.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\build.ps1
```

.NET SDK 8.0.425。M6 纯测试 **33/33 PASS**：脏闭包、共享宿主隔离、直接参数读取、失效关系传播、未知 ChangeSet 拒绝、普通定向验证、八个完整升级原因及非法枚举；成功顺序与 revision；解析/预检/捕获/执行/重建/后置条件/最终验证/暂存失败；提交失败；ChangeSet、revision、身份不一致；回滚失败及恢复验证失败；一次恢复与完整升级；嵌套事务；非法加载状态。纯测试不激活 COM，创建 0 Parts。

原生 SOLIDWORKS **2024，revision 32.0.1**。使用一份 100×60×8 mm 板件，包含独立的两孔线性布局和 2×2 矩形布局，分别 Ø8 和 Ø6；该组合只用于验证事务范围。

| M6 场景 | 原生结果 | 状态结果 |
|---|---|---|
| 成功参数编辑 | 间距 40→50，Ø8 孔 X=±25，Y=0 | 原子提交，revision 0→1 |
| 不可能的参数编辑 | 间距 500 越出宿主，修改前返回 OPERATION_PRECONDITION_FAILED | 未修改、未回滚、未提交；文件逐字节不变 |
| 修改后的失败/回滚 | 实际设置间距 55 并重建后，由测试装饰器拒绝必需后置条件；生产回滚恢复间距 50/孔 X=±25 | RollbackAttempted=true，RollbackSucceeded=true，StateCommitted=false；revision 1；文件逐字节不变 |
| 实际原子提交失败 | 间距 54 的原生编辑与最终验证通过；持有文件读取锁使 File.Replace 真实失败；回滚恢复 50 | STATE_COMMIT_FAILED；成功回滚；已暂存 revision 恢复为 1；文件逐字节不变 |
| 恢复后继续编辑/完整验证 | 间距 50→52，显式 Level 3 验证，Ø8 孔 X=±26 | 成功提交，revision 1→2，会话可继续使用 |

后置条件失败是测试专用注入，明确发生在真实原生修改和重建之后；不声称触发了 SOLIDWORKS 的不可能几何失败。该注入没有生产故障开关。真实文件提交失败使用 Windows 文件共享锁，未用提交 stub 替代原生验收。两次回滚都重建并读取全部托管参数、语义实体与关系，还核对恢复参数/几何与原提交快照。

成功普通编辑的 preflight/final 读取范围均为 **1/9 参数、9/21 实体、5/10 关系**。只读 `linear_holes.pattern_spacing`；实体为 `linear_holes`、`linear_seed`、种子 profile/wall、共享轴/框架/宿主及 body/direction_x。独立 `hole_seed` 和 `holes` 的实体/关系未进入读取集合。所有 **5** 个特征仍执行 Level 0 状态检查。回滚/显式完整验证读取 **9 参数、21 实体、10 关系**。读取集合是范围证据，没有进行计时或效率基准。

每个阶段独立检查实际实体几何：最终保持 6 个孔，矩形布局 X=±30、Y=±15；实体体积初始与最终均为 **46290.97359644714 mm³**。实体几何观测来自原生圆柱面与质量属性，是测试断言，独立于事务验证范围；没有将全实体观测放入普通编辑事务。

## 生命周期与证据

创建前：响应 true，GDI **557**（停止阈值 7000），打开测试所有 Part **0**；附加进程 **22996**。Part 在几何操作前注册，测试结束关闭并丢弃，原活动状态恢复 true，清理错误无。

累计创建尝试 **1/3**，创建 **1**，关闭 **1**，仍打开测试 Part **0**。独立 `artifacts/milestone6/native-budget.json` 保留耐久账本，禁止删除或绕过。已完成最小验收，无需使用剩余预算。

证据：`artifacts/milestone6/pure-result.json`、`native-result.json`、`native-budget.json`、`native-state.json`。原生结果追加保留每次调用；未保存额外 Part 文件，也未保留打开 Part 作为证据。native-state 属于已关闭、未保存测试 Part 的会话证据，savedPath 为空，不是可重开编辑的耐久 CAD 文件。

没有重跑 M0–M5 功能/原生测试，没有历史回归、广泛原生套件、LLM/Jev、性能基准或 stepwise baseline。解决方案构建只编译现有项目。

## 已知限制与后续边界

- 原生事务适配器限定于已有线性/矩形阵列的计数与间距，保持活动方向数；厚度、孔径、圆角/倒角等编辑及方向激活/停用未扩展。
- 当前构建会话、初始 XY 矩形框架仍为原生边界；未实现组合文件重开、控制器重启恢复或任意拓扑重解析。陈旧/漂移引用明确拒绝，策略允许完整升级不等于已实现所有恢复器。
- 原子提交针对 CADState JSON；原生编辑保留在打开模型中，没有 CAD 文件与 JSON 的跨文件联合提交。
- 原生适配器当前禁用自动恢复；通用事务的一次可选恢复及完整验证升级已做纯验证。
- M5 的 CadState 编辑重载仍为旧会话验证入口；M6 调用应使用 AtomicStateStore 重载或直接使用泛型事务/原生适配器。
- 读取范围缩小不等于已证明 PRD 的性能目标，未测量速度提升。

**Milestone 7 NOT IMPLEMENTED**。
