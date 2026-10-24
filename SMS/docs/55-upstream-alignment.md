# 上游对齐报告（2026-10-24）

> 目的：**3.21.1 地基必须落在官方（duke）的真历史上**，否则 3.22 的讨论与 PR 都无从谈起。
> 本文记录：仓库历史已修正、上游 3.21.1 之后改了什么、与我们的改动是否冲突。

---

## 1. 仓库历史已修正（重要）

| 项 | 修正前 | 修正后 |
| --- | --- | --- |
| `main` 的父提交 | `9b5fe81`（2024 年的旧提交） | **`1672355`（官方 tag `Minor_Relase_3.21.1`）** |
| 与上游关系 | 快照，无法对比/PR | **真实后代**，`git diff upstream/main` 就是干净差异 |
| 当前 `main` | `11424b6` 快照 | **`f649113`** = `3.21.1-SMS.1: SMS hotfix layer on official TAF 3.21.1` |
| tag `v3.21.1-sms.1` | 指向快照 | 指向 `f649113` |

验证方式（任何人都能复现）：

```bash
git fetch upstream
git log --oneline -1 upstream/Minor_Relase_3.21.1   # 1672355
git log --oneline -2 main                            # f649113 → 1672355
git diff --stat upstream/Minor_Relase_3.21.1 main    # 只剩我们的改动（44 文件）
```

---

## 2. 上游 3.21.1 之后做了什么（`1672355..51d0518`）

**26 个提交**，集中在 24 个文件（`+3199 / -883`），功能向：

| 方向 | 代表提交 |
| --- | --- |
| **舰船生成器重写** | `Modules/ShipGen.cs` 新增 1129 行、`Harmony/Ship.cs` +630、`Harmony/Part.cs` -381 |
| **数据表扩展** | `hullData.csv` 新表（waterline / stabilityForward）、`guns.csv`/`partModels.csv` 无限列、`gradeExtensions` 重构 |
| **UI/信息层** | Design/Fleet 统一信息头、研究列表倒序、战役窗口配色、港口图标、保存提示 |
| **共用设计校验** | `shared designs validator` 两处修复（null parts 检查、逻辑修正） |
| **战斗细节** | 自定义战斗满油、鱼雷管/发射器数量限制、炮弹贴花随口径缩放 |
| **可调参数** | 环境光强度、海军入侵距港口距离 |
| **基础设施** | 日志接口 `Modules/ModLog.cs`、全局缓存初始化检查 |

> 结论：duke 这几个月**其实做了不少事**（尤其 ShipGen 重写），这正好解释了"3.22 相对我们 3.21.1 的差异"里最值得吸收的部分。

---

## 3. 与我们的改动是否冲突（试合并实测）

在我们分支上执行：

```bash
git checkout -B test-merge 51d0518     # 上游 main
git merge --no-commit --no-ff sms-main
```

结果：

```
Auto-merging TweaksAndFixes/Modified/UiM.cs
Automatic merge went well
冲突文件：0
```

| 我们改的 4 个文件 | 上游 3.21.1 之后的提交数 | 合并结果 |
| --- | --- | --- |
| `TweaksAndFixes.cs` | 0 | 无冲突 |
| `TweaksAndFixes.csproj` | 0 | 无冲突 |
| `Modified/UiM.cs` | **4** | **自动合并成功**（改动区域不重叠） |
| `Harmony/CampaignController.cs` | 0 | 无冲突 |

⇒ **3.22.0-SMS.1 的机械合并是零冲突的**，工作重点在"合并后验证"，而不是"解决冲突"。

---

## 4. 合并后必须重点验证的点（风险清单）

| # | 风险 | 原因 | 验证方法 |
| --- | --- | --- | --- |
| 1 | 预制设计按钮锁定失效 | 上游也改了 `UiM.cs`（4 个提交），可能与我们的 3 处 `SMS.1.PU` 相邻 | `force_no_predef_designs=1` 下进自定义战斗，看按钮是否仍为"禁用/置灰" |
| 2 | 船体/部件判定行为变化 | 上游 `Ship.cs` +630、`Part.cs` -381、新增 `hullData.csv` | 非原版舰种：共享设计选 `cb`/`av`，看船体与部件是否可读可选 |
| 3 | 生成器行为变化影响真混合 | `Modules/ShipGen.cs` 新实现 | 1940 中华档跑 3~5 回合，看 `[SMS.1.G]` 输出与设计数量 |
| 4 | 共用设计校验改动与我们的回填冲突 | 上游修了 validator，我们 `S` 模块也做回填 | 无共享设计的局：确认回填仍生效、不再产生无效设计 |
| 5 | 新的日志接口 `ModLog` 与我们的 `HotfixLog` 并存 | 两套日志 | 确认 `[SMS.1.X]` 仍可过滤、无重复输出 |

---

## 5. 建议的 3.22.0-SMS.1 推进方式

1. 新建分支 `feature/3.22-base`，从 **`upstream/main`（51d0518）** 拉出；
2. `git merge sms-main`（预计零冲突，实测通过）；
3. 按 §4 跑验证清单，出问题按 `[SMS.1.X]` 定位；
4. 验证通过后合入 `main`，版本串改 `3.22.0-SMS.1`，打 tag。

> 前提：你要确认 **3.22.0 的 beta 是否就是这个 `main`（51d0518）**。
> 目前上游只有 `main` / `improved-drydock` / `new-save-system-4xx` / `split-predefs-4xx` 四条分支，
> 没有 `3.22.0` 的 tag；如果 duke 另有 beta 包（DLL/zip）而非分支，请给我文件名，我按 DLL 反查确认对应提交。
