# T5A Maintainer Package V1

日期：2026-10-13。仅为阶段B交接规范；没有正式任务数值、执行批准或M20结果。

## Candidate

使用 `artifacts/m20-preparation/T5A/adapter_v6/candidate-lock.json`，SHA256：`6c99a7ea094831d59f8fdbd5da793e8fceea09a0a2a001768ad257b3eb25ff45`。
锁定 HEAD `a14501ff3a5a50325975b00e78ad84daae7e88c7` 加已记录的未提交源码、脚本、schema和 `adapter_v6/bin` 二进制。不是仅凭commit名冻结。

维护者先核对此锁，再独立选择新值。不得运行待测provider、Planner/compiler或native backend来选题；不得根据试跑结果换题、改提示词或生产实现。若先提交代码改变HEAD，必须先重新锁定候选，再封存任务，不能继续使用这个旧锁。

## Files

| 文件 | 内容 |
|---|---|
| `p0.json` | `TaskDefinition` payload；package版本1决定格式。C4有限资格域、普通意图/锚点/规则、一个编辑角色、指定编辑值与cold继续编辑值、三个独立expected |
| `external.json` | `ExternalProtocol`：初始规格、一个scalar、随后一个真正same-Part多目标batch、初始/两个动作后规格；cold读最终规格 |
| `package.json` | version=1、唯一id、development=false、maintainer、sealedUtc、untuned=true、Oracle来源声明、候选锁及两个payload的FrozenFile |
| `seal.json` | `package.json` 的FrozenFile：绝对path、UTF-8文件实际SHA256、bytes |
| `manifest.json` | version=1/runId、development=false、候选/package/seal指纹、固定provider、core/manual回归规格、两个critical负向请求、三项有断言及版本相容依据的复用映射、proposedParts=18/proposedOpens=140 |

Envelope Schema：[t5a-acceptance-package-v1.schema.json](D:/CAD-Harness0.2/schemas/t5a-acceptance-package-v1.schema.json)。请求内部同时受既有生产Contract/Normalize约束；独立几何及seal受验收loader约束，不能只凭JSON Schema通过。
字段的可运行开发样例位于 `artifacts/m20-preparation/T5A/adapter_v6/development-package/`，**这些数值已经用于调试，不能改一个development标志后作为Held-out**。

## P0 Domain

- 一个世界XY中心矩形宿主，三个独立、相离、正剩余壁厚的blind capsule cuts。新宽/长/厚度/中心/方向等组合必须未用于待测版本调优。
- 请求为semantic v2，包含正常需求、有限rules和唯一 `CutDepth` editability角色；声明准确的来源key、锚点、reverse Z方向和保留项。
- `program=null`，`draftJson=""`。不提交答案feature tree、标准draft或展开IR；expected绝不注入prompt。
- `expected[0..2]` 各含 width/height/thickness及三个capsule：id、世界中心x/y、宽width、总长length、angle、depth。仅目标槽depth在两次编辑中变化，其他几何完全保持。
- **Oracle angle是从世界+Y逆时针的偏转角**；生产请求的XY capsule局部长轴从+X计角，因此同一方向为 `requestAngle = oracleAngle + 90°`（可按360°归一）。长度不是两圆心距离；圆心距=`length-width`。
- 每阶段独立计算 `V=W*H*T - Σ depth*(width*(length-width)+π*width²/4)`、两端圆心、半圆半径、上下端点、槽底标高及底面积；不用生产integrator/compiler算expected。
- 首次/指定/cold三个规格必须与需求、角色及编辑值一致。选择与尺寸合法性由维护者独立核对，loader提供边界、相离及三阶段不变项检查；不为不一致的维护者规格修补模型答案。

## External Domain

- 使用已批准且披露的core或用户人工原件；记录原件来源、此前诊断/调优、SHA、单配置和现有native历史。模型本身不是fresh，只有**新动作组合**可以是Held-out。
- 复用其真实初始矩形/贯穿圆孔/单方向+X线性阵列规格及精确Persistent References；从 `externalRegression` 的原件身份取值，不按名字、尺寸或位置猜测目标。
- `actions[0].kind="scalar"`，一项已资格化参数；`actions[1].kind="batch"`，至少两个独立目标，准确old/new值。仅用既有四项Scalar。`firstEditFault=false`，故障覆盖使用回归日程。
- `expected`恰三项：初始、scalar后、batch后。包含所有孔/实例/未改目标、host、配置、引用及原件身份。cold以最后一项验证，不自动增加尺寸或按fixture id选择编辑值。
- 选择合法、互不冲突的值并保持正壁厚/不相交。不能使用旧17/3/23组合或本轮开发样例冒充新组合。

## Provenance And Seal

sealedUtc严格晚于候选锁。维护者标识、未调优声明、输入/Oracle独立算术来源、已公开模型与未公开动作组合的区别必须真实可审计。
公开摘要可以只列任务类型、数量、candidate及package SHA，随后在正式Prepare阶段揭示payload。
揭示后发生相关源码/提示词/Oracle修复，原任务即成为development-visible：保留失败，重新锁版本、取得新独立组合并建立新run，不在同题上修到PASS。

## Prepare And Authority

封存包到位后的离线接入命令：

```powershell
scripts/test-milestone20.ps1 -Mode Prepare -RunId <new_run> `
  -Manifest <absolute_manifest_path> `
  -CandidateBin D:\CAD-Harness0.2\artifacts\m20-preparation\T5A\adapter_v6\bin
```

Prepare核对candidate/source/package/seal，离线捕获每请求真实prompt/schema并冻结成本与日程；不调用provider、不打开Part、不重建已封存版本。
此后单独申请一次正式执行授权。`ExecutionAuthorization`必须绑定新 `freeze.json` SHA、独立人类授权文字和时间、executionAuthorized=true、有限全局上限和每槽上限。授权记录不能来自维护者包或开发样例；上限需覆盖18创建/140打开，且明确允许15次provider请求及发送的数据范围。
**本次没有提供或生成任何有效Execute授权。** 技术READY与budgetApproval/executionAuthorized分别报告；不能因seal有效就执行。
