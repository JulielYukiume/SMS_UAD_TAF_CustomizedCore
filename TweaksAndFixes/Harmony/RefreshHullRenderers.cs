using System;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.RH] 舯段色差（miscolored fore/aft sections）—— **最小化候选修复**。
    ///
    /// 依据：3.22 的 `31e3af3 Replace RefreshHull function`（Beta5）重写了 `Ship.RefreshHull`，
    /// 其中关键一步是在段（section）增删后调用 **`hull.OnRenderersWasChanged()`**：
    ///   - 原版在 `RefreshHull` 里重建 middles 之后，若没有触发 renderer 列表刷新，
    ///     部分前/后段的材质实例就停留在旧状态 ⇒ 表现为"舯段/首尾段色差"。
    ///   - 我们暂不做整段重写（那是 500+ IL 的移植），先只补这一步，验证是否即可消除色差。
    ///
    /// 若本补丁无效，再考虑完整移植 `Prefix_RefreshHull` + `UpdateSections`。
    /// </summary>
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_RefreshHull_Renderers
    {
        private static int _n;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static void Postfix_RefreshHull(Ship __instance, bool updateSections)
        {
            if (__instance == null || !updateSections) return;

            try
            {
                var hull = __instance.hull;
                if (hull == null) return;

                hull.OnRenderersWasChanged();

                if (_n++ < 20)
                    HotfixLog.Info("[SMS.1.RH] OnRenderersWasChanged after RefreshHull on '"
                                   + __instance.vesselName + "'  n=" + _n);
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.RH] postfix: " + e.Message);
            }
        }
    }
}
