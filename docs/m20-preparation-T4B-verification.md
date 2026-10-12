# M20 Preparation T4B Verification

日期：2026-10-12（Asia/Taipei）。基线 `fcddf2d649e238247d6b0a39f6b746bb62ce74b6`，分支 `v0.3-main-update`。实现保留在工作树，未主动提交或推送。

**T4B: PARTIAL；T4 仍为 PARTIAL。** 有界语义编译、来源/编辑角色、严格批准及生产原生接入已实现并验证；C1 的真实规划仍未产生符合冻结任务的合法区段组合。没有执行 T5 或新的正式 M20，没有修改 PRD、旧任务数字、资格门控、几何容差或 Factory。

## 1. 实际实现

| 文件 | 修改与边界 |
|---|---|
| [SemanticConstructionCompiler.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/SemanticConstructionCompiler.cs) | Version 1 的语义 DTO 和确定性 lowering：一个逻辑 node ID；来源 key；矩形、圆、local-X capsule、正交内外径区段；组合、host、位置、方向、设计依赖仍由模型选择。不根据家族、名字或已知尺寸选择答案。 |
| 同上 | 自动产生 CCW 台阶角、点/driver/station/offset bindings、右手 frame、typed outputs 和必要的 tracked datum。拒绝重叠/空缺/未知或前向引用；不插补失败计划、不猜数值来源、不替模型决定整件历史。 |
| [ConstructionRequirements.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs) | 可选 `SemanticDraftVersion`、`EditabilityRequirements`、`DesignDependencies`。值推导 DAG 与真实 driver 设计联动分开；任务指定编辑的 source/parameter/独立性/test value/anchor/保持来源进入批准契约。 |
| [ConstructionPlanner.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionPlanner.cs) | 新入口一次模型调用后严格读取语义 draft，编译到现有 IR，再执行现有来源/几何/能力预检。预览保存 typed compilation；批准 hash 关联 request、draft/编译结果、source map、编辑目标、capability、context/revision。消费时重编译校验。 |
| [GenericPlanningProvenance.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPlanningProvenance.cs) | 可选、版本化 compiled-plan 记录；冷恢复重放 source map、原始 Given 和明确的后续 override。只移除 JSON 排版空白，保留所有字段；原有 65,536 字符/读取字节和 64 events 上限不扩大。 |
| [SolidWorksConstructionPlanningRuntime.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/SolidWorksConstructionPlanningRuntime.cs) | 披露 canonical planes 和现有合格 scalar pairs。语义 provider 投影移除与 node 引用冲突的 direct-IR output/feature 说明，保留原材料限制；完整真实 capability 仍绑定批准。 |
| [T4BTests.cs](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4BTests.cs)、[T4BCases.cs](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4BCases.cs)、[T4Native.cs](D:/CAD-Harness0.2/tests/CadHarness.ConstructionPlanning.Tests/T4Native.cs)、[脚本](D:/CAD-Harness0.2/scripts/test-m20-preparation-T4B.ps1) | 有限开发验证、逐步骤落盘、真实响应原字节回放和参数从 accepted binding 解析。复用生产 GenericPartSession、Handler、Transaction、Store 和 T2/T3 独立 Oracle，没有新 native executor 或通用测试框架。 |

Semantic v1 仅用于新建、拥有的模型。旧 direct IR/continuation 仍使用原严格路径；旧请求不新增必填字段，旧批准 hash 公式和旧包回放保持相容。没有新增 native subtype/accessor，也不宣称所有草图声明约束都成为原生关联约束。XZ 变体只取得纯编译几何证据，不宣称新的原生平面资格。

## 2. 纯验证与回归

- [最终 T4B 32/32](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/pure_final/result.json)：四族完整编译/来源/编辑适用性，冻结解析体积公式，符号改名、额外径向区段/尺寸、XZ capsule 变体。
- 负向覆盖未知 source/symbol、前向 host、重复 node、径向重叠/空缺、伪造/遗漏联动、derived primary、错 driver/role/unit、未消费 Given、错误 world/local 使用、不支持材料、版本/source map/expanded IR 篡改、真实合法输入导致的 provenance 上限拒绝、缺 critical input/伪澄清。未开放 SlotWidth 等未资格参数。
- [旧 T4 40/40](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/t4b_final_regression/result.json)；[M17 36/36](D:/CAD-Harness0.2/artifacts/milestone17/pure/20261012T100521608/results.json)；[T1 23/23](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/from_t4b/result.json)、[T2 36/36](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/from_t4b/result.json)、[T3 31/31](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/from_t4b/result.json)。无旧完整原生矩阵重跑。
- 最终 Release build 0 warnings/errors；`git diff --check` 通过。最终纯测试之后生产 DLL 与两次公开/候选创建时记录的四个生产 DLL SHA 全部一致；后期仅加强测试断言。

