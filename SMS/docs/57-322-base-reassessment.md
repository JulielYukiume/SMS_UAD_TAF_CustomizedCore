# 3.22 基线重新评估：为什么"用 GitHub main 当 3.22"是错的

> 2026-10-24 晚，修正 `3.22.0-SMS.1-preview1` 的基座判断。
> 结论：**GitHub 公开 `main`（51d0518）不等于 3.22.0**；Beta6 含有大量未提交代码。

---

## 1. 事实核对

| 检查 | 结果 |
| --- | --- |
| `main`（51d0518）里 `OceanType` | **0 处**（连类型都没有） |
| `main` 里 `Patch_Decor` / `RefreshDecor` / `Intersects` | **0 / 2 / 0**（`RefreshDecor` 只是被调用的空方法，不是 duke 的重写实现） |
| Beta6 反编译产物里 `OceanType` | **11 处**（含 UI 下拉 `OceanTypeDropdown`、`OceanGraphicsData`） |
| Beta6 反编译产物里 `Patch_Decor` / `RefreshDecor` / `Intersects` | **1 / 4 / 1**（decor 重写完整存在） |
| 文件数 | `main` 110 .cs；3.21.1 官版 70 .cs；Beta6 反编译 108 .cs（命名风格不同，但类型集不同） |

⇒ 与 [docs/38](38-six-version-comparison.md) 的旧结论一致：**我们的公开源码树停在 3.21.x，Beta6 的 3.22 特性从未进 GitHub**。
我上一轮把 `main` 当成 3.22 并宣称"合并即得 3.22"，是**错误判断**。

## 2. 现在能拿到 3.22 源码的唯一途径：反编译 Beta6

已执行（工具：`ilspycmd 7.2.1`，NuGet 可联网）：

```
taf/reference/beta6-decompiled/        108 个 .cs，约 1.6 MB
```

包含全部 3.22 特性：

| 特性 | 反编译位置 |
| --- | --- |
| 海洋类型下拉 | `TweaksAndFixes/UiM.cs` L3640-3680（`OceanTypeDropdown`）、`TweaksAndFixes.Data/OceanGraphicsData.cs` |
| Decor 重写（装饰件碰撞/不消失） | `TweaksAndFixes/Patch_Decor.cs`（2,042 B）、`Patch_Ship.cs` L306-380（`Prefix_RefreshDecor` + `Bounds.Intersects`） |
| 战斗天气/时段联动 | `Patch_DayCycleAndWeather.cs` |
| 舰船生成器 | `ShipGen` 相关（202 处命中） |
| hullData | 36 处命中 |

## 3. 为什么不能"直接编译反编译产物"

试编译（复制 csproj + 108 个反编译文件）：

```
error CS: 304 个
  CS1525 Invalid expression term 'ref'   ← Il2CppInterop 的 ref 返回/参数被 ILSpy 渲染成非法表达式
```

304 个错误集中在 `ref` 语义、`in`/`out` 修饰、以及少数字符串插值还原。
**可以修，但属于"逐个方法手改"的工作量**，且得到的是无注释、变量名 `num1/text2` 的代码，后续维护成本高。

## 4. 因此正确的路线（三选一，建议 A + C 并行）

| 方案 | 做法 | 成本 | 风险 |
| --- | --- | --- | --- |
| **A. 定向移植（推荐）** | 以 `3.21.1-SMS.1` 我们的树为基座，按你关心的特性，从反编译代码**逐个方法移植**：① decor 重写 ② 海洋类型 ③ 舯段配色 ④ RefitShipsStart/ModelState（如需要） | 中（每项 1~2 个文件） | 低，代码仍在我们的结构里 |
| B. 全量反编译重建 | 修完 304 个编译错误，建成"Beta6 等价"树 | 高（1~2 天） | 中，变量名/注释丢失，后续与上游同步困难 |
| **C. 向 duke 要源码（并行）** | 请他给 Beta6 对应提交哈希或源码压缩包 | 低（等他回） | 时间不可控 |

