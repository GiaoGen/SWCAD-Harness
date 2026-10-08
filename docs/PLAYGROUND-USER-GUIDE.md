# SWCAD-Harness Playground V0 使用指南

Playground 是已完成 GENERALIZED CAD HARNESS v0.2 的本地开发者控制台，展示自然语言、真实 Planner、有限 capability、预检、原生事务和 committed CADState。它不是 M11，不承诺任意 CAD 支持。

本次开发验收只使用 pure/mock 和 localhost 浏览器检查：**SOLIDWORKS launched: NO；Native Parts created: 0**。以下原生 smoke 由使用者手动执行。

## 前提

- Windows x64，固定 .NET SDK 8.0.425（`global.json`）及随 SDK 提供的 ASP.NET Core 8 runtime。脚本优先使用仓库本地 SDK；缺失时运行 `scripts/setup-dotnet.ps1`。
- 构建需要 SOLIDWORKS API redist 中的 `SolidWorks.Interop.sldworks.dll` 和 `SolidWorks.Interop.swconst.dll`。离线规划/预检不激活 COM，但编译仍需要这两个程序集。
- 手动原生建模需要已安装、许可可用的 SOLIDWORKS 和存在的 `.prtdot`。不同机器请替换以下路径。
- 真实规划需要支持 Responses strict json_schema 的 API。DeepSeek 默认官方 endpoint 和 `deepseek-chat`，实际 alias 以响应为准。
- 无 npm、React、Next.js、CDN 或远程前端资源依赖。

## 启动

仓库根目录 PowerShell：

```powershell
.\scripts\start-playground.ps1 `
  -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist' `
  -PartTemplate 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
```

打开 **http://127.0.0.1:5186/**。只绑定 IPv4 loopback；Host/Origin 门禁要求使用打印的 IP URL，不接受 `localhost` 别名。改端口可传 `-Port 5188`。

已有正确 dotnet 环境时：

```powershell
$env:SOLIDWORKS_INTEROP_DIR = 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
$env:CAD_HARNESS_PART_TEMPLATE = 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
dotnet run --project src/CadHarness.Playground -c Release
```

启动不连接 CAD、不调用 LLM、不自动选择环境 credential。`CAD_HARNESS_PLAYGROUND_PORT` 可改端口；template 未指定时连接后使用 SOLIDWORKS 默认 Part template。

## LLM 配置与安全

1. Provider 选 DeepSeek，Endpoint 保持 `https://api.deepseek.com/responses`，Model 为 `deepseek-chat`。
2. Credential source 选 Enter API key；或显式选择 `DEEPSEEK_API_KEY`、`OPENAI_API_KEY`、`CAD_HARNESS_LLM_API_KEY`。环境变量值不会返回浏览器。
3. Timeout 默认 120 秒，范围 1–300；Max output tokens 默认 8192，范围 256–32768。
4. 点击 Apply settings。密码输入框清空，顶部仅显示掩码后四位。
5. 点击 Test connection。这是一次真实、可能计费的 provider 请求，使用共享 Responses adapter 和当前 Planner schema；输出丢弃，不发布可执行计划，不连接/执行 CAD。

可选 Responses compatible 复用已有 OpenAI-compatible Responses adapter，要求 HTTPS 和 `text.format=json_schema / strict`。不支持 Chat Completions endpoint、JSON-mode fallback 或工具调用响应。DeepSeek 选项固定官方 URL；自定义 URL 请选 Responses compatible。

Key 只保存在 host 内存，不写 repo、state、report、日志、URL、localStorage 或 sessionStorage。localStorage 仅记主题。Clear API key 清除 credential 和待执行 plan；host 退出后 key 消失。当前 credential 会从返回和留存结果中脱敏；不要把其他密钥放入 intent。重新 Apply 配置会使旧 plan 失效。

## 第一个安全测试

输入：

```text
Create an 80 x 50 x 10 mm rectangular plate.
```

