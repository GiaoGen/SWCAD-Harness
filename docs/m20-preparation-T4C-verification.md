# T4C Verification and T4 Completion

日期：2026-10-12（Asia/Taipei）。分支 `v0.3-main-update`。

**T4C COMPLETE；T4 COMPLETE（技术补齐阶段）。** 新冻结四族真实规划满足任务结构、来源、几何及编辑角色；C1 真实响应通过公开创建、指定编辑及三控制器持久化恢复。C3 复用有效实机链并补新代码下冷读；C2/C4 取得实际新响应与既有生产域的直接相容映射。没有执行 T5 或正式 M20，不宣称正式 4/4 完整任务或 8/12 成功率达标。

开始读取 HEAD 为 `fcddf2d649e238247d6b0a39f6b746bb62ce74b6`，接续未提交 T4B。运行期间 HEAD 前进到保留 T4B 的 `c7746663070e3ab433384889b3467d8ef6735323`，未回退或覆盖该提交；本轮未主动 commit/push。冻结记录中的 `baseline=fcddf2d` 是沿用的旧标签，不是实际 HEAD 证明。[版本对账](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/final_audit/historical-integrity.json)明确这一差别；实际运行源码及 DLL SHA 可复核。

## 1. 本轮实现

| 生产文件 | T4C 修改 |
|---|---|
| [SemanticConstructionV2.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/SemanticConstructionV2.cs) | 独立 version 2 draft/compiler：`BaseOuter/BaseInner/ExternalRecesses/Dependencies`。模型声明 modifier 的 ID、host radius、start/end、floor/depth 来源；通用算法排序并拆分宿主，生成现有 half-section，不新增槽 Handler。 |
| 同上 | `external_recess_split_v1` source map 保存 owner、展开区段的端点/半径来源、base index 和 recess ID；原始 draft 保存全部基础区段与 modifier。批准和持久化回放重编译验证，不把展开数据伪装为新 Given。 |
| [RadialTaskAdmission.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/RadialTaskAdmission.cs) | Version 1 的可选 typed geometry obligations：基础外径、实心/孔边界、外槽及披露的轴向/原点。计划准入检查精确来源/结构，再独立扫描 emitted polygon 在每个轴向区间的实际内外边界，而非只看标签、体积或 compiler map。 |
| [ConstructionRequirements.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs) | 可选 `GeometryRequirements`；显式 semantic v1/v2 分派。结构来自正常结构化任务事实，不传最终五段、oracle 体积或标准 feature tree。正 X 默认布局单独标为 Defaulted / `principal_axis_x_layout` / v1，不冒充用户 Given。 |
| [SemanticConstructionCompiler.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/SemanticConstructionCompiler.cs) | 保留 v1 算法和 wire；v2 复用既有 primitive/CCW/binding 展开。版本化可选 `DraftV2/SourceMap`。编辑角色可明确已给的 sweep axis/direction；验证 C3 高度沿 +Z、C4 CutDepth 沿 -Z，不靠数值来源猜坐标角色。 |
| [ConstructionPlanner.cs](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionPlanner.cs) | 严格新旧 DTO/schema 分派，一次完整模型调用后编译/任务准入/预检再返回 preview；批准消费重新验证版本、modifier/map、结构、IR、来源、角色及 context/revision/capability/hash。 |
| [GenericPlanningProvenance.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericPlanningProvenance.cs) | v1 按原规则回放；v2 原始 Given、modifier/source map 和明确 scalar override 分别记录。每次径向 override 回放后重新核验结构/槽深关系；原 65,536 边界与既有发布协议不改。 |
| [SolidWorksConstructionPlanningRuntime.cs](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/SolidWorksConstructionPlanningRuntime.cs) | 披露 semantic 1/2；provider 投影只给语义操作及逻辑 node 引用，compiler 产生 sketch/datum/typed suffix。按请求已声明的旋转轴和 editable sweep layout 投影 Schema，不放宽 native qualification。 |

仍复用生产 `GenericPartSession.ExecuteConstructionPlan`、`Edit`、既有 Handler/Transaction/Store/恢复及独立 T2/T3 Oracle。没有修改 Native qualification、Setter、材料证明、事务发布协议、PRD、任务数字、Factory 或正式 M20 runner；没有 C1–C6/fixture ID/已知尺寸分支或另一套 native executor。

