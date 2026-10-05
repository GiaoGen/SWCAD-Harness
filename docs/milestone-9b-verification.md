# M9B — Circular Geometry Capability

状态：**COMPLETE**。仅实现用户指定 M9B，基于已验收 M9A；遵循 PRD G4、语义类型/绑定/状态/验证及 §26 原生测试规则和 M7 runtime capability projection 要求。没有修改 PRD、读取/复制本地 v0.1、增加 G4 生产 preset、执行 M9C+、旧里程碑测试或 broader generalization/benchmark。

## 实现与通用边界

`CreateExtrudeHandler` 增加 CircleProfile，并沿用 XY construction plane 查找、受控 sketch preferences/finally 清理和原生 blind extrusion。新增 `CircleProfileBackend` 创建以原点为中心的实际 circle sketch，所有尺寸来自 operation 参数。

`ProfileOutputs` 提供有限的 profile 输出集合。圆形拉伸有 feature、body、top/bottom PlanarFace、LocalFrame、逻辑 X/Y 轴、`.rotational_reference` CylindricalFace；没有矩形 LinearEdge 输出。矩形保留已有边输出，不输出 `.rotational_reference`。IR 注册表记录两个 profile 的输出词汇，实际 composition 与关系 preflight、runtime catalog 分别检查当前 profile 能产生的输出。

**旋转参考是实际外圆柱面**，通过 extrusion 直接产生的唯一圆柱面获得并捕获 native persistent reference；绑定后确实解析为 IFace2/ISurface cylinder。local frame/逻辑轴仍有显式 typed geometry，但不会用于冒充真实 circular native axis。

`CreateCircularPatternHandler` 加入 FeatureBackendRegistry，使用 typed Binder、native selection marks 和 FeatureCircularPattern4。允许同一 circle extrusion 顶面的托管 hole feature seed；axis 必须是该 extrusion 的 `.rotational_reference`。通过圆柱面轴方向统一为 local +Z 旋转，geometry-pattern/vary-sketch/second-direction/body-pattern 关闭，equal spacing 开启，禁止 skipped instances。数量限制 2..1024，span >0..360°，缺省 360°。

`PatternGeometry` 在纯 State 层计算通用 XY 实例位置。完整一圈步长 span/count，避免重复末端；部分圆弧 span/(count−1)，包含末端。`ProfileGeometry` 支持矩形或圆形宿主的圆孔严格包含检查。关系解算检查所有孔及其 pattern instances 的宿主边界和相交/相切，单宿主有限上限 4096 孔。圆周阵列支持 `pattern_seed`/`equal_spacing`，孔继续 `hosted_on`；圆周 centered/symmetric layout 未实现，preflight 拒绝。

没有 G4 专用分支、尺寸常量或语义 ID 写入生产文件；`tests/CadHarness.CircularGeometry.Tests/Fixtures/g4.json` 是四个通用操作的显式组合。纯测试使用不同尺寸、数量、角度和旋转 seed，验证输出与布局不依赖 G4 preset。

## 状态、读回、事务和 Planner

`SemanticStateCapture` 捕获圆盘 profile_diameter/extrusion_depth、孔径、圆周 pattern_count/pattern_angle，数值来自实际 cylinder radius/extrusion definition/circular definition，angle 以度保存。存储特征/实体 native persistent references、健康状态、几何、所有权、关系和 dependency graph。StateValidation 允许 circular pattern_seed/equal_spacing，其他关系类型保留已有边界。

`RelationNativeReadback` 检查圆盘原生尺寸、圆周 definition/count/span、seed 与 axis 的实际 persistent refs、orientation/uniform native modes。`NativeHoleInstanceVerifier` 用 seed/pattern features 直接产生的 faces 验证各实例真实位置、圆柱半径、Z 方向和 circular boundary 高度；普通编辑没有以整个 body 扫描代替 Level 2。测试运行器另对最终 body 做独立几何与质量属性检查。

**通用 MutationTransaction 核心不变；没有新增 parameter mutation handler。** 沿用 M9A 的 HoleDiameterMutationHandler、opaque rollback、Binder、ChangeSet/DirtySet、增量/完整验证、AtomicStateStore。共享 instance verifier 扩展到圆周布局；ChangeSet 包含圆周旋转面，dependency graph 把 bolt seed 的修改传播至 ring。atomic commit failure 后恢复 native sketch 半径、session metadata/revision、outputs，完整 rollback validation 检查所有参数和实体。

RuntimeCapabilityCatalog 增加只读结构化 `outputsByProfile`；backend 构造 catalog 的源头仍是实际 FeatureBackendRegistry/SupportedProfiles。axis accepted type 收窄为 CylindricalFace。生产 preflight 拒绝虚构圆盘线性边、矩形圆柱轴、逻辑 ReferenceAxis 或孔壁替代轴。Planner 的可编辑 target/parameter 对继续来自 mutation registry，依赖输入也要求唯一健康实体。G4 live catalog 只有两个实际 hole_diameter 编辑；没有 Circle 的 profile_diameter/extrusion_depth 或 CircularPattern 的 count/angle 编辑承诺。

## 专属验证