1. Generate Plan：真实 `CadPlanner` 请求 LLM，严格 envelope/IR 解析、capability 检查和 Planner 内部 pure preflight。创建 Part 数为 0。
2. Inspect：Visual 应为一个 80×50 centered_rectangle、10 mm extrusion。检查 Raw IR、Relations、Capabilities、Planner Details。语义 ID 可变化，runtime 不自动修复或重命名。
3. Run Preflight：独立的显式 production pure preflight，只有 aggregate 结果；不会虚构细粒度 PASS，也不保证 fillet 等原生可行性。
4. Connect SOLIDWORKS：明确连接已有应用或按 ownership 规则启动，顶部显示 Attached existing session / Started application。
5. Execute in SOLIDWORKS…：核对弹窗 operation/relation 数，再点击 Execute One Part。
6. Inspect：检查真实 operation result、transaction flags、timing 与 committed CADState。成功初次构建 revision=1，默认保持一个 Part 打开，状态 TEST PART OPEN。
7. Close Test Part：仅关闭/丢弃此 host 创建的文档，并恢复原活动文档。可先 Export Session Report / Download IR。

只有当前成功 plan + 当前显式成功 preflight + 已连接应用才开放 Execute；确认弹窗必须再次点击。各动作分开，无自动 follow-up 或 retry。

## 完整 G2 walkthrough（手动 smoke）

没有其他 Playground-owned Part 时输入：

```text
Create a 100 x 60 x 8 mm rectangular plate.
Add four diameter 6 mm through holes in a centered 2 x 2 rectangular pattern,
with 60 mm X spacing and 30 mm Y spacing.
```

Generate → inspect → Run Preflight → Connect → Execute… → Execute One Part。

检查组合为 extrusion、一个 through-hole seed、一个 rectangular pattern，不应额外重复创建独立孔。D1/D2 使用 extrusion 的 **ReferenceAxis `direction_x` / `direction_y`**。检查实际 `hosted_on`、`pattern_seed`、`equal_spacing`、`centered_about` 等关系与 2×2 count、60/30 mm spacing。中心孔位来自既有 relation solver，UI 不自行求解。

成功后观察原生四孔，CADState 中的 Feature、Body、PlanarFace、ReferenceAxis、LocalFrame、孔壁、绑定与依赖。Persistent reference 内容在 Advanced details，默认折叠。

## Edit example

保留成功 G2 Part 打开，Auto-close 不勾选。Edit intent：

```text
Change the plate thickness from 8 mm to 10 mm.
```

Generate Edit Plan 只规划。Visual 的 Requested parameter edit 显示实际 target、`extrusion_depth`、current=8、requested=10 和绑定；Raw IR 只有一个 edit_parameter，不生成新关系。

Preflight Edit 使用当前 live projection 和 production pure preflight，需要健康 state 和相同 revision。纯预检不编造尚未生成的 ChangeSet/DirtySet，真实集合在 transaction result 中展示。

Execute Edit… → 检查 revision → Execute One Edit。既有事务执行 Binder/native input resolution、preflight、rollback capture、mutation、rebuild、incremental/full validation、atomic state commit。成功 revision 1→2。

然后可输入 `Change the patterned through-hole diameter from 6 mm to 8 mm.`，重复检查、预检与确认。成功事务验证 dependent pattern 实例同步，revision 2→3。目标来自真实模型 context，不要求固定 semantic IDs。最后 Close Test Part。

## 面板说明

| 面板 | 真实内容 |
|---|---|
| Top status | LLM、应用连接方式、owned Part、session health、revision、busy |
| Visual / Raw IR | operation cards、参数、输入引用；完整严格 JSON 的 Copy / Download |
| Relations | kind / subject / reference；编辑保持原有关系 |
| Capabilities | 实际 RuntimeCapabilityCatalog 的 operations、profiles、edit pairs、relations/contracts/constraints，非完整 IR registry |
| Planner Details | provider、requested/actual model、call attempts、tokens（缺失保持 null/unknown）、provider/planning time、status/failure stage/code |
| Pipeline / Execution | 实际 stage、逐 operation result、transaction flags、ExecutionTelemetry、Changes/Dirty/validation scope/read sets |
| CADState | committed document/features/entities/parameters/bindings/relations/dependencies/revision，类型与 health，无 COM pointer |
| Event Log | 真实 action 完成/授权记录；逐 native operation 在结果中展示，没有伪造实时事件 |

Export Session Report 下载脱敏 JSON：当前 intent/plan、最后一次 action result、state、有界事件日志。需留存一次结果时在下一动作前导出。此工具不是 benchmark；timing 不构成性能结论。

## 失败解释

