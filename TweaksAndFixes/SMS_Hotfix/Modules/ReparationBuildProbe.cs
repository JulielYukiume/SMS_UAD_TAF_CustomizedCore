using System;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.1.REP] "能否从设计建造"只读探针。
    ///
    /// 目的：定位"赔偿设计（isReparationDesign=true）又能被玩家建造"的判定点。
    /// 做法：Postfix `PlayerController.CanBuildShipsFromDesign(Ship, out string)`，
    ///       仅当设计是赔偿设计时打印：结果、原因、舰名。不改任何返回值。
    /// </summary>
    [HarmonyPatch(typeof(PlayerController))]
    internal static class ReparationBuildProbe
    {
        private static int _n;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ReparationBuildProbe)
            {
                HotfixLog.Info("[SMS.1.REP] disabled");
                return;
            }

            try
            {
                var m = AccessTools.Method(typeof(PlayerController), "CanBuildShipsFromDesign");
                if (m == null) { HotfixLog.Warn("[SMS.1.REP] method not found"); return; }

                harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(ReparationBuildProbe), nameof(Postfix_CanBuild))));
                HotfixLog.Info("[SMS.1.REP] armed (read-only probe on CanBuildShipsFromDesign)");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.REP] install: " + e.Message); }
        }

        internal static void Postfix_CanBuild(Ship design, ref string reason, ref bool __result)
        {
            try
            {
                if (design == null) return;

                bool isRep = false;
                try { isRep = design.isReparationDesign; } catch { }
                if (!isRep) return;

                string name = "";
                try { name = design.vesselName; } catch { }

                HotfixLog.Warn("[SMS.1.REP] CanBuildShipsFromDesign(reparation) '" + name
                               + "' -> " + __result + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason))
                               + " status=" + (int)design.status);
            }
            catch { }
        }
    }
}