# Milestone 4 验证

**COMPLETE — MILESTONE 4 COMPLETE**。

依据 `Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` §27 M4，实现可复用的通孔、盲孔、线性阵列、矩形阵列、圆角、倒角处理器；按 §26/§28/§30 使用最小验收范围、独立预算账本和简洁证据。G1/G2 的创建均已通过，没有零件预设。CircularPattern 是 PRD 的条件项，本次未扩展旋转轴/圆盘支持路径，保留为未实现并在整计划执行前拒绝。

## 文件变更

新增后端：

- `src/CadHarness.SolidWorks/CompositionContext.cs`
- `src/CadHarness.SolidWorks/FeatureBackendRegistry.cs`
- `src/CadHarness.SolidWorks/NativeFeatureHandler.cs`
- `src/CadHarness.SolidWorks/HoleHandlers.cs`
- `src/CadHarness.SolidWorks/PatternHandlers.cs`
- `src/CadHarness.SolidWorks/EdgeTreatmentHandlers.cs`

修改后端：`BackendContracts.cs` 扩展构建上下文及失败状态字段；`CreateExtrudeHandler.cs` 登记创建输出；`CenteredRectangleProfileBackend.cs` 复用按新建草图句柄取得特征的函数；`ExtrudeStateAdapter.cs` 拒绝用 M3 单拉伸捕获器持久保存多特征组合，防止生成不完整组合状态。

修改 `src/CadHarness.Ir/Contracts.cs`，增加矩形拉伸方向边/四个外角边的语义输出。没有新增 planner 原生字段或可执行代码入口；这些输出不改变 JSON 字段结构，因此结构 schema 无需重新生成。

新增 `tests/CadHarness.Features.Tests`：项目、M4 纯测试、原生测试运行器及 `Fixtures/g1.json`、`g2.json`、`aux.json`。共享本项目既有 Part 生命周期/资源守卫；`tests/CadHarness.SolidWorks.Tests/NativeTestBudget.cs` 增加构造参数以允许 M4 独立上限 4，M2/M3 默认上限仍为 2。

新增 `scripts/test-milestone4.ps1`，修改 `scripts/build.ps1` 支持 M4 范围，`CadHarness.sln` 注册第八个项目，更新 `README.md`，新增本记录。PRD 未修改；没有读取/复制本地 v0.1 工作区。

## 实现边界

`CompositionBackend` 在首次修改前验证完整计划，拒绝不支持操作、关系、未登记外部输入、缺少重复方向间距和过大阵列。只允许一个初始矩形拉伸；以创建时的原生面/边输出连接通用处理器，通过原生持久引用访问后续输入，失效时明确返回 `STALE_REFERENCE`。初始拓扑输出要求唯一匹配，0 个返回 unresolved，多个返回 ambiguous。

外角竖直边按照矩形局部几何排序，不依赖 SOLIDWORKS 边枚举顺序或显示名。孔从顶面的局部原点定位，转换到原生草图坐标；通孔通过 ThroughAll，盲孔通过 Blind 定义执行。缺省孔位置为原点，缺省阵列方向为局部正 X/Y。阵列计数包含种子，最多 1024 个实例；矩形退化为 `1×N` 或 `N×1` 时归一化为一方向的原生阵列。圆角常半径，倒角等距离。

每个处理器检查原生定义的有关尺寸、端条件、计数/间距以及特征错误、警告和重建。不是 M6 的完整/增量验证器。失败结果记录 `MutationStarted`、`RollbackAttempted=false`、`RollbackSucceeded=false`、`StateCommitted=false`；M4 不声称具有事务或回滚。测试负责关闭/丢弃 Part，保留磁盘上的小型结果 JSON。

## 实际验证

```powershell
.\scripts\test-milestone4.ps1
.\scripts\test-milestone4.ps1 -Live -Case g1 -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone4.ps1 -Live -Case g2 -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone4.ps1 -Live -Case aux -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\build.ps1
```

