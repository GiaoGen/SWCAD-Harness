# M18 Verification

2026-10-13（Asia/Taipei）。**M18 COMPLETE：仅指 PRD M18 的有界 External Query／Preview／EditSet。** 基线 `0162a7d`，稳定原生验收 `artifacts/milestone18/acceptance_v3`。未开展M19、未新增原生Subtype／CAD操作／事务引擎，0 provider调用。

## Implementation

- `src/CadHarness.State/ExternalQueryPlanning.cs`：strict query v1、有限筛选、歧义／空集／不支持诊断、完整提案与依赖安全排序、冻结预览及一次性确认。
- `src/CadHarness.SolidWorks/ExternalPartSession.cs`：新增`PreviewEditSet(QueryEditSetRequest)`与`Edit(QueryEditApproval)`；原生重新核对、重新查询后，直接调用既有`Edit(EditSetRequest)`及M14事务／持久化实现。
- 筛选支持观测Subtype／NativeType、已有Scalar尺寸的值集或区间、已资格化Host、直接父子／下游／Pattern Seed及Feature编辑状态。2–16项编辑、最多16条查询，空集不退化为全部编辑，匹配超限不截断。
- `ExactlyOne`多匹配拒绝；`IntentionalBatch`必须明确确认整组。等尺寸的两个真实身份可以组成有意批量，并非自动绑定歧义。
- 预览包含原查询、依赖安全的有序目标／旧值／新值／单位、Native Reference、受影响目标与依赖、unsupported／ambiguous字段、完整验证要求、Source／Copy指纹、配置和Revision。成功预览的unsupported／ambiguous集合为空；不安全选择返回诊断而不授予执行预览。
- SHA、Revision及准确有序目标同时确认；预览不可被调用者数组修改，批准及dispatch各一次使用、绑定会话。跨Part／会话、未资格化参数及未知依赖仍拒绝。公开查询不拓宽M14板件／驱动／配置／几何支持。

## Native Matrix

开发输入：冻结`dev_core`（Factory开发夹具，不是Held-out），120×80×10mm，Seed Ø8、B Ø10、C Ø6，单方向阵列3实例／25mm。正向只经生产公开Query批准入口；独立Oracle复用M14官方API测量，不使用Factory Reader或生产几何公式作为测试结果。

| 要求 | 实际结果 | v3证据 |
|---|---|---|
| 真正欠指定单目标 | 三个不同原生孔匹配`ExactlyOne`，`AMBIGUOUS_TARGET_SET`；0生产Setter／Checkpoint | `steps/native/0042-selection-refusal.json` |
| 空集 | `QUERY_EMPTY`，无“edit all”fallback、无修改 | `0043-selection-refusal.json` |
| 实际成员／值漂移 | 自有副本B Ø10→Ø9；该孔退出原值集，公开dispatch以`STATE_DRIFT_DETECTED`拒绝；随后恢复探针 | `0048-refusal.json`、owned-membership-drift步骤 |
| 两个独立目标 | B Ø10→Ø11、C Ø6→Ø11；真实两个Setter，1 Checkpoint、R0→R1，保存与完整Oracle PASS | `0111-independent-batch.json` |
| 一次性／旧预览 | `QUERY_APPROVAL_USED`、`QUERY_PREVIEW_STALE`，均0生产Setter／Checkpoint | `0150-refusal.json`、`0151-refusal.json` |
| 依赖目标组 | Seed Ø8→Ø8.5＋阵列间距25→24；按M14安全顺序Seed先于Pattern，1 Checkpoint、R1→R2，全部实例及实体Oracle PASS | `0173-dependent-batch.json` |
| 中途故障／全量回滚 | 等尺寸B/C明确批量确认；首个真实Setter Ø11→Ø11.5后故障，第二个未执行；rollback=true、commit=false、1 Checkpoint，R2／Pointer不变，完整Oracle PASS | `0219-mid-batch-rollback.json` |
| 独立cold | PID31364提交／回滚，PID31100冷重开；R2、参数、引用、配置、Native／State／Manifest及完整Oracle PASS | `native-result.json`、`cold-result.json` |

实测5次生产Setter（2独立＋2依赖＋1预期失败）；另有2次自有副本的负向漂移探针Setter，不计为生产编辑通过。拒绝断言计的是公开入口Setter／Checkpoint，未将探针伪装成“完全没有API修改”。每个关键步骤立即写入不可覆盖文件。

## Failures And Regression

`acceptance_v1`保留**FAIL**：原生值为`7.999999999999998`／`10.000000000000002`／`6.0000000000000036`，最初逐位相等匹配返回EMPTY；1打开／1关闭、0 Setter／Checkpoint。失败companion及原始结果保留。修复仅允许**8 ULP内的数值表示等价**，不是附近目标猜测；原M14参数检查、几何／Oracle容差完全不变。新增原始数值及区间边界回归，0.0001mm的不同查询值仍拒绝。

`acceptance_v2`仅离线准备；执行前发现验收器不能读取自己独占的账本来取hash，改为立即保存已持有snapshot，释放后审计hash，并补齐共享Oracle／脚本指纹。稳定v3才重新执行原生日程；不修改旧冻结／失败，不重复无新增诊断的测试。

- 稳定Release纯验证 **40/40**：`artifacts/milestone18/pure_20261013141537487/result.json`，含严格Schema／未知字段／重复字段／枚举／单位／数值、独立及依赖目标、重复参数、只读／不支持／未知下游、最终几何不合法、8类身份／值／成员漂移、一次性与跨会话拒绝。
- M14纯回归 **86/86**：`artifacts/milestone14/pure/20261013T140550335/result.json`；核心纯回归 **52项PASS**：`artifacts/m20-preparation/T5/pure/20261013T140552562/result.json`。后续ULP修复仅在新Query模块，既有P0执行路径未变；未重跑M14–M20完整原生矩阵。
- 编译0警告／0错误，`git diff --check`通过。新测试／脚本：`tests/CadHarness.QueryEditing.Tests/`、`scripts/test-milestone18.ps1`。

## Ledger And Protection

原PRD预算0新Part／5打开；首次失败后用户明确授权无限次数并要求停滞退出，本轮仍限定剩余5个打开槽。旧v1账本原样保留；`acceptance_v2/ledger-carry-forward.json`证明旧1次计数逐字节接入新累计账本，未重置。

最终 `artifacts/milestone18/native-budget.json`：**0创建／0新Part、6打开／6关闭、owned=[]**；其中1次旧失败＋5次稳定验收。最终documents=[]、无Recovery Marker，SOLIDWORKS PID3364响应正常、GDI6217<7000，活动文档恢复通过；未启动／终止用户应用或接管工程师文档。

原件SHA256始终为`7c13241a63799595b8259063b1ad2b5f45fe96b225a1c3d41cee7daf888339f9`。冻结源码／二进制／共享Oracle／脚本／来源与日程SHA：`5b14bd831e98ff9fc51ba62224a13c8a18b16fbae9c6fbae0506e747affe7f10`，两原生阶段最终都复核通过。先前M14／M20账本与失败没有覆盖。

**结论：M18 COMPLETE。** M19 UI／关系探索以及独立C5/C6验收没有执行，不宣称全部P1完成。结构性观察见`docs/v0.4-technical-debt.md`，本阶段没有底座重构。
