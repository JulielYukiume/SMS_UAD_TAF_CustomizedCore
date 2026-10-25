# 61 · tonnage 污染问题总账（E 模块）—— 已全部关闭

> 2026-10-26 整理。记录"E 模块改写 `ship.tonnage`"这一整类问题的**所有已知触点**与最终处置。
> 任何后续改动只要触碰 `tonnage` / `TonnageMin` / `TonnageMax`，都必须回来核对本文件。

---

## 1. 铁律

| 规则 | 说明 |
| --- | --- |
| **`ship.tonnage` 必须恒等于 `ship.design.tonnage`** | 这是"事实同级舰"指纹之一（五项指纹：名字/类型/设计ID/吨位/吃水等） |
| **不得用 `CalcWeightAndCost` 的结果回写 tonnage** | 它算的是"排水量（含 beam/draught 修正）"，不是设计吨位 |
| **不得改写 `Ship.TonnageMin/TonnageMax`** | 这两个是数据层共用属性（初始化、船体缩放、航速），改它会连带改 tonnage |
| 显示层需要"改造后的数字"时 | 只能在 **UI 文本拼接处**取值，不能改被共用的属性 |

---

## 2. 已发现并被关闭的触点

| # | 位置 | 行为 | 处置 |
| --- | --- | --- | --- |
| 1 | `RefitCostRefresh.RecalcKeepTonnage`（原各处 `CalcWeightAndCost`） | 重算后 `tonnage = 设计吨位 × beamDraughtBonus` | ✅ 已改为重算后**回写 `design.tonnage`**（port17） |
| 2 | `RefitCostDisplay.Postfix_Tonnage`（挂在 `Ship.TonnageMin/TonnageMax`） | 读档时把 `status==Refit` 船的吨位边界换成"设计 ÷ 系数" ⇒ 船初始化据此回填 tonnage | ✅ **永久停用**（代码已注释化，port29） |
| 3 | duke 的 `RefitShipsStart` Postfix（`CalcWeightAndCost(true,true)`） | 改造瞬间用未结算状态重算，偏低吨位落盘 | ✅ 已禁用该 Postfix（port12） |
| 4 | `RefitCostRefresh.OverwriteFromDesign`（`refit_state_overwrite`） | 走 `FromStore(design)` 会覆写 tonnage | ⚠️ 默认关闭（`0`），开启需评估 |

## 3. 只读、无需处理的触点

| 位置 | 用途 |
| --- | --- |
| `Harmony/ShipRefreshHull.cs` L132-133 | `TonnageMin()/beamDraughtBonus` 作为**段数插值输入** |
| `Harmony/Ship.cs Postfix_SetDraught` | 构造器人工改船体时 `SetTonnage(...)`（原版语义，读真实边界） |
| `SMS_Hotfix/Modules/TonnageCheck.cs` | 只读校验/可选回填（`tonnage_repair`） |

---

## 4. 症状与判别方法

| 症状 | 判别 |
| --- | --- |
| 存档里 `tonnage` = `design.tonnage × beamDraughtBonus` | 触点 #1 或 #3 |
| 读档后 `tonnage` 从正确值变成"设计 ÷ 系数" | 触点 #2（`TonnageMin/Max` 被接管） |
| `[SMS.1.TON] MISMATCH` 且 `原始≈design`（差 0%） | 说明是"乘了系数"型污染 |

**校验命令**（读档即触发）：

```
[SMS.1.TON] summary: ships=N mismatched=0 (threshold 2%)
```

严格模式（`tonnage_check_strict=1`，默认）下要求 `tonnage == design`（容差 0.1 吨）。

---

## 5. 历史时间线

| 时间 | 事件 |
| --- | --- |
| 2026-10-19 | E 模块初版；当时只处理"回合中（刚改造）"的显示 |
| 2026-10-25 凌晨 | 发现 44 条历史坏档（`design × bonus` 型），`tonnage_repair=1` 可回填 |
| 2026-10-25 晚 | `RecalcKeepTonnage` 修复（port17），实测宵月/石垣吨位稳定 |
| 2026-10-26 凌晨 | 发现**读档**仍被污染（`TonnageMin/Max` 接管）→ port28 加开关、port29 **永久停用** |
