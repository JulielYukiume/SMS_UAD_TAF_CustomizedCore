using System;
using HarmonyLib;
using UnityEngine;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HC] "命定之死"：把射击概率的**下限**从原版 1%（0.01）改成可调（默认 0.1% = 0.001）。
    ///
    /// 定位过程（docs/05-design-mechanics/20a-minhit-1pct-floor.md）：
    ///   · `Ship::HitChance/10` 末尾有 6 处引用常量 0.01；其中最后一处（1.7 @ 0x181f63a80）是：
    ///       `chance = Gradient.Clamp(chance, 0.01f, min(1, Lerp(...)))`
    ///     —— **这就是"命定之死"的实现点**。
    ///   · 其余 0.01 分别是 `Util.RoundToStep` 的 step（百分比取整）与乘法系数，与下限无关。
    ///
    /// 做法：Prefix `UnityEngine.Mathf.Clamp(float value, float min, float max)`，
    /// 仅当 `min == 0.01f` 且 `max <= 1.01f`（概率区间）时把 min 换成参数值，其余调用不受影响。
    /// </summary>
    internal static class HitChanceGrid
    {
        internal const float VanillaFloor = 0.01f;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                var m = AccessTools.Method(typeof(Mathf), "Clamp", new Type[] { typeof(float), typeof(float), typeof(float) });
                if (m == null) { HotfixLog.Warn("[SMS.2.HC] Mathf.Clamp not found"); return; }
                harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceGrid), nameof(Prefix))));
                HotfixLog.Info("[SMS.2.HC] HitChanceFloor armed: hit_chance_floor=" + HotfixParams.HitChanceGrid
                    + " (vanilla 0.01)  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HC] install failed: " + e); }
        }

        internal static void Prefix(ref float min, float max)
        {
            try
            {
                if (!HotfixParams.IsApply) return;
                if (Math.Abs(min - VanillaFloor) > 1e-9f) return;   // 只认原版 1% 下限
                if (max > 1.01f || max < 0.5f) return;              // 只认概率区间（0..1，上限 >= 0.5）
                float f = HotfixParams.HitChanceGrid;
                if (f <= 0f || f >= VanillaFloor) return;           // 非法/更大 ⇒ 保持原版
                min = f;
            }
            catch { }
        }
    }
}
