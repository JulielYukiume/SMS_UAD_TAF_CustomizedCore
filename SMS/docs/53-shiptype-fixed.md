# 非原版舰种支持：**通用解达成**（2026-10-24 收口）

> 结论：**不依赖 `ShipTypesSupport`、不引入逐舰种 fallback 表**，通过"序号扩展 + 未知舰种 int 覆盖"解决。
> 关键证据与实现都在本文；Y3~Y7 的实验代码待白天清理。

---

## 1. 根因（三层，逐层被日志证实）

| # | 事实 | 证据 |
| --- | --- | --- |
| 1 | **`type()` 解析完全正常**（含非原版 `av`/`bm`/`cb`） | `[H] summary: parts=3272 hulls=580 withTypeKey=580 shipTypeNull=0 typeMismatch=0` |
| 2 | `Ui.GetShipTypeFromIntNumber(int)` **只认 0..5**，其余返回 `null` | 反汇编 `cmp ebx,5 / ja → null` + 6 项跳转表（RVA `0x1E637D0`） |
| 3 | **`Ui.GetIntNumberFromShipType(ShipType)` 对未知舰种返回 `0`（= bb），不是 -1** | 实测日志：`ShipType->int: 'cb' -> 0 (vanilla)` |

⇒ 组合起来的失败链：

```
cb  --GetIntNumberFromShipType-->  0      （原版把未知静默映射成 0）
0   --GetShipTypeFromIntNumber-->  bb
⇒ 设计被当成 bb，但所放船体要求 cb
⇒ 界面红字"（需选择不同舰种: ShipType-cb）"，船体不可选
```

## 2. 解法（`ShipTypeSupportApply` v3）

| 规则 | 实现 |
| --- | --- |
| 原版 6 种（bb/bc/ca/cl/dd/tb）**序号绝不改** | 只在"名字不属于这 6 种"时介入 |
| 其余舰种分配 **20 + 序号** | 遍历 `G.GameData.shipTypes` 中非原版的条目，`20, 21, 22 …`；双向共用一张表 |
| 表在**主菜单/加载阶段**建好 | 新增 `GameManager.ChangeState` 钩子：`Menu` / `Loading` / `LoadingCustom` 时 `EnsureTable()` |
| 覆盖错误的 0 | `GetIntNumberFromShipType` postfix：`if (!IsVanilla(name)) __result = ext;` |
| 解析扩展序号 | `GetShipTypeFromIntNumber` postfix：原版返回 null 时查扩展表 |
| 可用性放行（仅扩展序号） | `IsShipTypeActiveForCurrentPlayer` postfix：`_intToType.ContainsKey(n)` 时置 true |

实机日志（成功）：

```
[Y2] int->ShipType: 21 -> null (越界；当前硬编码 0..5)  total=15     ← Y 探针（原方法结果）
[Y2] int->ShipType: arg=21 -> 'cb' (extended)                        ← Y2 兜住
[Y2] ShipType->int: 'cb' 0 -> 21 (override non-vanilla)              ← 关键覆盖
```

**玩家可见结果**：共享设计里能选 CB 船体、能放船体、能正常装部件（截图确认）。

## 3. 当前参数状态

| 参数 | 值 | 说明 |
| --- | --- | --- |
| `shiptype_apply` | **1** | ✅ 生效中（v3 映射 + 早建表） |
| `shiptype_probe` | 1 | 只读探针（`[Y]`） |
| `shiptype_active_force` | 0 | 强制 true：**不再需要**（待清理） |
| `shiptype_generic_fix` | 0 | 部件通用放行：**不需要**（待清理） |
| `hull_type_bind` | 0 | ChangeHull 改舰种：**无效**（待清理） |
| `shiptype_part_probe` | 0 | IsPartAvailable 探针：诊断用（可保留） |
| `hull_type_dump` | 0 | hull 舰种表（一次性诊断，可保留但默认关） |
| `constructor_type_probe` | 0 | 构造器入参探针（可保留） |

## 4. 白天要做的清理（按优先级）

