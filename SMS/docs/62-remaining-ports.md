# 62 · 剩余移植清单（port33 后重新盘点）+ 各组依赖关系

> 2026-10-26。**重要**：之前报的 "230 条 Beta6 独有" 里，**124 条是编译器生成**
> （`<>c`、`<>c__DisplayClass`、`<Xxx>d__` 迭代器等），永远不会逐字对齐，**不影响功能**。
> 真正的语义缺口是 **106 条**。

---

## 1. 真正的缺口（按类）

| 类 | 条数 | 说明 | 依赖 |
| --- | --- | --- | --- |
| `Patch_Ship` | 13 | `Prefix_GetInitialFuelForCustomBattle`、`Prefix_Update`、`Prefix_CalcInstability`、`InitSectionsCyclical`/`UpdateSections`/`Prefix_RefreshHull`（**我们已有，类名不同**） | 部分已覆盖 |
| `ModUtils` | 13 | `NumToString(int/float/double)`、`GeneratePoissonDiscPoints`（我们叫 `PoissonDisc.Generate`）、`SplitSmart`、若干本地函数 | 独立，低风险 |
| `Modules.ShipGen` | 10 | `CanAddGun` / `CanAddTorpedo` / `CanAddFunnel` / `CanAdd` / `IsGunAvailible` / `ParsePartLimitData` | **整模块缺失** |
| `.PartLimits` | 9 | 配件限制构造/合并/解析（ShipGen 的配套） | 同上 |
| `Patch_Ui` | 9 | 预览图缓存 + `CreateAndRefreshSectionsPlanUI` + `RefreshDamagePlan` + `PrintShipDataExhaustive` | ⚠️ **依赖 `Patch_SceneManager.ConfigureForPreview`** |
| `CampaignEventSystem` | 7 | `OnNewTurn` / `AnswerEvent` / `QueueEvent` / `CheckForCampaignEnd` | main 分支有部分源码 |
| `TAF_Settings` | 4 | 字段/序列化 | 小 |
| `OceanGraphicsData` | 3 | 海洋外观数据 | 与 Patch_SceneManager 同批 |
| `Patch_SceneManager` | 2 | `ConfigureForPreview`、`UpdateStaticOcean`（静态海面） | B6 海洋特性 |

## 2. 关键发现：`Patch_Ui` 的预览组**拖带 B6 海洋特性**

```csharp
// Beta6 Patch_Ui
[HarmonyPatch("GetShipPreviewTexGeneric")]
[HarmonyPrefix]  static void Prefix(Ui __instance)  => Patch_SceneManager.ConfigureForPreview(active: false);
[HarmonyPostfix] static void Postfix(Ui __instance) => Patch_SceneManager.ConfigureForPreview(active: true);
```
`ConfigureForPreview` / `UpdateStaticOcean` 属于 **B6 的静态海面**（`StaticOcean*`），
即：想完整移植 Patch_Ui 这一组，就必须连 **Patch_SceneManager + OceanGraphicsData** 一起做。

⇒ 若暂时不做海洋，则 `Patch_Ui` 只能移植**不依赖 ConfigureForPreview** 的部分：

| 可独立移植 | 需要 ConfigureForPreview |
| --- | --- |
| `Prefix/Postfix_GetPartPreviewTex`（**部件预览图缓存**） | `GetShipPreviewTexGeneric` 的 Prefix/Postfix |
| `Prefix_GetShipPreviewTexGeneric` 里"禁用预览"的那半（`taf_ship_previews_disable`） | `CreateAndRefreshSectionsPlanUI` 的 Prefix/Postfix |
| | `RefreshDamagePlan` |

**注意**：`GetPartPreviewGuid(part, ship)` 的反编译**在 torpedo 分支有截断**
（`string.Concat(str2: ... )` 参数不完整，L2696-2699），移植时需要按
`G.GameData.partModels[text].models[ship.TechTorpedoGrade(part, true)]` 重建，
并以"能编译 + 缓存命中率日志"验证，不能照抄。

## 3. 建议顺序（更新）

| 顺序 | 批次 | 内容 | 风险 | 玩家可见 |
| --- | --- | --- | --- | --- |
| 1 | **ModUtils 补齐** | `NumToString(int/float/double)`、`SplitSmart` | 低 | 间接（数值显示） |
| 2 | **Patch_Ui 部件预览缓存** | `PartPreviewCache` + `GetPartPreviewGuid`（需重建 torpedo 分支） | 中 | **是**（设计/部件列表缩略图更快） |
| 3 | **CampaignEventSystem** | 事件系统 | 中-高 | 是（战役事件） |
| 4 | **ShipGen + PartLimits** | AI 造船约束整模块 | 高 | 间接（AI 舰队构成） |
| 5 | **Patch_SceneManager + OceanGraphicsData + Patch_Ui 预览组** | 静态海面 + 预览相机 | 中 | 是（海洋外观/预览图） |
| 6 | `PrintShipDataExhaustive` | 调试日志 | 低 | 否 |

## 4. 舯段色差：剩余可疑面

| 可疑 | 说明 |
| --- | --- |
| `Patch_Ui.CreateAndRefreshSectionsPlanUI` / `RefreshDamagePlan` | 名字直指"分段"，但属 UI（**依赖 ConfigureForPreview**，见 §2） |
| `Patch_SceneManager` 光照/天气参数 | 色差在不同天气下更明显（你早前的观察） |
| `TAF_Settings` 渲染相关字段 | 待确认 |

