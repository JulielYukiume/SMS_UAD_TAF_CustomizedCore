# 诊断开关（仅开发者用）—— `3.21.1-SMS.1`

> **这些开关不出现在玩家/Modder 的 `SMS_UAD_TAF_CustomizedCore.config.csv` 中。**
> 它们全部**默认关闭**，只在需要排查问题时，写进**批次覆盖表** `TAFHotfix-Batch1.config.csv`
> （该表优先级高于核心表）才会生效。

---

## 1. 为什么分离

| 表 | 面向 | 内容 |
| --- | --- | --- |
| `SMS_UAD_TAF_CustomizedCore.config.csv` | 玩家 / Modder | **26 项**，全部是"修复行为"与少数玩家可调项 |
| `TAFHotfix-Batch1.config.csv` | 开发者 / 排障 | **34 项** = 核心 26 项 + **8 项诊断**（默认 0） |

设计原则：核心表不放诊断项，避免玩家误开导致日志刷屏或性能负担；诊断项由代码默认值兜底（全 `false`）。

---

## 2. 诊断开关清单（8 项，全部默认 0）

| 参数 | 标签 | 作用 | 输出 |
| --- | --- | --- | --- |
| `verbose` | — | 全局详述模式（各模块的分类汇总会全量打印） | 大量日志 |
| `refit_diag` | `[SMS.1.E]` | 改造费用计算的中间量（旧/新 design、cost 差） | `RefitCost*` 诊断行 |
| `debug_ai_refit` | `[SMS.1.DBG]` | AI 改造位置/朝向保存与恢复的对比 | `DebugAiRefit` 行 |
| `predef_pool_probe` | `[SMS.1.P]` | 预制池取用（`RandomShipOfType` / `GetRandomShip`）全量追踪 | 每次取用一行 |
| `shiptype_probe` | `[SMS.1.Y]` | 舰种 UI 的**原版返回值**见证（`int→ShipType` 返回 null 时记录） | `[SMS.1.Y] … -> null` |
| `shiptype_part_probe` | `[SMS.1.Y3]` | `Ship.IsPartAvailable` 只读探针（部件/舰体可用性判定） | `[SMS.1.Y3] IsPartAvailable…` |
| `hull_type_dump` | `[SMS.1.H]` | 启动后一次性打印所有 hull 的 `type()`→`shipType` 归属 | 约 500 行 + summary |
| `constructor_type_probe` | `[SMS.1.Y5]` | 进入构造器时的 `shipTypeNew` 入参 | `[SMS.1.Y5] ToConstructor…` |

---

## 3. 用法

```csv
# Mods/TAFHotfix-Batch1.config.csv （只写要覆盖的项，其余继承核心表）
name,value,"comment"
hull_type_dump,1,"[SMS.1.H] 一次性 dump hull 归属"
shiptype_part_probe,1,"[SMS.1.Y3] 部件可用性探针"
verbose,1,"全局详述"
```

排查完请把这些行删掉（或置 0），避免长期占用日志。

---

## 4. 已经"结论化"的诊断（可不再开启）

| 诊断 | 结论 | 详见 |
| --- | --- | --- |
| `hull_type_dump` | `type()` → `shipType` **解析正确**（580 hull，null=0，mismatch=0） | [docs/53 §1](53-shiptype-fixed.md) |
| `shiptype_probe` | 原版 `GetIntNumberFromShipType` 对未知舰种返回 **0**（不是 -1） | [docs/53 §1](53-shiptype-fixed.md) |
| `shiptype_part_probe` | `IsPartAvailable` 对非原版舰种一律 false ⇒ **不能靠放行解决** | [docs/53 §7.3](53-shiptype-fixed.md) |
| `constructor_type_probe` | `ToConstructor(shipTypeNew = null)` 是正常路径；改 `ChangeHull` **无效** | [docs/53 §4](53-shiptype-fixed.md) |
| `predef_pool_probe` | TAF 的"has no valid predefined designs"是**按包噪音**，非真缺口 | docs/46 / 47 |

⇒ 除非出现新问题，**不需要再开这些开关**。

---

## 5. 代码中的默认值（确认为 false）

```csharp
Verbose = false; RefitDiag = false; DebugAiRefit = false;
PredefPoolProbe = false; ShipTypeProbe = false; IsPartProbe = false;
HullTypeDump = false; ConstructorTypeProbe = false;
```

功能性开关仍为 `true`：`ShipTypeApply`、`PredefForceOff`、`PredefAliasFallback`、`SuppressPredefPackErrors`。
