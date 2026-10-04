# Milestone 2 验证

状态：**COMPLETE — MILESTONE 2 COMPLETE**

依据：`Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` §27 Milestone 2、§26 资源/生命周期/预算、§28 完成报告、§30 最小证据。

## 实现及文件变更

新增：

- `src/CadHarness.SolidWorks/CadHarness.SolidWorks.csproj`
- `src/CadHarness.SolidWorks/AssemblyInfo.cs`
- `src/CadHarness.SolidWorks/BackendContracts.cs`：临时执行上下文、后端契约、程序预检与单位转换。
- `src/CadHarness.SolidWorks/SolidWorksConnection.cs`：STA COM 附加/启动和新 Part 创建。
- `src/CadHarness.SolidWorks/CenteredRectangleProfileBackend.cs`：原生居中矩形草图创建。
- `src/CadHarness.SolidWorks/CreateExtrudeHandler.cs`：原生拉伸与重建边界。
- `src/CadHarness.SolidWorks/ExtrudeMeasurement.cs`：仅测实体数及宽/高/深。
- `tests/CadHarness.SolidWorks.Tests/CadHarness.SolidWorks.Tests.csproj`
- `tests/CadHarness.SolidWorks.Tests/Fixtures/extrude.json`
- `tests/CadHarness.SolidWorks.Tests/Program.cs`
- `tests/CadHarness.SolidWorks.Tests/NativeResourceGuard.cs`
- `tests/CadHarness.SolidWorks.Tests/NativeTestBudget.cs`
- `tests/CadHarness.SolidWorks.Tests/TestPartScope.cs`
- `scripts/solidworks-interop.props`
- `scripts/test-milestone2.ps1`
- `docs/milestone-2-verification.md`

修改：`README.md`，补充 M2 范围、运行方式和限制。

PRD、M1 源码、M1 测试和 M1 脚本未修改。未读取本地 v0.1 工作区，未复制旧代码，未运行旧仓库测试或历史基准。参考了 `solidworks-automation` 的单位/原生 API 注意事项；其附带脚本缺失，实际实现依据安装的 interop 签名独立编写。

## 实际验证

```powershell
.\scripts\test-milestone2.ps1
.\scripts\test-milestone2.ps1 -Live -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

编译：PowerShell 7 自带 Roslyn，项目源码警告视为错误，Windows x64 .NET 8.0.31 执行。SDK 未安装，未实测 SDK/MSBuild 路径。编译中修正的命名冲突与 interop `ref` 签名问题均发生在创建原生 Part 之前。

纯测试：**8/8 PASS**，覆盖矩形拉伸预检、80/50/10 mm 到米转换、圆形后端拒绝、后续操作拒绝、多操作拒绝、关系拒绝、非法尺寸拒绝，以及 GDI 7000/已有测试文档/无响应的资源边界。此阶段没有连接或启动 SOLIDWORKS。

原生案例：**1 个案例，PASS，退出码 0**。SOLIDWORKS 2024，原生 revision `32.0.1`。只运行 PRD 指定的 80 × 50 矩形拉伸 10 mm，没有孔、阵列、圆形、圆角、倒角或额外模型。

| 验收项 | 原生结果 |
|---|---|
| 实体数 | 1 |
| 宽 | 80 mm |
| 高 | 50 mm |
| 深 | 10 mm |
| 重建 | 成功；特征无错误/警告，无待重建状态 |

尺寸读取来自实体 `GetExtremePoint` 的六个方向极值，独立于输入尺寸，比较容差为 0.000001 mm。

创建前资源守卫：进程响应 `true`，GDI **417**（阈值 7000），已打开测试所有 Part **0**。

原生 Parts 创建：**1**。关闭并丢弃：**1**。剩余测试所有文档：**0**。已恢复原活动状态（测试前无活动文档）。由测试启动的 SOLIDWORKS 会话在文档数为零后调用 `ExitApp` 请求退出；未强制终止或声称确认进程终止。无 Part 清理错误。

累计预算：创建尝试 **1/2**，创建 **1**，关闭 **1**。没有重试原生案例，没有用剩余预算增加验证，也未运行基准或扩展套件。

仅保存两个小 JSON：`artifacts/milestone2/native-budget.json`（预算与未关闭测试文档登记）和 `artifacts/milestone2/native-result.json`（本次原生结果）。预算账本跨进程独占锁定，在调用 `NewDocument` 前预留尝试；预算耗尽后拒绝新建。这是测试资源记录，不是 CADState。编译 DLL 同样位于忽略的 artifacts 目录。

## 已知限制及下一阶段

只支持空 Part 上单个居中矩形拉伸，要求原点 XY 对齐构造平面；其他模板未扩展验证。原生案例开始时无活动文档，因此已有用户文档恢复分支未额外验证。尚无草图尺寸参数绑定、CADState、持久引用、通用绑定器、完整验证器、关系执行、LLM/Jev 或事务/回滚系统。

原生验收后补充了连接所有权判断：COM 激活若复用启动前已存在的进程，则不退出该用户会话。此变更只影响会话清理所有权；随后重新编译并运行相同的 8 个纯测试，没有重新执行或扩展原生案例。

**Milestone 3 NOT IMPLEMENTED**。
