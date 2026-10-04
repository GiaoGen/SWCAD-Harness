# Milestone 8 — Bounded Judge / Jev 验收

日期：2026-10-05。状态：**MILESTONE 8 COMPLETE**。

依据 `Generalized_CAD_Harness_v0.2_CLEAN_PRD.md` 的 §11、§12、M8：实现 `IBoundedJudge`，测试受控歧义候选选择，Judge/Jev 为可选依赖，默认原生预算 0 Parts。PRD 明确将 Jev adapter 列为 optional；本阶段不添加未规定协议的 Jev 网络适配器。没有修改 M8 PRD 要求。

## 实现与调用边界

`IBoundedJudge.SelectCandidateAsync` 是纯语义选择接口，位于只依赖 IR 的 State 库。`SemanticEntityBinder` 接收可选 Judge 与有限超时；`BindAsync` 提供显式启用入口。原有同步 `Bind` 仍只执行确定性流程，即使实例配置了 Judge 也不会调用它。现有 SOLIDWORKS 同步绑定调用点保持默认无 Judge 的路径。

先验证并复制当前 state/contract 快照，复用原有 Binder 的类型、实体/owner 健康状态、所有权、几何点/方向/半径、依赖路径与 preferred-owner 排名。0 个候选为 `BINDING_UNRESOLVED`；唯一候选或唯一确定性 owner 匹配直接成功；以上路径 JudgeCalls=0。剩余歧义仅在配置了 Judge 且完整合法集合有 2–8 个候选时进入一次判断。超过 8 个时不采样或截断发送，保留歧义并提示缩小约束。原 Binder 的 64-ID 诊断截断也不会将大集合变成可发送的小集合。

`BoundedJudgeRequest` 只由 Binder 构造，候选集合为复制后的只读集合，候选/几何为不可变记录。它含请求 Guid、有限意图、输入名/角色及候选语义 ID、类型、所属特征 ID/operation kind、已有几何证据。它不包含完整 CADState、文档身份/路径、原生持久引用、参数集合、IR registry、COM 对象或执行回调。geometry 是确定性状态提供的证据，Judge 只用它选择实体，不提供尺寸读回、重建检查、实体数量检查或持久引用存在性判定。

`BoundedJudgeDecision` 必须返回原 RequestId、有限 Choice（Select/Abstain）、可空 SelectedSemanticId 和 Rationale。Select 必须精确匹配合法 ID，且理由非空；Abstain 必须 SelectedSemanticId=null。拒绝空响应、未知枚举、旧请求 ID、大小写不同/集合外 ID、矛盾决策形状和超限理由。不会将非法响应改写成第一个候选，也不会重试或自动修复。

| 边界 | 上限/行为 |
|---|---|
| 每次绑定 Judge 调用 | 最多 1 次，无循环 agent |
| 合法候选集合 | 2–8 个，完整发送 |
| 意图 | 非空且 ≤2048 UTF-8 bytes |
| 输入名 | 非空且 ≤128 UTF-8 bytes；role 必须为有限枚举或 null |
| 理由 | ≤1024 UTF-8 bytes；Select 时必须非空 |
| 等待超时 | 默认 10 秒，可配置 >0 且 ≤30 秒 |
| 调用方取消 | 传递 linked cancellation；失败保持歧义 |

`JudgedBindingResult` 携带原 BindingResult、JudgeStatus、JudgeCalls 和经过检查的理由。`NotNeeded` 包括确定性成功和零候选；`NotConfigured`、`CandidateLimitExceeded`、`InvalidRequest`、`Abstained`、`InvalidResponse`、`Failed`、`Cancelled`、`TimedOut`、`StateChanged` 均保持 `BINDING_AMBIGUOUS`。成功 Judge 选择标记 `Selected`，不会伪装成确定性绑定。

调用失败仅返回固定诊断，不回显 adapter exception/provider payload。等待通过 `WaitAsync` 和 linked timeout 限制；异步 adapter 忽略取消时仍可退出等待，晚到结果不会用于绑定，晚到异常被观察。适配器必须及时返回 Task；不承诺能强制终止同步阻塞代码或不合作 adapter 的后台工作。

