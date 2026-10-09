# Milestone 11 Verification

状态：**M11 COMPLETE**。依据 `SWCAD_Harness_v0.3_PRD.md`，版本 **v0.3-draft-2**，仅完成 §18 M11 契约与基线冻结。M12-M20 未实现；这不是 v0.3 core 完成，也不是原生迁移或外部编辑资格声明。

## 基线与范围

实现前冻结实际 HEAD `7f0109ddb5db1e5c0d70132d4bcd3020f159f113`，记录当时工作树中未跟踪的 PRD 和刚增加的冻结助手，不假设工作树干净。正式 v0.2 完成依据是 `docs/milestone-10-formal-verification.md`，不是 README 的历史 BLOCKED 文本。

`artifacts/milestone11/baseline.json/.sha256` 冻结 **2048** 个原有源码、二进制和历史证据文件。最终逐项哈希审计确认历史证据变化 **0**、已有生产源码变化 **0**。解决方案仅注册新测试项目；包含新增类型的程序集重新构建，其变更单独列在 audit 中。旧验证文档、旧 schema、旧 provider/prompt/runtime/handler/Binder/transaction 文件保留原字节。

允许范围为版本化字段/枚举/限制、来源与能力描述、需求记录、只读迁移读取器、纯契约测试和冻结证据。没有零件家族预设、外部 mutation、生产原生 handler 扩展或宽泛原生回归。

## 要求与结果

| M11 输出 / 检查 | 结果 |
|---|---|
| Program、observed state、requirement、EditSet 版本化字段/枚举/限制表 | PASS：`docs/milestone-11-contract.md`、生成的 `docs/v03-contract-fields.md`、九个 `schemas/v03-*.schema.json` |
| 草图/基准面/旋转/加料/切除名称与契约 | PASS：有限类型、签名坐标/offset、正长度、frame、profile/axis/host、end condition/body rule |
| 需求来源与不可变接受记录 | PASS：given/derived/defaulted/unresolved、规则版本、critical/ambiguity、固定 bytes/hash；缺关键事实不能 planned |
| Managed / external 模式与未知特征 | PASS：未知 inventory 保留，无虚构 OperationKind；引用/依赖缺失保持 unknown；partial inventory 非可编辑 |
| v0.2→v0.3 迁移矩阵和 reader | PASS：严格原数据、文件哈希、ownership cross-check、rollback path、inspectable proposal；无写回，无原生迁移完成声明 |
| 精确原生子类型 / API 读写候选矩阵 | PASS：拉伸深度、单圆贯穿切除直径、单活动方向阵列 count/spacing 四行；全部 Qualified=false |
| 原生 publish / reopen 状态图 | PASS：working copy/checkpoint、native/state flush、package verification、pointer publication、prior authority/recovery/quarantine |
| C1-C4 数值任务 / 独立 oracle / 后续 edit | PASS：冻结 geometry、primary variable、dependent relation、edit、volume/parameter/geometry/reopen oracle |
| Development / held-out 分离 | PASS：test-only definitions、分别记录 SHA256；production source 无 C1-C6 dispatch |
| v0.2 可执行能力没有意外扩大 | PASS：旧源文件字节不变；旧 enum/registry 9 alternatives；新增 extension executable alternatives=0 |
| Strict schema / unknown-feature / legacy-state / mode / capability 拒绝测试 | PASS：76/76 M11 pure cases |

原生子类型 API 可行性依据是现有 C# accessor 实现与成功编译的 interop signatures；没有因活动需要而启动 SOLIDWORKS。该依据不证明独立工程师模型的编辑资格。M14 必须完成每行正向 edit/reopen、独立几何读回和负向 ambiguity/unsupported 资格测试。

## 验证结果

使用仓库固定 .NET SDK **8.0.425**。完整解决方案 Release build：**0 warnings、0 errors**。

