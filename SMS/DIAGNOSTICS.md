# 诊断开关 / Diagnostics

> ⚠️ **普通玩家请忽略本文件**：这里只有排障用的开关，默认全部关闭，误开只会让日志变长。
> ⚠️ **Players: ignore this file.** Diagnostics only; all switches are off by default.


**中文** | [English](#english)

> 本文件**不面向普通玩家**。这 8 个开关**默认全部关闭**，且**不出现在** `SMS_UAD_TAF_CustomizedCore.config.csv` 中。
> 需要排查问题时，把它们写进 `Default_Files\TAFHotfix-Batch1.config.csv`（优先级高于核心表），排查完请置 0 或删除。
>
> Developer-only. These 8 switches are **off by default** and are **not** part of the player-facing config.
> Put them into `Default_Files\TAFHotfix-Batch1.config.csv` (higher priority) only while debugging, then set back to 0.

---

## 1. 开关清单 / Switch list

| 参数 / Key | 标签 / Tag | 作用 / Effect | 输出 / Output |
| --- | --- | --- | --- |
| `verbose` | — | 全局详述，各模块汇总不再截断 / global verbose | 大量日志 |
| `refit_diag` | `[SMS.1.E]` | 改造费用中间量 / refit cost internals | `RefitCost*` |
| `debug_ai_refit` | `[SMS.1.DBG]` | AI 改造位置/朝向对比 / AI refit pos diff | `DebugAiRefit` |
| `predef_pool_probe` | `[SMS.1.P]` | 预制池取用追踪 / predef pool draws | 每次取用一行 |
| `shiptype_probe` | `[SMS.1.Y]` | 原版 `int→ShipType` 返回 null 的见证 / vanilla null witness | `[SMS.1.Y] … -> null` |
| `shiptype_part_probe` | `[SMS.1.Y3]` | `Ship.IsPartAvailable` 只读探针 / part availability probe | `[SMS.1.Y3] IsPartAvailable…` |
| `hull_type_dump` | `[SMS.1.H]` | 一次性打印 hull 的 `type()`→`shipType` / one-shot hull dump | ~500 行 + summary |
| `constructor_type_probe` | `[SMS.1.Y5]` | 构造器 `shipTypeNew` 入参 / constructor entry type | `[SMS.1.Y5] ToConstructor…` |

---

## 2. 用法示例 / Example

```csv
# Default_Files/TAFHotfix-Batch1.config.csv
name,value,"comment"
hull_type_dump,1,"[SMS.1.H] one-shot hull dump"
shiptype_part_probe,1,"[SMS.1.Y3] part availability probe"
verbose,1,"global verbose"
```

---

## 3. 已"结论化"的诊断（无需再开）/ Closed investigations

| 诊断 / Probe | 结论 / Conclusion |
| --- | --- |
| `hull_type_dump` | `type()` → `shipType` 解析**正确**（580 hull，null=0，mismatch=0）/ parsed correctly |
| `shiptype_probe` | 原版未知舰种返回 **0**，不是 -1 / returns 0, not -1 |
| `shiptype_part_probe` | 非原版舰种一律 false；**放行会破坏船体筛选** / cannot be fixed by allowing |
| `constructor_type_probe` | `ToConstructor(shipTypeNew=null)` 属正常路径；改 `ChangeHull` 无效 / patching ChangeHull is ineffective |
| `predef_pool_probe` | "has no valid predefined designs" 是逐包噪音 / per-pack noise |

---

## 4. 代码默认值 / Code defaults

```csharp
Verbose = false; RefitDiag = false; DebugAiRefit = false;
PredefPoolProbe = false; ShipTypeProbe = false; IsPartProbe = false;
HullTypeDump = false; ConstructorTypeProbe = false;
```

功能性开关仍为 `true` / functional switches stay true：`ShipTypeApply`、`PredefForceOff`、`PredefAliasFallback`、`SuppressPredefPackErrors`。

---

<a id="english"></a>
# English

Developer only — these switches are off by default and absent from the player config. Enable them in
`Default_Files\TAFHotfix-Batch1.config.csv` while debugging.

| Key | Tag | Effect |
| --- | --- | --- |
| `verbose` | — | global verbose (no truncation) |
| `refit_diag` | `[SMS.1.E]` | refit cost internals |
| `debug_ai_refit` | `[SMS.1.DBG]` | AI refit position/rotation diff |
| `predef_pool_probe` | `[SMS.1.P]` | trace every predef pool draw |
| `shiptype_probe` | `[SMS.1.Y]` | witness vanilla `GetShipTypeFromIntNumber` returning null |
| `shiptype_part_probe` | `[SMS.1.Y3]` | read-only `Ship.IsPartAvailable` probe |
| `hull_type_dump` | `[SMS.1.H]` | one-shot dump of hull `type()` → `shipType` |
| `constructor_type_probe` | `[SMS.1.Y5]` | log `shipTypeNew` when entering the constructor |

Closed investigations: hull type parsing is correct (`shipTypeNull=0`); vanilla unknown ship types map to **0** (not `-1`);
`IsPartAvailable` returns false for every modded type and allowing it breaks hull filtering; `ChangeHull` is a downstream
symptom, not the cause; "has no valid predefined designs" is per-pack noise.

All eight default to `false` in code. Functional switches (`ShipTypeApply`, `PredefForceOff`, `PredefAliasFallback`,
`SuppressPredefPackErrors`) stay `true`.
