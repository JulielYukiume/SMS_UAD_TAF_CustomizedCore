using System;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.1.REP] 赔偿设计（`isReparationDesign == true`）**禁止玩家直接建造**。
    ///
    /// 背景：
    ///   · 3.21.1 时期我们的"能否从设计建造"判定让赔偿设计不可造；
    ///   · 移植 3.22 特性后该判定被 duke 的实现覆盖，赔偿设计又能建造；
    ///   · `PlayerController.CanBuildShipsFromDesign` 有**两个重载**，
    ///     之前按名字挂载会抛 `Ambiguous match`（日志 26-10-25_2-36-5 已确认）。
    ///
    /// 本模块：
    ///   · 显式挂两个重载的 Postfix：
    ///       CanBuildShipsFromDesign(Ship, out string)
    ///       CanBuildShipsFromDesign(Ship, int, out string)
    ///   · 若 `design.isReparationDesign` ⇒ 结果置 false + 原因 "reparation design"
    ///   · 开关 `reparation_design_block`（默认 1）；`reparation_build_probe`（默认 0，只打印不改）
    /// </summary>
    internal static class ReparationBuildProbe
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ReparationBuildProbe && !HotfixParams.ReparationDesignBlock)
            {
                HotfixLog.Info("[SMS.1.REP] disabled");
                return;
            }

            int n = 0;

            try
            {
                var m1 = AccessTools.Method(typeof(PlayerController), "CanBuildShipsFromDesign",
                    new Type[] { typeof(Ship), typeof(string).MakeByRefType() });
                if (m1 != null)
                {
                    harmony.Patch(m1, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ReparationBuildProbe), nameof(Postfix_NoAmount))));
                    n++;
                }
                else HotfixLog.Warn("[SMS.1.REP] overload(Ship, out string) not found");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.REP] overload(Ship,out string): " + e.Message); }

            try
            {
                var m2 = AccessTools.Method(typeof(PlayerController), "CanBuildShipsFromDesign",
                    new Type[] { typeof(Ship), typeof(int), typeof(string).MakeByRefType() });
                if (m2 != null)
                {
                    harmony.Patch(m2, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ReparationBuildProbe), nameof(Postfix_Amount))));
                    n++;
                }
                else HotfixLog.Warn("[SMS.1.REP] overload(Ship, int, out string) not found");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.REP] overload(Ship,int,out string): " + e.Message); }

            HotfixLog.Info("[SMS.1.REP] armed on " + n + "/2 overload(s)"
                           + "  block=" + (HotfixParams.ReparationDesignBlock ? 1 : 0)
                           + "  probe=" + (HotfixParams.ReparationBuildProbe ? 1 : 0));
        }

        private static void Handle(Ship design, ref string reason, ref bool __result, string which)
        {
            try
            {
                if (design == null) return;

                bool isRep = false;
                try { isRep = design.isReparationDesign; } catch { }
                if (!isRep) return;

                string name = "";
                try { name = design.vesselName; } catch { }

                if (HotfixParams.ReparationBuildProbe)
                    HotfixLog.Info("[SMS.1.REP] probe(" + which + ") '" + name + "' -> " + __result
                                   + (string.IsNullOrEmpty(reason) ? "" : ("  reason=" + reason))
                                   + " status=" + (int)design.status);

                if (HotfixParams.ReparationDesignBlock && __result)
                {
                    __result = false;
                    reason = "reparation design";
                    HotfixLog.Info("[SMS.1.REP] blocked build of reparation design '" + name + "'");
                }
            }
            catch { }
        }

        internal static void Postfix_NoAmount(Ship design, ref string reason, ref bool __result)
            => Handle(design, ref reason, ref __result, "Ship,out string");

        internal static void Postfix_Amount(Ship design, int amount, ref string reason, ref bool __result)
            => Handle(design, ref reason, ref __result, "Ship,int,out string");
    }
}