成功选择前再次验证调用方提供的 state，比较全部状态集合、原生引用、文档身份/revision 和 accepted-type contract，并重跑候选筛选。等待期间健康状态、几何、owner/原生引用、依赖或类型合同变化均拒绝使用旧选择。该纯接口不提交 state、不修改 revision、不操作 CAD。调用方应串行协调状态写入；如果外部用另一个 immutable record 替换当前 state，本方法无法获知外部指针替换，执行前仍需通常的当前文档/revision 校验。Judge 理由属于 adapter 的语义判断，本地成员资格检查不证明理由在语义上一定正确。

异步接口没有 COM 调用，也不会自动将 native STA 工作派到异步线程。实际原生使用必须在所属 STA 重验当前身份、版本及 persistent reference 后继续既有执行/验证路径。本次不增加原生异步驱动，也不将 Judge 暴露为 Planner 的任意 CAD 工具。M7 的 runtime capability catalog 要求继续有效。

## 验证

```powershell
.\scripts\test-milestone8.ps1
.\scripts\build.ps1
```

SDK 8.0.425；**48/48 PASS**；M8 三个纯项目（IR、State、Judging.Tests）及 **14 项目 Release 解决方案编译 0 警告、0 错误**。只执行 M8 专属纯/mock 功能测试，其他里程碑仅编译。

覆盖：无 Judge/Jev 唯一候选成功、无 Judge 歧义、0/1 候选及 owner 确定性排名零调用、同步 Binder 无模型调用、受控非首项选择、不可变紧凑请求、健康/type/geometry/owner/dependency 硬筛选、8 项边界、9/65/100 项拒绝且零调用、UTF-8 意图/理由上限、弃权与非法响应、非法健康候选越界选择、同步/异步异常与错误隐藏、null Task、调用方取消/忽略取消/超时/晚到成功、有限 deadline、等待期间 state/contract 改变、真正异步挂起后原生引用变化，以及纯选择不改 state/revision。

受控测试以两个已有健康平面为合法候选，使用中文意图选择最高宿主面。FakeJudge 返回基于候选证据的第二项及理由；成功绑定 `plate_a.face_1`，证明没有默认使用排序首项 `plate_a.face_0`。这是纯 mock 契约验收，不代表真实 Jev/模型服务质量或原生几何执行成功。

## 文件与证据

| 文件 | 内容 |
|---|---|
| `src/CadHarness.State/IBoundedJudge.cs` | 可选接口、有限请求/响应、判断来源状态 |
| `src/CadHarness.State/SemanticEntityBinder.Judging.cs` | 确定性优先、候选投影、一次调用、严格响应、取消/timeout、状态重验 |
| `src/CadHarness.State/SemanticEntityBinder.cs` | 改为 partial，共用原有确定性 Binder |
| `tests/CadHarness.Judging.Tests/*` | 纯 net8.0 项目，48 项受控/mock 验证，无 SOLIDWORKS 引用 |
| `scripts/test-milestone8.ps1` | M8 专属构建/验收入口 |
| `scripts/build.ps1` | Milestone8 scope，不访问 SOLIDWORKS COM 注册表/interop |
| `CadHarness.sln` | 加入 Judging.Tests；总计 14 项目 |
| `README.md`、本记录 | 使用方式、验收与限制 |
| `artifacts/milestone8/pure-result.json` | 最新专属验收结果、48 项状态、生命周期计数 |

创建 **0**、关闭 **0** native Parts；未启用可选 max1 Part smoke。真实 Judge/Jev 请求 **0**。没有连接或启动 SOLIDWORKS，没有测试文档或活动文档恢复需求，没有新增 native budget ledger，没有更改旧里程碑预算。未读/复制 v0.1 本地工作区，未运行 M0–M7 功能/原生回归、广泛原生套件、性能 benchmark、stepwise baseline 或 live LLM。

本阶段实现受控候选选择；§12 中有限 recovery policy、歧义分类和 validation profile 选择是允许用途示例，没有增加这些额外决策路径。Jev adapter 继续为 optional，没有真实模型判断准确率证据，也不作泛化或效率结论。

**Milestone 9 NOT IMPLEMENTED**。
