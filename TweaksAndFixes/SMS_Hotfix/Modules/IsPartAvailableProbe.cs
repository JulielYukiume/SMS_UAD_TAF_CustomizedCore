using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 非原版舰种：`Ship.IsPartAvailable` **只读探针**（零假定）。
    ///
    /// 设计原则（依据 2026-10-23 明确要求）：
    ///   · TAF 侧**不引入任何"哪个原版舰种是某个新舰种的近似"的假定**；
    ///   · 不引入额外配置文件；
    ///   · 只观测：谁在问"这个部件能不能用"、结果如何、失败时舰种/部件是什么。
    ///
    /// 观测点：
    ///   ① 实例 `bool IsPartAvailable(PartData)`                        postfix
    ///   ② 静态 `bool IsPartAvailable(PartData, Player, ShipType, Ship)` postfix
    ///
    /// 全部 read-only：不修改参数、不修改返回值、不 return false。
    /// </summary>
    internal static class IsPartAvailableProbe
    {
        private static int _calls, _falseCalls;
        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.IsPartProbe) { HotfixLog.Info("[SMS.1.Y3] IsPartAvailableProbe: disabled"); return; }
            int n = 0;
            try
            {
                var inst = AccessTools.Method(typeof(Ship), "IsPartAvailable", new Type[] { typeof(PartData) });
                if (inst != null)
                {
                    harmony.Patch(inst, postfix: new HarmonyMethod(AccessTools.Method(typeof(IsPartAvailableProbe), nameof(Postfix_Instance))));
                    n++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y3] instance patch: " + e.Message); }

            try
            {
                var st = AccessTools.Method(typeof(Ship), "IsPartAvailable",
                    new Type[] { typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship) });
                if (st != null)
                {
                    harmony.Patch(st, postfix: new HarmonyMethod(AccessTools.Method(typeof(IsPartAvailableProbe), nameof(Postfix_Static))));
                    n++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y3] static patch: " + e.Message); }

            HotfixLog.Info("[SMS.1.Y3] IsPartAvailableProbe: armed on " + n + "/2 (log-only, 零假定)");
        }

        internal static void Postfix_Instance(Ship __instance, PartData __0, bool __result)
        {
            try
            {
                try { HullTypeDump.DumpNow(); } catch { }
                string ty = ""; try { ty = __instance.shipType.name; } catch { }
                Log("instance", ty, PartTypeOf(__0), __result, null);
            }
            catch { }
        }

        internal static void Postfix_Static(PartData __0, Player __1, ShipType __2, Ship __3, bool __result)
        {
            try
            {
                string ty = ""; try { ty = __2 != null ? __2.name : ""; } catch { }
                string nm = ""; try { nm = __3 != null ? __3.vesselName : ""; } catch { }
                Log("static", ty, PartTypeOf(__0), __result, nm);
            }
            catch { }
        }

        private static readonly Dictionary<string, int> _keyCount = new Dictionary<string, int>();

        private static void Log(string which, string shipType, string partType, bool result, string vessel)
        {
            _calls++;
            if (!result) _falseCalls++;

            // 只关注非原版舰种；原版 6 种保持安静
            if (IsVanilla(shipType)) return;

            // 同一 (重载|舰种|部件|结果) 最多 3 条，避免 GetHull/RefreshSharedDesign 的循环刷屏
            string key = which + "|" + shipType + "|" + partType + "|" + result;
            int n; _keyCount.TryGetValue(key, out n);
            _keyCount[key] = n + 1;
            if (n < 3)
                HotfixLog.Info("[SMS.1.Y3] IsPartAvailable(" + which + "): type='" + shipType + "' part='" + partType
                               + "' -> " + result + (vessel != null && vessel.Length > 0 ? " ship='" + vessel + "'" : ""));

            // 每 2000 次给一条汇总，代替逐条刷屏
            if (_calls % 2000 == 0)
                HotfixLog.Info("[SMS.1.Y3] summary: calls=" + _calls + " false=" + _falseCalls
                               + " distinct=" + _keyCount.Count + " (逐条最多 3 条/组合)");
        }

        private static bool IsVanilla(string t)
        {
            if (string.IsNullOrEmpty(t)) return true;
            switch (t.ToLowerInvariant())
            {
                case "tb": case "dd": case "cl": case "ca": case "bc": case "bb": return true;
            }
            return false;
        }

        private static string PartTypeOf(PartData d)
        {
            try { return d.type; } catch { }
            return "?";
        }
    }
}