⇒ 结论：色差若要继续查，**绕不开第 5 批（静态海面/场景配置）**——因为那些 Prefix 会改变
"预览/构造器场景"的相机与光照，而这正是色差最明显的场景（共享设计/构造器）。

## 5. 已确认修复（本轮）

| 项 | port |
| --- | --- |
| `Decor.Awake` 空集合异常（Beta5 有、Beta6 无） | **port33**（完整替代实现，Beta6 同款） |

---

## 6. 第 2 批（部件预览缓存）实际结论：**我们早就有了**

`TweaksAndFixes/Harmony/Ui.cs`（3.21.1 起就存在）：

| 成员 | 位置 |
| --- | --- |
| `PartPreviewCache` / `LastPartPreviewGuid` | L2235-2236 |
| `Resize(Texture2D, int, int)` | L2238 |
| `GetPartPreviewGuid(PartData, Ship)` | L2252-2328（gun/torpedo/其它三分支齐全） |
| `Prefix_GetPartPreviewTex` | L2332 |
| `Postfix_GetPartPreviewTex` | L2370 |

⇒ worklist 里那几条 "Beta6 独有" 只是**签名/写法差异**（Beta6 用 `TechGunGrade(part, true)` 两参版），
不是功能缺失。

**唯一真正缺的**是这两个（各 4 行）：

```csharp
[HarmonyPatch("GetShipPreviewTexGeneric")][HarmonyPrefix]
static void Prefix(Ui __i) => Patch_SceneManager.ConfigureForPreview(active: false);

[HarmonyPatch("GetShipPreviewTexGeneric")][HarmonyPostfix]
static void Postfix(Ui __i) => Patch_SceneManager.ConfigureForPreview(active: true);
```

以及 `CreateAndRefreshSectionsPlanUI` / `RefreshDamagePlan` 的同款 Prefix/Postfix。

⇒ 它们**全部依赖 `Patch_SceneManager.ConfigureForPreview`（B6 静态海面）**，
所以"第 2 批"实际并入**第 5 批**执行，不能独立完成。

## 7. 修正后的执行顺序

| 顺序 | 批次 | 状态 |
| --- | --- | --- |
| 1 | ModUtils（`NumToString` / `SplitSmart`） | ✅ port34 |
| 2 | 部件预览缓存 | ⏭️ **已有**，仅缺 ConfigureForPreview 钩子 → 并入第 5 批 |
| 4 | **ShipGen + PartLimits** | ▶ 下一个做 |
| 5 | Patch_SceneManager + OceanGraphicsData + 预览钩子 + 分段计划 UI | 待第 4 批后 |

---

## 8. 第 4 批（ShipGen / PartLimits）规模实测 —— 需要拆成多步

| 文件 | 反编译行数 | 说明 |
| --- | --- | --- |
| `Modules/ShipGen.cs` | **1,383 行** | `PartLimits` 类（54-950）+ `ShipGen` 静态方法（951-1383） |
| `Patch_ShipGenRandom.cs` | 762 行 | 我们**已有**（3.21.1 自带的 AI 随机造船补丁） |
| `Patch_BatchShipGenerator.cs` | 743 行 | 我们**已有** |

### 内部结构（`ShipGen.cs`）

| 成员 | 行数 | 作用 |
| --- | --- | --- |
| `PartLimits` 构造/复制 | 54-83 | 从 `PartData`/`PartLimits` 建立限制 |
| `BuildRestrictionsFor(Part)` | 84-109 | 由部件反推限制 |
| `Merge(...)` ×2 | 110-250 | 合并两级限制 |
| **`Parse(PartData)`** | 251-264 | 入口 |
| **`ParseShipType(ShipType)`** | 265-504 | **240 行**，解析 shipType 侧限制 |
| **`ParsePartData(PartData)`** | 505-833 | **330 行**，解析 parts 侧（含 `shipgen_limit(...)` 语法） |
| `Print()` | 834-950 | 调试输出 |
| `CanAddGun` / `CanAddTorpedo` / `CanAddFunnel` / `CanAdd` | 955-1333 | 实际约束判定（挂 `Patch_ShipGenRandom` 等） |
| `ParsePartLimitData()` | 1366-1383 | 启动时灌表 |

### 结论

- **不可能一次 port 完成**：仅 `Parse` 两个方法就 570 行，且含大量反编译残留（`((Bounds)(ref x))`、`op_Implicit` 等）；
- 建议拆成：
  1. **P4-a**：`PartLimits` 数据结构 + `Merge` + `Parse`（解析侧，纯数据）
  2. **P4-b**：`CanAdd*` / `IsXAvailible` 判定 + 挂 `Patch_ShipGenRandom`
  3. **P4-c**：`ParsePartLimitData` 启动灌表 + 日志验证（18 行 SMS `shipgen_limit` 是否被读到）
- 每步都要编译 + 单测式日志，不能一口气写完再编译（反编译残留太多）。

### 与第 5 批的取舍

| 批次 | 反编译规模 | 玩家可见收益 | 与色差相关性 |
| --- | --- | --- | --- |
| 第 4 批（ShipGen） | **~2,100 行**（含 3 个文件） | 仅影响 AI 生成舰（且约束有限） | 无 |
| 第 5 批（SceneManager/Ocean/预览钩子） | ~700 行 | 预览图正确性 + 静态海面 | **有（预览/构造器场景的光照与相机）** |

⇒ 若时间有限，**第 5 批优先级高于第 4 批**。
