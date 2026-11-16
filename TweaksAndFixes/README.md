# SMS_UAD_TAF_CustomizedCore

**SMS（熔钢铸权）维护的 [TweaksAndFixes (TAF)](https://github.com/DukeDagor/UADRealismDIP) 分支**
— 为《终极提督：无畏舰》(Ultimate Admiral: Dreadnoughts) 提供战役与舰船相关的缺陷修复、参数体系与少量机制扩展。

**中文** | [English](#english)

> **当前状态：`3.22.0-SMS.2`（正式版，基线 = duke 的 TAF 3.22.0 Beta6 线）**
> `main` 分支 = 最新发布代码；包与校验值见 Releases 与包内 `RELEASE-NOTES.md`。

---

## 这个仓库是什么

TAF 是 UAD 的核心修复/扩展框架（作者 NathanKell，现由 DukeDagor 维护）。
本仓库是 **SMS 侧的定制分支**：在 TAF 3.22.0 源码基础上，加入一批"战役数据一致性"、"舰船生命周期"、"视觉/战斗表现"修复，
并补上了 **Mk6+ 主炮的数据链路**（原版只读 grade 1..5，我们补读 mod 的 `_6+` 列）。

> 可以理解为：**官方 TAF + 一层可选的热修层**。热修层每个模块都能用 CSV 单独开关。

### 与官方 TAF 的关系

```
NathanKell/UADRealism          最早的上游
   └── DukeDagor/UADRealismDIP 官方 TAF 主线（我们的基线：3.22.0 Beta6 线）
   └── 本仓库                   SMS 分支（official TAF + SMS_Hotfix + Mk6+ 支持）
```

- 上游 / Upstream：<https://github.com/DukeDagor/UADRealismDIP>
- 本分支不是"另一个 MOD"，**安装时替换的是 TAF 本身**（`Mods\TweaksAndFixes.dll`）。

## 3.22.0-SMS.2 的要点

| 类别 | 内容 |
| --- | --- |
| **Mk6+ 主炮** | `guns.csv` / `partModels.csv` 的 `_6` 及以上列（射表、模型、缩放、倍径、炮重）会被正确读入；缺列逐列回落到最近有效等级 |
| **命中率** | 原版 1% 硬下限可调，默认 0.1%（`hit_chance_floor`） |
| **科技—部件限制** | 共享设计 / 自定义战斗按设计自身科技检查 `needunlock` |
| **视觉** | 舯段色差（反射探针重叠）、构造器/预览光照、空水/海面模式 |
| **稳定性** | 共享设计构造器 NRE、mod 船体 `middlesBase` 空引用、自定义段序重建不确定性 |
| **参数体系** | 五级分类 + 场景分组 + 双语说明 + `params-report.md` |

## 修什么（按模块族）

| 模块族 | 解决的问题 |
| --- | --- |
| **改造链** | 改造后设计状态异常、改造链丢失、改造费用/位置显示取错设计 |
| **复制与赔偿** | 复制设计后的 Guid/年份/随机名/改造链残留；赔偿舰无法建造 |
| **舰船生命周期** | 维修中的船被"封存"导致进度丢失；沉船/擦除的船残留在列表；丢船保护 |
| **读档一致性** | 读档时丢失的设计重新挂回船；零值船（crew/fuel/ammo 归零）在加载时抢修 |
| **设计来源（真混合）** | 预制设计按包优先级 + 年份窗口抽取；科技计数闸；抽不到时兜底补科技；设计指纹去重 |
| **视觉 / 场景** | 舯段色差；空水/海面模式；船体分段材质；浮动高度 |
| **战斗表现** | 敌雷雷迹剧透；舰船航迹；命中贴花按口径缩放 |
| **数据 / 科技** | Mk6+ 主炮数据链路；科技表按数据定尺寸（支持 Mk6/Mk7/多联装鱼雷） |
| **UI / 交互** | 设置面板；共享设计构造器稳定性 |
| **日志 / 诊断** | 统一日志前缀（`[SMS.1.` / `[SMS.2.` / `[SMS.3.`）、英文日志、探针开关（`verbose` / `*_probe`） |

## 安装

1. 需要 **MelonLoader 0.7.x**（0.7.3 验证）。
2. 从 Releases 下载 `3.22.0-SMS.2` 的 ZIP，**把里面的 `Mods` 文件夹合并到游戏根目录的 `Mods`**（`Ultimate Admiral Dreadnoughts\Mods\`，不要再套一层 `Mods\Mods`）。
3. `Mods\Default_Files\` 是可选的参考/覆盖数据（TAF 默认 CSV、游戏原始 CSV 与语言文件、参数表 xlsx），不需要可整个删除；覆盖时只复制单个文件。
4. 首次启动较慢（TAF 读预定义设计库），之后正常。

## 参数

- release 配置 **28 项**（`SMS_UAD_TAF_CustomizedCore.config.csv`），绝大多数修复默认生效、无需调整；
- DEBUG 配置 **104 项**（`TAFData\SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv`），含探针与实验项；
- 全参数总表：`TAFData\params-report.md`。

## 反馈

- Issue / Discussion 均可；请附 `MelonLoader\Latest.log` 与复现步骤；
- 上游问题（与 duke 的 TAF 本身相关）请先对照包内 `RELEASE-NOTES.md` 的"相对上游改动"一节。

---

<a id="english"></a>
# English

**SMS's fork of [TweaksAndFixes (TAF)](https://github.com/DukeDagor/UADRealismDIP)** — campaign/ship bug fixes, a parameter framework and a few mechanical extensions for *Ultimate Admiral: Dreadnoughts*.

> **Current: `3.22.0-SMS.2` (release; base = duke's TAF 3.22.0 Beta6 line)** — install replaces TAF itself (`Mods\TweaksAndFixes.dll`).

### Highlights

| Area | What |
| --- | --- |
| **Mk6+ guns** | `_6+` columns of `guns.csv` / `partModels.csv` (tables, models, scales, caliber length, weight) are loaded correctly; missing columns fall back per-column to the nearest valid grade |
| **Hit chance** | the hard-coded 1% floor is tunable (default 0.1%) |
| **Tech ↔ part limits** | shared designs / custom battles honour the design's own techs |
| **Visuals** | mid-hull colour band, constructor/preview lighting, ocean modes |
| **Stability** | shared-design constructor NREs, mod hulls with empty `middlesBase`, deterministic section rebuilds |
| **Parameters** | five levels, scenario groups, bilingual docs, `params-report.md` |

### Install

MelonLoader 0.7.x. Unzip a release and **merge its inner `Mods` folder into the game's `Mods`** (`Ultimate Admiral Dreadnoughts\Mods\`) - do not create `Mods\Mods`.
`Mods\Default_Files\` is optional reference/override data (TAF defaults, vanilla CSVs and language files, parameter xlsx); delete it if unused and copy only individual files when overriding.
First launch is slower while TAF loads the predefined design library.

### Feedback

Open an issue with `MelonLoader\Latest.log` and reproduction steps; check the package's `RELEASE-NOTES.md` for the upstream-vs-fork change list first.