- Provider failure：HTTP/transport/timeout/refusal，stage 为 provider_structured_output 等，不发布部分 plan，不自动 retry。
- strict_envelope / cad_program_parse：模型不符合 contract，不能执行。
- runtime_capability_validation：操作、轮廓、引用类型或 edit pair 不在当前投影。
- pure_preflight：关系/布局/越界/孔相交拒绝，Mutation started=NO、CAD 未修改。这通常是 Harness 正确行为，不一定是应用损坏。
- Native/validation failure：查看原生 operation 与 transaction stage，API bool、重建、几何、引用读回均可能拒绝。
- 修改后 rollback：直接读 transaction flags。典型失败为 MutationStarted=true、RollbackAttempted=true、RollbackSucceeded=true、StateCommitted=false；不能把恢复显示成 commit。
- Rollback failure / invalid session：停止编辑、导出报告、Close Test Part。
- Atomic commit failure：由原事务回滚，检查 RollbackFailureCode/Message。state JSON commit 不是 Part 文件与 JSON 的跨文件联合事务。

## Safety 与 ownership

每次明确执行最多一次 NewDocument。创建前要求 responsive process、可读 GDI <7000、没有已打开测试 Part、合法 template。GDI ≥7000 或测试 Part 未关闭返回 TEST_RESOURCE_LIMIT，不补建、不重试。

Inspect mode 默认保持一个成功 Part。第二次创建被拒绝：`Close current Playground test Part first.`。Auto-close checkbox 默认 OFF；勾选后仅在成功创建验证后关闭，结果保留 observed state，但不能 live edit。失败 Part 留给检查和显式 Close。

Close 使用 server 保存的 native document identity，浏览器不能指定用户文件名或句柄。不要外部关闭、编辑、切换配置或保存/重命名测试 Part 后沿用旧 state；漂移会使引用或 live revision 校验失败。

正常 Ctrl+C/退出等待事务并在原 STA 清理 owned Part。已存在用户 SOLIDWORKS session 不会被退出；host 自己启动的应用仅在无任何文档时按原 ownership 规则退出。强制杀进程可能留下孤立 Part；新 host 会因测试 Part 打开而拒绝创建，需使用者手动关闭孤立 Part。无跨 host 恢复。

## Troubleshooting

| 问题 | 处理 |
|---|---|
| Invalid API key / HTTP 401 | 核对权限，Clear 后重新 Apply；不在日志/聊天粘贴 key |
| Provider timeout | 核对网络/服务，手动调 bounded timeout；不自动重试 |
| SOLIDWORKS not installed | 安装/修复 x64 COM 注册和许可；仍可做零 Part 规划 |
| Template missing | 启动脚本指定存在的 -PartTemplate，再重启 |
| InteropDir missing | 指向 redist 两 DLL；编译不需要激活应用 |
| Managed Part already open | 先 Close Test Part，不连续点击 Execute |
| Stale revision / plan | 重新 Generate Edit Plan 和 Preflight，不复用旧 token |
| Preflight failure | 按 failure code/message 调整 intent，重新规划，不跳过检查 |
| Resource limit | 停止创建、关闭 owned Part；使用者处理应用资源 |
| Host lost during mutation | 状态未知，先检查 host/模型，不盲目重复执行 |
| HTTP 403 | 使用打印的 127.0.0.1 URL/端口 |

## Architecture 与限制

```text
Browser (local HTML/CSS/JS)
 ↓ loopback HTTP
Local Playground Host (one authoritative session)
 ↓
CadPlanner / capability / strict IR / pure preflight
 ↓ manual confirmation + dedicated STA
Harness transaction → SOLIDWORKS
 ↓ validation / AtomicStateStore
DTO / timeline / committed CADState
```

当前支持一个初始 XY centered rectangle/circle extrusion、through/blind holes、linear/rectangular/circular hole patterns、finite fillet/chamfer。线性/矩形方向用 native datum ReferenceAxis，圆周轴用真实外圆柱面。孔在宿主内部且不接触/相交，blind depth 小于板厚，pattern ≤1024 实例，program ≤12 operations，细节以 Capabilities 为准。

编辑仅 registry 注册且当前 state 健康、唯一绑定的 pairs：矩形 extrusion depth、through-hole diameter、linear/rectangular pattern 活动方向的 count/spacing。没有任意 profile size、circular count/angle、blind depth/diameter、fillet radius、chamfer distance 编辑。圆盘不输出矩形 pattern direction。

V0 不支持多用户、任意用户模型编辑、任意追加、文件重开/跨 host session 恢复、自动修复或 Stepwise UI。真实 LLM 可能被严格拒绝；本任务没有真实 LLM 调用、native smoke、benchmark 或 M11。