## 5. 立即可做的定向移植清单（方案 A 的第一步）

| # | 目标 | 反编译来源 | 目标位置（我们的树） | 说明 |
| --- | --- | --- | --- | --- |
| 1 | **Decor 重写** | `Patch_Decor.cs` + `Patch_Ship.cs` L306-380 | `TweaksAndFixes/Harmony/Ship.cs`（替换我们的 `[SMS.1.DECOR]` 清理补丁） | 修"装饰件不消失 / 救生艇碰撞失效"；这是我们当前 hack 的正解 |
| 2 | **海洋类型选择** | `UiM.cs` 的 `oceanType` + `OceanTypeDropdown` 段、`OceanGraphicsData.cs`、`Patch_DayCycleAndWeather.cs` | `TweaksAndFixes/Modified/UiM.cs` + 新增 `Data/OceanGraphicsData.cs` | 对应你截图里看不到的"海洋类型"选项 |
| 3 | 舯段配色 | `Patch_Ship.cs`/材质相关 | 待定位 | 3.21.1 已知色差 |
| 4 | ModelState / RefitShipsStart | `Patch_Ship.cs`、`Patch_ShipDesign.cs` | 待定位 | 视需要 |

## 6. 仓库处理

- `feature/3.22-base`（基于 `main` 的合并）**不作为 3.22 基线**：建议改名为 `scratch/main-322line` 保留作参考，或直接删除；
- 真正的 3.22 工作分支将从 **`main`（3.21.1-SMS.1 冻结状态）** 拉出 `feature/322-port`，逐项移植并在 `[SMS.*]` 日志下验证；
- `dist/3.22.0-SMS.1-preview1` 里的 DLL 应视为"**main 线 + 我们热修层**"，不是 3.22 特性版，待作废或改名。

---

## 7. 正式路线确认（2026-10-24 定稿）

**基座 = `3.21.1-SMS.1`（我们的冻结版）**，在此之上按 `docs/13 §2.2` 的缺口清单**逐批整合 Beta6 特性**，
最后再处理 **decor 重写**（"甲板装饰物生成重写 + 救生艇无碰撞"是 Beta6 独有的可识别特征）。

### 7.1 分支

| 分支 | 用途 |
| --- | --- |
| `main` | `3.21.1-SMS.1` 冻结状态（发布基线） |
| `feature/322-port` | 本次整合（从 `main` 拉出） |
| ~~`scratch/main-322line`~~ | 已删除（基于 upstream main 的错误尝试） |

### 7.2 基线度量（首次量化，可复现）

```
TafInspector inspect <our dll> docs/generated/sms1-322port
powershell -File tools/fidelity.ps1 -A docs/generated/sms1-322port/06-methods.tsv \
                                     -B docs/generated/v3.22.0-beta6/06-methods.tsv
```

| 指标 | 值 |
| --- | --- |
| 我方方法数 | 1,768（含 SMS 热修层 28 模块） |
| Beta6 方法数 | 1,621 |
| **方法体哈希完全一致** | **1,204（68.1%）** |
| 同 IL、不同哈希（代码生成差异） | 99（5.6%） |
| **IL 条数不同（真实差异）** | **65（3.7%）** |
| 仅我方有 | 400（主要是 SMS 热修层 + 3.21.1 独有） |
| **仅 Beta6 有** | **247**（= 待移植的缺口方法） |

### 7.3 首批要补的（按 IL 规模排序，均为 Beta6 独有或显著差异）

