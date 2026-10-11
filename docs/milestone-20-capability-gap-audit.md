# M20 Capability Gap Audit

日期：2026-10-11（Asia/Taipei）。**结论：`BLOCKED_CAPABILITY`。建议先独立补充实现，再开展新的冻结 M20 验收；不恢复当前日程来反复尝试。**

## 审查边界与基线

- 实际 HEAD：`b25467d`，分支 `v0.3-main-update`。M20 runner/script 尚未提交；本报告依据当前工作树，不将它们冒充基线提交内容。
- 正式依据：[PRD](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:326) §14、§16、§21、M20，并参考 §11 和 M11–M17 的有限资格边界。原始数字任务来自 [M11 fixture](D:/CAD-Harness0.2/tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json)，不简化任务或更改门槛。
- 已停止的 [M20 freeze](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_v3/freeze.json) 和 [停止审计](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_v3/stop-audit-20261011T145238147.json) 是当前原生结果依据。`acceptance_v1/v2/v3` 全部保留；v2 同名文档生命周期失败及其恢复审计不被 v3 覆盖。
- 本次只读重算 174 个冻结文件和 1,477 个已保留 JSON/log 的 SHA256，变化均为 **0**；冻结文件包含 100 个生产源码条目及原件/Factory Registry 等受保护输入。
- **未连接或启动 SOLIDWORKS、未创建/打开 Part、未运行测试、未发出 LLM 请求、未搜索模型。** 唯一新增文件是本报告；生产代码、PRD、验收程序和历史证据不修改。

分类含义：`QUALIFIED` = 对该行限定能力有生产实现和真实原生证据；`PARTIAL` = 有实现/局部成功但覆盖不完整；`BLOCKED_CAPABILITY` = 当前生产路径明确不能满足；`UNVERIFIED` = 不能由现有证据证明。基础操作的 `QUALIFIED` 不等于完整家族通过。

## 代码与证据索引

以下缩写用于矩阵；链接均指实际文件，不是待执行方案。

| 索引 | 代码或验收依据 |
|---|---|
| K1 | [ConstructionKind / ParameterKey](D:/CAD-Harness0.2/src/CadHarness.Ir/V03Contracts.cs:7)；[六项构建能力](D:/CAD-Harness0.2/src/CadHarness.Planning/V03ContractCapabilities.cs:12) |
| K2 | [实体材料预检](D:/CAD-Harness0.2/src/CadHarness.Ir/FeatureConstructionPreflight.cs:17)：单旋转实体或同向平行扫掠、有限包含/相离区域；不支持混合旋转/棱柱或混合扫掠轴 |
| K3 | [原生参数捕获](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs:99)、[Setter/事务](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs:354)、[三项 Generic 编辑资格](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs:469) |
| K4 | [规划轮廓与尺寸绑定](D:/CAD-Harness0.2/src/CadHarness.Planning/ConstructionRequirements.cs:60)、[实际能力投影](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/SolidWorksConstructionPlanningRuntime.cs:24)：单圆、局部矩形、局部 X 槽；旋转历史后不开放继续构建 |
| K5 | [内存 GenericStore](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/GenericFeatureConstruction.cs:46)、[持久化 Store](D:/CAD-Harness0.2/src/CadHarness.State/ManagedRevisionStore.cs:46)、[M12 有限恢复词汇](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ManagedContextRestore.cs:16) |
| K6 | [既有 Fillet Handler](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/EdgeTreatmentHandlers.cs:31)、[legacy Registry](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/FeatureBackendRegistry.cs:11)；其 legacy 原生证据见 [M9F](D:/CAD-Harness0.2/docs/milestone-9f-verification.md) |
| K7 | [草图固定几何/单圆驱动尺寸](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/PlanarSketchBackend.cs:252)、[支持的圆半径编辑](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/PlanarSketchBackend.cs:104) |
| K8 | [外部 Scalar/EditSet 生产门控](D:/CAD-Harness0.2/src/CadHarness.State/V03NativeQualificationCandidates.cs:12)、[公开事务入口](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalPartSession.cs:112) |
| E15 | [M15](D:/CAD-Harness0.2/docs/milestone-15-verification.md)、[native_v8](D:/CAD-Harness0.2/artifacts/milestone15/native_v8/result.json)：初始/非初始平面、偏移、旋转 frame、固定坐标草图、独立圆尺寸/回滚 |
| E16 | [M16](D:/CAD-Harness0.2/docs/milestone-16-verification.md)、[public rotation](D:/CAD-Harness0.2/artifacts/milestone16/native_v11/steps)、[public composition](D:/CAD-Harness0.2/artifacts/milestone16/native_v12/steps)：有限旋转、平行加料/切除、三项驱动编辑、回滚、Save3 |
| E17 | [M17](D:/CAD-Harness0.2/docs/milestone-17-verification.md)、[native_v4_final](D:/CAD-Harness0.2/artifacts/milestone17/native_v4_final/result.json)：有来源记录的完整计划/澄清、公开批准执行、prism + circle cut；不宣称通用冷恢复 |
| E20 | [M20 v3 slots](D:/CAD-Harness0.2/artifacts/milestone20/acceptance_v3/slots)：各槽 `planning-result.json`、`native-result.json` 和即时 `steps/*.json`；未执行槽无结果，不按 FAIL 或 PASS 补造 |

