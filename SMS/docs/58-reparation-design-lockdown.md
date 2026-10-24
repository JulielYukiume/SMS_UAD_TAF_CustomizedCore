# 58 · 赔偿舰"越狱"（赔偿设计可被建造）—— 锁定与恢复手册

> **一句话**：TAF 为了让"从改造设计直接造船"更顺手，在 `TweaksAndFixes/Harmony/CampaignFleetWindow.cs` 的
> `Patch_CampaignFleetWindow.Postfix_Refresh` 里把「建造舰船」按钮**无条件**置为可点，
> 没有判断选中的是不是**赔偿设计**（`isReparationDesign`）。
> 结果：赔偿舰设计（列表里显示为灰字）依然可以点「建造舰船」→ 越权建造。
>
> **修复必须直接改 TAF 实现**（外部 Harmony 补丁改不到这一句），
> 所以每次合并上游 / 重新拉 3.21.1+热修 / 重新融 3.22 之后，都要检查这段是否被覆盖回去。

---

## 1. 症状

| 现象 | 说明 |
| --- | --- |
| 舰队/设计管理列表里，赔偿设计名称显示为**灰字**（游戏本身已标记 `isReparationDesign`） | 正常 |
| 但选中它之后「**建造舰船**」按钮**仍可点击**，能打开建造对话框、选数量 | ❌ 异常 |
| 原版 1.5.1.6 / 1.7 不会出现（原版闸门就在这一层：`BuildShip.interactable` 由选定项决定） | 对照基准 |
| 与存档无关 | 新档同样复现 |

## 2. 根因（上游代码位置）

文件：`TweaksAndFixes/Harmony/CampaignFleetWindow.cs`
方法：`Patch_CampaignFleetWindow.Postfix_Refresh(CampaignFleetWindow __instance)`

```csharp
// ❌ 上游原样（无条件放行）
if (G.ui.FleetWindow.selectedElements.Count > 0)
    G.ui.FleetWindow.BuildShip.interactable = true;
```

TAF 3.22 的意图是"允许从改进设计直接造船"（降低设计管理繁琐度），但**漏掉了赔偿标记的检查**。
因为这段是 TAF **自己重写的 UI 刷新逻辑**，用 Harmony 外部 Postfix 无法精确覆盖
（我们在 `ReparationBuildProbe` 里拦 `CanBuildShipsFromDesign` / `BuildShipsFromDesign` 只是**执行层兜底**，
UI 仍然会显示可点、能选数量），所以必须直接改源码。

## 3. 修复（SMS 固化版本）

```csharp
// ✅ SMS 版本（2026-10-25 固化）
if (G.ui.FleetWindow.selectedElements.Count > 0)
{
    bool isReparationDesign = false;
    try
    {
        Ship selected = G.ui.FleetWindow.selectedElements[0].CurrentShip;
        if (selected != null)
            isReparationDesign = selected.isReparationDesign;
    }
    catch { }

    G.ui.FleetWindow.BuildShip.interactable = !isReparationDesign;
}
```

效果：
- 选中**赔偿设计** ⇒ 按钮**不可点**（与原版一致）；
- 选中**普通设计 / 改造设计** ⇒ 保持 TAF 的便利（可建造）。

**位置**：`Postfix_Refresh` 内，紧随 `Delete.interactable` 那段之后（约 L79-80）。
**识别标记**：代码上方带注释 `// ★ [SMS.1.REP]`，便于 `grep` 找回。

## 4. 为什么会被"冲掉"

| 场景 | 说明 |
| --- | --- |
| 重新从 3.21.1 + 热修层开始 | 热修层文件列表里若不含 `CampaignFleetWindow.cs`，就会用上游原版 |
| 重新引入 3.22.0 特性（合并/覆盖） | 上游该文件被整体覆盖 |
| 代码行号漂移 | 上游在附近插入代码 → 行号变化，容易漏看 |

⇒ 因此本改动**必须作为"直接修改 TAF 实现"的条目登记**，而不是当作普通热修模块。

## 5. 回归检查清单（每次合并后跑一遍）

1. `grep -n "BuildShip.interactable" TweaksAndFixes/Harmony/CampaignFleetWindow.cs`
   - 期望看到 `!isReparationDesign`；
   - 若看到裸的 `= true;` ⇒ **被覆盖了，需重打**。
2. 进游戏 → 舰队管理（或设计管理）→ 选中一个赔偿设计 → 「建造舰船」按钮应为**灰色不可点**。
3. 选中普通设计/改造设计 → 按钮可点（确认没有误伤 TAF 的便利功能）。

## 6. 相关代码（执行层兜底，非必须但保留）

`TweaksAndFixes/SMS_Hotfix/Modules/ReparationBuildProbe.cs`（`[SMS.1.REP]`）：

| 钩子 | 作用 |
| --- | --- |
| `PlayerController.CanBuildShipsFromDesign(Ship, out string)` Postfix | 赔偿设计 ⇒ `false` + reason |
| `PlayerController.CanBuildShipsFromDesign(Ship, int, out string)` Postfix | 同上（**两个重载都要显式挂**，按名字挂会 `Ambiguous match`） |
| `PlayerController.BuildShipsFromDesign(Ship, int, bool, Player)` Prefix | 执行层拒绝，返回空列表（防 UI 绕过） |

参数：`reparation_design_block`（默认 1）、`reparation_build_probe`（默认 0）。

> 注意：这两个重载的存在是**必须显式按签名挂载**的原因 —— 曾经的 `Ambiguous match` 导致探针
> 静默失败、看起来"补丁没生效"。

## 7. 历史

| 时间 | 事件 |
| --- | --- |
| 2026-10-18 之前 | sms.0 已修过一次（当时记录：TAF 的 `CampaignFleetWindow` 无条件放行） |
| 2026-10-19 18:30~19:30 | 确认「改进设计 status 置 0」方案，并确认赔偿设计 `isReparationDesign=true` ⇒ 玩家不可建造 |
| 2026-10-25 | 重新引入 3.22.0 后该修复丢失（行号漂移/文件被覆盖），赔偿舰再次可造；本文件即为**固化记录** |
