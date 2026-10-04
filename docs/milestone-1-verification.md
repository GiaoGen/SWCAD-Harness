# Milestone 1 验证

状态：**COMPLETE — MILESTONE 1 COMPLETE**

依据：`Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` §27 Milestone 1、§26 纯测试范围、§28 完成报告、§30 最小证据。

新增文件：

- `src/CadHarness.Ir/`：`CadHarness.Ir.csproj`、`Model.cs`、`Contracts.cs`、`EditableParameters.cs`、`WireNames.cs`、`ProgramValidator.cs`、`CadProgramJson.cs`、`ProgramJsonSchema.cs`。
- `tests/CadHarness.Ir.Tests/`：`CadHarness.Ir.Tests.csproj`、`Program.cs`。
- `schemas/cad-program.schema.json`：由契约生成的结构 schema。
- `scripts/test.ps1`：本阶段测试入口，支持 SDK 和本地离线 Roslyn 编译。
- `.gitignore`、`README.md`、`docs/milestone-1-verification.md`。

PRD 原文件未修改。未访问或运行旧仓库测试。

实际执行：

```powershell
.\scripts\test.ps1 -UseBundledCompiler -WriteSchema
.\scripts\test.ps1
```

本地 Roslyn 将库和测试编译为 .NET 8 程序，项目源代码警告视为错误；由已安装 .NET 8.0.31 运行。结果：**80/80 纯测试通过**，退出码 0。

必测要求覆盖：

| PRD 要求 | 结果 |
|---|---|
| 合法程序可解析 | PASS，包含 PRD 示例、两种轮廓和九类操作 |
| 未知操作拒绝 | PASS，返回 `OPERATION_UNSUPPORTED` |
| 缺失输入拒绝 | PASS |
| 错误语义类型拒绝 | PASS，包含声明类型与本计划输出不符 |
| 非法数值拒绝 | PASS，包含非正尺寸、非有限值、非整数计数 |
| planner 字段禁止 API/COM 名称 | PASS，覆盖 ID、语义 ID、输入引用与 operation kind |
| 序列化往返 | PASS，规范化序列化稳定且语义类型保留 |
| 注册表查询 | PASS，kind/name 一致且契约完整 |

严格解析的相关纯测试还覆盖未知/重复字段、JSON 格式、操作数边界、非法参数编辑、关系声明和 schema 与契约一致性；均属于 Milestone 1。

原生 Parts 创建：**0**。原生 Parts 关闭：**0**。未连接或启动 SOLIDWORKS，未打开 Part 文档。

已知限制：本机无 .NET SDK，SDK/MSBuild 路径未实测；已验证的是相同源码的本地 Roslyn 编译及 .NET 8 执行。schema 提供结构约束，跨操作约束由程序验证器执行。关系仅为声明；外部实体不做绑定；省略的阵列间距等须在未来执行前解决。未实现或验证原生几何行为。

下一里程碑：**Milestone 2 NOT IMPLEMENTED**。