SDK 8.0.425，SOLIDWORKS 2024 revision `32.0.1`。M4 纯测试 **17/17 PASS**：G1/G2/盲孔倒角计划预检、参数组合变化、关系拒绝、未知外部输入、缺少重复方向间距、单向矩形间距规则、实例上限、非法 host/direction 类型、非有限圆角半径、空边集合、编辑/圆周阵列拒绝、处理器种类不符和语义拓扑引用严格往返。拒绝案例用空 COM 上下文执行确认在访问原生文档前返回，且没有修改或状态提交。纯测试没有激活 COM。

仅执行下面三个最小原生组合，每个创建 1 个独立 Part：

| 案例 | 实测实体尺寸（mm） | 实测孔/边处理 | 实体体积（mm³） | 结果 |
|---|---|---|---|---|
| G1 | 80 × 50 × 10，1 实体 | 2 × Ø8，X=±20、Y=0；贯穿 Z=0..10；4 个 R3 外角圆角 | 38917.43368967433 | PASS |
| G2 | 100 × 60 × 8，1 实体 | 4 × Ø6，X=±30、Y=±15；贯穿 Z=0..8；2×2 原生阵列 | 47095.22131576613 | PASS |
| 盲孔/倒角 | 70 × 45 × 12，1 实体 | Ø10，位置 (7,-4)，Z=7..12，深 5；4 个外角边等距 2 mm 倒角 | 37311.300918301284 | PASS |

最终几何检查独立读取实体极值点、圆柱面参数、圆周边界 Z 高度及原生质量属性中的体积；对照解析几何期望值，不直接返回 IR 数值。G1 的种子 X=-20 和 G2 的种子 (-30,-15) 是测试 JSON 中的显式放置，用于证明创建能力；没有把居中关系公式编码进通用处理器。G2 的厚度/孔径编辑不属于本阶段验收。

| 生命周期/资源 | G1 | G2 | 盲孔/倒角 |
|---|---|---|---|
| 创建前响应 | true | true | true |
| 创建前 GDI（阈值 7000） | 547 | 550 | 551 |
| 创建前已打开测试 Part | 0 | 0 | 0 |
| Part 创建/关闭 | 1/1 | 1/1 | 1/1 |
| 原活动状态恢复 | true | true | true |
| 清理错误 | 无 | 无 | 无 |

累计创建 **3** 个 Part、关闭并丢弃 **3** 个，打开测试文档 **0**；预算 **3/4**，未消费第四次创建。没有扩大原生套件或为重复验证重新创建 Part，未重跑 M0–M3 功能测试、历史回归或性能基准。原生证据集中在 `artifacts/milestone4/native-result.json`，累计账本在 `native-budget.json`，没有保存额外 Part 文件或打开文档作为证据。

Release 构建 0 警告、0 错误；统一解决方案包含八个项目，构建只编译，不运行其他里程碑测试或调用 SOLIDWORKS。

## 已知限制

- 当前只支持初始 XY 矩形拉伸顶面的孔放置以及已登记的构建输出；其他平面、任意已有模型、旋转轴、圆盘/CircularPattern 未实现。
- `1×N`/`N×1` 矩形路径通过纯预检，原生最小验收覆盖线性与 `2×2`；没有扩展到更多原生案例。
- 面/边通过原生持久引用在本次构建中使用，没有完整组合 CADState 持久化/恢复或参数编辑；M3 捕获器保持单拉伸边界。
- 没有关系求解、通用 CADState Binder、依赖图、自动拓扑重解析、事务回滚、增量验证、规划器或 Jev。草图轮廓由坐标创建，尚未增加宽高/孔径参数绑定。
- 同一操作失败可能留下部分构建，结果明确报告未回滚/未提交状态；原生测试已验证成功路径及清理，未消耗 Part 预算测试失败恢复。

**Milestone 5 NOT IMPLEMENTED**。
