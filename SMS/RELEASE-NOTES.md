# 3.22.0-SMS.2 发布说明 / Release Notes

**中文** | [English](#english)

- 基线 / Base：UAD **1.7.0.0** + duke 的 TAF **3.22.0（Beta6 线）** / duke's TAF 3.22.0 (Beta6 line)
- 版本串 / Version：`3.22.0-SMS.2`
- DLL：`TweaksAndFixes.dll` **892,928 B** / SHA256 `8DAD370B3E4F7D7A…`
- 状态 / Status：**正式版**；后续进入 SMS.3（性能 / AI 自动设计 / 战力与 AI 队列）

---

## 一、这是什么 / What this is

在 duke 的 TAF 3.22.0（Beta6）源码树上，追加一组**战役与舰船缺陷修复、参数体系与 Mk6+ 支持**（`SMS_Hotfix` + 我们对 duke 文件的最小改动）。
不替换任何游戏原始资源，全部通过 Harmony 运行期生效；行为集中在两张 CSV（release 28 项 / DEBUG 104 项）。

An add-on to duke's TAF 3.22.0 (Beta6) providing **campaign/ship bug fixes, a parameter framework and Mk6+ support**.
No game assets are replaced; everything runs via Harmony. Behaviour lives in two CSVs (28 release / 104 DEBUG entries).

| 组成 / Item | 说明 / Notes |
| --- | --- |
| `TweaksAndFixes.dll` | TAF 3.22.0 + SMS 定制核心 / TAF 3.22.0 + SMS custom core |
| `SMS_UAD_TAF_CustomizedCore.config.csv` | **28 项**，`ALL` 级 / 28 entries, `ALL` level |
| `TAFData\SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv` | **104 项**，含 DEBUG/EXPERIMENTAL / 104 entries incl. debug hooks |
| `TAFData\params-report.md` | 全参数总表 / full parameter report |

---

## 二、相对 duke TAF 3.22.0（Beta6）的改动 / Changes vs. duke's TAF 3.22.0 (Beta6)

### 2.1 修改的 duke 官方文件（主要）/ Modified upstream files (main)

| 文件 / File | 改动 / Change | 原因 / Why |
| --- | --- | --- |
| `Harmony/Ship.cs` | `RefreshHull` 重写：`middlesBase == null` 交回原版；`UpdateSections` 先校验后重建（含回退前清空） | 修 mod 船体崩溃 + "某舯段变化时有时无" / fix mod-hull crashes and nondeterministic sectioning |
| `Utils/Database.cs` | 四张科技表按**数据**定尺寸（`gun` / `torp_mark` / `torpedo_tubes`）；写侧边界检查；读侧按表长 | 修 Mk6/Mk7/多联装鱼雷越界崩溃 / fix out-of-range crashes for Mk6+ |
| `Data/GradeExtensions.cs` | 新增 `ReapplyGradeData()`；`Utils/ModUtils.cs` 的 `FillGradeData` 改为**逐列、按有效性**回滚 | Mk6 缺列时只补缺的那一列（缺模型→沿用上一级） / per-column fallback |
| `Data/GradeColumns.cs`（新增） | 读取 `guns.csv` / `partModels.csv` 的 `_6+` 列写入游戏字典 | 原版只认 `_1.._5` 字段 / vanilla only reads grades 1–5 |
| `Modified/UiM.cs` | `UpdateShipTypeButtons` 的 `SSCDropdown == null` 兜底 | 数据库加载中断时不再刷 NRE / stop NRE spam |
| `Harmony/GameData.cs` | **共享设计校验补齐**（Hull / ShipType / Player / Part / **Tech** + null 检查 + 修正 `break`）；无效设计在启动时移除（= 屏蔽） | 缺科技的设计会在进入共享设计时崩溃（`no such TechnologyData from save`） |
| 其余 SMS 文件 | 见 `SMS_Hotfix/`（模块化 hotfix，参数见 `params-report.md`） | — |

### 2.2 SMS.2 周期内玩家可感知的修复 / Player-facing fixes in SMS.2

| 项 / Item | 说明 / Notes |
| --- | --- |
| 命中率 1% 硬下限 / 1% hit-chance floor | 可调，默认 0.1%（`hit_chance_floor`） |
| 科技—部件限制 / tech ↔ part limits | 共享设计 / 自定义战斗按设计自身科技检查 `needunlock` |
| Mk6+ 主炮 / Mk6+ guns | 射表、模型、缩放、倍径、炮重全部从 mod 的 `_6+` 列读取；缺列逐列回退 |
| 舯段色差 / mid-hull colour band | 原版 9 个重叠反射探针，已修（`constructor_probe_mode=2`） |
| 构造器光照 / constructor lighting | 首次生效；`oceanGraphics.csv` 同理 |
| 改造/吨位/设计状态 / refit-tonnage-design | 默认修复 |
| 命中贴花 / decals | 按口径缩放（鱼雷 ×0.25） |
| 甲板装饰 / deck decor | 回到原版初始化 |
| 航迹/雷迹 / wakes | 己方航迹修复；敌方鱼雷未发现不显示 |
| 叛乱 / rebellion | 显示=结算（`rebellion_chance_mode=2`） |
| 海面模式 / ocean modes | 菜单/港口/战斗分别应用 |
| 参数体系 / parameters | 五级分类 + 场景分组 + 双语说明 + 报表 |

### 2.3 参数变化 / Parameter changes

- 与上一测试包 rc38 **完全一致**：release **28** / DEBUG **104**；级别分布 `ALL 28 / DEBUG 53 / EXPERIMENTAL 23 / RETIRE 12 / REMOVED 22`。
- 本版本**没有**新增或删除参数键（Mk6+ 相关全部是代码路径，不暴露新开关）。

### 2.4 代码结构 / Code layout

- `Harmony/`：duke 原文件 + 我们对它的改动（与 duke 目录保持同名同位置）
- `SMS_Hotfix/Modules/`：SMS 自有模块（按场景拆分）
- `Data/GradeColumns.cs`：Mk6+ 列读取（新增，见 §2.1）

---

## 三、验证与校验值 / Verification & checksums

| 项 / Item | 值 / Value |
| --- | --- |
| 游戏版本 / Game | UAD 1.7.0.0 |
| MelonLoader | 0.7.3 |
| DLL 大小 / size | 891,904 B（rc40） |
| SHA256（前 16） | `25B2E2E75E2D2553` |
| 实测环境 / Tested with | 原版 + DIP + Silly 的 mod 套件 / vanilla + DIP + Silly's mod pack |

自检要点 / self-checks:

1. 日志出现 `Loaded database and config`，无 `Exception`；
2. 1950+ 主炮/副炮可正常打开，Mk6 炮塔模型与 `partModels` 一致（`verbose=1` 时可见 `[SMS.3.GRADE]` 调试行）；
3. 共享设计构造器切换设计不刷 NRE；
4. 大吨位船体常规情况无舯段色带（极端个例见 §四）。

---

## 四、已知限制 / Known limitations

| 限制 / Limitation | 说明 / Notes |
| --- | --- |
| 非原版舰种的预制设计路径 / predef path with modded ship types | 设 `taf_force_no_predef_designs=1` 规避 |
| 共享设计指纹去重（T6）/ shared-design dedupe | 尚未实现，延后 SMS.3 |
| 超大吨位偶发舯段色带 / colour band on extreme hulls | 仅个别原版船体、极大吨位且额外舯段时出现，疑与造船厂水下岸底碰撞有关（SMS3-25） |
| 部分参数只在 DEBUG 配置 / some params DEBUG-only | 见 `params-report.md` 的"级别"列 |

---

## 五、范围：我们明确**不**做的事 / Scope: what we deliberately do not do

| 项 / Item | 原因 / Why |
| --- | --- |
| 重写游戏数据（VP 的 `params` 类改造）/ rewriting data tables | 与数据治理方法论冲突，两侧都拒绝 |
| 上层建筑"无限制"模式 / unrestricted superstructure | 开发者作弊项 |
| 水雷部件隐藏、早期鱼雷限制等 / mine-part hiding, early torpedo limits | modder 用 `compTypes` / `need()` 实现，不走代码 |

---

<a id="english"></a>
# English

- Base: UAD **1.7.0.0** + duke's TAF **3.22.0 (Beta6 line)**
- Version: `3.22.0-SMS.2`
- DLL: `TweaksAndFixes.dll` **892,928 B** / SHA256 `8DAD370B3E4F7D7A…`
- Status: **release**; next cycle is SMS.3 (performance / AI autodesign / combat power & AI queue)

## 1. What this is

An add-on to duke's TAF 3.22.0 (Beta6): **campaign/ship bug fixes, a parameter framework, and Mk6+ support**, applied at runtime via Harmony; no game assets replaced. Behaviour lives in two CSVs (28 release / 104 DEBUG).

## 2. Changes vs. duke's TAF 3.22.0 (Beta6)

| File | Change | Why |
| --- | --- | --- |
| `Harmony/Ship.cs` | `RefreshHull` rewrite: bail out when `middlesBase == null`; `UpdateSections` validates before rebuilding (and clears before fallback) | fix mod-hull crashes and nondeterministic sectioning |
| `Utils/Database.cs` | tech tables sized **from data** (`gun` / `torp_mark` / `torpedo_tubes`); write-side bounds checks; read-side uses table lengths | fix Mk6/Mk7/multi-tube torpedo overflows |
| `Data/GradeExtensions.cs` | new `ReapplyGradeData()`; `FillGradeData` now does **per-column, validity-aware** fallback | only missing columns fall back |
| `Data/GradeColumns.cs` (new) | reads `_6+` columns of `guns.csv` / `partModels.csv` into the game dictionaries | vanilla only reads grades 1–5 |
| `Modified/UiM.cs` | `SSCDropdown == null` guard | stop NRE spam after an aborted data load |
| others | see `SMS_Hotfix/` modules and `params-report.md` | — |

### Player-facing fixes in SMS.2

Tunable 1% hit-chance floor (default 0.1%); tech↔part limits outside campaigns; full Mk6+ gun support from mod CSVs; mid-hull colour-band fix; constructor/preview lighting; refit/tonnage/design repairs; calibre-scaled hit decals; vanilla deck-decor init; wake fixes; rebellion display=resolution; ocean modes; five-level parameter framework.

### Parameters

Unchanged from testing package rc38: release **28**, DEBUG **104**; **no new keys** — all Mk6+ work is code-side.

## 3. Verification

UAD 1.7.0.0, MelonLoader 0.7.3, tested with vanilla + DIP + Silly's mod pack. Self-checks: no exceptions at load; Mk6 guns open at 1950+ and their turret model matches `partModels`; shared-design constructor does not spam NREs; no colour band on regular heavy hulls.

## 4. Known limitations

Predefined-design path may crash with modded ship types (set `taf_force_no_predef_designs=1`); shared-design T6 dedupe not implemented; occasional colour band on one extreme hull (tracked for SMS.3); some parameters are DEBUG-only.

## 5. Scope

We deliberately do not rewrite game data tables, enable developer "unrestricted" cheats, or re-implement modder-side restrictions (mines/early torpedoes) in code.