资格范围明示：recess 严格位于一个基础外径区段内部，宿主按明确来源和包含区间唯一选择；多个 modifier 严格分离，不支持跨台阶、重叠/接触或 last-write-wins。基础相邻边界须使用相同 source identity；不同同值 key 不自动合并/替换，无法保留唯一证明的组合拒绝。内外域端点可以分别保留各自显式推导来源，不靠同值猜源。正壁厚、现有点/driver/operation/binding 数量和原材料域继续预检。

这不是任意自由文本 NLP 或通用 CAD 设计系统，也不承诺未原生验证的新平面/参数/材料组合。M15 固定坐标/驱动尺寸的 native 关联能力边界未改变。

## 2. 针对性纯验证与历史相容

[最终纯验证](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/pure_final/result.json)：**42/42 PASS**，Release 0 warnings/errors。

- 独立 expected：三个 stock 区段加一个 recess 得到 0–20/20–35/35–39/39–60/60–80，半径15/12/10/12/9；体积 `11704π`，编辑后 `12688π`。不从 compiler 输出反生成 oracle。
- 泛化：不同尺寸/槽位置（独立体积 `14017.5π`）、多个槽及声明顺序、另一基础 driver 作为宿主（`11656π`）、不同名字/source keys、无槽单区段和正壁厚内孔；没有 fixture 名称映射。
- 负向：内空腔冒充外槽、漏/错 modifier、错误 host/span/floor/depth、基础 gap/overlap、跨台阶、重叠/重复/超量、零/负壁厚、未知 source/单位、默认洗白、同值独立源、derived primary、破坏 preserved source、世界轴/方向、版本/map/modifier/IR/批准漂移和真实 payload 超限。
- 新版错误空腔通过实际 Planner 在 preview 前命中 `STRUCTURE_INNER`，[拒绝证据](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/pure_final/wrong-cavity-production-refusal.json)：0 Setter/Checkpoint/native open；不是只命中版本错误。
- [旧三次 C1 回归](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/pure_final/old-c1-unchanged.json)：v1 host 错误、v2 合法但错误内腔、v3 appended overlap 均保持原意和 FAIL，不转换为 modifier 或升格 PASS。
- [T4B v1 32/32](D:/CAD-Harness0.2/artifacts/m20-preparation/T4B/from_t4c_current/result.json)、[旧 T4 40/40](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/from_t4c_final/result.json)、[M17 36/36](D:/CAD-Harness0.2/artifacts/milestone17/pure/20261012T130148795/results.json)通过。
- [M12 48/48](D:/CAD-Harness0.2/artifacts/milestone12/pure/20261012T131201938/results.json)、[M14 86/86](D:/CAD-Harness0.2/artifacts/milestone14/pure/20261012T130629813/result.json)；[T1 23/23](D:/CAD-Harness0.2/artifacts/m20-preparation/T1/from_t4c/result.json)、[T2 36/36](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/from_t4c/result.json)、[T3 31/31](D:/CAD-Harness0.2/artifacts/m20-preparation/T3/from_t4c/result.json)。未重跑旧完整原生矩阵或复制大量旧生成目录。

保留早期失败：[pure_v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/pure_v1/result.json)的 C3/C4 是 instructions 对不存在的 radial obligations 序列化 null 时命中 strict reader，修复为明确 `none`；没有放宽几何。首次编译的层间命令类型依赖改为 Planning override DTO，另有测试 nullable tuple/fixture 属性拼写错误修正，均发生在 live/native 前。`pure_v2/v3/v4/v5` 全保留。相邻源身份/几何与 native 容差不修改。

## 3. 两轮有限真实规划

日程每族一次、每轮固定4次、无自动重试。temperature=0、max output=16,384、timeout=180s；request/raw response/schema/source SHA/完整 expanded IR/source map/批准 SHA/token/耗时均保留。alias `deepseek-chat`，实际全部返回 **`deepseek-flash`**。只发送已授权的合成任务、能力和 Schema，不发送工程师 Part、原生引用、文件路径或凭据正文。

| 家族 | [live_v1](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/live_v1/result.json) | [live_v2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/live_v2/result.json) |
|---|---|---|
| C1 | 外槽声明/拆分已正确，但选择 YZ，local-X 旋转轴为 world Y；`STRUCTURE_LAYOUT` 拒绝 | **PASS**：三个 stock + 显式 recess，world X，外槽/实心/全部区段与独立 radius role 正确 |
| C2 | PASS | **PASS**：明确 bore 段端点、独立第二孔 driver、正壁厚及来源/结构正确 |
| C3 | 第一节点写成 self-hosted AdditiveBoss，后续高度沿 Y；host 预检拒绝 | **PASS**：先 root extrusion、再 +Z additive height、Z/X 孔和有界末端圆角；编辑角色正确 |
| C4 | PASS | **PASS**：三独立 Y-axis capsule cuts、top datum、-Z 左槽 CutDepth，其他来源正确 |