所有试验保留：`pure_v2` 的 XZ 用例漏消费显式位置来源，修正测试映射后通过；`pure_v8` 使用超出 Intent 自身长度限制的大小测试输入，`pure_v9` 的历史未实际超限，均为测试设置失败。最终测试使用合法的 4,096 字符、需要 JSON 转义的输入，明确命中 `Planning history exceeds the existing provenance bound.`，不是命中哈希或其他先行 guard。一次重编译与未退出 native controller 的 DLL 锁冲突（MSB3027/MSB3021）；等其受控结束后重建成功，无 Part 操作或原生结果被重跑来掩盖编译失败。

## 3. 真实开发规划

每个日程均先冻结源码、compiler、schema、请求及设置；temperature=0、max output=16,384、timeout=180s。每槽一次，无自动重试。请求 alias `deepseek-chat`，所有返回实际 model 为 **`deepseek-flash`**；只发送已授权的合成任务/规则/schema，不发送工程模型、文件路径、Persistent Reference 或凭据正文。

| 家族 | [live_v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/live_v1/result.json) | [live_v2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/live_v2/result.json) 与独立任务复核 | [live_v3](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/live_v3/result.json) |
|---|---|---|---|
| C1 | `.body` host 不存在；还把槽拆成后续 cut | 结构预检通过，但外环槽变成内部空腔，plane 也不同；**任务 FAIL** | 仅 C1 一次；外槽追加在全部轴段之后，区段重叠/不连续；编译拒绝 |
| C2 | 合法、独立第二孔 driver，PASS | 编译、来源、编辑角色和冻结任务几何复核 PASS | 未重跑 |
| C3 | `.body` host/edge.host 与语义 ID 冲突，拒绝 | 任务复核 PASS，真实公开原生完整链 PASS | 未重跑 |
| C4 | `.body` host 不存在，拒绝 | 任务复核 PASS：三独立槽、世界 Y 方向、自动 top datum、左槽 CutDepth 角色 | 未重跑 |

v1 暴露 provider 边界同时发送“逻辑 ID 不加后缀”和“IR host 必须 `.body`”的真实契约冲突；修正通用能力投影后新冻结 v2，不对旧响应改写。v2 的 `passed` 原字段只代表合法 preview，其原文件不改；[独立复核](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/pure_final/live_v2-task-recheck.json)明确其为 **4/4 结构接受、3/4 任务几何通过**。C1 实测解析体积 `36065.48366321083`，冻结要求 `36769.20041761494` mm³。不能凭 covered sources、合法 driver 或可编辑空腔把它宣布成功。

新增通用 outer-envelope/inner-bore 语义解释后仅复测 C1；模型仍错误组织区段，停止该 live 路径。编译器没有拆分重叠范围或从数字推断正确槽意图。共 **9 次开发调用**，输入 **119,609** / 输出 **3,588** tokens；各版本不能合并挑选为正式成功率，schema 也不保证比旧接口更小。旧 T4 live 0/8 保持原结论。

## 4. 公开原生与冷恢复

