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
        private static int _hideLog;

        // ★★★ [SMS.1.DECOR] 2026-10-26：由 **Prefix(return false 跳过原版)** 改为 **Postfix**。
        //
        // 原因：原版 `Ship.RefreshDecor` 负责**生成甲板装饰物**（poisson 布点、deckPropCoverage 等）。
        //   我们之前用 Prefix 直接 `return false`，等于把原版整段跳过 ⇒ **甲板装饰物从未生成**
        //   （Beta6 的写法是"重写整个 RefreshDecor"，里面自带生成逻辑；我们只移植了"遮挡隐藏"那一半）。
        //
        // 现在：先让原版执行（生成 + 注册），再在我们的 Postfix 里做"被炮塔/炮管遮挡则隐藏"。
        private static int _rdCalls;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RefreshDecor))]
        internal static void Postfix_RefreshDecor(Ship __instance)
        {
            if (_rdCalls++ < 5)
                HotfixLog.Dbg("[SMS.2.PROP] Postfix_RefreshDecor called (n=" + _rdCalls + ")");
            if (__instance == null) return;

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

                // ★ [SMS.1.DECOR] 遮挡隐藏默认关闭（decor_hide_occluded=0）。
                //   实测（2026-10-26 port45）：即使把 RefreshDecor 改成 Postfix 让原版先生成，
                //   甲板装饰物仍然不可见 ⇒ 说明是我们的隐藏逻辑把它们全 Show(false) 了
                //   （dec.bounds 可能为默认值 ⇒ 与任意 part 相交 ⇒ 全部隐藏）。
                //   先关掉这段，确认装饰物恢复；确认后再逐个修判定条件。
                if (!HotfixParams.DecorHideOccluded)
                {
                    if (_hideLog++ < 3)
                        HotfixLog.Info("[SMS.1.DECOR] 遮挡隐藏已禁用 (decor_hide_occluded=0)，decor=" 
                                       + (__instance.decor != null ? __instance.decor.Count : -1));
                    return;
                }

                var parts = __instance.parts;
                Bounds overlap = default;

                foreach (var dec in __instance.decor)
                {
                    if (dec == null || !dec.check) continue;

                    var decBounds = dec.bounds;
                    Vector3 size = decBounds.size;

                    // ★ 合理性检查（2026-11-03）：
                    //   `dec.bounds` 在部分装饰/早期阶段为默认值（size≈0），
                    //   直接参与相交判定会命中所有部件 ⇒ 装饰物被"全隐"。
                    //   这里跳过异常 bounds，只对"真实尺寸"的装饰做碰撞隐藏。
                    float mag = size.sqrMagnitude;
                    if (mag < 1e-4f) continue;                 // 尺寸为零：跳过（保持可见）
                    if (size.x > 200f || size.y > 200f || size.z > 200f) continue;  // 明显异常：跳过

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
}

    /// <summary>
    /// [SMS.2.RH] 吞掉 `Decor.Awake` 抛出的 `InvalidOperationException: Sequence contains no elements`。
    ///
    /// 原因：原版 `Decor.Awake` 里有 `Enumerable.Aggregate(...)`，当装饰物在 Awake 时
    /// **还没有任何子渲染器**（主菜单封面船、预制体预热的常见情形）就会抛异常。
    /// 这属于原版缺陷，异常会以 ERROR 级别刷屏（每个装饰物一次）。
    ///
    /// 用 Harmony **Finalizer** 只"清除异常"，不改变 Awake 已完成的任何工作：
    ///   · 装饰物该注册的已经注册（异常发生在 Awake 末尾的 Aggregate 处）；
    ///   · 不需要像 `decor_awake_guard` 那样整段替代原版（那会丢初始化）。
    /// 开关：`decor_awake_swallow`（默认 1）。
    /// </summary>
