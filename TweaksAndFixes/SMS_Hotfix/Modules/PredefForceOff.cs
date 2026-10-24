using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 当 TAF 的 `taf_force_no_predef_designs=1`（只允许慢速生成）时，
    /// **同时关掉"自定义战斗/设计器"里的预制设计取用**，避免 TAF 的预制路径在非原版舰种上
    /// 触发 `ArgumentOutOfRangeException`（TAF 内部用固定 6 舰种列表按 shipType 序号索引）。
    ///
    /// 依据（2026-10-24 实测）：
    ///   `[SMS.1.Y2] ShipType->int: 'cb' 0 -> 21` 之后紧接着
    ///   `ArgumentOutOfRangeException: Index was out of range … List<T>.set_Item`
    ///   ⇒ TAF 侧用"扩展序号"去写固定长度的舰种列表。
    ///
    /// 本模块只在 "taf_force_no_predef_designs=1" 时生效，不做其它判断。
    /// </summary>
    internal static class PredefForceOff
    {
        private static bool _enabled;
        private static int _n;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                // 惰性判定：Config 在 OnInitializeMelon 之后才解析，因此不在此处 return
                _enabled = true;

                int n = 0;
                // ① TAF 的预制取用入口（CampaignDesigns.RandomShip 的 TAF Prefix 之后仍会调用它）
                try
                {
                    var m = AccessTools.Method(typeof(TweaksAndFixes.PredefinedDesignsData), "GetRandomShip");
                    if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(PredefForceOff), nameof(Prefix_GetRandomShip)))); n++; }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.PF] GetRandomShip: " + e.Message); }

                // ② ShipsPerPlayer.RandomShipOfType（TAF 会在这里报 "has no valid predefined designs"）
                try
                {
                    var m2 = AccessTools.Method(typeof(ShipsPerPlayer), "RandomShipOfType");
                    if (m2 != null) { harmony.Patch(m2, prefix: new HarmonyMethod(AccessTools.Method(typeof(PredefForceOff), nameof(Prefix_RandomShipOfType)))); n++; }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.PF] RandomShipOfType: " + e.Message); }

                // ③ CampaignDesigns.RandomShip：让原版走自己的（慢速）路径
                try
                {
                    var m3 = AccessTools.Method(typeof(CampaignDesigns), "RandomShip");
                    if (m3 != null) { harmony.Patch(m3, prefix: new HarmonyMethod(AccessTools.Method(typeof(PredefForceOff), nameof(Prefix_CampaignDesignsRandomShip)))); n++; }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.PF] CampaignDesigns.RandomShip: " + e.Message); }

                HotfixLog.Info("[SMS.1.PF] predef force-off armed on " + n + "/3 (lazy: ForceNoPredefsInNewGames)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.PF] install: " + e); }
        }

        internal static bool ForceOn()
        {
            try { return TweaksAndFixes.Config.ForceNoPredefsInNewGames; } catch { return false; }
        }

        internal static bool Prefix_GetRandomShip(ref Ship.Store __result)
        {
            if (!_enabled || !ForceOn()) return true;
            __result = null;
            if (_n++ < 5) HotfixLog.Info("[SMS.1.PF] GetRandomShip -> null (force no predef)");
            return false;
        }

        internal static bool Prefix_RandomShipOfType(ref Ship.Store __result)
        {
            if (!_enabled || !ForceOn()) return true;
            __result = null;
            return false;      // 静默：不打印 TAF 的 "has no valid predefined designs"
        }

        internal static bool Prefix_CampaignDesignsRandomShip()
        {
            if (!ForceOn()) return true;
            return true;       // 交给原版（慢速生成）；仅用于记录
        }
    }
}
