# 诊断与调试开关（3.22.0-SMS.2）

**中文** | [English](#english)

本仓库不再维护 SMS.1 时期的 `TAFHotfix-Batch1.config.csv`。当前版本只有两张配置表：

| 文件 | 内容 | 是否自动加载 |
| --- | --- | --- |
| `SMS/config/SMS_UAD_TAF_CustomizedCore.config.csv` | **release，28 项**（`ALL` 级） | 是（随包） |
| `SMS/config/SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv` | **DEBUG，104 项**（`DEBUG` / `EXPERIMENTAL`） | 否：需要时复制到 `Mods\` 覆盖 release 同名文件 |

## 排查顺序

1. **先看日志**：`MelonLoader\Latest.log` 搜 `[SMS.1.` / `[SMS.2.` / `[SMS.3.` 前缀；
2. **再开详细日志**：在 release 配置里把 `verbose` 设为 `1`（会输出 `[DBG]` 与 `[SMS.3.GRADE]` 等）；
3. **需要单模块开关**：用 DEBUG 配置替换 release，改对应 `key,value`（`1` 开 / `0` 关）；
4. 全参数说明（级别 / 默认值 / 所属模块 / 代码路径）见发布包内 `Mods\TAFData\params-report.md`。

> 诊断类参数默认全部关闭，不影响正常游玩；排障结束后请恢复 release 配置。

---

<a id="english"></a>
# Diagnostics & debug switches (3.22.0-SMS.2)

The SMS.1-era `TAFHotfix-Batch1.config.csv` is gone. This version ships two configs:

| File | Contents | Auto-loaded |
| --- | --- | --- |
| `SMS/config/SMS_UAD_TAF_CustomizedCore.config.csv` | **release, 28 entries** (`ALL`) | yes |
| `SMS/config/SMS_UAD_TAF_CustomizedCore_DEBUG.config.csv` | **DEBUG, 104 entries** (`DEBUG` / `EXPERIMENTAL`) | no - copy over the release file when needed |

Troubleshooting: search the log for `[SMS.1.` / `[SMS.2.` / `[SMS.3.` prefixes; set `verbose=1` for detailed logs;
swap in the DEBUG config to toggle individual modules; see `Mods\TAFData\params-report.md` in the release package for the full parameter reference.
