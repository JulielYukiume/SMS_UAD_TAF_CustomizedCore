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