v1 **2/4**；分析实际错误后补“第一 solid 必须 create_extrude/revolve，Additive 只能已有宿主”语义说明，并把已声明旋转轴/编辑 sweep layout 投影为有限 Schema，重新纯验证后冻结 v2。没有修补旧响应或临时换简单任务；v2 **4/4 plannerAccepted + taskGeometryPassed + editabilityPassed**。未继续第三轮抽样。

总计 **8 次开发调用**：输入 **102,125**、输出 **3,375** tokens。版本不同的两轮不合并挑选为正式成功率。不同尺寸 C1 泛化在纯验证完成，没有额外未冻结 live call。

## 4. C1 真实原生完整链

实际 [raw response](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/live_v2/shaft/call-01-response.json) SHA `b9ea4db8603f0aebaab68d4316948862a92afc4b7f52c7d0641f68d9614a275f`。新 native context 原字节重验并新批准，不调用模型、不换 candidate。

| 阶段 | 控制器 | 直接结果 |
|---|---:|---|
| 公开批准创建、原生保存/发布 | 23096 | [R1/半径12](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/create/082-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/create/081-independent-oracle.json)；同一批准复用拒绝 |
| 新控制器冷恢复、指定公开 Scalar Edit | 4564 | [R2/半径13](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/edit/022-result.json)，[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/edit/021-independent-oracle.json)；参数从 accepted edit target/精确绑定读取 |
| 第三控制器独立冷读 | 30544 | [R2](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/read/008-result.json)，[完整 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/read/007-independent-oracle.json) |

官方 API 实测一实体、五个完整圆柱面对应全部区段及各圆环轴向边界、共 world-X 轴、世界包络和体积。外槽实际35–39、槽底10→11、中段12→13、槽深仍2，其余轴向/半径段保持；所有预期圆柱/边界必须唯一匹配且无额外圆柱，支持排除错误内部空腔，不用 volume-only 验证。只读 T2 独立公式和 surface/extreme API，不从新 compiler/map 或 Factory 生成 expected。

初始体积 `11704π`；最终实测 `39860.52758874731`、独立期望 `39860.527588747296` mm³，差 `1.46e-11`。Native/State/Program/Manifest/Pointer、原生定义/驱动及健康引用、Format 2 和 compilation v2 一致；两条 history events 是原始 accepted plan + 独立 override，Given 未改。R1→R2 一次提交，无未完成 marker。

最终 native SHA `dbe3c9b561c290da80ad99c80bd5765f27e175f9de6521d4034906f9af199884`。所有关键步骤即时落盘；Source/Binaries 与成功冻结对应，不重新解释历史中断状态为 PASS。

## 5. C2/C3/C4 与旧包相容性

[实际四族映射](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/actual_mapping/result.json)从 v2 raw 原字节编译，分别证明 emitted IR/bindings 与既有严格 primitive lowering 完全相同；检查实际操作均 qualified、精确参数身份、公开批准、完整 typed provenance 往返及指定 override。这里的 strict lowering 比较仅证明表示相容，不把旧 raw 错误修成成功，不替代 native Oracle。

| 项目 | 直接依据与范围 |
|---|---|
| C2 新实际产物 | [映射](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/actual_mapping/sleeve-actual-plan-mapping.json)：同既有 radial profile/独立内孔 radius accessor/正壁厚域；after-edit provenance 19,232字符。既有 [T2 公开12→13](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/edit/024-result.json)及[独立冷读](D:/CAD-Harness0.2/artifacts/m20-preparation/T2/public_v1/sleeve/read/008-result.json)可复用。 |
| C3 新实际产物及关键链 | [映射](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/actual_mapping/bracket-actual-plan-mapping.json)：既有跨方向/凸圆角/高度 accessor，after-edit provenance 30,320字符；复用 T4B 原实际模型公开创建/40→44/第三控制器冷读，不把它说成新 v2 native construction。 |
| C3 v1 新代码冷恢复 | 控制器17508，[R2/高度44](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/compat_v1/bracket/read/009-result.json)、[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/compat_v1/bracket/read/008-independent-oracle.json)。原 SHA `c1ceb484d6995e6918596a9a995ec4a6303e1fb1756ef4c99100bdcb45e2f74e` 不变。 |
| C4 新实际产物 | [映射](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/actual_mapping/slots-actual-plan-mapping.json)：同既有 tracked datum、旋转 frame/local-X capsule、独立 CutDepth；after-edit provenance 24,320字符。原 [M16 原生资格](D:/CAD-Harness0.2/docs/milestone-16-verification.md)与 T4/T4B 来源/override 回放相容，无新 native subtype/accessor。 |
| 另一关键 v1 radial 包 | 控制器30248，[R2/半径13](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/compat_v1/shaft/read/008-result.json)、[Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/compat_v1/shaft/read/007-independent-oracle.json)。原 SHA `0c868bc882eba7fcbba264e0f6aef8a8b0a79c913762c77403a576284f0ad168` 不变；它继续是 T4B candidate，只作为 v1 包相容证据。 |

