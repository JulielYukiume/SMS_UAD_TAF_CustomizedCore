using System.Collections.Generic;
using HarmonyLib;
using TAFHotfixBatch1;
using Il2Cpp;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.DECOR] 甲板装饰件（Decor）重写移植 + 碰撞修复。
    ///
    /// 来源：Beta6 的 `Patch_Decor::Prefix_Awake` 与 `Patch_Ship::Prefix_RefreshDecor`
    /// （这两个是 Beta6 独有的"甲板装饰重写"实现，见 docs/13 §2.2）。
    ///
    /// 与 Beta6 的**关键差异（我们的修复）**：
    /// Beta6 用 Prefix **接管并跳过** `Decor.Awake`，因此原版 Awake 里"装饰件重叠检查注册"没有执行，
    /// 导致救生艇等在炮塔回转范围内不再消失（docs/38 §3.1 已实测确认 Beta6 同样有此问题）。
    /// 我们改为：
    ///   · `Decor.Awake` 只做 **Postfix 补充**（设置 bounds / layer），**不跳过原方法**；
    ///   · `Ship.RefreshDecor` 采用 Beta6 的重写逻辑（收集 decor、按包围盒相交隐藏被遮挡的装饰件）。
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
                Bounds partBox = default;

                var parts = __instance.parts;
                foreach (var dec in __instance.decor)
                {
                    if (dec == null || !dec.check) continue;

                    bool hidden = false;
                    if (parts != null)
                    {
                        foreach (var part in parts)
                        {
                            if (part == null || part.gameObject == null) continue;
                            var data = part.data;
                            if (data != null && dec.forceIgnoreX != null && dec.forceIgnoreX.Contains(data.type))
                                continue;

                            var decBounds = dec.bounds;
                            Vector3 size = decBounds.size;
                            size.Scale(dec.maxOverlapSizeBox);
                            overlap = new Bounds(dec.transform.position, size);

                            partBox = new Bounds(part.transform.position, part.partVisualSize.size);

                            if (overlap.Intersects(partBox))
                            {
                                hidden = true;
                                break;
                            }
                        }
                    }

                    dec.Show(!hidden);
                }
            }
            catch (System.Exception e)
            {
                HotfixLog.Warn("[SMS.1.DECOR] RefreshDecor: " + e.Message);
            }

            return false;   // 我们自己完成了重写
        }
    }

    /// <summary>
    /// `Decor.Awake` **补充**（Postfix，不跳过原方法）：
    /// 原方法负责"重叠检查注册"，一旦被 Prefix 跳过就会导致救生艇/装饰件碰撞失效（Beta6 的问题）。
    /// 这里只补 Beta6 想做的两件小事：设置包围盒、放到 Decor 层。
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
            catch (System.Exception e)
            {
                HotfixLog.Warn("[SMS.1.DECOR] Awake postfix: " + e.Message);
            }
        }
    }
}