M11 的 [契约完成](D:/CAD-Harness0.2/docs/milestone-11-verification.md)只冻结了词汇/任务，不授予原生能力。M12 的 [冷恢复](D:/CAD-Harness0.2/docs/milestone-12-verification.md)只覆盖有限 legacy history。M13 应读取后续 [completion](D:/CAD-Harness0.2/docs/milestone-13-completion.md)，不能停留在早期 PARTIAL。M14 以 [最终批准范围报告](D:/CAD-Harness0.2/docs/milestone-14-final-acceptance.md)为当前结论，早期 M14A/B/C 的阶段性 PARTIAL 仍保留。

## C1–C4 覆盖矩阵

| 家族 / 必要能力 | 状态 | 代码、实际证据与边界 |
|---|---|---|
| C1：三段直径/长度及 35–39 mm 环槽的初始实体 | QUALIFIED | K1/K2/K7；E20 `C1-deterministic/steps/0060-independent-oracle.json` 初始实体通过。固定多段轮廓一次旋转已可实现环槽，**不必先新增名为 shaft/groove 的操作或专用模板** |
| C1：同轴、轴向台阶与环槽初始关系 | QUALIFIED | K3 原生轴/角/轮廓验证 + 上述独立圆柱半径/轴向边界/体积；仅证明初始几何，不声称原生联动约束 |
| C1：中段半径 12→13、槽底 10→11，其他段不变 | BLOCKED_CAPABILITY | K3 仅为单圆旋转捕获 `RevolveRadius`；K7 固定折线没有该驱动映射，也没有槽底随中段联动的编辑实现。E20 `0062-required-follow-up.json` 在修改前拒绝 `V03_CONTRACT_INVALID` |
| C1：真实 LLM 可表达/绑定完整台阶轮廓 | BLOCKED_CAPABILITY | K4 明确拒绝非矩形多段轮廓，无轴向分段/径向站位的需求绑定；E20 `C1-real-1/2/3` 全部 Unsupported。不能靠再次采样克服这个明确边界 |
| C1：保存后新控制器恢复可编辑状态 | BLOCKED_CAPABILITY | K5 没有 Generic 持久化/重建绑定路径；本次 Save3 还失败，冷重开未执行。具体保存根因另列，不能混作已经测到冷恢复失败 |
| C2：外径/法兰/阶梯内孔的初始实体 | QUALIFIED | K1/K2/K7；E20 `C2-deterministic/steps/0044-independent-oracle.json`，固定轮廓一次旋转通过。这里没有缺少初始建模 Setter |
| C2：初始同轴、肩部位置、壁厚 | QUALIFIED | K3 与上述独立内外圆柱/边界检查；不等于内孔编辑后的关系已被验证 |
| C2：阶梯内孔半径 12→13，法兰不变、壁厚正确 | BLOCKED_CAPABILITY | K3/K7 同样缺少多段径向驱动及修改后几何映射；E20 `0046-required-follow-up.json` 修改前拒绝。单圆旋转的 torus 半径资格不是阶梯内孔资格 |
| C2：真实 LLM 完整几何计划 | BLOCKED_CAPABILITY | K4 轮廓子集不足；E20 `C2-real-1/2/3` 实际因 primitive 字段不匹配被 Rejected，未触达 native。**本次拒绝直接原因是计划契约，不把它伪称为原生 API 失败** |
| C2：保存/可编辑冷恢复 | BLOCKED_CAPABILITY | K5；Save3 失败，独立冷槽未执行 |
| C3：平面/偏移草图、单体加料及加料深度编辑基础 | QUALIFIED | K1/K2/K3/K7、E15/E16；有限平行-plane 合并加料、`AdditiveBossDepth` 40→44 所需 accessor 类别存在，但**该 L 支架整体尚无资格证据** |
| C3：基板 Z 孔 + 立板 X 孔的同一实体组合 | BLOCKED_CAPABILITY | K2 明确拒绝混合扫掠轴。冻结模型确实需要 Z/X 两种孔轴；不是所有垂直平面都不支持，也不是 SOLIDWORKS API 不会切孔，而是材料/实体验证及生产准入不支持该组合 |
| C3：指定顶部边圆角和健康原生边引用 | BLOCKED_CAPABILITY | K6 的 legacy 圆角已存在且有原生证据；但 K1 的 v0.3 构建 IR、K4 capability/schema、K2 材料验证及 Generic 状态没有圆角接入。缺的是可复用的通用路径集成，**不是项目完全没有 Fillet API** |
| C3：完整支架的 boss 编辑、垂直性、孔 host/位置不变 | UNVERIFIED | K3 提供有限 boss Setter/回滚；受以上两个阻塞影响，E20 `C3-deterministic` 没有创建 Part，不能用最小 boss 证据代替完整关系验收 |
| C3：真实规划及可编辑冷恢复 | BLOCKED_CAPABILITY | K1/K2/K4/K5；三次实际规划均 Unsupported（缺圆角）；还存在上述混合轴和恢复缺口 |
| C4：100×60×8 基体、三次独立 Y 向 capsule blind cut | QUALIFIED | K1/K2/K7；E20 `C4-deterministic/steps/0094-independent-oracle.json` 初始通过。局部 X 槽可由明确旋转 frame 成为世界 Y 槽，并非缺少 Y 槽建模 |
| C4：左槽切深 3→4，另外两槽不变 | QUALIFIED | K3 `CutDepth`；E20 `0100-required-follow-up.json` 提交 R1→R2，`0101-independent-oracle.json` 验证最终几何/体积/槽圆柱位置及深度 |
| C4：host、frame、原生引用及独立组合泛化 | PARTIAL | 上述初始/编辑通过；`held-out-composition` 的不同尺寸/布局和中槽 4→5 也通过 native/edit Oracle，但保存/冷重开未通过，不能计完整 held-out PASS |
| C4：真实 LLM 产生可执行完整计划 | PARTIAL | K4 可以表达所需操作/轮廓；E20 `C4-real-1/2/3` 均因“reference must bind an earlier typed output or principal datum”拒绝。属于已实现路径的真实规划可靠性缺口，不是缺少 capsule/cut Handler |
| C4：保存及新控制器可编辑恢复 | BLOCKED_CAPABILITY | K5 通用状态不可恢复；Save3 失败，未启动冷重开 |

