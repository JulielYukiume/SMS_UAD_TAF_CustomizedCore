# SMS_UAD_TAF_CustomizedCore

**SMS（熔钢铸权）维护的 [TweaksAndFixes (TAF)](https://github.com/DukeDagor/UADRealismDIP) 分支**
— 为《终极提督：无畏舰》(Ultimate Admiral: Dreadnoughts) 提供战役与舰船相关的缺陷修复。

**中文** | [English](#english)

[![Latest release](https://img.shields.io/github/v/release/JulielYukiume/SMS_UAD_TAF_CustomizedCore?label=release)](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

> **当前版本 / Current: `3.22.0-SMS.2`**（基线 / base = duke 的 TAF **3.22.0 Beta6** 线；UAD **1.7.0.0**，MelonLoader 0.7.3）
> `main` = 发布主线；包、校验值与完整改动见 [**Releases**](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases)。

---

## 3.22.0-SMS.2 要点

| 类别 | 内容 |
| --- | --- |
| **Mk6+ 主炮** | `guns.csv` / `partModels.csv` 的 `_6` 及以上列（射表、模型、缩放、倍径、炮重）会被正确读入（原版只读 grade 1..5）；缺列逐列回落到最近有效等级 |
| **共享设计兼容检查** | 启动时校验每个共享设计的 Hull / ShipType / Player / Part / **Tech**，无效者在列表中被移除（与 duke Beta6 一致），避免进入共享设计崩溃 |
| **命中率** | 原版 1% 硬下限可调（`hit_chance_floor`，默认 0.1%） |
| **科技—部件限制** | 共享设计 / 自定义战斗按设计自身科技检查 `needunlock` |
| **视觉稳定性** | 舯段色差、构造器/预览光照、海面模式 |
| **参数体系** | 五级分类 + 场景分组 + 双语说明 + `params-report.md`（包内 `Mods\TAFData\`） |

## 这个仓库是什么

TAF 是 UAD 的核心修复/扩展框架（作者 NathanKell，现由 DukeDagor 维护）。
本仓库是 **SMS 侧的定制分支**：在官方 TAF 的源码基础上，加入一批"战役数据一致性"与"舰船生命周期"的修复模块，
并保持与上游同步的能力。

> 可以把它理解为：**官方 TAF + 一层可选的热修层**。热修层的每个模块都能用 CSV 单独开关。

### 与官方 TAF 的关系

```
NathanKell/UADRealism          最早的上游
   └── DukeDagor/UADRealismDIP 官方 TAF 主线（我们的基线）
   └── 本仓库                   SMS 分支（official TAF + SMS_Hotfix）
```

- 上游 / Upstream：<https://github.com/DukeDagor/UADRealismDIP>
- 本分支不是"另一个 MOD"，**安装时替换的是 TAF 本身**（`Mods\TweaksAndFixes.dll`）。

## 修什么（按模块族，非版本清单）

| 模块族 | 解决的问题 |
| --- | --- |
| **改造链** | 改造后设计状态异常、改造链丢失、改造费用/位置显示取错设计 |
| **复制与赔偿** | 复制设计后的 Guid/年份/随机名/改造链残留；赔偿舰无法建造 |
| **舰船生命周期** | 维修中的船被"封存"导致进度丢失；沉船/擦除的船残留在列表；丢船保护 |
| **读档一致性** | 读档时丢失的设计重新挂回船；零值船（crew/fuel/ammo 归零）在加载时抢修 |
| **设计生成** | 重复 id 设计重抽；AI 预制设计池回退；真混合（预制 + 随机生成）与每舰种上限 |
| **共享设计** | 无共享设计时按同型回填；**启动时兼容性校验**（Hull/ShipType/Player/Part/Tech，无效者移除） |
| **数据 / 科技** | **Mk6+ 主炮数据链路**（`_6+` 列读取）；科技表按数据定尺寸（支持 Mk6/Mk7/多联装鱼雷） |
| **非原版舰种** | UI 序号映射（让 `cb`/`av` 等 mod 舰种能在设计器里被选中、建造） |
| **体验/兼容** | 强制关闭预制设计时的界面锁定、已知日志噪音抑制、作弊菜单科技修复 |

每个模块的详细说明、开关键名与已知限制，见：

- 📄 发布包内 `Mods\README.md` 与 `Mods\RELEASE-NOTES.md`（玩家向说明 + 相对 duke 的逐文件改动）
- 📄 发布包内 `Mods\TAFData\params-report.md`（全参数总表：级别 / 默认值 / 模块 / 代码路径）
- 📄 [SMS/RELEASE-NOTES.md](SMS/RELEASE-NOTES.md) — 3.22.0-SMS.2 发布说明（= 包内同文件）
- 📄 [SMS/DIAGNOSTICS.md](SMS/DIAGNOSTICS.md) — 诊断/详细日志开关与排查顺序
- 📄 [SMS/docs/](SMS/docs/) — 仅保留**代码注释引用**的少数几篇（历史资料）

## 安装

1. 先装 **MelonLoader 0.7.x**（<https://github.com/LavaGang/MelonLoader>）。
2. 到 [**Releases**](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases) 下载最新发布包（zip）。
3. **把 ZIP 里的 `Mods` 文件夹合并到游戏根目录的 `Mods`**（`Ultimate Admiral Dreadnoughts\Mods\`）：
   - 不要再套一层 `Mods\Mods`；
   - 若解压出 `TweaksAndFixes_..._for_UAD_1.7.0.0\Mods\...`，把内层 `Mods` 移到游戏根目录。
4. `Mods\Default_Files\` 是**可选**的参考/覆盖数据（TAF 默认 CSV、游戏原始 CSV 与语言文件、参数表 xlsx）：不需要可整个删掉；覆盖时**只复制单个文件**（整目录复制语言文件会退回原版文本）。
5. 启动游戏，检查 `MelonLoader\Latest.log` 是否有 `TAF-3.22.0-SMS.2` 与 `[SMS.*]` 行。

## 配置

| 文件 | 用途 |
| --- | --- |
| `Mods\SMS_UAD_TAF_CustomizedCore.config.csv` | **主配置（release，28 项）**：只含"所有配置都应默认包含"的 `ALL` 级参数 |
| `Mods\TAFData\SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv` | **DEBUG 配置（104 项，不自动加载）**：需要精调时复制到 `Mods\` 覆盖同名文件 |

- 值 `1` = 启用，`0` = 关闭；`mode,apply` 实际生效，`mode,log` 只观察不写入；
- 改了配置**不需要重新编译**，重启游戏即可；
- 参数级别（`ALL` / `DEBUG` / `EXPERIMENTAL` / `RETIRE` / `REMOVED`）见 `params-report.md`。

## 构建（开发者）

需要：UAD 1.7.0.0 + MelonLoader 的 `Il2CppAssemblies`、.NET 6 SDK。

```powershell
$env:UAD_PATH = 'E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\'
dotnet build TweaksAndFixes\TweaksAndFixes.csproj -c Release
# 产物：TweaksAndFixes\bin\Release\net6.0\TweaksAndFixes.dll
```

## 仓库结构

```
TweaksAndFixes/               官方 TAF 源码 + 我们的改动
  SMS_Hotfix/                 热修层（Bootstrap / HotfixParams / HotfixLog / Modules）
  Harmony/ Modified/ Data/ …  官方源码（其中 4 个文件被我们修改，见 RELEASE-NOTES）
SMS/                          本分支的配置与文档
  config/                     两张 CSV
  docs/                       专项调查与说明
.github/                      上游的 CI 配置
```

分支约定：

| 分支 | 用途 |
| --- | --- |
| `main` | 发布主线（当前为 SMS 版本） |
| 临时分支 / PR | 单个修复或实验 |

同步上游：

```bash
git remote add upstream https://github.com/DukeDagor/UADRealismDIP.git
git fetch upstream
# 按需 cherry-pick / 手动合并上游的修复
```

## 许可与致谢

- 代码许可：**MIT**，见 [LICENSE](LICENSE)（保留上游版权声明）。
- 上游作者：**NathanKell**（TAF 创始）、**DukeDagor**（TAF 主线维护）。
- 本分支维护：**JulielYukiume**（SMS 项目）。
- 本分支的部分修复参考了社区实现（如 `ShipTypesSupport` 的 UI 舰种映射思路），已在对应源码注释中标注。

> 本仓库**只包含代码与配置**；模型、贴图、语言文件等资产属于 SMS MOD 本体，不在此仓库分发。

## 相关链接

- SMS MOD（NexusMods）：<https://www.nexusmods.com/ultimateadmiraldreadnoughts/mods/51>
- 官方 TAF 上游：<https://github.com/DukeDagor/UADRealismDIP>
- 更早的上游：<https://github.com/NathanKell/UADRealism>
- MelonLoader：<https://github.com/LavaGang/MelonLoader>

---

<a id="english"></a>
# English

**SMS fork of [TweaksAndFixes (TAF)](https://github.com/DukeDagor/UADRealismDIP)** — campaign/ship bug fixes, a parameter framework and Mk6+ support for *Ultimate Admiral: Dreadnoughts*.

> **Current: `3.22.0-SMS.2`** (based on duke's TAF **3.22.0 Beta6** line; UAD **1.7.0.0**, MelonLoader 0.7.3).
> Packages, checksums and the full change list are on [**Releases**](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases).

TAF is the core fix/extension framework for UAD (created by NathanKell, now maintained by DukeDagor). This repository
adds an optional hotfix layer on top of the official TAF source and keeps the ability to sync with upstream.

### What it fixes (by module family)

- **Refit chain** — design status after refit, lost chains, wrong design used for refit cost/position display.
- **Copy & reparation** — new GUID/year/random name, cleared refit chain, reparation ships made buildable.
- **Ship lifecycle** — repairing ships no longer mothballed away, erased/sunk/scrapped ships removed from lists, ship-loss guard.
- **Save/load consistency** — lost designs re-linked to ships on load; zeroed ships repaired while loading.
- **Design generation** — duplicate-id redraw, predefined-pool alias fallback, "true mixed" generation with a per-type cap.
- **Shared designs** — backfill by hull likeness; **startup compatibility validation** (Hull/ShipType/Player/Part/Tech, invalid entries removed).
- **Data / tech** — **Mk6+ gun pipeline** (`_6+` columns of `guns.csv` / `partModels.csv`); tech tables sized from data (Mk6/Mk7/multi-tube torpedoes).
- **Non-vanilla ship types** — UI index mapping so modded types (`cb`, `av`, …) can be selected and built.
- **QoL / compatibility** — locked predef toggle when predefs are forced off, log-noise suppression, cheat-menu tech fix.

See [SMS/RELEASE-NOTES.md](SMS/RELEASE-NOTES.md) for the current release notes, [SMS/DIAGNOSTICS.md](SMS/DIAGNOSTICS.md) for the debug/verbose switches, and [SMS/docs/](SMS/docs/) for the few source-referenced design notes.

### Install

1. Install **MelonLoader 0.7.x**.
2. Download the latest zip from [**Releases**](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases).
3. **Merge the inner `Mods` folder into the game's `Mods`** (`Ultimate Admiral Dreadnoughts\Mods\`) - do not create `Mods\Mods`;
   if the extractor produced `TweaksAndFixes_..._for_UAD_1.7.0.0\Mods\...`, move that inner `Mods` up to the game root.
4. `Mods\Default_Files\` is optional reference/override data (TAF defaults, vanilla CSVs and language files, parameter xlsx):
   delete it if unused; copy only individual files when overriding (copying all `.lng` files reverts localisation to vanilla).
5. Check `MelonLoader\Latest.log` for `TAF-3.22.0-SMS.2` and `[SMS.*]` lines.

### Config

`Mods\SMS_UAD_TAF_CustomizedCore.config.csv` is the release config (28 entries, `ALL` level; `1` = on, `0` = off, `mode,apply|log`).
`Mods\TAFData\SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv` holds 104 debug/experimental entries (copy over the release file when needed).
No rebuild needed — just restart.

### Build

```powershell
$env:UAD_PATH = 'E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\'
dotnet build TweaksAndFixes\TweaksAndFixes.csproj -c Release
```

### License

MIT — see [LICENSE](LICENSE). Upstream authors: **NathanKell**, **DukeDagor**. SMS fork maintained by **JulielYukiume**.
This repository contains code and configuration only; models, textures and localisation assets belong to the SMS mod itself.
