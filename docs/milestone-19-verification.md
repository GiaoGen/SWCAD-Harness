# Milestone 19 Verification

日期：2026-10-13。结论：**M19 COMPLETE（PRD 指定的 pure/mock API + UI 验收）**。

## 范围与实现

以原始 v0.3 PRD 的 §12、§13 和 M19 为依据，接续已有 M18 工作树。M19 修改仅位于 Playground、其测试与运行脚本；没有修改 IR、Planner/compiler、State、生产 CAD Handler、资格门控或事务协议。工作树中 `ExternalPartSession.cs`、`ExternalQueryPlanning.cs` 等 M18 改动保留，不计为 M19 新增底座修改。

| 要求 | 本轮实现与验证 |
|---|---|
| 局部关系查询 | `ModelViews` 投影直接父/子、Host、PatternSeed、关联参数、引用健康、依赖完整性和支持状态；精确 ID 查询，没有第二 Binder、闭包推理或按位置猜测关系 |
| Native 与设计关系分开 | External 标记 `observed_native` / 资格化原生几何；Generic 标记 `tracked_native` 或 `declared_design`。无边显示为“未记录边”，不声称证明不存在依赖 |
| 外部只读检查 | `ExternalPartInspection.Inspect` 的独立只读副本；关闭后的观察 DTO 不能授予编辑权限 |
| 外部工作副本及恢复 | 复用 `ExternalPartSession.CreateCopy/Open`；显示 source、working copy、configuration 和实际 Revision，不假定 Revision 1 |
| Scalar / Batch | Scalar 使用现有资格检查与 `ExternalEditPlanning.Prepare`，执行走公开 `ExternalPartSession.Edit`；Batch 复用 M18 `PreviewEditSet`、不可变预览及一次性批准 |
| 精确确认 | 显示旧值、新值、实际原生引用、精确集合、执行顺序和完整验证范围；确认绑定 ID/hash/Revision/顺序。Empty、Ambiguous、Unsupported 均无可执行预览 |
| 新特征及尺寸来源 | 严格/semantic v1/v2 构造计划可视化、局部草图图形与 placement；Given / Defaulted / DerivedByRule 分开。图形是批准程序的预览，不是原生 Oracle 或原生关联约束证明 |
| Generic 创建与恢复 | 复用 `GenericPartSession.Create/Open/ExecuteConstructionPlan`；新建会话用原始 provider 响应在新上下文确定性重验证，比较完整响应、compilation 和能力投影，不拿 expanded IR 当 semantic 响应重放 |
| 状态与故障 | committed、provisional、read-only、owned-uncommitted、quarantined 分开；Generic 显示权威持久包，隔离时不读取中途原生状态；交易失败保留实际 flags，不伪报提交 |
| 安全 | 复用长生命周期 STA、非阻塞 admission、loopback/Origin/CSP、秘密清理、固定请求上界；浏览器不能提交执行 IR；批准在 dispatch 前消费，失败也不能自动重试 |

新增代码入口：`ModelViews.cs`、`ModernNativeSession.cs`、`ModernPlaygroundSession.cs`、`ConstructionReplay.cs`，均在 `src/CadHarness.Playground`。现有 v0.2 路由及 UI 保留为独立 legacy workspace。只读观察不是文档所有权，不能影响另一个工作流的受控关闭。

Generic 的已资格化参数仍显示后端事实；本轮 Scalar/Query 控件用于 External 会话，不新增 Generic Query/EditSet 或任意草图编辑入口。机械设计关系、通用几何识别和原生关联约束不属于本轮扩展。

## 验收证据

执行：`scripts/test-milestone19.ps1 -Mode All`。最终构建 **0 warning / 0 error**。

- [综合验收及源码 SHA256](D:/CAD-Harness0.2/artifacts/milestone19/regression/20261013T155523106/summary.json)：历史 M14–M20 原生账本哈希前后相同。
- [API / 回归日志](D:/CAD-Harness0.2/artifacts/milestone19/regression/20261013T155523106/api.log)：原 Playground **97 项通过**；新增 M19 **54 项通过**。其中有 Scalar/Batch 成功、Mock 原子回滚、实时 Revision 漂移拒绝、精确顺序/hash 拒绝、单次消费、忙会话、秘密输出拒绝及原始 semantic v1/v2 重绑定。
- [M19 逐步 API 证据](D:/CAD-Harness0.2/artifacts/milestone19/qualification/20261013T155528575/summary.json)：每个断言即时落盘，另保留检查、关系、歧义、预览、Mock 提交和回滚记录。
- [浏览器证据](D:/CAD-Harness0.2/artifacts/milestone19/ui/2026-10-13T155532039Z/summary.json)：**22 项通过**，1440×1000 与 390×844；真实浏览器点击检查/取消/确认、只读禁用、歧义禁用、精确双目标确认、单次消费、构造 plan/preflight/execute。0 页面运行错误，无横向溢出，按钮文字可容纳，草图图形非空且尺寸稳定。
- 同目录保留 `desktop-inspection.png`、`desktop-batch.png`、`mobile-batch.png`、`desktop-construction.png`、`mobile-construction.png`，已人工视觉复核。

Mock 只替代 provider/native transport，实际纯契约、规划器、编译器、能力投影、M14 preflight 和 M18 查询仍运行。Mock Setter / Checkpoint / Revision / rollback **不是新增原生资格证据**。

## 失败保留

[首次浏览器失败](D:/CAD-Harness0.2/artifacts/milestone19/ui/2026-10-13T154042561Z/failure.json) 保留：测试输入给 semantic 编译器重复提供多个 `layout_zero`，导致 `V03_CONTRACT_INVALID: Required unique disclosed representation rule missing: layout_zero`。修正 Mock 输入为唯一、可复用的零布局源；未改生产 compiler、Prompt、几何容差或通过标准。旧截图及所有后续运行目录均未覆盖。

## 资源与边界

本轮 **0 新 Part、0 原生打开、0 原生关闭、0 真实 provider 调用**。自动化 host 注入无 COM connector 的 doubles；真实 adapter 仅测试 lazy/offline 能力投影。Mock host 与 headless browser 已释放，无遗留测试原生文档/恢复标记；未接管或关闭工程师文档，未写任何 `.SLDPRT`。

M18 原生账本仍为 **6 opens / 6 closes / 0 Parts / owned=0**。M14–M20 历史预算与失败结果不重置。M20 未重跑，Fixture Factory 未扩展。

可选的 M19 原生 smoke **NOT_RUN**；若开展，必须作为独立、有界、显式授权的运行，不能混入本次 0-native 正式验收。后端 M14–M17/T1–T4 的既有原生资格不由 UI 标签升级。

## 后续

本地 Playground 可用于查看和操作既有资格范围。C5/C6 的独立 P1 建模验收仍单列待办；M19 COMPLETE 不宣称其通过。状态/会话统一、观测指纹精简、Generic 更丰富的 GUI 编辑器等记录为 v0.4 技术债，不在本轮重构底座。
