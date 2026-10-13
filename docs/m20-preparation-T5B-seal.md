# T5B Maintainer Seal

日期：2026-10-13。**阶段B：SEALED；阶段C正式接入/最终readiness尚未执行。** 本报告不揭示待测数值，不批准provider/native执行，不宣布T5或M20通过。

## Package

公开摘要：[public-summary.json](D:/CAD-Harness0.2/artifacts/m20-preparation/T5B/maintainer_v1/public-summary.json)。

| 封存项 | SHA256 |
|---|---|
| 候选锁 `T5A/adapter_v6/candidate-lock.json` | `6c99a7ea094831d59f8fdbd5da793e8fceea09a0a2a001768ad257b3eb25ff45` |
| `T5B/maintainer_v1/package.json` | `9d9b76a525ea6f3b0637bd10b03e04752e59d978518d1e0e18ef4833e99e897c` |
| `T5B/maintainer_v1/manifest.json` | `2042e9af979be18351c418340d0c71bc4ba8de6fa5145e7c7d8566ab308df1a8` |
| `T5B/maintainer_v1/bundle-seal.json` | `0c0f7d856a8c64fd96dcabcecbb5109ebdaf974b6962527462c75e9df1db634c` |

包内封存一个新P0 C4组合和一个已批准人工原件上的新external动作组合。payload的逐文件hash、正常需求、三阶段expected、精确引用及来源声明由package/manifest/bundle关联；不提供标准feature tree、draft答案或最终IR。公开摘要仅含类别、数量和指纹。

## Independence

维护者为用户明确授权的本对话阶段B角色。依计划§3，独立性采用**候选锁之后的任务选择与禁止调优**，不是另一个人员的盲测；该区别写入selection/provenance，不冒充第三方或fresh工程师模型。

- 在核验427项候选源码/脚本/schema/bin指纹及HEAD未变之后，选择一次新参数/布局及动作组合。
- P0宿主、槽尺寸、非对称位置、方向、分别不同的初始深度以及两次后续深度，与核心及T5A公开开发组合不同。
- external使用用户手工原件的已公开原生身份/初始事实；模型已参与诊断，**只有动作组合未用于候选调优**。没有借换文件名声称新历史。
- 新动作关键数值在既有M14/M20/preparation JSON记录检索无命中；不复用旧17/3/23及T5A开发动作。检索范围和无法证明未记录活动的限制明确保留。
- 没有按模型表现筛选、重采或调整任务；没有运行待测LLM、Planner、compiler或native backend。

## Independent Checks

维护者独立算术核对请求与expected一致、三个capsule完全位于宿主内、互相分离、各阶段剩余壁厚为正；计算三阶段体积、槽端圆心、直边/半圆连接点、floor标高/面积、bounds及指定/冷恢复编辑不变项。
external单独计算所有孔及阵列实例中心、贯穿跨度、体积、边界/孔间距及未编辑目标；scalar后和batch后的规格分别存储，cold读batch后同一权威Revision。

使用标准JSON Schema验证四个payload/envelope文件均通过；未加载候选程序集或执行其任务准入。Phase C仍须按冻结候选核验seal及契约接入，不能把此数学/Schema结果称作原生验收通过。

辅助算术脚本曾发生一次PowerShell数组表达式括号错误，记录在 `arithmetic-tool-failure-v1.json`；纠正的是维护者工具语法，任务数值、候选实现和提示词均未修改，0次任务重采。封存后的辅助脚本及相关来源/Oracle记录有单独bundle指纹。

## Protection And Stop

候选HEAD仍为 `a14501ff3a5a50325975b00e78ad84daae7e88c7`，427个已锁文件未变。1551份既有M20文件逐项原hash复核未变，原件SHA/bytes未变；旧5创建/15打开/20关闭账本保持。
本轮provider、Planner、compiler、native调用均0；没有创建/打开Part或修改原件，没有运行正式Prepare或M20。

**停止于封存公开摘要。** `executionAuthorized=false`、`budgetApproval=PENDING`，最终readiness留待独立的Phase C任务接入。此后若为这些已揭示任务修改相关实现、提示词或期望，必须保留原结果、将任务转为development-visible并重新取得新组合，不得继续宣称fresh。
