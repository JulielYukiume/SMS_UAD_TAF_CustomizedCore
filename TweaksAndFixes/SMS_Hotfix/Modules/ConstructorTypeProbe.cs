using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 诊断（只读）：确认"进入构造器时的舰种参数"从哪里来。
    ///
    /// 背景：自定义战斗里选非原版舰种 → 提示"需要 shipType-av" 之类，说明
    /// 构造器拿到的 `ShipType` 不是 av（很可能是 null 或别的值），于是 hull 过滤失败。
    ///
    /// 观测：
    ///   ① `GameManager.ToConstructor(..., ShipType shipTypeNew, ...)`：打印 shipTypeNew
    ///   ② `Ui.ChangeShipTypeInSkirmish(int chooseShipType)`：打印传入的序号与解析结果
    /// </summary>
    internal static class ConstructorTypeProbe
    {
        private static int _n;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ConstructorTypeProbe) { HotfixLog.Info("[SMS.1.Y5] ConstructorTypeProbe: disabled"); return; }
            int n = 0;
            try
            {
                var m = AccessTools.Method(typeof(GameManager), "ToConstructor");
                if (m != null)
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConstructorTypeProbe), nameof(Post_ToConstructor))));
                    n++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y5] ToConstructor: " + e.Message); }

            try
            {
                var m2 = AccessTools.Method(typeof(Ui), "ChangeShipTypeInSkirmish");
                if (m2 != null)
                {
                    harmony.Patch(m2, prefix: new HarmonyMethod(AccessTools.Method(typeof(ConstructorTypeProbe), nameof(Pre_ChangeShipType))));
                    n++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y5] ChangeShipTypeInSkirmish: " + e.Message); }

            HotfixLog.Info("[SMS.1.Y5] ConstructorTypeProbe: armed on " + n + "/2 (log-only)");
        }

        internal static void Post_ToConstructor(bool newShip, Ship viewShip, bool allowEdit,
                                               Il2CppSystem.Collections.Generic.IEnumerable<Ship> allowEditMany,
                                               ShipType shipTypeNew, bool needCleanup, Player newPlayer)
        {
            try
            {
                if (_n++ > 30 && !HotfixParams.Verbose) return;
                string st = shipTypeNew != null ? shipTypeNew.name : "<null>";
                string vs = "<null>";
                try { if (viewShip != null) vs = viewShip.vesselName + "(type=" + (viewShip.shipType != null ? viewShip.shipType.name : "null") + ")"; } catch { }
                HotfixLog.Info("[SMS.1.Y5] ToConstructor: newShip=" + newShip + " shipTypeNew=" + st
                               + " viewShip=" + vs + " player=" + (newPlayer != null ? newPlayer.data.name : "?")
                               + "  n=" + _n);
            }
            catch { }
        }

        internal static void Pre_ChangeShipType(int chooseShipType)
        {
            try
            {
                if (_n++ > 30 && !HotfixParams.Verbose) return;
                // GetShipTypeFromIntNumber 是实例方法，这里不强行解析，只记录序号
                string resolved = "-";
                HotfixLog.Info("[SMS.1.Y5] ChangeShipTypeInSkirmish: chooseShipType=" + chooseShipType
                               + " -> GetShipTypeFromIntNumber=" + resolved + "  n=" + _n);
            }
            catch { }
        }
    }
}
