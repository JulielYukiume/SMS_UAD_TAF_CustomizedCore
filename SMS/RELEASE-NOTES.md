# 3.21.1-SMS.1 发布说明 / Release Notes

**中文** | [English](#english)

- 基线 / Base：UAD **1.7.0.0** + TAF (TweaksAndFixes) **3.21.1**（官方源码 / official source）
- 版本串 / Version：`3.21.1-SMS.1`
- DLL：`TweaksAndFixes.dll` **756,736 B** / SHA256 `B9CF79C72C532E7A…`
- 状态 / Status：**已冻结 / frozen**（2026-10-24）；后续修复进入 `3.22.0-SMS.1` / `SMS.2`

---

## 一、这是什么 / What this is

在官方 TAF 3.21.1 上追加一组**战役与舰船缺陷修复**（`SMS_Hotfix`），不改动游戏原始资源，
全部通过 Harmony 在运行时生效；行为开关集中在两张 CSV。

An add-on to official TAF 3.21.1 providing a set of **campaign / ship bug fixes** (`SMS_Hotfix`).
No game assets are modified; everything is applied at runtime via Harmony. Behaviour is controlled by two CSVs.

| 组成 / Item | 说明 / Notes |
| --- | --- |
| `TweaksAndFixes.dll` | 官方 TAF 3.21.1 + 28 个 hotfix 模块 / official TAF + 28 hotfix modules |
| `SMS_UAD_TAF_CustomizedCore.config.csv` | **26 项**，玩家可调 / 26 entries, player-facing |
| `Default_Files\TAFHotfix-Batch1.config.csv` | **34 项**（26 + 8 诊断，默认 0）/ 34 entries incl. 8 diagnostics (all 0) |

---

## 二、相对官方 TAF 3.21.1 的改动 / Changes vs. official TAF 3.21.1

### 2.1 修改的官方文件（4 个）/ Modified official files (4)

| 文件 / File | 改动 / Change | 原因 / Why |
| --- | --- | --- |
| `TweaksAndFixes.cs` | 版本串 → `3.21.1-SMS.1`；`OnInitializeMelon` 调用 `Bootstrap.Install` | 载入 hotfix / load hotfix |
| `TweaksAndFixes.csproj` | 增加 `UnityEngine.AudioModule` 引用 | 官方源码缺此引用（`SceneManager.cs` 用 `AudioSource`）/ official project misses it |
| `Modified/UiM.cs` | 自定义战斗"预制设计"按钮在 `ForceNoPredefsInNewGames` 时**默认禁用/不可点/置灰** | 官方该参数只声明未使用；非原版舰种走预制会越界崩溃 / predef path crashes with modded ship types |
| `Harmony/CampaignController.cs` | `OnNewTurn` 清理里 `(Ship)vessels[j]` → `is Ship` 类型检查 | `Vessels` 含非 `Ship` 实体（潜艇）⇒ `InvalidCastException` 中断清理（"卖掉的船不消失"）/ cast exception aborted cleanup |

### 2.2 新增 `SMS_Hotfix/`（34 文件 / 28 模块）/ Added directory

| 标签 / Tag | 模块 / Module | 修复内容 / Fix |
| --- | --- | --- |
| `A` | RefitChainFix | 改造链重建 + 设计 status 归一化 / rebuild refit chain, normalise design status |
| `A2b` | ReparationCopyFix | 复制设计：新 Guid / 当年份 / 随机名 / 清改造链 / 赔偿件转可造 |
| `A2` | ReparationMothball | 赔偿舰 → 非手动封存 / reparation ships → non-manual mothball |
| `B` | ShipLossGuard | 否决"维修中→封存" + 进度快照回写 + 擦除诊断 |
| `B` | SunkGraceWindow | 沉船宽限窗口（默认 6 回合）/ sunk grace window |
| `B` | DuplicateDesignGuard | 重复 id 设计重抽（换桶 → 最后抽中） |
| `B2` | ShipDesignRelink | 读档时按指纹把设计重挂回船（一次性） |
| `C3` | ZeroedShipRepair | 读档抢修零值船（`Priority.First+100`） |
| `E` | RefitCostRefresh / RefitCostDisplay | 改造中船的显示费用/位置取改造后设计 |
| `F` | AiRefitRestore | AI 改造位置/朝向保存与还原 |
| `K` | KeepLastDesign | 保护最后一份/在役设计 |
| `G` | TrueMixed | 真混合：强制预制分支 + 每舰种上限（默认 6） |
| `S` | SharedBackfillBind | 无共享设计时按同型回填 |
| `P` | PredefPoolProbe / PredefForceOff | 预制池别名回退；`force_no_predef_designs=1` 时后端拒绝取预制 |
| `L` | LogSuppress | 抑制已知噪音（预制包报错、.NET9 互操作告警） |
| `T` | CheatTechFix | 作弊菜单研究焦点兜底 |
| `Y2` | ShipTypeSupportApply | 非原版舰种 UI 序号映射（原版 0..5 不动，其余 `20+`） |

诊断模块（默认关，仅批次表开启）/ diagnostics (off by default)：`Y`、`Y3`、`Y5`、`H` 及若干早期探针。

---

## 三、关键结论（为什么这么做）/ Key findings

| 结论 / Finding | 证据 / Evidence |
| --- | --- |
| `type()` 解析正常（含非原版）/ hull `type()` is parsed correctly | `[H] summary: hulls=580 shipTypeNull=0 typeMismatch=0` |
| 原版 `GetIntNumberFromShipType` 未知舰种返回 **0（bb）**，不是 -1 | `[Y2] 'cb' 0 -> 21 (override non-vanilla)` |
| 扩展 int 会踩 TAF 固定 6 舰种列表 ⇒ 越界 | `ArgumentOutOfRangeException … List.set_Item`（55ms 后） |
| `IsPartAvailable` 放行会破坏船体筛选 ⇒ 不采用 | 1513 次调用全 false 的实测 |
| 虚方法双补丁 ⇒ Il2Cpp 自递归 `StackOverflow` | 栈 `DM1Cpp.Submarine:Mothball1 ↔ managed` |

---

## 四、已知限制 / Known limitations

| 限制 / Limitation | 规避 / Workaround |
| --- | --- |
| 非原版舰种 + TAF 预制设计路径会越界 | 设 `force_no_predef_designs=1`（按钮自动锁定）/ set it to 1 |
| 预制池按包报错是噪音，非真缺口 | `suppress_predef_pack_errors=1` |
| 汉化重复键 / FoF 模型清单报错 | 属 FoF 数据侧，由 FoF 修复 |

---

## 五、安装与验证 / Install & verify

1. 备份 / back up 原 `Mods\TweaksAndFixes.dll` 与同名 CSV；
2. 覆盖本包 `Mods\` 下同层文件 / overwrite files in `Mods\`；
3. 启动日志应包含 / expect in log：

```
[SMS.1.A] RefitChainFix: patched …
[SMS.1.Y2] ShipTypeSupportApply v3: armed …
[SMS.1.PF] predef force-off armed …
[SMS.1.L] LogSuppress: armed on 3 Error + N Warning overload(s)
```

4. 冒烟测试 / smoke test：改造（链/费用/位置）、复制设计、AI 改造、重复设计、沉船、读档重挂、真混合、非原版舰种（共享设计）、过回合（船列表刷新）。

---

## 六、校验值 / Checksums

| 文件 / File | 大小 / Size | SHA256（前16 / first 16） |
| --- | --- | --- |
| `TweaksAndFixes.dll` | 756,736 B | `B9CF79C72C532E7A` |
| `SMS_UAD_TAF_CustomizedCore.config.csv` | 2,818 B | `E942B127C2A9EC80` |
| `Default_Files\TAFHotfix-Batch1.config.csv` | 3,529 B | `75E1178F7CC308D5` |

---

<a id="english"></a>
# English

- Base: UAD **1.7.0.0** + official TAF **3.21.1** source
- Version: `3.21.1-SMS.1`; frozen 2026-10-24; further fixes go to `3.22.0-SMS.1` / `SMS.2`.

## Modified official files (4)

1. `TweaksAndFixes.cs` — version string + `Bootstrap.Install(HarmonyInstance)`.
2. `TweaksAndFixes.csproj` — add `UnityEngine.AudioModule` reference (required by `Harmony/SceneManager.cs`; missing upstream).
3. `Modified/UiM.cs` — skirmish "predefined designs" toggle is forced **off, non-interactive and greyed out** when `ForceNoPredefsInNewGames` is true (the flag exists upstream but was never used).
4. `Harmony/CampaignController.cs` — `Prefix_OnNewTurn` used `(Ship)vessels[j]`; `CampaignData.Vessels` contains non-`Ship` entities (submarines), causing `InvalidCastException` that aborted the erased/sunk/scrapped cleanup (symptom: sold or scrapped ships stayed in the fleet list until reload). Now uses an `is Ship` type check.

## Added `SMS_Hotfix/` (34 files, 28 modules)

Functional (enabled by default): `A` refit chain rebuild; `A2b` design copy rebuild; `A2` reparation ships → non-manual mothball; `B` ship-loss guard (veto Repairing→Mothballed, restore progress, erase diagnostics), sunk grace window, duplicate-design redraw; `B2` design re-link on load; `C3` zeroed-ship repair on load; `E` refit cost display/refresh; `F` AI refit position restore; `K` keep last design; `G` true-mixed predef branch + per-type cap; `S` shared-design backfill by hull; `P` predef alias fallback + predef force-off; `L` log suppression; `T` cheat-menu tech fix; `Y2` non-vanilla ship-type UI index mapping (vanilla 0..5 untouched, extended from 20).

Diagnostics (off by default, opt-in via `TAFHotfix-Batch1.config.csv`): `Y`, `Y3`, `Y5`, `H` and early probes.

## Key findings

- Hull `type(...)` and `PartData.shipType` are parsed correctly for modded types (`shipTypeNull=0`, `typeMismatch=0`).
- Vanilla `GetIntNumberFromShipType` returns **0 (bb)** for unknown types, not `-1`; hence `cb → 0 → bb` and the "select a different ship type" block. Fix: override non-vanilla names with an extended index (20+).
- Extended indices hit TAF's hard-coded 6-type list and throw `ArgumentOutOfRangeException`; mitigated by forcing predef usage off.
- Patching both `Ship.Mothball` and `Submarine.Mothball` (virtual + override) makes Il2CppInterop recurse until `StackOverflow`; the patch was removed in favour of `manualMothballed`.

## Known limitations

- Modded ship types + TAF predef path: set `taf_force_no_predef_designs = 1` (toggle is locked automatically).
- "has no valid predefined designs" is per-pack noise; suppressed by `suppress_predef_pack_errors=1`.
- Duplicate localisation keys and FoF model-manifest errors come from FoF data, not this package.

## Install

Back up `Mods\TweaksAndFixes.dll` and the CSVs, overwrite the files from this package, then check the log for the `[SMS.1.*]` armed lines. Diagnostics live in `Default_Files\DIAGNOSTICS.md`.
