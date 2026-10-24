# SMS_UAD_TAF_CustomizedCore

**SMS（熔钢铸权）维护的 [TweaksAndFixes (TAF)](https://github.com/DukeDagor/UADRealismDIP) 分支**
— 为《终极提督：无畏舰》(Ultimate Admiral: Dreadnoughts) 提供战役与舰船相关的缺陷修复。

**中文** | [English](#english)

[![Latest release](https://img.shields.io/github/v/release/JulielYukiume/SMS_UAD_TAF_CustomizedCore?label=release)](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

---

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
| **共享设计** | 无共享设计时按同型回填 |
| **非原版舰种** | UI 序号映射（让 `cb`/`av` 等 mod 舰种能在设计器里被选中、建造） |
| **体验/兼容** | 强制关闭预制设计时的界面锁定、已知日志噪音抑制、作弊菜单科技修复 |

每个模块的详细说明、开关键名与已知限制，见：

- 📄 [SMS/RELEASE-NOTES.md](SMS/RELEASE-NOTES.md) — 当前发布版本的完整改动清单
- 📄 [SMS/DIAGNOSTICS.md](SMS/DIAGNOSTICS.md) — 诊断开关（仅排障用，默认全关）
- 📄 [SMS/docs/53-shiptype-fixed.md](SMS/docs/53-shiptype-fixed.md) — 非原版舰种的调查与实现
- 📄 [SMS/docs/54-diagnostics.md](SMS/docs/54-diagnostics.md) — 诊断项说明

## 安装

1. 先装 **MelonLoader 0.7.x**（<https://github.com/LavaGang/MelonLoader>）。
2. 到 [**Releases**](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases) 下载最新发布包（zip）。
3. 解压到游戏根目录（`Ultimate Admiral Dreadnoughts\`），覆盖同名文件：
   - `Mods\TweaksAndFixes.dll`
   - `Mods\SMS_UAD_TAF_CustomizedCore.config.csv`
   - `Mods\RELEASE-NOTES.md`
4. 启动游戏，检查 `MelonLoader\Latest.log` 是否出现 `[SMS.*] ... armed` 行。

> 发布包里的 `Default_Files\` 是**可选**的：不装也能正常游戏；需要 TAF 默认数据时再按需复制。

## 配置

| 文件 | 用途 |
| --- | --- |
| `Mods\SMS_UAD_TAF_CustomizedCore.config.csv` | 主配置（玩家可调） |
| `Default_Files\TAFHotfix-Batch1.config.csv` | 批次覆盖配置（优先级更高，含诊断开关） |

- 值 `1` = 启用，`0` = 关闭；`mode,apply` 为实际生效，`mode,log` 只观察不写入。
- 改了配置**不需要重新编译**，重启游戏即可。

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

**SMS fork of [TweaksAndFixes (TAF)](https://github.com/DukeDagor/UADRealismDIP)** — bug fixes for campaign data
consistency and ship lifecycle in *Ultimate Admiral: Dreadnoughts*.

TAF is the core fix/extension framework for UAD (created by NathanKell, now maintained by DukeDagor). This repository
adds an optional hotfix layer on top of the official TAF source and keeps the ability to sync with upstream.

### What it fixes (by module family)

- **Refit chain** — design status after refit, lost chains, wrong design used for refit cost/position display.
- **Copy & reparation** — new GUID/year/random name, cleared refit chain, reparation ships made buildable.
- **Ship lifecycle** — repairing ships no longer mothballed away, erased/sunk/scrapped ships removed from lists, ship-loss guard.
- **Save/load consistency** — lost designs re-linked to ships on load; zeroed ships repaired while loading.
- **Design generation** — duplicate-id redraw, predefined-pool alias fallback, "true mixed" generation with a per-type cap.
- **Shared designs** — backfill by hull likeness when no shared design exists.
- **Non-vanilla ship types** — UI index mapping so modded types (`cb`, `av`, …) can be selected and built.
- **QoL / compatibility** — locked predef toggle when predefs are forced off, log-noise suppression, cheat-menu tech fix.

See [SMS/RELEASE-NOTES.md](SMS/RELEASE-NOTES.md) for the exact change list of the current release,
[SMS/DIAGNOSTICS.md](SMS/DIAGNOSTICS.md) for developer switches, and [SMS/docs/](SMS/docs/) for design notes.

### Install

1. Install **MelonLoader 0.7.x**.
2. Download the latest zip from [**Releases**](https://github.com/JulielYukiume/SMS_UAD_TAF_CustomizedCore/releases).
3. Extract into the game root (`Ultimate Admiral Dreadnoughts\`), overwriting
   `Mods\TweaksAndFixes.dll`, `Mods\SMS_UAD_TAF_CustomizedCore.config.csv`, `Mods\RELEASE-NOTES.md`.
4. Check `MelonLoader\Latest.log` for `[SMS.*] ... armed` lines.

`Default_Files\` inside the archive is optional; copy only what you need.

### Config

`Mods\SMS_UAD_TAF_CustomizedCore.config.csv` is the player-facing config (`1` = on, `0` = off, `mode,apply|log`).
`Default_Files\TAFHotfix-Batch1.config.csv` overrides it and holds the diagnostics switches. No rebuild needed — just restart.

### Build

```powershell
$env:UAD_PATH = 'E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\'
dotnet build TweaksAndFixes\TweaksAndFixes.csproj -c Release
```

### License

MIT — see [LICENSE](LICENSE). Upstream authors: **NathanKell**, **DukeDagor**. SMS fork maintained by **JulielYukiume**.
This repository contains code and configuration only; models, textures and localisation assets belong to the SMS mod itself.
