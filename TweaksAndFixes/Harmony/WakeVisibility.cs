using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.2.WAKE] 可见性：**直接复用原版机制**。
    ///
    /// 原版通过 `Ship.ShowForOthers(bool show, bool fake)` 与
    /// `Torpedo.ShowForOthers(bool show, bool fake)` 决定"实体是否呈现给玩家"
    /// （未探测的敌舰/敌雷会被 show=false）。
    ///
    /// 我们 patch 这两个方法记录状态，绘制航迹时：
    ///   · 记录 false ⇒ 不画（与原版本体一致）
    ///   · 记录 true  ⇒ 画
    ///   · 未记录     ⇒ fail-open（保持旧行为，避免误隐藏）
    ///
    /// 这样航迹可见性与本体可见性由同一开关驱动，不会不同步。
    /// </summary>
    internal static class WakeVisibility
    {
        private static readonly Dictionary<int, bool> ShipVis = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> TorpVis = new Dictionary<int, bool>();
        internal static int TorpShownTrue;
        internal static int TorpShownFalse;

        internal static void RecordShip(int id, bool show) { ShipVis[id] = show; }
                internal static void RecordTorp(int id, bool show)
        {
            TorpVis[id] = show;
            if (show) TorpShownTrue++; else TorpShownFalse++;
        }

        internal static bool? ShipFlag(Ship s)
        {
            try
            {
                if (s == null) return null;
                bool v;
                if (ShipVis.TryGetValue(s.GetInstanceID(), out v)) return v;
            }
            catch { }
            return null;
        }

        internal static bool? TorpFlag(Torpedo t)
        {
            try
            {
                if (t == null) return null;
                bool v;
                if (TorpVis.TryGetValue(t.GetInstanceID(), out v)) return v;
            }
            catch { }
            return null;
        }

        internal static int ShipCount { get { return ShipVis.Count; } }
        internal static int TorpCount { get { return TorpVis.Count; } }

        /// <summary>
        /// [SMS.2.WAKE] 舰船可见性。
        ///
        /// ⚠ 2026-11-10 修正：**玩家自己的船永远画**。
        /// 原因：`Ship.ShowForOthers(false, …)` 不只为"敌方未发现"调用，
        /// 某些界面/相机切换也会对我们自己的船调用一次 false；而此后再没有 true 调用，
        /// 于是 `ShipVis` 里残留 false ⇒ 我方船的航迹被误隐藏（简化海面模式下更容易察觉）。
        /// 敌船仍按原版标记判定（防剧透）。
        /// </summary>
        internal static bool ShipVisible(Ship target)
        {
            if (!HotfixParams.WakeVisibleOnly) return true;

            bool isHuman = false;
            try { isHuman = target != null && target.player != null && !target.player.isAi; } catch { }
            if (isHuman) return true;

            var flag = ShipFlag(target);
            if (flag.HasValue) return flag.Value;
            return true;
        }

        internal static bool TorpedoVisible(Torpedo t)
        {
            if (!HotfixParams.WakeVisibleOnly) return true;
            var flag = TorpFlag(t);
            if (flag.HasValue) return flag.Value;
            // 原版未记录：鱼雷默认**隐藏**（防剧透），可通过 torp_wake_fail_open=1 改回放行
            return HotfixParams.TorWakeFailOpen;
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.ShowForOthers))]
    internal static class Patch_Ship_ShowForOthers
    {
        [HarmonyPostfix]
        internal static void Postfix(Ship __instance, bool show, bool fake)
        {
            try { if (__instance != null) WakeVisibility.RecordShip(__instance.GetInstanceID(), show); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Torpedo), nameof(Torpedo.ShowForOthers))]
    internal static class Patch_Torpedo_ShowForOthers
    {
        [HarmonyPostfix]
        internal static void Postfix(Torpedo __instance, bool show, bool fake)
        {
            try { if (__instance != null) WakeVisibility.RecordTorp(__instance.GetInstanceID(), show); }
            catch { }
        }
    }
}