### 保存失败与几何 Oracle 的独立结论

C1/C2/C4 及 held-out 均记录 `Save3=false, errors=1, warnings=0, dirty=true`，见上述各槽的 `native-save-api.json`。M16/M17 历史有限模型 Save3 曾成功，故不能仅凭此次日志断言“整个保存 API 未实现”或确定某个生产缺陷。**当前状态为 PARTIAL，根因 UNVERIFIED**：需要后续独立开发诊断区分模型/文档状态、控制器流程与生产实现；本审查不重试。

即使修好此次 Save3，K5 的内存状态、无恢复接入仍是独立且确定的生产缺口。测试输出 `saved-state.json` 或重开后量到正确体积，不能代替生产恢复程序/Sketch owner/driver/reference/Revision 并继续编辑。

E20 Oracle 已独立量到圆柱、轴向圆边界、包络及体积；production 另验证草图、定义和依赖。C4 的独立 Oracle 没有单独记录 capsule 直线切线边长度/连接检查；C3 目前没有可执行的完整边圆角 Oracle 分支。这是**测试覆盖/Oracle 缺口**，不是因此新增产品功能。K7 的固定坐标和单圆尺寸资格也不等于全部声明约束已成为原生关联约束。

## M20 门槛与可达性

| PRD 门槛 | 当前结论 | 可达性 / 证据 |
|---|---|---|
| §16 确定性 C1–C4 **4/4**，含指定编辑和必要重开 | BLOCKED_CAPABILITY | 当前完整成功 **0/4**。C1/C2 初始 geometry 成功、edit 失败；C3 不可执行；C4 native/edit 成功但保存失败及恢复能力缺失。不得把这说成初始建模 0/4 |
| 每族真实计划 **2/3 完整任务**，总计 **8/12** | BLOCKED_CAPABILITY | 已记录 12 次真实请求：C1/C3 各 0 Planned（Unsupported），C2/C4 各 0 Planned（Rejected）；完整成功 **0/12**。别名请求 `deepseek-chat`、实际返回记录见各槽 provider-response。结果不是 provider 不可用，也不能用 fixture replay 代替 |
| §21.1 一个 managed Part 新控制器恢复并继续保持关系编辑 | QUALIFIED（有限历史） | M12 authorized-v3 的真实跨进程编辑/读回；**不覆盖 Generic C1–C4**。M20 特定 G2-regression/G2-cold 槽因停止而尚未执行，属于新日程证据缺失 |
| §21.2 两个外部原生历史只读观测、未知保留 | QUALIFIED | M13 原始两工程师模型 + completion 中压制/配置证据；无需为此重启第三方搜索 |
| §21.3/4 四项外部 Scalar、原件保护、原子批量、故障/恢复 | QUALIFIED（M14 有限范围） | K8、M14 最终报告及 v13/v14/v15/v20/v22/v24/v25/v27/v28/v32/v33/v34 原生证据；并非通用新建模型的持久化资格 |
| M20 外部每广告家族至少两个 distinct native histories | PARTIAL | core/manual 已有两套来源与布局的正向提交；仍需逐参数明确不同 driver/建模历史及 qualified/read-only 映射，**不能仅凭两文件或两组尺寸认定两种历史**。M14 direct/equation、驱动尺寸、GeometryPattern 等既有证据可复核复用，不因此要求所有另一历史都可编辑 |
| §21.5 通用 sketch/revolve/boss/cut 可组合 | PARTIAL | E15/E16/E17 有真实有限组合，K2 仍阻止冻结 C3 所需混合轴；legacy 圆角没有接入 Generic。现有有限成功不是任意组合资格 |
| §21.6 全部 C 家族及真实成功率 | BLOCKED_CAPABILITY | 上述 4/4、2/3、8/12 均未达到；反复验收不会增加被关闭的能力 |
| §21.7 一个 held-out 新建组合及一个外部任务组合 | PARTIAL | 外部任务可引用 M14 v33 批准范围内冻结组合 17/3/23；来源如实标明。新建 held-out C4 native/edit 成功但无持久化通过；不能记整体 held-out 已通过 |
| §16/§21.8 两个 partial-dimension 意图 E2E + 两个 critical refusal | QUALIFIED（既有有限证据） | M17 native_v4_final 的 `new-part`（layout defaults）和 `continuation`（diameter→radius + defaults）均有公开构建/Oracle，前者复用 live response、后者另有 native_v2 live 调用；两者请求/规则见 Cases.Plate/Hole 与 NativeTests。M20 missing-shaft/missing-fit 真实澄清无 mutation。单独 `derived_radius` planning-only 不被冒充 native PASS，也不把 M17 小例子计入 C 家族成功率 |
| §21.9 Binder/预检/事务/恢复/资源保护 | PARTIAL（组合范围） | M14 durable 事务及 M16 live rollback 合格，Generic `RecoveryAllowed=false` 且无 durable Store。M20 v2 同名关闭事故保留，v3 唯一 basename 的已执行槽 cleanup 成功；不能宣称全部新建生命周期场景都已验证 |
| §21.10 冻结、分阶段尝试/计时/资源报告 | PARTIAL | freeze、原始请求/结果和即时步骤存在；日程用户停止未完成。runner summary 还使用无效 `Set-Content -NoClobber`，并需成对归并 external/native-cold，而非用每行的 followUp/reopen 同时为真判分。这是测试汇总问题，不是产品缺功能，也不改变原始结果 |

