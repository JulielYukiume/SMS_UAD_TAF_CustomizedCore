# 待办 · 收口前必须回头处理的事项（备忘）

> 建立于 2026-10-25。**发版（3.22.0-SMS.1 或 3.21.1-SMS.2）之前逐条过一遍。**

---

## 1. `tonnage_repair` 默认改为 1（收口前必须做）

| 项 | 内容 |
| --- | --- |
| 现状 | `tonnage_repair = 0`（默认关闭，只报告不修改） |
| 收口前 | **改为默认 1**，用于修复 3.21.1-SMS.1 期间 E 模块写坏的历史存档 |
| 依据 | E 的 `CalcWeightAndCost` 曾把 `ship.tonnage` 写成「设计吨位 × beamDraughtBonus」；已确认 44 条受影响（1940-04 赔偿舰批次为主） |
| 已验证 | `tonnage_repair=1` 一次读档即可回填为 `design.tonnage`，再读档 `mismatched=0` |
| 注意 | 回填只对"实存船（designId != empty）且与设计不一致"的条目生效；`status`/`designId` 不动 |
| 提醒 | ⚠️ 这条**必须等其它功能都收口后**再切默认，避免修复与后续调试互相干扰 |

**切换位置**：`TweaksAndFixes/SMS_Hotfix/HotfixParams.cs` → `TonnageRepair = false` → `true`。

---

## 2. E 模块（改造费用/状态刷新）待复核项

用"已修吨位的 1940 中华档"复核：

| # | 检查点 | 期望 |
| --- | --- | --- |
| 1 | 改造一艘普通船 | 舰队列表费用/维护费立刻变为新设计值，`[SMS.1.E]` 日志正常，无 `install failed` |
| 2 | 改造时改动吃水/船宽 | `[SMS.1.TON]` 不新增 MISMATCH（tonnage 恒等于 design） |
| 3 | 反复改造同一艘船 | 费用不会逐次漂移；吨位不漂移 |
| 4 | 改造后读档再改造 | 仍正常（E 的延迟重算只在改造事件触发） |
| 5 | 与 3.22 的 `RefitShipsStart` Prefix 共存 | 只有我们的 Prefix 清缓存；duke 的即时 `CalcWeightAndCost` 已禁用（`RefitShipsStartFix` 里早退） |

---

## 3. 已确认修复、需长期守护的"直接改 TAF 实现"项

| 项 | 文件 | 检查方式 |
| --- | --- | --- |
| 赔偿舰越狱（`BuildShip.interactable` 无条件放行） | `TweaksAndFixes/Harmony/CampaignFleetWindow.cs` | `grep -n "BuildShip.interactable"` 必须含 `!isReparationDesign` |
| 设计 status 归一化（新改造设计=0；赔偿设计=0；Erased=7 保留） | `SMS_Hotfix/Modules/DesignStatusAudit.cs` + `Harmony/RefitShipsStartFix.cs` | 读档日志 `[SMS.1.STA] summary` |

详见 `docs/58-reparation-design-lockdown.md`。

---

## 4. 3.22 特性移植进度（截至 port23）

| 已移植 | 说明 |
| --- | --- |
| 装饰件碰撞 v3 | `Harmony/DecorFix.cs` |
| `Ship.RefreshHull` 完整重写 | `Harmony/ShipRefreshHull.cs`（duke 源码 338 行） |
| `hullData.csv` 数据层 | `Data/HullData.cs` + 随包 CSV |
| 设置菜单三项 | Ocean Type / Predef Mode / Shared Mode |
| 动态装甲堡约束 | `Harmony/ShipCitadel.cs` |
| 改造入口状态重置 | `Harmony/RefitShipsStartFix.cs`（Postfix 已禁用即时重算） |
| tonnage 校验/回填 | `SMS_Hotfix/Modules/TonnageCheck.cs` |
| 设计 status 稽核/归一化 | `SMS_Hotfix/Modules/DesignStatusAudit.cs` |
| 赔偿设计禁造 | `CampaignFleetWindow.cs` + `ReparationBuildProbe.cs` |

**未移植（Beta6 独有）**：`CampaignEventSystem`（~1200 IL）、`Patch_Ui.PrintShipDataExhaustive`（3067 IL）、
`Constructor::CanPlaceSoftCached/CheckWarnParts/DelayedUpdateHull/UpdateHull`、`OceanGraphicsData` + 静态海面、
`MakeVisualShip`、`LoadPredefs` 重构等。

---

## 5. 色差（首/尾段错色）问题状态

- 已排除：`hullData`/`sectioning`、`OnRenderersWasChanged` 单点、`RefreshHull` 完整重写；
- 结论：不在上述代码路径，仍需专项排查（记录见 `docs/57` §8）；
- **未解决**，但不阻塞其它功能。
