# Milestone 5 验证

**COMPLETE — MILESTONE 5 COMPLETE**。

依据 PRD §11/§13/§14/§27 M5，实现 Semantic Entity Binder、`centered_about`、`symmetric_about_axis`、`pattern_seed`、`hosted_on`、`equal_spacing` 和依赖图，将阵列参数与种子位置的耦合放入独立关系处理器。三个要求的居中编辑任务均已原生通过，累计创建 3 个 Part，符合 §26 上限。

## 文件变更

新增纯状态/语义实现：

- `src/CadHarness.State/SemanticGeometry.cs`
- `src/CadHarness.State/StateRelationData.cs`
- `src/CadHarness.State/DependencyGraph.cs`
- `src/CadHarness.State/SemanticEntityBinder.cs`
- `src/CadHarness.State/DesignRelationEngine.cs`
- `src/CadHarness.State/RelationParameterEditor.cs`

修改 `CadState.cs` 增加可选几何元数据，`StateValidation.cs` 校验类型化关系/依赖、几何、引用端点、有限边界及原生依赖无环性；既有空数组状态仍可解析。空 `savedPath` 明确表示未保存的会话文档，GUID/配置仍必需；没有 v0.1 迁移。

新增原生后端：

- `src/CadHarness.SolidWorks/SemanticStateCapture.cs`
- `src/CadHarness.SolidWorks/NativePatternEditor.cs`
- `src/CadHarness.SolidWorks/RelationNativeReadback.cs`
- `src/CadHarness.SolidWorks/RelationBackend.cs`

修改 `CompositionContext.cs` 登记局部框架/中心轴、圆形轮廓及 M5 会话状态，通过 CADState Binder 使用原生输出；`HoleHandlers.cs` 绑定创建的孔轮廓；`PatternHandlers.cs` 复用方向/参数映射；`NativeFeatureHandler.cs` 在 M5 模式按输入契约进行绑定；`DocumentIdentityAdapter.cs` 增加未保存文档的 live binding 身份读取，M3 保存要求保持原入口边界。`src/CadHarness.Ir/Contracts.cs` 声明局部轴与孔轮廓语义输出，`Model.cs` 更新关系职责注释；未增加 planner-facing 原生 API 字段，结构 schema 不变。

新增 `tests/CadHarness.Relations.Tests` 项目、纯测试/原生运行器、`Fixtures/linear.json`、`rect2x2.json`、`rect2x3.json`、`combined.json`；共享既有资源守卫、账本和 Part 清理工具。新增 `scripts/test-milestone5.ps1`；更新 `scripts/build.ps1`、`CadHarness.sln`、`README.md`，新增本记录。统一解决方案现有九个项目。

PRD 未修改，没有读取或复制本地 v0.1 工作区，没有增加生产零件预设。

## 实现与边界

Binder 从 CADState 查询候选，以输入契约检查语义类型，过滤健康状态、所有权、几何和依赖。0→`BINDING_UNRESOLVED`，1→直接绑定，N→最多 64 个候选的有证据排序；显式首选所有者仍不能给出唯一候选时返回 `BINDING_AMBIGUOUS`。不调用 LLM，不按枚举顺序取第一个。原生桥接再验证持久引用、接口和实际几何合法性。

五类关系有独立类型化处理器。`centered_about` 从一般的计数/间距/方向计算跨度，让种子位于负半跨度；`symmetric_about_axis` 只约束垂直于中心轴的坐标。`hosted_on`/`pattern_seed` 必须与操作的宿主/种子输入一致，`equal_spacing` 的 reference 为种子，断言每个重复方向使用统一、明确的步距，不凭空补长度。重复关系、共享种子的冲突约束、越出矩形宿主、相交实例或不支持的关系在修改前拒绝。

依赖边区分原生创建依赖与关系影响。原生依赖必须无环；种子→阵列→种子的关系循环允许存在，以有限集合计算影响闭包。返回的 AffectedFeatures 是图查询结果，没有实现 M6 的 ChangeSet/DirtySet 或增量验证系统。

原生编辑只改当前线性/矩形阵列的计数和间距，保留已验证的种子/方向选择，把关系解出的种子坐标应用到已绑定的圆形草图；通过原生特征数据修改并重建。读回检查实际草图宿主、阵列种子、方向、计数/间距、跳过实例/变实例设置，以及实际圆心与半径。成功后更新会话 CADState/revision；失败报告已开始修改、未回滚、未提交，并禁止继续使用可能部分修改的上下文。

局部框架与中心轴有类型化几何，以拥有者特征的原生持久引用作为逻辑锚点；没有创建原生参考轴特征。关系与依赖保持既有 JSON 数组槽，严格解码为 DesignRelation/DependencyEdge。纯测试验证原子 StateStore 往返，原生测试不声称提供组合文件的重开/控制器重启恢复。

## 验证

```powershell
.\scripts\test-milestone5.ps1
.\scripts\test-milestone5.ps1 -Live -Case linear -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
# 修正编辑调用后，保持总预算，合并两孔与 2×2 编辑任务：
.\scripts\test-milestone5.ps1
.\scripts\test-milestone5.ps1 -Live -Case combined -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\test-milestone5.ps1 -Live -Case rect2x3 -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\build.ps1
```

