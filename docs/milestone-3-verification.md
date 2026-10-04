# Milestone 3 验证

**COMPLETE — MILESTONE 3 COMPLETE**。

依据 PRD §13 新 CADState、§27 Milestone 3、§26 生命周期/资源/预算、§28 完成报告及 §30 最小证据。用户授权先补齐 M0，再执行 M3；M0 标准 SDK 构建与纯运行器通过后才开始状态实现。

## 文件变更

新增：

- `src/CadHarness.State/CadHarness.State.csproj`
- `src/CadHarness.State/CadState.cs`
- `src/CadHarness.State/StateValidation.cs`
- `src/CadHarness.State/AtomicStateStore.cs`
- `src/CadHarness.SolidWorks/DocumentIdentityAdapter.cs`
- `src/CadHarness.SolidWorks/PersistentReferenceAdapter.cs`
- `src/CadHarness.SolidWorks/ExtrudeStateAdapter.cs`
- `tests/CadHarness.State.Tests/CadHarness.State.Tests.csproj`
- `tests/CadHarness.State.Tests/PureTests.cs`
- `tests/CadHarness.State.Tests/Program.cs`
- `scripts/test-milestone3.ps1`
- 本记录。

修改：`CadHarness.sln` 注册状态库和 M3 测试项目；`src/CadHarness.SolidWorks/CadHarness.SolidWorks.csproj` 引用状态库；本项目 M2 的三个测试生命周期辅助文件支持独立 M3 预算/标签以及保存后的标题更新；`scripts/test.ps1`、`scripts/test-milestone2.ps1` 优先选择工作区 SDK，后者离线编译补上状态库依赖；`README.md` 更新范围和入口。

PRD、M1 IR 源码与操作 schema、M2 几何创建路径未修改。没有使用本地 v0.1 工作区或历史测试系统。

## 实现范围

通用 v0.2 状态结构有 document、features、entities、parameters、bindings、relations、dependencies、revision。文档和配置使用原生自定义属性中的独立 GUID，保存配置名与规范化文件路径；恢复前全部核对。节点记录语义 ID、特征所有权、原生持久引用与健康状态，参数绑定由拥有者语义 ID 和有限参数枚举组成。

本阶段实际捕获一个拉伸特征、一个该特征的 `FeatureRef` 实体、一个拉伸深度参数及绑定。没有固定引用数或模型预设；ID 从 IR 获取。关系/依赖槽必须为空，没有关系引擎、图扩展或绑定器。仅用 SOLIDWORKS 原生持久引用精确恢复，不做面/边重新解析。

状态提交：严格校验 → 同目录临时文件 → 刷盘 → 原子替换/首次移动。没有 v0.1 迁移，没有联合 CAD/JSON 事务或回滚系统。

## 实际验证

```powershell
.\scripts\test-milestone3.ps1
.\scripts\test-milestone3.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
# 修正原生类型判定后，使用第二个且最后一个 Part 预算执行同一个案例：
.\scripts\test-milestone3.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
# 最后补齐参数绑定类型检查并进行仅编译/纯测试的工程兼容检查：
.\scripts\test-milestone3.ps1
.\scripts\build.ps1
.\scripts\test-milestone2.ps1 -BuildOnly -UseBundledCompiler
```

固定 SDK 8.0.425，.NET 8 x64；M3 项目及包含七个项目的统一解决方案 Release 构建均 **0 警告、0 错误**。旧 M2 入口仅编译通过，没有重跑 M2 功能或原生测试，也没有运行 M1/旧仓库回归。

M3 纯测试最终 **13/13 PASS**，覆盖：身份/所有权/参数绑定往返、完整原子替换、非法提交保留旧文件、实际文件替换失败保留旧文件及清理临时文件、v0.1 拒绝、文档/配置/路径不匹配、未知所有权、未绑定/重复绑定、非法持久载荷、绑定标量类型约束、非有限数值、重复/未知 JSON 字段、非空关系槽拒绝。纯测试未激活 COM。

原生验证为同一个单拉伸案例的两次尝试，SOLIDWORKS revision `32.0.1`：

| 项目 | 第一次 | 第二次 |
|---|---|---|
| 创建前响应 | true | true |
| GDI（阈值 7000） | 545 | 546 |
| 已打开测试 Part | 0 | 0 |
| 持久身份写入、原生保存 | 成功 | 成功 |
| 状态捕获、原子提交、磁盘加载 | 成功 | 成功 |
| 持久引用恢复 | 类型名称检查拒绝 | PASS，healthy |
| 原生深度读取 | 未执行 | 10 mm |
| 测试 Part 创建/关闭 | 1/1 | 1/1 |
| 原活动状态恢复 | true | true |
| 清理错误 | 无 | 无 |

首轮使用 `GetTypeName2() == "Boss"` 判断合法拉伸过窄，返回 `STALE_REFERENCE`。改为检查 `IExtrudeFeatureData2` 原生定义接口，以支持 SOLIDWORKS 的原生类型名称差异；没有改用显示名称或几何猜测，也没有放宽引用健康检查。第二轮退出码 **0**。

成功案例从磁盘加载 v0.2 JSON，在没有 `CreatedFeature` 句柄的新上下文中恢复 `base_plate`，读取 `base_plate.extrusion_depth` 的原生值 **10 mm**。值来自原生特征定义，不直接返回状态文件中的数值。文档 GUID、配置 GUID、配置名及路径均匹配。

累计原生 Parts 创建 **2**、关闭并丢弃 **2**，剩余测试文档 **0**，预算 **2/2 已用满**。原活动状态已恢复（该连接测试前无活动文档）。不再创建原生 Part；未来自动重复会在创建前返回 `ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED`。

保留一份成功案例原生文件 `artifacts/milestone3/CADHarnessM3Test_plate_2.SLDPRT`、当前 `cad-state.json`、小型 `native-budget.json` 和最新 `native-result.json`。失败案例的 Part 文件已移除；未保留打开的文档或历史运行档案。

## 已知限制

未重启控制器进程或另行重开原生文件；PRD 将控制器重启列为允许项，本次采用最小的磁盘加载与新上下文验证。尚未捕获面/边、绑定轮廓宽高、执行参数编辑、处理配置重命名/Save As 身份迁移、实现关系、通用绑定器、完整原生验证器或回滚。JSON 原子提交不保证与 CAD 文件形成联合事务。

**Milestone 4 NOT IMPLEMENTED**。
