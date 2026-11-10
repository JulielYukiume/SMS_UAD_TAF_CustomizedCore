using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HC] "命定之死"：把射击概率的量化网格从 1%（原版）改成可调（默认 0.1%）。
    ///
    /// 背景（docs/05-design-mechanics/20a-minhit-1pct-floor.md）：
    ///   `Ship::HitChance/10` 用 `Util.RoundToStep(chance, 0.01f)` 把射击概率量化到 1% 网格
    ///   （2 处调用点），因此最低非零概率恒为 1%；而显示层 `Ship.HitChanceCalc.Add` 用的是
    ///   `RoundToStep(value, 0.001f)`（0.1% 网格），于是出现"最低 1% + 其它千分位"的观感。
    ///
    /// 本模块只在 `step == 0.01` 时把 step 换成参数值，不影响其它 RoundToStep 调用
    /// （0.001 伤害取整 / 0.1 百分比 / 1000 自定义战斗 / 0.2 甲板物件 等）。
    /// </summary>
    internal static class HitChanceGrid
    {
        internal const float VanillaGrid = 0.01f;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                var m = AccessTools.Method(typeof(Util), "RoundToStep", new Type[] { typeof(float), typeof(float) });
                if (m == null) { HotfixLog.Warn("[SMS.2.HC] Util.RoundToStep not found"); return; }
                harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceGrid), nameof(Prefix))));
                HotfixLog.Info("[SMS.2.HC] HitChanceGrid armed: hit_chance_grid=" + HotfixParams.HitChanceGrid
                    + " (vanilla 0.01)  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HC] install failed: " + e); }
        }

        internal static void Prefix(ref float step)
        {
            try
            {
                if (!HotfixParams.IsApply) return;
                if (step != VanillaGrid) return;                 // 只碰射击概率那一档
                float g = HotfixParams.HitChanceGrid;
                if (g <= 0f || g >= VanillaGrid) return;          // 0/非法/更大 ⇒ 保持原版
                step = g;
            }
            catch { }
        }
    }
}
