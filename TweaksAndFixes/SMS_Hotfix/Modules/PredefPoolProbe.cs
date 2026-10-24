using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using TweaksAndFixes;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 预制池：别名桶回退（方案 A，见 docs/46）。
    ///
    /// 背景：TAF 的 LoadPredefSets 逆序处理包，CSV 最后一个包（stock）成为"基座"并整体替换设计池；
    /// 其余包以 AddShip 叠加，但设计被临时打上别名 "{包序}_{玩家}" 才入库。
    /// 于是真实玩家名（britain）的 shipsPerType 桶里只有 stock 内容，其它包（含 SMS 的 1890 设计）在别名桶里。
    /// TAF 的 ShipsPerPlayer Prefix 只查真实键 ⇒ 缺键就报 "has no valid predefined designs" 并返回 null。
    ///
    /// 本模块在 TAF 的判断**之前**（Priority.First）插一脚：
    ///   真实键存在 → 不干预（交回 TAF）
    ///   真实键缺失 → 在 shipsPerType 中查找形如 "{i}_{玩家}" 的别名桶，命中则随机取一份返回
    ///   别名桶也没有 → 不干预（让 TAF 自己报错，保持原语义）
    /// </summary>
    internal static class PredefPoolProbe
    {
        private static int _served, _missed, _logged, _grLogged;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.PredefAliasFallback && !HotfixParams.PredefPoolProbe)
            {
                HotfixLog.Info("[SMS.1.P] PredefAliasFallback: disabled");
                return;
            }
            try
            {
                // ★ 有多个重载：必须全部挂上，否则 TAF 在"未被我们接管的重载"上仍会打 ERROR。
                var all = AccessTools.GetDeclaredMethods(typeof(ShipsPerPlayer));
                int patched = 0;
                foreach (var m in all)
                {
                    if (m == null || m.Name != "RandomShipOfType") continue;
                    var hm = new HarmonyMethod(AccessTools.Method(typeof(PredefPoolProbe), nameof(Prefix)));
                    hm.priority = Priority.First;   // 必须早于 TAF 自己的 Prefix（它返回 false 会跳过后续 prefix）
                    harmony.Patch(m, prefix: hm);
                    patched++;
                    HotfixLog.Info("[SMS.1.P] patched RandomShipOfType overload: " + m);
                }
                if (patched == 0) { HotfixLog.Warn("[SMS.1.P] RandomShipOfType not found"); return; }
                // ★ 追加：直接观察 TAF 的查找入口 GetRandomShip（ShipsPerPlayer 层根本没被调用到）
                var gm = AccessTools.Method(typeof(TweaksAndFixes.PredefinedDesignsData), "GetRandomShip");
                if (gm != null)
                {
                    var pm = new HarmonyMethod(AccessTools.Method(typeof(PredefPoolProbe), nameof(Postfix_GetRandomShip)));
                    harmony.Patch(gm, postfix: pm);
                    HotfixLog.Info("[SMS.1.P] GetRandomShip probe armed");
                }
                HotfixLog.Info("[SMS.1.P] PredefAliasFallback: armed  enabled=" + HotfixParams.PredefAliasFallback
                               + " probe=" + HotfixParams.PredefPoolProbe + "  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.P] install failed: " + e); }
        }

        internal static bool Prefix(ShipsPerPlayer __instance, ref Ship.Store __result, Player player, ShipType shipType)
        {
            try
            {
                if (shipType == null || player == null) return true;

                string type = null;
                try { type = shipType.name; } catch { }
                if (string.IsNullOrEmpty(type)) return true;

                bool hasReal = false;
                try { hasReal = __instance.shipsPerType.ContainsKey(type); } catch { }
                if (hasReal) return true;              // 真实键存在：交给 TAF

                if (!HotfixParams.PredefAliasFallback) return true;

                string raw = RawName(player);
                if (string.IsNullOrEmpty(raw)) return true;

                // 注册表级查找：同玩家（真名 + 别名桶）任意年份里的该舰种
                var pool = new System.Collections.Generic.List<Ship.Store>();
                try
                {
                    var cd = CampaignController.Instance._currentDesigns;
                    if (cd != null)
                    {
                        foreach (var spy in cd.shipsPerYear)
                        {
                            foreach (var spp in spy.Value.shipsPerPlayer)
                            {
                                string k = spp.Key;
                                if (k == null) continue;
                                if (k != raw && !k.EndsWith("_" + raw, StringComparison.Ordinal)) continue;
                                Il2CppSystem.Collections.Generic.List<Ship.Store> list;
                                if (!spp.Value.shipsPerType.TryGetValue(type, out list) || list == null) continue;
                                for (int i = 0; i < list.Count; i++)
                                {
                                    var s = list[i];
                                    if (s != null) pool.Add(s);
                                }
                            }
                        }
                    }
                }
                catch (Exception e1) { HotfixLog.Debug("[SMS.1.P] registry scan: " + e1.Message); }

                if (pool.Count == 0)
                {
                    _missed++;
                    if (_missed <= 6)
                    {
                        LogKeys(__instance, type, raw);
                        LogRegistry(type, raw);         // ★ 追加：注册表侧该玩家各年份/各包是否有货
                    }
                    return true;                        // 别名桶也没有：保持原语义（TAF 会报错）
                }

                int idx = UnityEngine.Random.Range(0, pool.Count);
                __result = pool[idx];
                _served++;
                HotfixLog.Info("[SMS.1.P] alias fallback: player=" + raw + " type=" + type
                               + " -> served from alias bucket (" + pool.Count + " candidate(s), total served=" + _served + ")");
                return false;                           // 跳过 TAF（它会误报缺舰种）
            }
            catch (Exception e) { HotfixLog.Debug("[SMS.1.P] prefix: " + e.Message); return true; }
        }

        /// <summary>诊断：GetRandomShip 返回 null 时，打印各包对该玩家的可见性。</summary>
        internal static void Postfix_GetRandomShip(TweaksAndFixes.PredefinedDesignsData __instance, Player player, ShipType type, int desiredYear, ref Ship.Store __result)
        {
            try
            {
                if (__result != null) return;
                if (_grLogged >= 6) return;
                _grLogged++;
                string pName = "?";
                try { pName = player != null ? player.data.name : "<null>"; } catch { }
                string tName = "?";
                try { tName = type != null ? type.name : "<null>"; } catch { }

                var sb = new StringBuilder();
                var fld = typeof(TweaksAndFixes.PredefinedDesignsData).GetField("_predefFileData",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var list = fld != null ? fld.GetValue(__instance) as System.Collections.IList : null;
                if (list != null)
                {
                    int i = 0;
                    foreach (var item in list)
                    {
                        if (item == null) continue;
                        var t2 = item.GetType();
                        object fn = t2.GetField("filename")?.GetValue(item);
                        object nm = t2.GetField("name")?.GetValue(item);
                        object ld = t2.GetField("loadedData")?.GetValue(item);
                        object yr = t2.GetField("yearsPerPlayer")?.GetValue(item);
                        bool hasP = false; int nYears = 0;
                        try
                        {
                            var tryGet = yr != null ? yr.GetType().GetMethod("ContainsKey") : null;
                            if (tryGet != null) hasP = (bool)tryGet.Invoke(yr, new object[] { pName });
                            var cnt = yr != null ? yr.GetType().GetProperty("Count") : null;
                            if (cnt != null) nYears = (int)cnt.GetValue(yr);
                        }
                        catch { }
                        sb.Append(" [#").Append(i).Append(' ').Append(nm ?? fn)
                          .Append(" loaded=").Append(ld).Append(" players=").Append(nYears)
                          .Append(" hasPlayer=").Append(hasP).Append(']');
                        i++;
                    }
                }
                HotfixLog.Warn("[SMS.1.P] GetRandomShip null: player='" + pName + "' type='" + tName + "' year=" + desiredYear + sb.ToString());
            }
            catch { }
        }
        private static string RawName(Player p)
        {
            try { var pd = p.data; if (pd != null && !string.IsNullOrEmpty(pd.name)) return pd.name; } catch { }
            try { return p.Name(false); } catch { }
            return null;
        }

        /// <summary>诊断：注册表 shipsPerYear 里该玩家（含别名）在各年份的舰种分布。</summary>
        private static void LogRegistry(string type, string raw)
        {
            try
            {
                var cd = CampaignController.Instance._currentDesigns;
                if (cd == null) { HotfixLog.Warn("[SMS.1.P] registry null"); return; }
                int turn = 0;
                try { turn = CampaignController.Instance.CurrentDate.turn; } catch { }

                var sb = new StringBuilder();
                int years = 0;
                foreach (var spy in cd.shipsPerYear)
                {
                    if (years++ >= 8) { sb.Append(" …"); break; }
                    var parts = new StringBuilder();
                    foreach (var spp in spy.Value.shipsPerPlayer)
                    {
                        string k = spp.Key;
                        if (k == null) continue;
                        if (k != raw && !k.EndsWith("_" + raw, StringComparison.Ordinal)) continue;
                        parts.Append(' ').Append(k).Append('[');
                        int t = 0, cnt = 0;
                        foreach (var spt in spp.Value.shipsPerType)
                        {
                            cnt += spt.Value != null ? spt.Value.Count : 0;
                            if (spt.Key == type) t = spt.Value != null ? spt.Value.Count : 0;
                        }
                        parts.Append("types=").Append(spp.Value.shipsPerType.Count)
                             .Append(",total=").Append(cnt)
                             .Append(",ofType=").Append(t).Append(']');
                    }
                    sb.Append(" year=").Append(spy.Key).Append(parts.ToString());
                }
                HotfixLog.Warn("[SMS.1.P] registry(turn=" + turn + ") for '" + raw + "' type '" + type + "':" + sb.ToString());
            }
            catch (Exception e) { HotfixLog.Debug("[SMS.1.P] LogRegistry: " + e.Message); }
        }
        /// <summary>诊断：找出这个 spp 实例在注册表中的真实身份（年份 + 桶名）。</summary>
        private static string Identity(ShipsPerPlayer inst)
        {
            try
            {
                var cd = CampaignController.Instance._currentDesigns;
                if (cd == null) return "<no-registry>";
                foreach (var spy in cd.shipsPerYear)
                {
                    foreach (var spp in spy.Value.shipsPerPlayer)
                    {
                        if (ReferenceEquals(spp.Value, inst))
                            return "year=" + spy.Key + " bucket='" + spp.Key + "'";
                    }
                }
            }
            catch { }
            return "<not-found>";
        }
        private static void LogKeys(ShipsPerPlayer inst, string type, string raw)
        {
            try
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var kv in inst.shipsPerType)
                {
                    if (n++ >= 14) { sb.Append(" …"); break; }
                    sb.Append(kv.Key).Append('(').Append(kv.Value != null ? kv.Value.Count : 0).Append(") ");
                }
                HotfixLog.Warn("[SMS.1.P] missing type '" + type + "' for player '" + raw + "' -> instance " + Identity(inst) + " keys: " + sb.ToString());
            }
            catch { }
        }

        internal static void LogSummary()
        {
            if (_served + _missed > 0)
                HotfixLog.Info("[SMS.1.P] summary: aliasServed=" + _served + " stillMissing=" + _missed);
        }
    }
}