暂停时 core 外部四标量、双孔 EditSet（一个 checkpoint、R4→R5）、FirstEdit 回滚均通过，且新控制器 **7988→27876** 冷读 R5 通过；见 E20 `core-external`、`core-external-cold`。manual 的同组事务及回滚也通过，但独立 `manual-external-cold` 未执行。equation-readonly、unknown-readonly、G2 两槽同样未执行；已有历史资格仍有效，未运行的 M20 槽不被补成成功。原始各结果的 `completeTask=false` 在分离外部 native/cold 槽上不能直接当作整组编辑失败。

## 最小阻塞清单

以下为独立补充阶段的开发优先级，不是本次授权实施；所有变更后须新版本、新冻结、新正式运行，保留本轮失败。

| 优先级 | 最小必要缺口 | 性质 / 影响 |
|---|---|---|
| 1 | Generic 原生模型的 durable publish、精确绑定恢复和可编辑冷重开，接入既有协议而非再造事务引擎 | **必须开发**；横跨 C1–C4。保存文件与内存 commit 不够；需程序/需求/草图 driver/host/axis/reference/Revision 一致、故障恢复和新控制器编辑 |
| 2 | 多段旋转轮廓的有限驱动参数与关系保持编辑 | **必须开发**；C1 中段与槽底联动、C2 阶梯内孔。复用已有轮廓/旋转建模；不需要家族模板或任意拓扑编辑，也不要求全面 symbolic solver |
| 3 | 单体混合扫掠轴的有界材料/几何与依赖验证，及相应准入 | **必须开发**；冻结 C3 的横向贯穿孔。不能仅删除 K2 拒绝或以 Rebuild 成功代替材料证明 |
| 4 | 既有圆角能力接入 Generic typed plan、精确边引用、状态/几何/回滚与恢复 | **必须开发集成**；C3。可复用 K6，不必从零写 Fillet API；不得硬编码支架边或 case 名称 |
| 5 | Planner 扩展到上述合格的多段轮廓/分段需求及圆角组合，保持完整尺寸溯源 | **必须开发**；C1/C2/C3。必须在原生资格之后广告；单加 schema 枚举不能开放能力 |
| 6 | 诊断已测 Save3 失败，以及 C4 真实规划引用/字段拒绝 | **根因尚未确认**；已有 Handler 路径，可能需要产品修复、planner 改进或测试程序修复，不能预先归为新 primitive。仍是恢复验收前的实际 blocker；仅补漂亮报告无效 |