| 输入 / 阶段 | 控制器 | 结果与证据 |
|---|---:|---|
| C3 v2 原字节响应，fresh plan/一次批准创建 | 29368 | [R1、高度40](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/public_v1/bracket/create/113-result.json)，[独立 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/public_v1/bracket/create/112-independent-oracle.json)；重复批准拒绝 |
| C3 新控制器恢复并 public Edit | 21236 | [R2、高度44](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/public_v1/bracket/edit/030-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/public_v1/bracket/edit/029-independent-oracle.json)；Z max=52，孔/圆角/安装面通过 |
| C3 第三控制器独立冷读 | 25200 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/public_v1/bracket/read/009-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/public_v1/bracket/read/008-independent-oracle.json)；体积32592.8319133605，差 -3.64e-12 mm³ |
| C1 明确标为 deterministic semantic candidate，批准创建 | 29236 | [R1、半径12](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/candidate_v1/shaft/create/081-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/candidate_v1/shaft/create/080-independent-oracle.json)；**不是 LLM 成功** |
| C1 candidate 新控制器 public Edit | 25272 | [R2、半径13](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/candidate_v1/shaft/edit/022-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/candidate_v1/shaft/edit/021-independent-oracle.json)；槽底10→11，深度仍2，不相关区段保持 |
| C1 candidate 第三控制器独立冷读 | 4424 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/candidate_v1/shaft/read/008-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/candidate_v1/shaft/read/007-independent-oracle.json)；体积39860.52758874731，差1.46e-11 mm³ |

两包均由 `GenericPartSession.ExecuteConstructionPlan` 和公开 `Edit` 使用生产 Handler/事务/发布，不是测试脚本 Setter。保存过程真实 Save3/Rebuild/参数读回；独立 Oracle 直接读取 API 的面、圆柱/圆角、轴向边界、孔位、包络和体积，expected 来自冻结任务公式及 T2/T3 独立几何实现，不从新 compiler 输出生成 expected。

冷恢复验证 Native/State/Program/Manifest/Pointer、Format 2/3、compiled draft/source map、原始 Given、R1→R2 的两条历史 events 及单独 scalar override。两个成功包没有 recovery marker。最终 native SHA：C1 candidate `0c868bc882eba7fcbba264e0f6aef8a8b0a79c913762c77403a576284f0ad168`；C3 `c1ceb484d6995e6918596a9a995ec4a6303e1fb1756ef4c99100bdcb45e2f74e`。

C2/C4 本轮只取得新语义规划与编译证据，没有冒充新完整原生链。C2 输出仍是原 T2 已验证的独立径向孔 driver/正壁厚域，映射 [T2 sleeve 公共编辑](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/edit/024-result.json)及[冷读](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/read/008-result.json)。C4 输出仍是 M16 已验证的 local-X capsule、tracked datum/旋转 frame 和 CutDepth 域，映射 [M16 报告](D:/CAD-Harness0.2/docs/milestone-16-verification.md)及原 T4 四族 provenance 纯回放；不能据此宣称新 C4 已实际原生建模、编辑成功。

## 5. 资源与历史保护

[连续账本](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/native-budget.json)从 **3 creates / 11 opens / 14 closes** 继续至 **5 creates / 21 opens / 26 closes**：本轮新增 **2 Parts、10 opens、12 closes**，六个控制器槽，所有实际生命周期均逐步骤落盘；0 owned。创建槽各2 opens，编辑槽各2 opens，冷读槽各1 open，没有预算重置。

[最终只读审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/final_audit/native-audit.json)：SOLIDWORKS PID 16516 响应正常、GDI 4687 < 7000、实际文档为空，原活动文档恢复；无未解决权威恢复。四个 Factory 原件与冻结 Registry SHA 一致，人工原件 SHA 仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`；未打开或修改工程师原件、未终止应用。

[历史完整性记录](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/final_audit/historical-integrity.json)确认旧 live_v1/v2 和原 T4 报告29份文件 SHA 不变；停止的 M20 的1477份旧证据也在最终审计全部通过。旧 T4 首次无权威发布失败的隔离 marker/disposition 有意保留，禁止复用；不声称磁盘 marker 数量为零。T4B 所有新失败、raw response、freeze 和即时步骤未覆盖。

## 6. 停止点

实现和针对性验证已交付，但未满足修复方案“四族真实完整合法规划，且关键径向真实计划接入”的全部条件。**确切剩余：C1 正确的外槽区段组合及其真实规划→批准创建→编辑→独立冷恢复证据。** 不能用 candidate 的原生成功填补此项；不能把 v2 的结构接受或合法内部空腔当作任务成功。

生产仍保留 preview/批准的人类审查边界；数值来源全覆盖并不能自动证明任意自然语言语义正确。本轮不增加无限修复循环、零件模板或自动执行。T4B/T4 保持 PARTIAL，停在此处，不进入 T5，不启动正式 M20，也不宣布 READY_FOR_M20。
