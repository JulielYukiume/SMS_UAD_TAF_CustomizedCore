# 版权与致谢 / Copyright & Attribution

本仓库是 **TweaksAndFixes (TAF)** 的 SMS 定制分支。TAF 的版权归原作者所有，本项目在其 MIT 许可下继续分发，
并保留 [LICENSE](LICENSE) 中的原始版权声明。

This repository is an SMS fork of **TweaksAndFixes (TAF)**. TAF remains copyright of its original authors and is
redistributed here under the MIT license; the original copyright notice in [LICENSE](LICENSE) is preserved.

---

## 上游 / Upstream

| 项目 | 说明 |
| --- | --- |
| **NathanKell/UADRealism** | TAF 的起源项目（2024） |
| **DukeDagor/UADRealismDIP** | 官方 TAF 主线；本分支的基线源码来自其 `Minor_Relase_3.21.1` |
| 许可证 | MIT（见 [LICENSE](LICENSE)，`Copyright (c) 2024 NathanKell`） |

链接：

- <https://github.com/NathanKell/UADRealism>
- <https://github.com/DukeDagor/UADRealismDIP>

## 本分支 / This fork

| 项 | 说明 |
| --- | --- |
| 维护者 | **JulielYukiume**（SMS / 熔钢铸权 项目） |
| 代码改动 | 4 处官方文件修改 + 新增 `TweaksAndFixes/SMS_Hotfix/`（28 个模块），逐条列于 [SMS/RELEASE-NOTES.md](SMS/RELEASE-NOTES.md) |
| 许可 | 与上游一致：**MIT** |

```text
Copyright (c) 2024 NathanKell            （原始 TAF）
Copyright (c) 2026 JulielYukiume         （SMS 分支的修改与新增部分）
```

## 第三方参考 / Third-party references

| 来源 | 用在哪 |
| --- | --- |
| **ShipTypesSupport**（社区插件，作者 黑桐 10086） | 非原版舰种的 **UI 序号映射** 思路（`GetShipTypeFromIntNumber` / `GetIntNumberFromShipType` / `IsShipTypeActiveForCurrentPlayer`）；我们按"原版 0..5 不动、其余 20+"的编排重新实现，未复制其代码 |
| **UAD / MelonLoader / HarmonyX / Il2CppInterop** | 运行环境与补丁框架（各自遵循其许可） |

## 资产声明 / Assets

本仓库**只包含代码、配置与文档**。以下内容属于 SMS MOD 本体（通过 NexusMods 等渠道分发），不在本仓库内：

- 3D 模型、贴图、材质
- 语言文件（`*.lng`）与地图数据覆盖
- 预制设计包、舰名表等数据包

This repository contains **code, configuration and documentation only**. Models, textures, materials, localisation
files, map overrides and data packs belong to the SMS mod distribution and are not included here.
