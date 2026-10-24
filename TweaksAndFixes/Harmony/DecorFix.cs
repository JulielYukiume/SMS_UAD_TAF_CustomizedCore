using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.DECOR] 甲板装饰件（Decor）重写移植 + 碰撞修复（v2）。
    ///
    /// 来源：Beta6 的 `Patch_Ship::Prefix_RefreshDecor`（甲板装饰重写）。
    ///
    /// **v2 的改动（2026-10-24，按实测反馈）**：
    /// v1 用 `part.partVisualSize` 做相交判定，导致"炮座几何体"就足以隐藏救生艇，
    /// 与原版不符。原版只在 **deckSize / hangSize 碰撞体积** 相交时才隐藏。
    /// v2 改为：
    ///   · 只使用 `PartData.deckSizes` / `PartData.hangSizes`（List&lt;Rigidbody&gt;）的碰撞体包围盒；
    ///   · **不**纳入"理论射界/回转范围"（duke 后续会重写有效射界逻辑，届时再接入）。
    ///
    /// 关于 `Decor.Awake`：Beta6 用 Prefix 跳过原方法，导致原版"重叠检查注册"丢失
    /// （docs/38 §3.1 实测：Beta6 自身救生艇不消失）。我们改为 **Postfix 补充**，不跳过原方法。
    /// </summary>
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_RefreshDecor
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.RefreshDecor))]
        internal static bool Prefix_RefreshDecor(Ship __instance)
        {
            if (__instance == null) return true;

            try
            {
                if (__instance.decor == null)
                    __instance.decor = new Il2CppSystem.Collections.Generic.List<Decor>();

                __instance.decor.Clear();

                var hullAndParts = __instance.hullAndParts;
                if (hullAndParts != null)
                {
                    foreach (var part in hullAndParts)
                    {
                        if (part == null || part.gameObject == null) continue;
                        foreach (var dec in part.gameObject.GetComponentsInChildren<Decor>())
                        {
                            if (dec == null || dec.gameObject == null) continue;
                            dec.part = part;
                            __instance.decor.Add(dec);
                        }
                    }
                }

                Bounds overlap = default;
                var parts = __instance.parts;

                foreach (var dec in __instance.decor)
                {
                    if (dec == null || !dec.check) continue;

                    var decBounds = dec.bounds;
                    Vector3 size = decBounds.size;
                    size.Scale(dec.maxOverlapSizeBox);
                    overlap = new Bounds(dec.transform.position, size);

                    bool hidden = false;
                    var ignore = dec.forceIgnoreX;

                    if (parts != null)
                    {
                        foreach (var part in parts)
                        {
                            if (part == null || part.gameObject == null) continue;
                            var data = part.data;
                            if (data == null) continue;
                            if (ignore != null && ignore.Contains(data.type)) continue;

                            if (IntersectsDeckOrHang(part, overlap))
                            {
                                hidden = true;
                                break;
                            }
                        }
                    }

                    dec.Show(!hidden);
                }
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.DECOR] RefreshDecor: " + e.Message);
            }

            return false;
        }

        /// <summary>
        /// 只检查 deckSizes / hangSizes（炮座与吊臂的碰撞体积）。
        /// 这些 Rigidbody 属于部件模板，坐标以模板原点为基准，因此用部件实例的世界坐标做平移。
        /// </summary>
        private static bool IntersectsDeckOrHang(Part part, Bounds decBox)
        {
            try
            {
                // Part.deckSizes / hangSizes 是部件实例上的碰撞体积（炮座/吊臂），
                // 其 collider.bounds 已是世界坐标，无需再平移。
                if (IntersectsList(part.deckSizes, decBox)) return true;
                if (IntersectsList(part.hangSizes, decBox)) return true;
            }
            catch { }
            return false;
        }

        private static bool IntersectsList(Il2CppSystem.Collections.Generic.List<Rigidbody> list, Bounds decBox)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var rb = list[i];
                if (rb == null) continue;
                var colliders = rb.GetComponentsInChildren<Collider>();
                if (colliders == null) continue;
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    if (col.bounds.Intersects(decBox)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// `Decor.Awake` **补充**（Postfix，不跳过原方法）：
    /// 原方法负责"重叠检查注册"，被 Prefix 跳过会导致碰撞失效（Beta6 的问题）。
    /// </summary>
    [HarmonyPatch(typeof(Decor))]
    internal class Patch_DecorAwake
    {
        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        internal static void Postfix_Awake(Decor __instance)
        {
            if (__instance == null) return;
            if (!Application.isPlaying) return;

            try
            {
                Bounds bounds = default;
                bool any = false;
                foreach (var renderer in __instance.GetComponentsInChildren<Renderer>())
                {
                    if (renderer == null) continue;
                    if (!any) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    any = true;
                }
                if (any) __instance.bounds = bounds;

                int decorLayer = LayerMask.NameToLayer("Decor");
                if (decorLayer >= 0)
                    Util.SetLayer(__instance.gameObject, decorLayer);
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.DECOR] Awake postfix: " + e.Message);
            }
        }
    }
}