不支持径向/prismatic 混合历史等原限制保持。底层 native Handler/Setter/材料证明没有改动；新语义责任在已有 IR 前及 provenance。实际新 C2/C4 的输入/IR/driver/format/provenance 映射、纯 override 验证与原实机资格已经形成直接对应，未发现必须重做完整原生链的相容缺口；因此不重复四族旧原生矩阵，也不把映射列作新 native complete-task 成功。

## 6. 最终完成矩阵

| T4C/T4 必需条件 | 状态 | 证据 |
|---|---|---|
| 通用 modifier 拆分、结构/独立 emitted 边界检查 | PASS | 42项纯验证、生产 Planner/批准/provenance 复验、独立错误空腔拒绝 |
| 四族新真实完整计划满足任务/来源/编辑角色 | PASS | live_v2 四族 raw、expanded IR、compiler map、逐项 flags；保留 live_v1失败 |
| C1 真响应的公开创建/指定编辑/跨控制器恢复 | PASS | public_v1 三控制器、真实 Setter/Rebuild/Save/Revision/完整 Oracle |
| C3 跨方向/圆角关键实机链仍有效 | PASS（复用+补证） | T4B 实际模型链；新代码下 compat_v1 冷恢复和原字节保护 |
| C2/C4 生产接入、编辑/来源的对应关系 | PASS（映射） | 实际新响应 strict IR 等价、qualified type/精确 driver、bounded history/override、旧 native 资格 |
| 旧 direct/v1 契约、批准、包与来源安全 | PASS | T4/T4B/M12/M14/M17回归、两份 v1实机恢复、hash/version/payload拒绝 |
| 无零件模板/原件改造/安全放宽 | PASS | 生产差异和源码检查；native qualifiers、Handler、容差/协议未改 |
| 生命周期、权威恢复与历史保护 | PASS | 最终审计、连续账本、原件及历史 SHA |

这些是技术补齐计划规定的 T4 完成条件；不是正式 M20 的逐任务实机成功率统计。C2/C4 本轮没有新 native 运行，candidate 没有重新标为 live；后续 T5/正式 M20 的完整四族集成、held-out 和每族2/3/总8/12仍未执行。

## 7. 连续账本和证据保护

[T4 原账本](D:/CAD-Harness0.2/artifacts/m20-preparation/T4/native-budget.json)从 **5 creates / 21 opens / 26 closes** 连续至 **6 creates / 28 opens / 34 closes**。本轮 **1新 Part、7 opens、8 closes**，0 owned；包括 C1 create2/edit2/read1 和两份 v1包各1 open，无预算重置或未计入尝试。

[最终只读审计](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/final_audit/native-audit.json)：实际文档为空，原活动文档恢复，SOLIDWORKS PID16516 responding，GDI4915 <7000，无未解决权威恢复；未终止用户进程。四个 Factory 原件与 Registry SHA 不变；人工原件 SHA 仍为 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`。

[历史完整性](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/final_audit/historical-integrity.json)：原 T4/T4B raw、freeze、结果、报告68文件 SHA 全部不变；停止的 M20 1477份旧证据 SHA 全部通过。旧无权威首次发布失败的 quarantine marker/disposition 保留，不复用，不声称磁盘 marker 总数为零。

成功 live_v2 后生产源码零变化；创建/编辑/最终审计四个生产 DLL SHA 完全一致。只有测试的 Program/T4CTests 后加只读映射 mode/helper，不改变既有 native/live 路径。[final audit](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/final_audit/native-audit.json)及各槽 `002-source.json` 保存可复核版本依据。`git diff --check` 通过。

**停止位置：T4C/T4 COMPLETE，等待用户下一阶段指令。未进入 T5，未启动或恢复正式 M20。** 旧 T4/T4B PARTIAL 报告保留原 scope；本报告补齐其未满足条件，不重写历史结论。