```powershell
.\scripts\test-milestone9b.ps1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\test-milestone9b.ps1 -Live -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
.\scripts\build.ps1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
```

SDK 8.0.425；**32/32 M9B 纯测试 PASS**。覆盖 composition、profile 输出可用性、native handler/catalog/schema、axis 类型/所有权拒绝、完整与部分 span、转动 seed、非 G4 尺寸/数量、非法 count/angle、圆形宿主边界/相切/实例及中心孔相交、只读 scalar 期望、现有孔径 mutation projection、健康旋转输入、DirtySet 和严格 deterministic Planner。无真实模型 API 请求、纯测试创建 0 Parts。

**16 项目 Release 编译 PASS，0 警告、0 错误**；全解决方案仅编译，未执行旧测试。新增独立 CircularGeometry.Tests 项目、Milestone9B build scope 和 test-milestone9b.ps1。

原生连接 SOLIDWORKS revision **32.0.1**、process **1984**。运行前 responding=true，GDI=**549**（阈值7000），打开测试 Part=0。

G4 初始与最终：单 body，精确 support-point 尺寸 **100×100×12 mm**；1 个 Ø100 外圆柱面、1 个 Ø20 中心贯穿孔壁、6 个 Ø8 bolt 通孔壁。bolt centers：`(35,0)`、`(17.5,30.310889...)`、`(-17.5,30.310889...)`、`(-35,0)`、`(-17.5,-30.310889...)`、`(17.5,-30.310889...)`，PCD=70；所有 cylinder circular boundaries 为 Z=0 与12。原生质量属性体积 **86858.7536864506 mm³**，与 `π × (50²−10²−6×4²) ×12` 一致（容差0.001 mm³）。所有 native feature error/warning/rebuild 检查通过。

| 同一个 Part 的原生阶段 | 结果 |
|---|---|
| 通用构建 + state round-trip | 4 features、15 entities、6 parameters、4 relations，revision=0；AtomicStateStore 提交/读取后，全部实体 Binder 唯一绑定及 persistent reference 重新解析成功；实际 circular definition axis 等于 disk 外圆柱面 reference，seed 等于 bolt_seed feature reference |
| 部分圆弧和漂移 | 测试代码临时用 native ModifyDefinition 改为4 instances/210°，独立检查实际四孔的正向位置、边界、体积及 state count/angle readback；用原 committed G4 state 请求编辑返回 STATE_DRIFT_DETECTED，修改前拒绝并升级完整读回，文件逐字节不变；finally 恢复6/360° |
| 现有 HoleDiameter Ø8→Ø9 | 六个真实 bolt 孔均 Ø9，位置/贯穿高度保持；增量验证、revision 0→1、原子提交 |
| 实际 atomic commit failure | 真实 Ø9→Ø9.5、native propagation/validation 成功后，用 FileShare.Read 锁使 File.Replace 失败；STATE_COMMIT_FAILED，真实 rollback 至六孔 Ø9，完整恢复验证通过，revision仍1，state文件逐字节不变 |
| 继续使用并恢复 G4 | 成功提交 Ø9→Ø8，revision 1→2；全部实体重新绑定、native axis/seed 检查通过，最终 body 为名义 G4，原 document/configuration identity、feature refs 与 relations 保持，无临时 commit 文件 |

部分圆弧阶段是测试内直接原生 definition 修改与读回，**不声称实现圆周 count/angle mutation handler**。完整 G4 的创建使用生产 circular handler；部分 span 使用同一 native equal-spacing definition 验证角度含义与 +Z 方向。

普通孔径编辑的状态标量读取范围为 **1/6 参数**、**7/15 实体**、**3/4 关系**；四个 managed features 均做原生状态检查。圆周 definition 的 count/span、axis/seed、mode 检查属于布局关系读回，额外的实例 face 读回属于必要 native geometry postcondition。漂移升级及 rollback 使用完整验证。没有计时或效率结论。

## 生命周期、证据和限制

M9B 独立耐久账本上限2次创建尝试；实际 **1/2**。创建 **1**、关闭/丢弃 **1**，并发测试 Part 最大1，最终0；原活动文档恢复 true，cleanup error=null。没有消耗第二次尝试或改写旧里程碑 budget/evidence。

证据在 `artifacts/milestone9b/`：`pure-result.json`、`construction-capabilities.json`、`native-run.log`、`native-result.json`、`initial-state.json`、`native-state.json`、`native-edit-capabilities.json`、`native-budget.json`、`solution-build.log`。原生报告 COMPLETE，无证据替代验收或失败豁免。数值比较采用1e-6 mm/degree容差，质量属性采用0.001 mm³容差。

当前仍为初始 XY circle extrusion 的托管 construction session；支持该宿主顶面的圆孔和同宿主外圆柱面 rotation reference。没有任意空间圆盘/外部轴、圆边倒圆/倒角、拓扑猜测恢复、组合 native Part 重开/controller restart、构造 transaction rollback、native Part/JSON 联合原子提交。未保存 native Part，savedPath为空，JSON 是已关闭测试 Part 的会话证据，不能作为可重开执行的模型。

**M9B COMPLETE；M9C+、完整 M9 泛化评估与 comparative benchmark 未执行。**
