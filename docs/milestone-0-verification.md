# Milestone 0 补齐验收

**COMPLETE — MILESTONE 0 COMPLETE**。

本次仅补工程设施，没有改写已通过的 M1/M2 CAD 实现，也未创建原生文档。

新增文件：`CadHarness.sln`、`global.json`、`NuGet.Config`、`config/toolchain.json`、`scripts/setup-dotnet.ps1`、`scripts/sdk-environment.ps1`、`scripts/build.ps1`、`scripts/test-bootstrap.ps1`、`tests/CadHarness.Bootstrap.Tests/CadHarness.Bootstrap.Tests.csproj`、`tests/CadHarness.Bootstrap.Tests/Program.cs`、本记录；更新 README。

SDK：微软官方 Windows x64 .NET **8.0.425**，归档 SHA-512 与官方发布元数据一致，在 `artifacts/toolchain/dotnet` 安装，未修改系统 SDK、PATH 或注册表。版本由 `global.json` 固定。配置用环境变量/脚本参数提供 interop 路径；统一解决方案引用所有已有项目。没有第三方 NuGet 包，包源清空，构建/缓存留在工作区。

执行：

```powershell
.\scripts\setup-dotnet.ps1
.\scripts\build.ps1
.\scripts\test-bootstrap.ps1
```

标准 SDK/MSBuild Release 全工程构建成功，**0 警告、0 错误**。Bootstrap 纯运行器 **2/2 PASS**，验证 .NET 8 x64 执行和安装的两个 interop 程序集元数据；未激活 COM。

原生 Parts 创建 **0**、关闭 **0**。没有运行旧仓库测试、M1/M2 功能回归或基准。

下一步按用户授权执行 M3；M0 本身不实现 CADState。
