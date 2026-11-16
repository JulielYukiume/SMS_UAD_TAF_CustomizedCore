using System;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.CIT] 动态装甲堡（dynamic citadel）尺寸约束 —— 移植 3.22 B5 的实现。
    ///
    /// 来源：Beta6 反编译 `Patch_Ship` 的三个小方法（B5 未提交代码）：
    ///   · `Prefix_GetCenterBetweenTwoEdgeFunnelsPositionZ`（25 IL）
    ///   · `Postfix_GetDynamicCitadelMaxZ`（53 IL）
    ///   · `Postfix_GetDynamicCitadelMinZ`（53 IL）
    ///
    /// 开关：TAF 配置参数 `taf_force_minimum_citadel_size`（0 = 完全不干预，保持原版）。
    /// 行为：启用后给动态装甲堡一个"最小尺寸"约束（前后各留 1/5 船体长度），
    ///       避免装甲堡被压缩到不可用。
    /// </summary>
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_Citadel
    {
        private static bool Enabled()
        {
            try { return Config.Param("taf_force_minimum_citadel_size", 0) != 0; }
            catch { return false; }
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetCenterBetweenTwoEdgeFunnelsPositionZ")]
        internal static bool Prefix_GetCenterBetweenTwoEdgeFunnelsPositionZ(Ship __instance,
                                                                           ref float __result,
                                                                           int minDC, int maxDC)
        {
            try
            {
                if (!Enabled()) return true;

                if (minDC + maxDC == 0)
                {
                    __result = 0f;
                    return false;
                }

                __result = (minDC + maxDC) / 2f;
                return false;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.CIT] center: " + e.Message);
                return true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetDynamicCitadelMaxZ")]
        internal static void Postfix_GetDynamicCitadelMaxZ(Ship __instance, bool isMagnitude,
                                                           bool isSomeRound, ref float __result)
        {
            try
            {
                if (!Enabled()) return;

                if (__instance.hullPartSizeZ == -1f)
                {
                    __instance.CheckHullSizeMaxMin();
                    if (__instance.hullPartSizeZ != 0f)
                    {
                        __result = 0f;
                        return;
                    }
                }

                float num = __instance.hullPartMaxZ - __instance.hullPartSizeZ / 5f;
                __instance.dynamicCitadelPosMaxZ = num;

                if (isMagnitude) num = 8f;
                if (isSomeRound) num = Mathf.RoundToInt(num * 10f) / 10f;

                __result = Mathf.Max(num, __result);
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.CIT] maxZ: " + e.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetDynamicCitadelMinZ")]
        internal static void Postfix_GetDynamicCitadelMinZ(Ship __instance, bool isMagnitude,
                                                           bool isSomeRound, ref float __result)
        {
            try
            {
                if (!Enabled()) return;

                if (__instance.hullPartSizeZ == -1f)
                {
                    __instance.CheckHullSizeMaxMin();
                    if (__instance.hullPartSizeZ != 0f)
                    {
                        __result = 0f;
                        return;
                    }
                }

                float num = __instance.hullPartSizeZ / 5f + __instance.hullPartMinZ;
                __instance.dynamicCitadelPosMaxZ = num;

                if (isMagnitude) num = 2f;
                if (isSomeRound) num = Mathf.RoundToInt(num * 10f) / 10f;

                __result = Mathf.Min(num, __result);
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.CIT] minZ: " + e.Message);
            }
        }
    }
}
