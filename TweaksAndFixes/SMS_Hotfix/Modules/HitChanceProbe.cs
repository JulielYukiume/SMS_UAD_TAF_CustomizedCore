using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HCP] "1% 下限"诊断探针（**只读**）。
    ///
    /// rc9 结论：`Util.RoundToStep(…, 0.01)` 的输入是**百分比尺度**（1.0~2.7），
    /// 也就是说那两处只是"最终值再取整"，不是下限的来源。
    ///
    /// rc10 改为直接看**曲线源头**：`GunData.HitChanceMaxRange(Ship, PartData)` 与
    /// `GunData.HitChanceCurver(Ship, PartData)` —— 二者都**没有 nullable 参数**，可安全 patch。
    /// 公式（guns.csv 注释）：`accuracy_base = K ^ (x ^ b)`，K = hitChanceMaxRange。
    /// 若 K 本身被抬到 0.01，则"1% 下限"就是数据/字段层面的钳位。
    /// </summary>
    internal static class HitChanceProbe
    {
        private static int _mr, _cv;
        private static readonly HashSet<string> _seen = new HashSet<string>();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HitChanceProbe) { HotfixLog.Info("[SMS.2.HCP] disabled"); return; }
            try
            {
                var mr = AccessTools.Method(typeof(GunData), "HitChanceMaxRange", new Type[] { typeof(Ship), typeof(PartData) });
                if (mr != null)
                    harmony.Patch(mr,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Pre_MaxRange))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Post_MaxRange))));
                else HotfixLog.Warn("[SMS.2.HCP] HitChanceMaxRange missing");

                var cv = AccessTools.Method(typeof(GunData), "HitChanceCurver", new Type[] { typeof(Ship), typeof(PartData) });
                if (cv != null)
                    harmony.Patch(cv, postfix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Post_Curver))));
                else HotfixLog.Warn("[SMS.2.HCP] HitChanceCurver missing");

                HotfixLog.Info("[SMS.2.HCP] gun-data probes armed (read-only)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HCP] install failed: " + e); }
        }

        internal static void Pre_MaxRange(GunData __instance, Ship ship, PartData partData)
        {
            try
            {
                if (_mr >= 20) return;
                _mr++;
                HotfixLog.Info("[SMS.2.HCP][K] field=" + __instance.hitChanceMaxRange.ToString("F6")
                    + " part=" + Name(partData));
            }
            catch { }
        }

        internal static void Post_MaxRange(GunData __instance, ref float __result)
        {
            try
            {
                if (_mr > 20) return;
                HotfixLog.Info("[SMS.2.HCP][K] result=" + __result.ToString("F6"));
            }
            catch { }
        }

        internal static void Post_Curver(GunData __instance, ref float __result)
        {
            try
            {
                if (_cv >= 20) return;
                _cv++;
                HotfixLog.Info("[SMS.2.HCP][B] field=" + __instance.hitChanceCurver.ToString("F6")
                    + " result=" + __result.ToString("F6")
                    + " mult=" + __instance.hitChanceMult.ToString("F6"));
            }
            catch { }
        }

        private static string Name(PartData p)
        {
            try { return p == null ? "-" : (p.name ?? "-"); } catch { return "?"; }
        }
    }
}