| 验证 | 结果 / 证据 |
|---|---|
| M11 契约、迁移、拒绝、schema/field snapshot、oracle tests | **76/76 PASS**；`artifacts/milestone11/pure-results.json` |
| 现有 IR pure runner | **80/80 PASS**，使用经过语义等价证明的独立 schema 副本；历史顺序问题见下文 |
| 现有 M3 State pure runner | **13/13 PASS** |
| 当前 provider / runtime / Stepwise state contract qualification | **28/28 PASS**；完全 mock/pure，无真实 provider/native 调用 |
| 历史 M7 Planning pure runner | **41/47**；与单独编译的冻结前 HEAD 完全相同的 6 项历史失败，无新增回归 |
| 源码 / 历史证据 preservation | **PASS**；`artifacts/milestone11/preservation-audit.json` |

所有旧 runner 使用 `artifacts/milestone11/v02-pure-regression` 独立 root，必要历史 JSON 只复制读取；没有覆盖 M3/M7/M9/M10 的历史结果。没有重跑真实模型、native smoke 或正式 benchmark。

### 已有回归测试问题

原 IR runner 对提交 schema 的 `JsonNode.DeepEquals` golden assertion 会得到 **79/80**。差异仅三处 `enum` 中 `linear_edge` / `reference_axis` 顺序；JSON Schema 的 enum 是集合，接受范围未改变。独立编译原 HEAD 复现同一失败。先证明仅排序枚举集合后 schema 完全相等，再在测试副本生成 schema，IR runner 得到 80/80；原 schema 文件未修改。原始失败、语义等价审计均保留，未把副本结果冒充原字节 golden 通过。

历史 M7 的 6 条断言仍要求隐藏后来已经资格化的圆形 profile、circular pattern 等能力。独立 Git archive 中编译冻结前生产代码，得到同样 **41/47**、相同失败名称和错误；当前结果逐条比对完全一致。Git archive 的 LF 与原工作树 CRLF 只做文本等价核验，原工作树另由原始 SHA256 核验；没有修改这些旧测试来制造通过。当前资格 suite 的 28 条能力/strict provider/state/prompt 测试全部通过。

因此结果是“**无新增 v0.2 回归**”，不声称所有历史 test runner 全部通过。两个历史测试维护问题不扩大运行时能力，也不掩盖 M11 新契约失败。

## 文件与复现

新增生产文件：`src/CadHarness.Ir/V03Contracts.cs`、`V03ContractJson.cs`、`V03ContractValidation.cs`；`src/CadHarness.State/V03ObservedState.cs`、`V03LegacyMigrationReader.cs`、`V03NativeQualificationCandidates.cs`；`src/CadHarness.Planning/V03ContractCapabilities.cs`。

新增纯测试项目 `tests/CadHarness.V03Contracts.Tests`、数值任务 fixture、九个 v0.3 field schemas、契约/字段/验证文档和四个 M11 scripts；`CadHarness.sln` 注册项目。用户提供的 PRD 未修改。

```powershell
.\scripts\test-milestone11.ps1
.\scripts\test-milestone11-regression.ps1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\verify-milestone11-freeze.ps1
# -WriteSchemas 仅用于显式更新契约快照；冻结后改变源码需新的命名版本。
# 初始 baseline 和最终 contract freeze 不允许原位覆盖。
```

主证据在 `artifacts/milestone11`：baseline/hash、contract-freeze/hash、preservation-audit、pure-results、capability-contracts、native-budget、solution-build.log、v02-regression、legacy-schema-equivalence、原 HEAD 与当前 pure logs。`contract-freeze` 保存最终源码、任务子集和能力契约哈希；不是 M20 provider/正式测量 freeze。

## Native 使用与限制

SOLIDWORKS 启动 **0**；新 Parts **0**；native open cycles **0**；关闭 test Parts **0**；native originals 修改 **0**；真实 provider requests **0**。预算为 0 new Parts / 至多 1 个有必要性说明的 probe open cycle，本轮使用 0/0。未打开原生 fixture，所以原生 fixture 前后 checksum 不适用；纯 JSON/源码/历史文件的哈希均已记录。

当前没有实际外部 intake、cold reopen、native migration publish、单项/批量外部编辑、通用 sketch solver/native handler、新操作执行、partial-intent 实际规划或 P1 UI。相应实现与原生证据属于 M12-M20。C1-C4 和外部 held-out 是冻结任务/fixture specification；M11 不虚构已创建的 `.SLDPRT` 或通过的原生验收。
