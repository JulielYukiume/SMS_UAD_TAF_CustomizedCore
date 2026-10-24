using System;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.RS] 改造入口（`PlayerController.RefitShipsStart`）的状态重置 —— 移植 3.22 B4。
    ///
    /// 来源：Beta6 反编译 `Patch_PlayerController`（B4 未提交代码，33 + 56 IL）。
    ///
    /// 作用：
    ///   · Prefix：把目标船的各类缓存（尺寸比、转速、重量、材质缓存、科技缓存）置为无效，
    ///     避免用改造前的旧缓存计算改造后的设计；
    ///   · Postfix：重算稳性、重建材质缓存、重算科技集与重量/成本。
    ///
    /// 这解释了"改造后费用/性能显示不对"这一类问题的根因。
    /// </summary>
    [HarmonyPatch(typeof(PlayerController))]
    internal class Patch_PlayerController_RefitShipsStart
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerController.RefitShipsStart))]
        internal static void Prefix_RefitShipsStart(Il2CppSystem.Collections.Generic.List<Ship> shipList, Ship refitDesign)
        {
            try
            {
                if (shipList == null || shipList.Count == 0) return;

                var ship = shipList[0];
                if (ship == null) return;

                ship.cachedSizeAntiRatio = -1f;
                ship.cachedSizeRatio = -1f;
                ship.rotationSpeed_cached = -1f;
                ship.rotationChange_modified = -1f;
                ship.rotationChange_basic = -1f;
                ship.quartersWeight = -1f;

                ship.matsCache.Clear();
                ship.statEffectsCacheSplit.Clear();
                ship.statEffectsCache.Clear();
                ship.techsActualNew.Clear();
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.RS] refit prefix: " + e.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerController.RefitShipsStart))]
        internal static void Postfix_RefitShipsStart(Il2CppSystem.Collections.Generic.List<Ship> shipList, Ship refitDesign)
        {
            try
            {
                if (shipList == null || shipList.Count == 0) return;

                var ship = shipList[0];
                if (ship == null) return;

                ship.CalcInstability(true);

                ship.matsCache.Clear();
                var parts = ship.parts;
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        if (part == null) continue;
                        ship.matsCache.Add(part, ship.PartMats(part.data, true, part));
                    }
                }

                ship.CStats();

                ship.techsActualNew.Clear();
                var techs = ship.techsActual;
                if (techs != null)
                {
                    foreach (var t in techs)
                    {
                        if (t == null) continue;
                        ship.techsActualNew.Add(t);
                    }
                }

                ship.CalcWeightAndCost(true, true);
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.RS] refit postfix: " + e.Message);
            }
        }
    }
}
