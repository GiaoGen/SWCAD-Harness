# T5A Formal Schedule And Cost

日期：2026-10-13。候选 `adapter_v6`；正式run、独立封存数值、预算及Execute批准均待确定。本表不是授权。

## Frozen Order

1. C1–C4各一次新的deterministic完整链，必须4/4。
2. C1三次、C2三次、C3三次、C4三次独立真实请求，固定核心任务、固定12分母；每族至少2/3且总体至少8/12。
3. 一个新P0组合：单独真实请求及完整链，不计核心12分母。
4. core-external、独立cold；manual-external、独立cold；新external动作组合、独立cold。
5. equation-readonly、unknown-readonly，公开intake拒绝/保留inventory。
6. 一次新G2构建/迁移/公开编辑、独立cold。
7. 两个critical-input负向规划请求；按封存的具体缺项/失败码评分，不把任意拒绝算通过。

29槽（包含4个单独external/G2 cold槽），25聚合评分行。real/deterministic的native、cold、read由三个不同控制器配对；negative只有plan，无Part；source回归复用是预先映射的证据，不替代任何新核心或Held-out槽。
机器日程和逐槽成本见 `artifacts/m20-preparation/T5A/adapter_v6/adapter_v6/freeze.json`、`schedule-costs.json`。开发包的名字/数值不是正式任务，阶段B后将生成新的正式冻结记录。

## One-Time Cost

| 组别 | 创建机会 | 正常/预定故障打开 | 打开上限 | 正常/最大关闭 |
|---|---:|---:|---:|---:|
| 核心4 deterministic + 12 real | 16 | 96 | 96 | 112 / 112 |
| 新P0完整链 | 1 | 6 | 6 | 7 / 7 |
| core/manual公开四Scalar、batch、FirstEdit回滚及cold | 0 | 16 | 26 | 16 / 26 |
| 新external scalar+batch及cold | 0 | 4 | 6 | 4 / 6 |
| 两个readonly | 0 | 2 | 2 | 2 / 2 |
| G2构建/迁移/编辑及cold | 1 | 3 | 4 | 4 / 5 |
| 两个负向规划 + 旧回归证据复用 | 0 | 0 | 0 | 0 / 0 |
| **总计** | **18** | **127** | **140** | **145 / 158** |

15次provider机会：12核心、1P0、2负向，每槽最多一次、无自动重试。未planned的槽0 Part，但仍保留其失败行/调用证据；不能回退typed答案或补采样。source external动作不发送原件、路径、原生引用或凭据到provider。

成本依据是现有API生命周期及旧结果中的累计差值，不是新原生实验：

- 通用完整链：native=3打开（blank reopen/create publish/edit publish）、cold=2（恢复+编辑publish）、read=1；创建1、关闭7。`T5/development_v4/slots/C4-deterministic/{native,cold,read}-result.json`实际为3/2/1打开。
- core/manual：初始copy open1、五次提交读回5、计划内FirstEdit回滚1、独立cold1，共8/组。v4 core虽旧收尾报告FAIL仍实耗7，v5 manual native亦实耗7；不是将失败改成PASS。每native槽12上限包含5次额外恢复余量，cold另1。
- 新external：初始1、两次提交2、cold1，另2恢复余量；无新增完整故障矩阵。
- G2旧v5 native实际2打开+1创建、cold1打开；native最多3、cold1，另1恢复余量。

原方案147打开示例为新external也分配13；本轮限制该新槽为一个scalar+一个batch，按实际协议给6，故总上限140。没有减少核心12抽样、独立cold或计划内失败验证。

**这是一次性提案，不改PRD。** 未获得新run的独立批准前，原16创建/32打开条件仍有效，且本次没有执行权限。批准后采用新run账本及全局/每槽reservation；每次失败/恢复也计数，超限在API调用前停止，不自动追加。

## Reuse And Stops

`adapter_v6/reuse-map-draft.json`提供原始结果SHA、具体JSON断言、当前生产源码与历史T5 freeze相容核验。最终manifest须带完整条款映射及维护者复核，不只拷贝一个DEVELOPMENT_PASS摘要：

- defaults：C3/C4的披露规则/默认及完整原生链；原preview、Oracle和报告随条款引用。
- save/pointer/recovery：T1 durable_v2三种独立恢复、T3 public fault/recover及其原始故障步骤；提交点前后语义保持。
- histories/identity/drivers：M14报告的direct/equation、driving/driven孔、普通/geometry pattern、原生引用/配置/源漂移；v12的native内部候选边界仍注明内部，不升级为公开入口证据。

任何原件、seal、prompt、schema、源码、binary或资源/所有权完整性失败停止日程；不能算作普通受控模型失败。已完成步骤即时落盘，缺阶段为NOT_RUN/PARTIAL，started无result为STOPPED。
同槽不重跑；相关修复必须新版本/新命名空间。旧M20账本5创建/15打开/20关闭及旧停止证据完整保留，新的正式账本不复用或清零旧账本。