| # | 项 | 动作 |
| --- | --- | --- |
| 1 | `ShipTypeGenericFix`（Y4） | **删除**（曾导致"无视年代/崩溃"） |
| 2 | `HullTypeBind`（Y6） | **删除**（已证明无效，且动 `ChangeHull` 属过度干预） |
| 3 | `ShipTypeActiveForce`（Y7） | **删除**（强制 true 是错的） |
| 4 | `IsPartAvailableProbe`（Y3） | 保留但默认关（诊断有用，已降噪） |
| 5 | `HullTypeDump`（H） | 保留但默认关（一次性诊断已用完） |
| 6 | `ConstructorTypeProbe`（Y5） | 保留但默认关 |
| 7 | `ShipTypeProbe`（Y1） | 保留（只读，已限流） |
| 8 | 配置表 | 去掉被删模块对应的 3 个参数，按模块分组重排 |

## 5. 遗留风险 / 待观察

| 项 | 说明 |
| --- | --- |
| **序号映射的稳定性** | 扩展序号 = 遍历 `shipTypes` 的顺序 + 20，**同一进程内自洽**；不写入存档 ⇒ 跨进程无需一致。若将来要持久化（设计里存 int），需改成名字驱动 |
| **`IsShipTypeActiveForCurrentPlayer` 的谓词** | 我们只在"扩展序号"时放行；若将来该谓词对年份有更细判定，可能需要读取 `shipTypes.csv` 的年份列 |
| **自定义战斗年代过滤** | 本次未回归验证"是否又出现无视年代"——白天需专门确认一次（这是 Y2 早期版本的副作用） |
| **`ShipTypesSupport` 共存** | 两者功能重叠（都改 `GetIntNumberFromShipType` 等）；**不建议同时启用**，若同装应关掉我们的 `shiptype_apply` |

## 6. 结论

- **"填 null"路线被证实正确**（我中途转向"强制放行/改 ChangeHull"都是弯路）；
- 真正的坑是 **`GetIntNumberFromShipType` 对未知舰种返回 0**，这一条此前没有被任何文档/分析捕获；
- 现在的能力等价于 `ShipTypesSupport` 的"UI 四项"，但**不含**它的 fallback 近似与生成器补丁，因此对原版行为侵入更小。

---

## 7. 清理与命名（2026-10-24 白天）

### 7.1 已删除的弯路模块

| 模块 | 原因 |
| --- | --- |
| `ShipTypeGenericFix`（原 Y4） | 曾导致"无视年代显示全部舰种 / 加舰种崩溃" |
| `HullTypeBind`（原 Y6） | 改 `ChangeHull` 是下游，已验证无效 |
| `ShipTypeActiveForce`（原 Y7） | 强制 `IsShipTypeActive=true` 绕过谓词，方向错误 |

配置由 35 项 → **33 项**；代码减少约 300 行。

### 7.2 日志标签统一加批次前缀

所有模块日志标签由 `[X]` 改为 **`[SMS.1.X]`**（例：`[SMS.1.Y2]`、`[SMS.1.B2]`、`[SMS.1.K]`），
这样以后看到任何一条日志都能知道"哪个批次修的"。共 26 个文件、约 350 处。

### 7.3 Y（只读探针）现状

| 观察点 | 状态 |
| --- | --- |
| `int->ShipType: N -> null` | 仍会在 Y2 介入前记录"原版返回 null"，**仅见证** |
| `ShipType->int: … -> -1` | **死代码**（原版对未知返回 `0` 而非 `-1`） |
| `AddedShipInShipList` / `AddPart` | 见证用 |

⇒ Y 已降为 **DEBUG**（`verbose=1` 才输出），代码保留作诊断。

### 7.4 与 `ShipTypesSupport` 的代码级冲突（重要）

| 位置 | 我们 | ShipTypesSupport | 冲突 |
| --- | --- | --- | --- |
| `Ui.GetShipTypeFromIntNumber` | postfix（扩展表 20+） | postfix（内部顺序表，含 `tr` 特例） | ⚠ **两者都扩展**，后执行者的值生效 ⇒ 同一进程内两次调用可能得到不同舰种 |
| `Ui.GetIntNumberFromShipType` | postfix（覆盖未知的 `0`） | postfix（同向扩展） | ⚠ 同上，**序号基准不同**（我们 20+，他们 6+） |
| `Ui.IsShipTypeActiveForCurrentPlayer` | postfix（仅扩展序号放行） | postfix（放行） | 合并后一般无害，但依赖前两项一致 |
| `Ship.AddPart` | **只读** prefix（探针） | prefix（去重上限） | 无（我们不返回 false） |
| `Ship.IsPartAvailable` | 无（已删） | prefix（fallback 替换） | 无 |
| `AddRandomPartsNew.MoveNext` | 无 | prefix（烟囱字典补键） | 无 |

