using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 非原版舰种：**只读探针**（log 层）。
    ///
    /// 目的：在不动任何行为的前提下，观察"非原版舰种"在哪些调用点出现、以什么形式出现，
    /// 从而判断通用修复应该前移到哪一层（而不是逐个 postfix 兜底）。
    ///
    /// 覆盖的观察点（全部 read-only）：
    ///   ① Ui.GetShipTypeFromIntNumber(int)     返回 null 时记录（int 越界）
    ///   ② Ui.GetIntNumberFromShipType(ShipType) 返回 -1 时记录（原版 6 种之外）
    ///   ③ Ui.AddedShipInShipList(Ship)         记录新舰种的船/设计进入列表
    ///   ④ Ship.AddPart(Part)                   记录新舰种实际被挂上的部件类型
    ///
    /// 参数：shiptype_probe=1（默认开，只打日志）；shiptype_probe_verbose=0
    /// </summary>
    internal static class ShipTypeProbe
    {
        private static readonly HashSet<string> _vanilla =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tb", "dd", "cl", "ca", "bc", "bb" };

        private static readonly Dictionary<string, int> _hits = new Dictionary<string, int>();
        private static int _intMiss, _numMiss, _listHits, _partHits;
        // 存活探针：证明我们的 postfix 确实被执行过（用于区分"没跑"与"跑了但无事可做"）
        private static int _aliveInt, _aliveNum;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ShipTypeProbe) { HotfixLog.Debug("[SMS.1.Y] ShipTypeProbe: disabled"); return; }
            int n = 0;
            n += Patch(harmony, typeof(Ui), "GetShipTypeFromIntNumber", nameof(Postfix_GetTypeFromInt), false);
            n += Patch(harmony, typeof(Ui), "GetIntNumberFromShipType", nameof(Postfix_GetNumFromType), false);
            n += Patch(harmony, typeof(Ui), "AddedShipInShipList", nameof(Prefix_AddedShipInShipList), true);
            n += Patch(harmony, typeof(Ship), "AddPart", nameof(Prefix_AddPart), true);
            HotfixLog.Debug("[SMS.1.Y] ShipTypeProbe: armed on " + n + "/4 method(s)  (log-only)");
        }

        private static int Patch(HarmonyLib.Harmony harmony, Type t, string name, string patchName, bool prefix)
        {
            try
            {
                var m = AccessTools.Method(t, name);
                if (m == null) { HotfixLog.Warn("[SMS.1.Y] " + t.Name + "." + name + " not found"); return 0; }
                var hm = new HarmonyMethod(AccessTools.Method(typeof(ShipTypeProbe), patchName));
                if (prefix) harmony.Patch(m, prefix: hm); else harmony.Patch(m, postfix: hm);
                return 1;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y] patch " + name + " failed: " + e.Message); return 0; }
        }

        // ① int -> ShipType
        internal static void Postfix_GetTypeFromInt(int __0, ShipType __result)
        {
            try
            {
                if (_aliveInt++ == 0)
                    HotfixLog.Debug("[SMS.1.Y] probe alive: GetShipTypeFromIntNumber came through (first result="
                                   + (__result != null ? __result.name : "null") + ", arg=" + __0 + ")");
                if (__result != null) return;
                _intMiss++;
                if (_intMiss <= 20 || HotfixParams.Verbose)
                    HotfixLog.Debug("[SMS.1.Y] int->ShipType: " + __0 + " -> null (越界；当前硬编码 0..5)  total=" + _intMiss);
            }
            catch { }
        }

        // ② ShipType -> int
        internal static void Postfix_GetNumFromType(ShipType __0, int __result)
        {
            try
            {
                if (_aliveNum++ == 0)
                    HotfixLog.Debug("[SMS.1.Y] probe alive: GetIntNumberFromShipType came through (first result="
                                   + __result + ", type='" + NameOf(__0) + "')");
                if (__result >= 0) return;
                string nm = NameOf(__0);
                _numMiss++;
                Count("int-miss:" + nm);
                if (_numMiss <= 20 || HotfixParams.Verbose)
                    HotfixLog.Debug("[SMS.1.Y] ShipType->int: '" + nm + "' -> -1 (原版 6 种之外)  total=" + _numMiss);
            }
            catch { }
        }

        // ③ 设计/船进入列表
        internal static void Prefix_AddedShipInShipList(Ship __0)
        {
            try
            {
                var t = TypeOf(__0);
                if (t == null || _vanilla.Contains(t)) return;
                _listHits++;
                Count("list:" + t);
                if (_listHits <= 30 || HotfixParams.Verbose)
                    HotfixLog.Debug("[SMS.1.Y] AddedShipInShipList: 新舰种 '" + t + "' 进入列表 (ship='" + NameOf(__0) + "')  total=" + _listHits);
            }
            catch { }
        }

        // ④ 部件挂载
        internal static void Prefix_AddPart(Ship __instance, Part __0)
        {
            try
            {
                var t = TypeOf(__instance);
                if (t == null || _vanilla.Contains(t)) return;
                _partHits++;
                string pt = "?";
                try { pt = __0.data.type; } catch { }
                Count("part:" + t + "/" + pt);
                if (_partHits <= 30 || HotfixParams.Verbose)
                    HotfixLog.Debug("[SMS.1.Y] AddPart: 新舰种 '" + t + "' 挂上部件 '" + pt + "'  total=" + _partHits);
            }
            catch { }
        }

        private static string TypeOf(Ship s)
        {
            try { return s.shipType.name; } catch { }
            try { return s.ToStore().shipType; } catch { }
            return null;
        }

        private static string NameOf(ShipType t)
        {
            try { return t.name; } catch { }
            return "?";
        }

        private static string NameOf(Ship s)
        {
            try { return s.vesselName; } catch { }
            return "?";
        }

        private static void Count(string k)
        {
            try
            {
                int n; _hits.TryGetValue(k, out n); _hits[k] = n + 1;
            }
            catch { }
        }
    }
}