| 方法 | 差异 | 归属 |
| --- | --- | --- |
| `UiM.ApplySettingsMenuModifications` | +226 IL（19→681） | B1：**设置菜单选项**（用户指出的"应有选项"） |
| `UiM.ApplyCampaignWindowModifications` | +240 IL | B2/B5：战役窗口项 |
| `Patch_ShipGenRandom.OptimizeComponents` | +202 IL | B1：生成器优化 |
| `.<ToMainMenuCoroutine>d__25.MoveNext` | +237 IL | B1：主菜单流程 |
| `PredefinedDesignsData.LoadPredefs` | −439 IL | B1：预制设计加载重构 |
| `MainMenuM.MakeVisualShip` | −731 IL | B5：主菜单封面船（改用本体加载器） |

### 7.4 执行顺序

1. **P1（先做，用户可见）**：`oceanType` + `OceanGraphicsData` + `UpdateStaticOcean`、`ApplySettingsMenuModifications`、`ApplyCampaignWindowModifications`
2. **P2**：decor 重写（`Patch_Decor` + `Prefix_RefreshDecor`）——**最后验收项**，因为它是 Beta6 的识别特征
3. **P3**：`LoadPredefs`、`MakeVisualShip`、`ToMainMenuCoroutine`、`OptimizeComponents`
4. **P4**：`CampaignEventSystem` 等二线缺口

每批完成后重跑 `fidelity.ps1`，以"哈希一致率"作为进度指标（当前 68.1%）。

---

## 8. 舯段/首尾段错色：排查记录（2026-10-24 深夜）

### 8.1 已排除的假设（都实测过）

| # | 假设 | 做法 | 结果 |
| --- | --- | --- | --- |
| 1 | 与 `hullData.csv` 的 `sectioning` 有关 | 移植 HullData + UpdateSections | ❌ CSV 里 206 条全是 `cyclical`/空，无数据可用 |
| 2 | 段重建后缺 `OnRenderersWasChanged` | Postfix 补调用 | ❌ 无变化 |
| 3 | `Ship.RefreshHull` 的 3.22 重写本身 | **完整移植 duke 源码**（31e3af3，338 行） | ❌ 色差仍在（但该移植保留，属 3.22 正确实现） |

### 8.2 剩余差异（TafInspector 07-harmony.tsv 对比）

我方 551 个 patch，Beta6 605 个；**Beta6 有、我方没有的仅 16 条**：

| Patch 类型 | 数量 | 是否与颜色相关 |
| --- | --- | --- |
| `Patch_Decor`（duke 版） | 3 | 否（我们有自研 DecorFix） |
| `Patch_DecalsManager` | 3 | 否（弹着贴花） |
| `Patch_Effect` | 6 | 否（贴花口径） |
| `Patch_Util` | 1 | 待查 |
| `Patch_WarPreparationWindow` | 3 | 否（战争赔款 UI） |

⇒ **在 Harmony 补丁层面，没有"材质/颜色"相关的缺口**。

### 8.3 新假设：色差可能由**数据文件**决定，而非代码

Beta5/Beta6 的发布包是"**完整套件**"（docs/13 §1），除 DLL 外还带 `Default_Files`（含 `partModels.csv` / `parts.csv` / `oceanGraphics.csv`）。
本工作区**未归档**这些数据文件（只有 DLL），所以我们一直用游戏原版 CSV 测试。

⇒ 若 duke 是通过更新 `partModels.csv`（材质列）/`parts.csv` 修掉错色，那么无论怎么移植代码都不会复现修复效果。

**验证方式**：拿到 Beta5/Beta6 包里的 `partModels.csv`、`parts.csv`、`oceanGraphics.csv`，用同一 DLL 对比：
- 换数据即可变色 ⇒ 数据修复；
- 换数据无变化 ⇒ 仍在代码里，继续找。

### 8.4 当前结论

色差**不是** RefreshHull 重写能解决的（已实测），也**不是** HullData/sectioning（无数据）。
下一步优先级：
1. 取得 Beta6 的 `Default_Files` 数据文件做对照（最快判定）；
2. 同时继续移植剩余 B5/B6 特性（oceanType、citadel、事件系统等），不为此停滞。