**结论：不要同时启用两套 UI 序号映射。**
建议：

| 场景 | 配置 |
| --- | --- |
| 只装 SMS.1（推荐） | `shiptype_apply=1` |
| 同时装 `ShipTypesSupport` | **`shiptype_apply=0`**（把 UI 映射让给它，避免序号基准打架） |
| 只想观察 | `shiptype_probe=1` + `verbose=1` |

### 7.5 参考来源标注

`ShipTypeSupportApply`（`[SMS.1.Y2]`）的**核心逻辑**（UI 四项：int↔ShipType、IsShipTypeActive、AddedShipInShipList 处理）
参考自 **ShipTypesSupport**（作者：黑桐 10086）。
差异仅在"扩展 int 的编排方式"：

| 项 | ShipTypesSupport | 我们 |
| --- | --- | --- |
| 扩展序号起点 | 6（vanilla 6 之后顺延，含 `tr` 特例） | **20**（预留区间，绝不与 0..5 冲突） |
| 未命中时的行为 | 依赖内部顺序表 | **覆盖原版返回的 `0`**（这是本次定位到的真正根因） |
| 建表时机 | 启动早期 | **主菜单/加载阶段**（`ChangeState` 钩子） |

---

## 8. 预制设计：自定义战斗的界面级锁定（2026-10-24）

### 8.1 问题

FoF 下进自定义战斗：己方用非原版舰种，对方 CA 未手动设计 ⇒ 战斗初始化卡死。
日志：

```
[SMS.1.Y2] ShipType->int: 'cb' 0 -> 21 (override non-vanilla)
[ERROR] System.ArgumentOutOfRangeException … List`1[T].set_Item   （55ms 后）
```

⇒ TAF 的预制设计路径用**固定 6 舰种列表按 shipType 序号索引**，扩展序号 21 直接越界。

### 8.2 根因（TAF 自身缺陷）

| 事实 | 位置 |
| --- | --- |
| `force_no_predef_designs` 参数**只声明、从未被使用**（仅出现在报错文案里） | `Config.cs` L242-243 |
| 自定义战斗界面的"预制设计"按钮是 **TAF 自建**（`"TAF Predef Designs"`），默认 `usePredefs = true` | `Modified/UiM.cs` L1348-1366、L1023 |
| 战斗填充时读 `sk.usePredefs` → `PredefinedDesignsData.GetRandomShip` | `Harmony/BattleManager.cs` L320-346 |

### 8.3 解法（直接改 TAF 源码，3 处）

```csharp
// ① 建按钮前强制禁用
if (Config.ForceNoPredefsInNewGames) skirmishSetupMod.usePredefs = false;

// ② 点击直接返回（切不动）
if (Config.ForceNoPredefsInNewGames) return;

// ③ 按钮置灰
if (Config.ForceNoPredefsInNewGames) predefDesignsButton.interactable = false;
```

效果：按钮显示 **"预制设计: 禁用"**，且不可点击；与战役开局"AI 设计速度"被锁成慢速同理。

**后端兜底** `[SMS.1.PF]`（`PredefForceOff`）保留：`PredefinedDesignsData.GetRandomShip` / `ShipsPerPlayer.RandomShipOfType`
在标志开启时返回 null，确保不会再触碰越界路径。

> 注：标志用**惰性读取** `TweaksAndFixes.Config.ForceNoPredefsInNewGames`（不要用 `Config.Param("taf_force_no_predef_designs")`，键名不匹配恒为 0）。

### 8.4 我们对官方 TAF 源码的改动（合并上游时必须保留）

| 文件 | 改动 |
| --- | --- |
| `TweaksAndFixes.cs` | `MelonInfo` 版本串 → `3.21.1-SMS.1`；`OnInitializeMelon` 末尾 `Bootstrap.Install(HarmonyInstance)` |
| `TweaksAndFixes.csproj` | 增加 `UnityEngine.AudioModule` 引用 |
| `Modified/UiM.cs` | 3 处 `SMS.1.PU` 预制设计锁定 |

其余全部改动都在 `SMS_Hotfix/` 目录内（可整目录搬迁）。
