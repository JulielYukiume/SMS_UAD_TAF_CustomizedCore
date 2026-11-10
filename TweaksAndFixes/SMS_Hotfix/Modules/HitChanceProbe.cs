using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HCP] "命定之死"诊断探针：只打印数值，不改行为。
    /// 目的：区分"底层真的是 1%"与"底层更小但显示被抬到 1%"。
    /// </summary>
    internal static class HitChanceProbe
    {
        private static int _hc, _add;
        private static readonly HashSet<string> _seen = new HashSet<string>();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HitChanceProbe) { HotfixLog.Info("[SMS.2.HCP] disabled"); return; }
            try
            {
                var hc = AccessTools.Method(typeof(Ship), "HitChance");
                if (hc != null)
                    harmony.Patch(hc, postfix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Post_HitChance))));
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(Ship.HitChanceCalc)))
                {
                    if (m.Name != "Add") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 3 || ps[0].ParameterType != typeof(float)) continue;
                    harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Post_Add))));
                }
                HotfixLog.Info("[SMS.2.HCP] HitChance probe armed");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HCP] install failed: " + e); }
        }

        internal static void Post_HitChance(Ship from, Ship target, ref float __result)
        {
            try
            {
                if (__result < 0f || __result > 0.05f) return;
                if (_hc >= 120) return;
                _hc++;
                string k = "hc:" + __result.ToString("F6");
                if (_seen.Contains(k)) return;
                _seen.Add(k);
                HotfixLog.Info("[SMS.2.HCP][hit] result=" + __result.ToString("F6")
                    + " from=" + Name(from) + " target=" + Name(target));
            }
            catch { }
        }

        internal static void Post_Add(Ship.HitChanceCalc __instance, float mult, string reason, string value)
        {
            try
            {
                if (_add >= 120) return;
                if (mult > 0.05f && mult > -0.05f) return;
                _add++;
                float comb = 0f;
                try { comb = __instance.multsCombined; } catch { }
                HotfixLog.Info("[SMS.2.HCP][add] mult=" + mult.ToString("F6")
                    + " combined=" + comb.ToString("F6")
                    + " reason=" + (reason ?? "-") + " value=" + (value ?? "-"));
            }
            catch { }
        }

        private static string Name(Ship s)
        {
            try { return s == null ? "-" : (s.name ?? "-"); } catch { return "?"; }
        }
    }
}
