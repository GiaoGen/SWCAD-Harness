# T4C 独立验收复核

日期：2026-10-12（Asia/Taipei）。  
审查 HEAD：`c7746663070e3ab433384889b3467d8ef6735323`，包含当前未提交 T4C 工作树。  
结论：**T4C 达到完成方案规定的验收条件；可以标 T4C COMPLETE、T4 COMPLETE（技术补齐阶段），进入 T5。**

## 1. 审查范围与方法

对照 [T4C 完成方案](D:/CAD-Harness0.2/docs/m20-preparation-T4C-completion-plan.md) §9 与 [执行报告](D:/CAD-Harness0.2/docs/m20-preparation-T4C-verification.md)，读取实际生产实现、测试代码、原始模型响应、公开原生执行记录、Oracle、相容映射及清理记录。

本轮独立重新计算源码、生产 DLL、C1 raw response、最终原生模型、原件与历史证据 SHA256，并使用已保存构建重新执行 T4C 的 42 项纯测试。没有连接/启动 SOLIDWORKS、创建/打开 Part、调用 provider 或执行正式 M20。没有改生产代码及旧报告/结果。

纯验证命令：

```powershell
./scripts/test-m20-preparation-T4C.ps1 -Mode Pure -RunId review_pure_20261012 -NoBuild
```

该命令使用保留构建，没有重新编译。本轮核验四个生产 DLL 的 hash 与成功创建、编辑、冷读及最终审计记录一致。新增纯验证证据：[42/42](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/review_pure_20261012/result.json)，退出码 0。

## 2. 完成条件核对

| 条件 | 判定 | 复核依据 |
|---|---|---|
| 通用 modifier 展开与结构准入 | 达成 | `SemanticConstructionV2.Expand` 根据显式来源/宿主拆分；`RadialTaskAdmission` 检查 draft 结构并扫描实际 emitted polygon 的区间内外边界；负向不依赖 compiler map |
| 四族新真实完整规划 | 达成 | `live_v2/result.json` 四族均 plannerAccepted/taskGeometryPassed/editabilityPassed；原始响应、请求及冻结存在，首轮失败保留 |
| C1 真实响应完整原生链 | 达成 | 三独立控制器公开创建 R1、编辑 R2、冷读 R2；raw SHA 完全对应 live_v2，未换 candidate；参数从 accepted source binding/role 解析 |
| C3 跨方向/圆角关键实机链 | 达成，复用并补证 | T4B 实际模型公开链；T4C 新代码恢复旧 v1 bracket R2、独立 Oracle 和原生文件 hash 一致 |
| C2/C4 生产接入映射 | 达成，限定映射范围 | 实际新响应经现有 strict primitive lowering 得到相同 IR/bindings；核验合格操作、具体 driver、批准及来源/override 回放；底层 Handler/accessor/material 域未改 |
| 旧契约/包、批准与来源安全 | 达成 | 显式 v1/v2 分派、recompile/hash/source map 校验；两份 v1 实机冷恢复及相关纯回归；本轮纯验证重复通过 |
| 无零件模板或资格放宽 | 达成 | production 修改位于 Planning、capability 与 provenance；无 C1–C6/固定尺寸分派，recess 仍限制单宿主内部及严格分离，原生资格和执行引擎保持 |
| 生命周期与历史保护 | 达成 | 三个 C1 cleanup 原活动文档恢复、owned 为空；最终审计无权威待恢复项；本轮重新核验历史与原件无变化 |

映射证据按完成方案允许的方式计入 T4；不把 C2/C4 映射称为本轮新完整原生执行，不把旧 candidate 称为真实规划成功。

## 3. C1 核心证据

- 真实响应：[live_v2/shaft](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/live_v2/shaft/call-01-response.json)。请求 alias `deepseek-chat`，实际返回 `deepseek-flash`。结构为三个基础外径段、zero 内部基线和显式 external recess。
- 原始响应 SHA：`b9ea4db8603f0aebaab68d4316948862a92afc4b7f52c7d0641f68d9614a275f`，与 native create 的 preserved-live-replay 记录一致。
- 控制器 23096：公开批准创建、发布 R1，主半径 12。
- 控制器 4564：新控制器恢复并公开编辑，发布 R2，主半径 13。
- 控制器 30544：再次独立冷读 R2，半径 13，两个历史 events。
- [最终独立 Oracle](D:/CAD-Harness0.2/artifacts/m20-preparation/T4C/public_v1/shaft/read/007-independent-oracle.json)实际圆柱/环边界匹配 `(R15,0–20)、(R13,20–35)、(R11,35–39)、(R13,39–60)、(R9,60–80)`，world-X 轴，包络 `[0,-15,-15,80,15,15]`。
- 槽底 10→11、槽深仍为 2；未编辑的轴段保持。实测最终体积 `39860.52758874731`，期望 `39860.527588747296` mm³，差 `1.455e-11`。
- 当前 working Part SHA：`dbe3c9b561c290da80ad99c80bd5765f27e175f9de6521d4034906f9af199884`，与编辑和冷读记录一致。

官方 API Oracle 检验完整圆柱和轴向圆边界的数量、唯一对应、轴、包络及体积，支持排除先前的错误空腔；生产恢复另验证 driver、reference、原生定义及 revision/package/provenance。没有使用体积单项替代该闭环。

## 4. 独立版本与完整性检查

| 本轮重算项 | 结果 |
|---|---|
| 成功 live_v2 冻结中的 113 个生产源码文件 | 当前 SHA 全部匹配，0 差异 |
| 创建/编辑/冷读/最终审计四组生产 DLL，共 16 条记录 | 当前文件全部匹配；每种 DLL 在四组记录中只有一个 SHA |
| T4/T4B 原始历史记录 68 份 | 0 SHA 差异 |
| 停止的 M20 旧证据 1,477 份 | 0 SHA 差异 |
| 四个 Factory 原件与一个人工原件 | 0 SHA 差异 |
| 当前 C1 raw response 及 working Part | 与成功链记录一致 |

冻结文件中的 `baseline=fcddf2d` 是过期标签；执行报告已披露 HEAD 在运行期间前进到 `c774666`。实际源码/DLL hash 可复核且一致，本次不将标签当作真实 HEAD，也不因此否定已经绑定到文件内容的成功证据。

记录的最终账本为 6 creation attempts / 6 Parts / 28 opens / 34 closes / 0 owned，本轮原生增量 0。记录的最终应用审计 responding、GDI 4915、无打开文档；这是已有运行时记录，本轮没有查询实时应用状态。

## 5. 结论的适用范围与下一步

此次证据满足 T4C 完成方案和 T4 技术补齐条件，没有发现要求继续保持 T4 PARTIAL 的阻塞。

验收仍属于受支持 primitive、结构化需求/来源/编辑角色和有限材料域。一次开发四族通过不代表任意自然语言工程设计能力或总体成功率；C2/C4 本轮没有新的完整原生链。

下一步为 T5：修正正式 runner/Oracle/目标解析和汇总，做完整集成及外部历史证据对账，安排新的 held-out 与正式冻结。**本轮不判定 READY_FOR_M20，不宣称正式 M20 的 4/4、每族 2/3 或 8/12 已通过。**
