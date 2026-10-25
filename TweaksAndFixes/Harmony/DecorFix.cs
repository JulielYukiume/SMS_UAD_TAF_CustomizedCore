using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.DECOR] 甲板装饰件（Decor）重写移植 + 碰撞修复（v3）。
    ///
    /// 来源：Beta6 的 `Patch_Ship::Prefix_RefreshDecor`（甲板装饰重写）。
    ///
    /// 判定规则（v3，2026-10-24 按实测 + Blender 层级反馈修正）：
    ///   **三者取"或"**，任一相交即隐藏救生艇等装饰件：
    ///   ① `part.partVisualSize`（炮塔主体/炮座几何体，v1 的行为）
    ///   ② `Part.deckSizes` / `Part.hangSizes` 碰撞体（近似"无高度"，因此**向上扩展**到模型全高再判）
    ///   ③ 炮管：遍历 `part` 下层名字含 `barrel`（小写）的物体，取其 Renderer 世界包围盒
    ///
    /// 为什么加 ③：`Visual` 下级的 3D 物体通常只有炮塔体，**不含炮管**；炮管是独立子物体，
    /// 名字里带 `barrel`。v1 只测 partVisualSize，所以炮管转过去时救生艇不消失。
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

                var parts = __instance.parts;
                Bounds overlap = default;

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

                            if (PartMatches(part, overlap))
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

        /// <summary>① 主体几何体 或 ② deck/hang 碰撞体 或 ③ 炮管，任一相交即可。</summary>
        private static bool PartMatches(Part part, Bounds decBox)
        {
            try
            {
                // ① 炮塔/炮座主体（v1 行为）
                var visual = part.partVisualSize;
                if (visual.size.sqrMagnitude > 0.0001f)
                {
                    var vBox = new Bounds(part.transform.position, visual.size);
                    if (vBox.Intersects(decBox)) return true;
                }

                // ② deckSizes / hangSizes（扁盒 → 向上扩展至模型全高）
                float fullHeight = Mathf.Max(visual.size.y, 0.1f);
                if (IntersectsFlatList(part.deckSizes, part.transform.position, fullHeight, decBox)) return true;
                if (IntersectsFlatList(part.hangSizes, part.transform.position, fullHeight, decBox)) return true;

                // ③ 炮管（名字含 barrel，小写）
                if (IntersectsBarrels(part.gameObject, decBox)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// deckSizes/hangSizes 的碰撞体几乎只有平面（高度 ~0.1），
        /// 因此把盒子的 Y 向按"模型全高"向上扩展，再做 3D 相交。
        /// </summary>
        private static bool IntersectsFlatList(Il2CppSystem.Collections.Generic.List<Rigidbody> list,
                                               Vector3 origin, float fullHeight, Bounds decBox)
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
                    var b = col.bounds;
                    b.center += origin;

                    // 向上扩展：保留底部，顶部拉到模型全高
                    float bottom = b.center.y - b.size.y * 0.5f;
                    float top = bottom + Mathf.Max(fullHeight, b.size.y);
                    var expanded = new Bounds(
                        new Vector3(b.center.x, (bottom + top) * 0.5f, b.center.z),
                        new Vector3(b.size.x, top - bottom, b.size.z));

                    if (expanded.Intersects(decBox)) return true;
                }
            }
            return false;
        }

        /// <summary>遍历所有子物体，名字含 "barrel"（不区分大小写）的，用其 Renderer 世界包围盒判定。</summary>
        private static bool IntersectsBarrels(GameObject root, Bounds decBox)
        {
            if (root == null) return false;

            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null) return false;

            foreach (var r in renderers)
            {
                if (r == null) continue;
                string n = r.gameObject.name;
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (r.bounds.Intersects(decBox)) return true;
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

        private static int _awakeEmpty;

        /// <summary>
        /// [SMS.1.DECOR] 前置拦截：**没有子渲染器**的 `Decor` 直接跳过原版 `Awake`。
        ///
        /// 为什么必须用 Prefix 而不是 Finalizer（2026-10-26 实测）：
        ///   `Decor.Awake` 是 IL2CPP 原生方法，它内部的 `Enumerable.Aggregate` 抛出的
        ///   `InvalidOperationException` 是在**原生侧**产生的，由 Il2CppInterop 的
        ///   `RaiseExceptionIfNecessary` 在托管 detour **之外**转换并抛出 ⇒
        ///   Harmony 的 Finalizer **捕获不到**（port31 加了 Finalizer 但日志仍在，即此原因）。
        ///   唯一可靠做法：在调用原方法**之前**判断，条件成立就不执行原方法。
        ///
        /// 判据：`GetComponentsInChildren&lt;Renderer&gt;()` 为空。
        /// 这类 Decor 没有可见网格，原版 `Awake` 的"重叠检查注册"本来也无对象可注册，
        /// 跳过它不会损失功能，只是避免抛异常。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("Awake")]
        internal static bool Prefix_Awake(Decor __instance)
        {
            try
            {
                if (__instance == null) return true;

                foreach (var r in __instance.GetComponentsInChildren<Renderer>())
                {
                    if (r != null) return true;   // 有渲染器 ⇒ 走原版
                }

                _awakeEmpty++;
                if (_awakeEmpty == 1 || _awakeEmpty % 50 == 0)
                    HotfixLog.Info("[SMS.1.DECOR] Decor.Awake 跳过空渲染器实例（累计 " + _awakeEmpty + " 次）");

                return false; // 跳过原方法，避免 Aggregate 空集合异常
            }
            catch
            {
                return true;
            }
        }

        private static int _awakeFail;

        /// <summary>
        /// （保留）原版 `Decor.Awake` 内部异常吸收 —— 对**托管**抛出的异常有效；
        /// 对 IL2CPP 原生侧抛出的 "Sequence contains no elements" 无效（见上），
        /// 真正的防线是 `Prefix_Awake`。这里仅作兜底，不删。
        /// </summary>
        [HarmonyFinalizer]
        [HarmonyPatch("Awake")]
        internal static Exception Finalizer_Awake(Exception __exception)
        {
            if (__exception == null) return null;

            // 只吞"空集合"这一种；其它异常保持原行为
            bool isEmptyAggregate = false;
            try
            {
                isEmptyAggregate = __exception is InvalidOperationException
                                   && __exception.Message != null
                                   && __exception.Message.Contains("Sequence contains no elements");
            }
            catch { }

            if (!isEmptyAggregate) return __exception;

            _awakeFail++;
            if (_awakeFail == 1 || _awakeFail % 50 == 0)
                HotfixLog.Info("[SMS.1.DECOR] Decor.Awake 空渲染器集合已忽略（累计 " + _awakeFail + " 次）");

            return null; // 吞掉
        }
    }
}