无需为这份冻结任务另行资格化 `RevolveAxialSpan`、`SlotWidth/Length/CenterX/CenterY` 全部参数；C1/C2 已冻结径向 edit、C4 已冻结 cut-depth edit，满足 §14 明文可选方式即可。枚举里有参数不代表它已广告或已实现。C5/C6、M18/M19 和任意外部几何兼容性扩展也不是解决这些 P0 缺口的替代任务。

## 实现、证据与 PRD 设计缺口的区分

- **实现缺失**：清单 1–5 已由生产状态/能力/预检边界直接确认。不能通过重跑、换模型、放宽 gate 或几何容差解决。
- **纯证据/测试缺口**：尚未执行的 manual cold/G2/read-only 槽、外部各参数两历史的明确映射、C4 独立直线边检查、完整 C3 Oracle 和报告正确归并。先复用相容的旧证据，只补不可替代项，不重做 M14–M17 完整矩阵。
- **PRD 实施分配缺口**：M12 的有限 legacy 恢复没有被后续实现扩展为 Generic 恢复，而 M20 明确要求完整可编辑/持久化任务；M16 只列三种新实体操作，C3 却还要求 legacy fillet 在新路径中组合，未落实接入阶段；M11 冻结多段旋转任务与 M17 窄轮廓子集不匹配。**这是能力交付/阶段衔接欠账，不是取消验收要求的理由。**
- **PRD/冻结任务表达待对账**：§14 C4 简表写 positional edits，但明文后续编辑允许 cut depth，冻结任务也选择 cut depth，不能擅自升级为必须新增位置 Setter。C3 冻结 `bracket_cosmetic_corner` rule 未落入当前四项 rule catalog；后续需明确有界圆角半径的披露/绑定接入。M14 已授权来源/歧义修订未同步重写 M11 fixture 的历史来源说明，后续矩阵应按当前批准条款解释并标注历史差异，不覆盖旧 freeze。

## 推荐决策

**先开展独立、有限的补充实现/资格阶段，不继续正式 M20。** 推荐以 Generic durable 恢复为横向基础；再完成可复用的旋转轮廓参数、C3 混合轴与圆角接入、匹配的 planner 投影；每项先针对性纯/原生资格及冷恢复，再冻结稳定版本做完整 M20。不能在当前 measured run 中修 Handler/提示词后继续累计为同一个成功率。

保留 `acceptance_v3` 为 **STOPPED_BY_USER / 未达到门槛**，不把未执行项记为产品 FAIL，也不把已失败项改为 PASS。没有依据预测补充后的真实 LLM 一定达到 2/3；它仍需新的独立测量，不能用确定性成功作保证。

[连续 M20 账本](D:/CAD-Harness0.2/artifacts/milestone20/native-budget.json)仍为 **5 creation attempts / 5 Parts / 15 open attempts / 20 closes / 0 owned titles**；本次增加 **0**。停止审计确认无未处理 native recovery marker，本审查不对当前用户应用界面作实时状态声明。M11–M17 的旧资格、所有历史失败、原件与冻结输入保持不变。

最终判定：**`BLOCKED_CAPABILITY`，不是仅缺夹具或仅缺测试报告。**
