# 75 · 舯段色差：段序数据层不匹配（根因假设与修复）

> 2026-10-30。这是"解冻色差"调查的**第一条代码级证据链**。
> 结论先行：**色差不是材质/渲染管线问题，而是"段（section）顺序数据被放错容器"导致中段用了错误的模型。**

---

## 1. 证据链

| # | 事实 | 来源 |
| --- | --- | --- |
| 1 | Beta6 包内**没有**二进制块、没有嵌入资源（`.resources`=0、PNG 魔数=0） | 字节扫描三份 DLL |
| 2 | OURBUILD（用 Beta6 反编译源码重建）**色差干净** ⇒ 修复在可反编译的代码里 | 历史测试 |
| 3 | `InitSectionsCyclical` 的 IL **完全一致**（相似度 1.000） | `ilspycmd -il` + 归一化 diff |
| 4 | `UpdateSections` IL 相似度仅 **0.755**，且我们的实现**少一条分支**：找不到 `sectioningParsedPart` 时直接回退 | 同上 |
| 5 | 我们的 `HullData` **没有 `sectioningParsedPart`**，且 `LoadData` 把 `sect_*` 参数错误地写进了 `sectioningParsed` | 源码 + IL（`ldfld HullData::sectioning`） |
| 6 | Beta6 的 `hullData.csv` 与我们的**数据行完全一致**（207 行，0 差异） | `Compare-Object` |

---

## 2. 两个容器，语义完全不同（关键）

| 容器 | 数据来源 | 索引 | 用途 |
| --- | --- | --- | --- |
| `sectioningParsed` | `hullData.csv` 的 **`sectioning` 列**（`"1, 1;3, 2;5;4"`） | **middleCount**（段数） | 全局段序（在 CSV 里按"几段"给方案） |
| `sectioningParsedPart` | **`parts.csv` 的 `sect_<n>(...)` 参数** | 部件名字典 → middleCount | 按部件的段序覆盖（同一 hull 不同变体） |

Beta6 的 `UpdateSections` 逻辑：

```csharp
if (middleCount < hullData.sectioningParsed.Count)          // ① 先查 CSV 全局表
    order = hullData.sectioningParsed[middleCount];
else if (hullData.sectioningParsedPart[hull.data.name].ContainsKey(middleCount))
    order = hullData.sectioningParsedPart[hull.data.name][middleCount];   // ② 再查部件表
else
    InitSectionsCyclical(...);                              // ③ 回退
```

**我们（port76 及之前）**：`sect_*` 被塞进 `sectioningParsed`，
于是 `useDefaultSectioning=false` 但 `sectioningParsed` 里是**按 `sect_<n>` 的 n 补位**的列表
（`sect_1` → 索引 1，索引 0 被填 `{-1}`），与 `middleCount` **完全不对应**：

| middleCount | 我们旧逻辑取到的 | 正确行为 |
| --- | --- | --- |
| 0 | `{-1}`（占位）→ 回退 cyclical | 应为该 hull 的自定义段序 |
| 1 | `sect_1` 的内容 → **段序错位** | 应为 `sectioningParsedPart[name][1]` 或回退 |
| ≥ 2 | 多数越界 → 回退 cyclical | 同上 |

⇒ **中段（middles）会拿到错误的基础模型/顺序**，表现为"舯段贴图/材质错位"，也就是我们看到的色差。

---

## 3. 修复（port77）

| 文件 | 改动 |
| --- | --- |
| `Data/HullData.cs` | 新增 `sectioningParsedPart : Dictionary<string, Dictionary<int, List<int>>>`；`LoadData` 里把 `sect_*` 写入该容器（同时以**部件名**与**参数键**两个 key 存放，兼容不同查询方式）；`sectioningParsed` 只保留 CSV `sectioning` 列的数据 |
| `Harmony/ShipRefreshHull.cs` | `UpdateSections` 与 duke 对齐：① CSV 全局表 → ② 部件表 → ③ `InitSectionsCyclical` 回退；`{-1}` 仍表示"强制默认序" |

## 4. 验证方法

进战斗/构造器看日志（`UpdateSections` 会打印）：

```
  Sections <model> <current> -> <target>
    Custom ordering      ← 走了自定义段序
    Default ordering     ← 回退
```

| 观察 | 含义 |
| --- | --- |
| 舯段颜色恢复正常 | ✅ 根因确认 |
| 仍有色差、但日志出现 `Custom ordering` 且段数正确 | 段序已对，色差另有原因（转 `Variation.Init` / 材质路径） |
| 仍有色差、日志全是 `Default ordering` | 该 hull 的数据里没有 `sect_*` 覆盖 ⇒ 需要另找（如 `hullData.csv` 的 `sectioning` 列需补数据） |

## 5. 若本修复不足，下一个嫌疑（已排好队）

1. `Prefix_RefreshHull` 的 **IL 相似度只有 0.519**（我们改写幅度大：多了 `Vector3`/`BoxCollider` 遍历，少了日志 handler）⇒ 重新与 duke 对齐，把我们的装饰物逻辑挪到 duke 流程之后；
2. `Variation.Init`（`hullCont` 的变体初始化，直接决定材质/贴图选择）；
3. `Patch_Ui` 的预览/材质缓存路径。