SDK 8.0.425；M5 及九项目 Release 解决方案构建 **0 警告、0 错误**。构建只编译，没有运行其他里程碑测试。

M5 纯测试最终 **39/39 PASS**：三种布局的中心求解、线性和矩形 X/Y 间距编辑、计数编辑、独立对称坐标、关系顺序无关、共享种子冲突、局部 Y 方向、错误 host/seed/equal-spacing/reference、未给间距、越界及不支持编辑/关系拒绝；Binder 唯一/零/多候选、所有权、类型、圆柱几何、健康状态、依赖、有限有证据排序；图闭包和原生环拒绝；关系/依赖/几何 StateStore 往返、非法 JSON/类型/端点拒绝；共享宿主的独立阵列编辑不交叉耦合。纯测试无 COM 激活、0 native Parts。

首个 `linear` 原生尝试：创建与中心布局通过，编辑收到 `0x80010105 (RPC_E_SERVERFAULT)`，返回 `GEOMETRY_INVALID`、revision 0、未回滚、未提交。该测试 Part 已关闭并丢弃，原活动状态恢复。随后将原生编辑缩小为必要的计数/间距标量定义，保留原种子/方向选择，移除不必要的轴/种子赋值及半径 setter；后续验证成功。首轮没有逐调用定位证据，因此不声称某一个 setter 已被单独确认是异常根因。

为不超过 3 个 Part，上述失败之后，将两孔和 2×2 的要求放入一个共享板件的通用 JSON 组合中验证，两组孔使用不同孔径、独立种子/关系；只添加测试组合，未添加生产流程。另一个独立 Part 验证 2×3。每次运行均只拥有一个测试 Part，调用结束后关闭并丢弃。

| 要求 | 编辑 | 直接读取的最终孔位（mm） | 关系/参数/引用 |
|---|---|---|---|
| 两孔居中编辑 | 间距 40→50 | Ø8，X=±25，Y=0 | PASS；revision 1，原生绑定值 50，特征持久引用保持 |
| 2×2 居中编辑 | X 间距 60→70 | Ø6，X=±35，Y=±15 | PASS；同一 Part revision 2，原生绑定值 70，独立线性布局保持 |
| 2×3 居中编辑 | Y 计数 3→4 | Ø8，X=±30，Y=-30,-10,10,30；最终 2×4 | PASS；revision 1，原生绑定值 4，特征持久引用保持 |

合并 Part 为 100×60×8，初始/编辑后分别测量实际孔数、位置、孔径、贯穿 Z=0..8 和实体体积 **46290.97359644714 mm³**，两组孔的几何中心分别为原点。保存 10 项关系、12 条依赖的会话状态。这里保存指会话 CADState 的内容；没有耐久原生文件/状态联合提交。

2×3 Part 为 120×80×10，初始 6 孔、X=±30、Y=-20,0,20，体积 **92984.07105255377 mm³**；编辑后 8 孔、X=±30、Y=-30,-10,10,30，体积 **91978.76140340503 mm³**，均贯穿 Z=0..10。会话状态包含 5 项关系、6 条依赖。实体读回独立于求解器输出；校验实际圆柱面、圆周边界及原生质量属性，未直接返回 IR 数值作为观测。

| 生命周期/资源 | 首次 linear | 合并两孔/2×2 | 2×3 |
|---|---|---|---|
| 创建前响应 | true | true | true |
| 创建前 GDI（阈值 7000） | 552 | 555 | 556 |
| 创建前已打开测试 Part | 0 | 0 | 0 |
| Part 创建/关闭 | 1/1 | 1/1 | 1/1 |
| 原活动状态恢复 | true | true | true |
| 清理错误 | 无 | 无 | 无 |

累计创建 **3**、关闭/丢弃 **3**、打开测试文档 **0**，**预算 3/3 已用满**。账本 `artifacts/milestone5/native-budget.json` 保留累计消耗；`native-result.json` 集中保留首次失败和两次成功的简洁结果。禁止删账本绕过预算。后续原生调用会在附加应用/创建文档前返回 `ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED`。没有保存额外 Part 文件或使用打开文档作为证据。

没有重跑 M0–M4 功能/原生测试、旧仓库回归、广泛原生套件、LLM/Jev、性能基准或 stepwise baseline。

## 已知限制

- 当前支持初始 XY 矩形框架上的孔阵列关系，中心轴仅为局部 X/Y；不支持任意已有模型、任意旋转框架或圆周阵列。
- M5 原生编辑只支持阵列计数/间距，维持当前活动方向数；激活/停用方向、厚度、孔径、倒角/圆角等参数原生编辑尚未实现。纯解算器支持更多有限 X/Y 变化，但不等同于已验证原生路径。
- 原生验收覆盖当前构建会话，未重开文件或重启控制器恢复整个组合；未增加原生文件保存/状态联合事务。
- 成功编辑更新会话 CADState/revision，`stateCommitted=false`。无 M6 通用事务、回滚、ChangeSet/DirtySet、增量验证或失效拓扑重新解析；修改中途失败可能留下部分模型，上下文立即禁止继续使用。
- 首次 COM 异常的单调用根因未独立定位；经过缩小编辑路径的成功原生验证证明当前所需路径可用，没有扩展测试预算定位其他路径。

**Milestone 6 NOT IMPLEMENTED**。
