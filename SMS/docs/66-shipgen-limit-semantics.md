# 66 · `shipgen_limit` / `shipgen_clamp` 语义手册（写 hull 参数前必读）

> 2026-10-26 实测确认。**核心结论**：`*_barrels` 是**单炮塔**管数，不是全舰总数；
> 写 `min_*_gun_barrels: 8` 这种值会导致**所有候选被拒**（表现为"AI 永远放不下主炮"）。

---

## 1. 字段语义总表（以 Beta6 `ShipGen.IsGunAvailible` 源码为准确认）

| 字段 | 作用域 | 判定方式 | 说明 |
| --- | --- | --- | --- |
| `min/max_main_gun_cal` | **单炮** | `data.GetCaliberInch(ship)` | 主炮口径（英寸）区间 |
| **`min/max_main_gun_barrels`** | **单炮塔** | `data.barrels` | ⚠️ 炮塔管数（通常 2–4），**不是全舰总数** |
| `max_main_gun_count` | **全舰** | `ship.mainGuns.Count` | 主炮塔数量上限 |
| `max_main_gun_cal_count` | **全舰** | 不同口径**档数** | 配合 `taf_shipgen_min_cal_gap`（默认 3 英寸）分档 |
| `min/max_sec_gun_cal` | **单炮** | 逐件口径 | 副炮口径区间 |
| **`min/max_sec_gun_barrels`** | **单炮塔** | 逐件 `data.barrels` | ⚠️ 同上，单塔管数 |
| `max_sec_gun_count` | **全舰** | 遍历 `ship.parts` 计数 | 同型副炮塔数量上限；`=0` 表示禁用副炮 |
| `max_sec_gun_cal_count` | **全舰** | 不同口径档数 | 同 gap 分档 |
| `min/max_torp_tube_count` | **单发射器** | `data.barrels` | 鱼雷发射器管数 |
| `max_torp_count` | **全舰** | `ship.torpedoesAll.Count` | 发射器数量上限；`=0` 表示禁用鱼雷 |
| `pref_torp_diameter` | 偏好 | `-1`/`0..4` | 鱼雷直径偏好档位（0.33 / 0.66 / 1.0 分位） |

### 1.1 `shipgen_clamp`（生成后夹取）

| 字段 | 含义 | 备注 |
| --- | --- | --- |
| `speed_min` / `speed_max` | 最高航速区间 | 单位**节**；内部按 m/s 换算（×0.5144444） |
| `beam_min` / `beam_max` | 船宽偏移区间 | 线性重映射（`Reratio`） |
| `draught_min` / `draught_max` | 吃水偏移区间 | 同上 |

> `shipgen_clamp` 可写在 **`shipTypes.csv`** 或 **`parts.csv`（船体行）**；两者同时存在时**船体行优先**。

---

## 2. 常见误用（都会导致"AI 造不出合适的船"）

| 误用写法 | 后果 | 正确写法 |
| --- | --- | --- |
| `min_main_gun_barrels: 8` | 单塔最多 4 管 ⇒ **永远不满足** ⇒ 主炮全部被拒 | 删掉；要"整舰 ≥8 管"用 `min_main_gun_count: 2`（2×4） |
| `max_main_gun_barrels: 12` | 单塔 ≤12 恒真 ⇒ **无效果** | 要"整舰 ≤12 管"用 `max_main_gun_count: 3`（3×4） |
| `min_sec_gun_barrels: 12` | 单塔 4~6 管 ⇒ **永远不满足** ⇒ 副炮全部被拒 | 删掉；用 `max_sec_gun_count` 控制数量 |
| `max_sec_gun_cal: 5` + 主炮口径 10~12 | 若主/副炮归类失败，主炮会走副炮分支被拒 | 我们的实现已改为**按口径区间归类**（见 §3） |
| `max_main_gun_cal_count: 1` 但主炮口径跨档 | 只能有 1 种主炮口径 ⇒ 混装会被拒 | 确认舰体设计意图 |

---

## 3. 我们相对 Beta6 的两处**有意修正**

| 修正 | 原因 |
| --- | --- |
| **主/副炮归类改用口径区间**（`IsMainByLimits`） | Beta6 用 `ship.IsMainCal(data)`；AI 选炮时船上还没有主炮 ⇒ 主炮候选被判为副炮 ⇒ 被 `max_sec_gun_cal` 全拒。改用"落在 `[min_main,max_main]` 即主炮"后该问题消失 |
| **`Part.CanPlace` 接线只在拒绝时短路** | Beta6 直接 `return false` 并给出 `__result`，会**跳过游戏原生的碰撞/放置判定** ⇒ 出现"三个烟囱重叠"。我们改为"拒绝才短路，允许则交回原版" |

---

## 4. 调试方法

配置 `shipgen_limits,2` 后，日志会输出：

```
[SMS.1.SHIPGEN][diag] IsGunAvailible 'gun_xx' cal=11.5 IsMainCal=False IsMainByLimits=True
    | main: cal 10~12 barrels 8~12 count<=4 | sec: cal 0~5 barrels 12~0 count<=0
[SMS.1.SHIPGEN][diag]   DENY main barrels=2 range 8~12
```

| 看什么 | 结论 |
| --- | --- |
| `main: cal 0~0 ...` | 限制**没解析到**（检查 `shipgen_limit(...)` 是否写在正确列、`;` 分隔是否正确） |
| `IsMainByLimits` 全部 False | 口径归类异常（检查 `GetCaliberInch` 单位/口径区间） |
| `DENY ... range 8~12` | **数据不可满足**（按 §2 修正写法） |

---

## 5. 与旧式参数的对照

| 旧式（游戏本体处理） | 新式（TAF ShipGen） |
| --- | --- |
| `ai_min_caliber_main(13)` | `min_main_gun_cal: 13` |
| `ai_max_caliber_main(15)` | `max_main_gun_cal: 15` |
| `ai_min_caliber_sec(5)` | `min_sec_gun_cal: 5` |
| `ai_min_caliber_ter(4)` | — （新式无对应；三級炮未建模） |

> 旧式由**游戏本体**读取，一直有效；新式依赖 TAF 的 `ShipGen` 模块。
> 两者可共存，但**语义不要混写**（旧式没有"管数/档数"概念）。
