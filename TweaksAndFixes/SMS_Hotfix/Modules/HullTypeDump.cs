using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 船体舰种归属只读诊断（hull_type_dump）。
    ///
    /// 背景：已保存的设计能从存档里的 `shipType` 字符串正确反读舰种；
    /// 但**未保存**的设计（构造器里）必须由 hull 的 `type(...)` 推算归属 —— 这一步对非原版舰种失效。
    ///
    /// 本模块在 GameData 就绪后（第一次进入 World 状态）遍历 `G.GameData.parts`，
    /// 对所有 `type == "hull"` 的 PartData 打印：
    ///     name | paramx 是否含 "type" 键 | shipType（null / 名字） | param 前若干字符
    ///
    /// 只读、默认开（`hull_type_dump=1`），每个进程只跑一次。
    /// </summary>
    internal static class HullTypeDump
    {
        private static bool _done;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HullTypeDump) { HotfixLog.Info("[SMS.1.H] HullTypeDump: disabled"); return; }
            try
            {
                var m = AccessTools.Method(typeof(GameManager), "ChangeState");
                if (m == null) { HotfixLog.Warn("[SMS.1.H] ChangeState not found"); return; }
                harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(HullTypeDump), nameof(Postfix))));
                HotfixLog.Info("[SMS.1.H] HullTypeDump: armed (one dump per process)");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.H] install: " + e.Message); }
        }

        internal static void Postfix(GameManager.GameState newState)
        {
            try
            {
                if (_done) return;
                if (newState != GameManager.GameState.World) return;
                _done = true;
                Dump();
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.H] postfix: " + e.Message); }
        }

        internal static void DumpNow()
        {
            if (_done) return;
            _done = true;
            Dump();
        }

        private static void Dump()
        {
            try
            {
                var gd = G.GameData;
                if (gd == null) { HotfixLog.Warn("[SMS.1.H] GameData null"); return; }
                var parts = gd.parts;
                if (parts == null) { HotfixLog.Warn("[SMS.1.H] GameData.parts null"); return; }

                int total = 0, hulls = 0, withTypeKey = 0, nullShipType = 0, mismatch = 0;
                int printed = 0;
                foreach (var kv in parts)
                {
                    total++;
                    var pd = kv.Value;
                    if (pd == null) continue;
                    string ptype = null;
                    try { ptype = pd.type; } catch { }
                    if (ptype != "hull") continue;
                    hulls++;

                    bool hasTypeKey = false;
                    string typeVal = null;
                    try
                    {
                        if (pd.paramx != null && pd.paramx.ContainsKey("type"))
                        {
                            hasTypeKey = true;
                            var list = pd.paramx["type"];
                            if (list != null && list.Count > 0) typeVal = list[0];
                        }
                    }
                    catch { }

                    string stName = null;
                    bool stNull = true;
                    try
                    {
                        var st = pd.shipType;
                        if (st != null) { stNull = false; stName = st.name; }
                    }
                    catch { }

                    if (hasTypeKey) withTypeKey++;
                    if (stNull) nullShipType++;
                    if (hasTypeKey && !stNull && typeVal != null && stName != null && typeVal != stName) mismatch++;

                    if (printed < 500)
                    {
                        printed++;
                        string param = "";
                        try { param = pd.param; } catch { }
                        if (param != null && param.Length > 40) param = param.Substring(0, 40);

                        HotfixLog.Info("[SMS.1.H] hull '" + kv.Key + "' | typeKey=" + (hasTypeKey ? "'" + typeVal + "'" : "<none>")
                                       + " | shipType=" + (stNull ? "<null>" : "'" + stName + "'")
                                       + " | param=" + param);
                    }
                }

                HotfixLog.Warn("[SMS.1.H] summary: parts=" + total + " hulls=" + hulls
                               + " withTypeKey=" + withTypeKey + " shipTypeNull=" + nullShipType
                               + " typeMismatch=" + mismatch);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.H] dump: " + e.Message); }
        }
    }
}
