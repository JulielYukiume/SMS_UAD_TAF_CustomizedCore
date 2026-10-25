# 60 · `PrintShipDataExhaustive` 移植手术方案（未完成，交接用）

> 状态：**未移植**。本文记录 2026-10-25 深夜的侦察结论与已验证可行的生成器方案，
> 下次开工照此执行即可，不需要重新摸索。

---

## 1. 规模（为什么不能手抄）

| 指标 | 数值 |
| --- | --- |
| Beta6 方法 IL | **3,067** |
| 反编译行数 | **889 行**（`taf/reference/beta6-decompiled/TweaksAndFixes/Patch_Ui.cs` L592–1480） |
| `DefaultInterpolatedStringHandler` 块 | **179 个** |
| `AppendLiteral` / `AppendFormatted` | 186 / 185 |
| 方法性质 | **纯只读**：把一艘船的全部字段 dump 到日志，无任何副作用 |

⇒ 手写不现实，但**机械转换可行**（模式高度统一）。

## 2. 已验证的转换方案

生成器（已写好，可直接复用）：
`taf/work/version-diff/gen-printship.py`

做法：
1. 定位方法范围（大括号配平）；
2. 逐块识别 `new DefaultInterpolatedStringHandler(n, m)` + 连续的 `AppendLiteral/AppendFormatted`，
   折叠成一行 `$"..."` 插值；
3. 输出到 `taf/work/version-diff/PrintShipDataExhaustive.gen.cs`。

**已验证**：一次运行可把 899 行 → **515 行**，179 个插值块全部转换成功。

## 3. 还差的收尾处理（下次接着做）

| # | 工作 | 说明 |
| --- | --- | --- |
| 1 | 删除每块后残留的 `ModLog.MLogM(defaultInterpolatedStringHandlerN.ToStringAndClear());` | 生成器把"日志发射"和"字符串构造"拆开了，需要合并成 `Melon<TweaksAndFixes>.Logger.Msg($"...")` |
| 2 | `get_X()` → `X`（属性访问） | 例如 `s.get_allowedAiRetreat()` → `s.allowedAiRetreat`；`get_Item(i)` → `[i]` |
| 3 | `((VesselEntity)s).get_vesselName()` → `s.vesselName` | 多余强转 |
| 4 | `((Bounds)(ref b)).get_size()` → `b.size`；`set_X(v)` → `X = v` | 值类型 `ref` 形式 |
| 5 | `Object.op_Inequality(a, null)` → `a != null` | 反编译残留 |
| 6 | 补 `using`（`UnityEngine`、`Il2Cpp`、`MelonLoader` 等） | 按需 |
| 7 | 编译 → 修剩余错误（预期个位数） | — |

**验收标准**：TafInspector 里该方法 IL 接近 3,067，且 Beta6 独有数下降约 1 条
（注意：单个方法只占 1 条，但它是**最大的**一条）。

## 4. 价值判断（先看这段再决定要不要做）

- 该方法**只被 `PrintShipDataExhaustive` 的调用点使用**（调试面板/控制台命令），
  玩家正常游玩不可见；
- 移植它**不会**修复任何玩家可见问题，只是把保真度指标推高；
- ⇒ 若时间有限，**优先级应低于** `LoadPredefs`（影响新档预置设计）与 `MakeVisualShip`（主菜单）。

## 5. 建议的执行顺序（更新版）

| 顺序 | 项 | 理由 |
| --- | --- | --- |
| 1 | `LoadPredefs`（先定位 main 真实文件） | 影响新战役生成 |
| 2 | `MakeVisualShip`（四段摘取） | 主菜单可见 |
| 3 | `PrintShipDataExhaustive`（本文件方案） | 纯调试用途，最后做 |
| 4 | `CampaignEventSystem` | 需要你验收 |
| 5 | `OceanGraphicsData` + 静态海面 | 你说不急 |
