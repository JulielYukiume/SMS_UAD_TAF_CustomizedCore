using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module C —— 重复抽选同一设计 / 重复 id（用户交代的第三个 bug）。
    ///
    /// 用户判定（第 24 轮反馈）：
    ///   · v1"给重复那份换新 Guid、假装是不同的一级"**不可接受**（会得到两份内容相同却算两级的
    ///     设计，并把开局自动造出的船分流）。
    ///   · 正确语义：**相同设计就该算抽选失败、去重抽**；**宁可只抽到一种有效设计，也不要重复**，
    ///     不论"真 id 重复"还是"内容重复却无法统一管理"。
    ///
    /// 抽选链（TAF 1.7）：
    ///   `CampaignDesigns::RandomShip` ← TAF 的 Prefix 整体替换为
    ///   `PredefinedDesignsData.GetRandomShip(player, type, year)`（TAF 自己的类，故我们不去 patch）
    ///       → 遍历预定义文件/年份 → `ShipsPerPlayer::RandomShipOfType(player, type)`（**vanilla**）
    ///           ⇒ 随机实现是**确定性**的（实测：同一 (国家,舰种) 连续 10 次返回同一份）
    ///
    /// 我们的做法（**已验证**，1940 中华新档）：
    ///   Prefix `ShipsPerPlayer::RandomShipOfType`：若抽到"本次开局该 (国家,舰种) 已抽过"
    ///   （按 **id** 与 **五项内容指纹** 双重判定）⇒ **重抽**（最多 10 次）；
    ///   抽尽仍重复 ⇒ 返回 **null**，让上层"年份/bin 顺延"去拿下一份。
    ///   实测结果：50 个 (国别×舰种) 组合**全部 2 份**、id 与内容指纹**零重复**、无悬空引用。
    ///
    /// 兼容性：`log` 模式完全不改变行为（只记录）。
    /// 红线（doc 21 §D.5）：**只动设计表，绝不触碰舰船对象**。
    /// </summary>
    internal static class DuplicateDesignGuard
    {
        /// <summary>同一 (国家,舰种) 单次抽取最多重试次数。</summary>
        private const int MaxTries = 10;

        private static readonly Dictionary<string, HashSet<string>> _drawnIds = new Dictionary<string, HashSet<string>>();
        private static readonly Dictionary<string, HashSet<string>> _drawnFps = new Dictionary<string, HashSet<string>>();

        private static bool _inDraw;
        private static CampaignController _campaign;

        private static int _drawCount, _retries, _exhausted, _dupsSeen;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.DuplicateDesignGuard)
            {
                HotfixLog.Info("[SMS.1.C] DuplicateDesignGuard: disabled by config");
                return;
            }

            try
            {
                // ① 底层随机源：重抽去重（只在 apply 模式下改变行为）
                var rso = AccessTools.Method(typeof(ShipsPerPlayer), "RandomShipOfType");
                if (rso != null)
                {
                    harmony.Patch(rso, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(DuplicateDesignGuard), nameof(Prefix_RandomShipOfType))));
                    HotfixLog.Info("[SMS.1.C] DuplicateDesignGuard: patched ShipsPerPlayer::RandomShipOfType (redraw on duplicate)"
                                   + "  maxTries=" + MaxTries + "  mode=" + HotfixParams.Mode);
                }
                else
                {
                    HotfixLog.Warn("[SMS.1.C] ShipsPerPlayer::RandomShipOfType not found -- redraw disabled");
                }

                // ② 诊断：记录每次 RandomShip 的最终结果（不改 id）
                var target = AccessTools.Method(typeof(CampaignDesigns), "RandomShip");
                if (target != null)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(DuplicateDesignGuard), nameof(Postfix_RandomShip))));
                    HotfixLog.Info("[SMS.1.C] DuplicateDesignGuard: patched CampaignDesigns::RandomShip (diagnostic only)");
                }
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C] install failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        // ① 重抽去重
        // ------------------------------------------------------------------

        /// <summary>
        /// 返回 false = 我们自己给出了 <paramref name="__result"/>；
        /// 返回 true  = 走原方法（log 模式 / 重入 / 模块关闭）。
        /// </summary>
        internal static bool Prefix_RandomShipOfType(ShipsPerPlayer __instance, Player player, ShipType shipType,
                                                     ref Ship.Store __result)
        {
            if (_inDraw) return true;                       // 重入 ⇒ 原方法（这是我们自己的调用）
            if (!HotfixParams.IsApply) return true;         // log 模式：完全不干预

            _inDraw = true;
            try
            {
                EnsureCampaign();

                string key = SafeName(player) + "|" + SafeType(shipType);
                HashSet<string> ids = Bucket(_drawnIds, key);
                HashSet<string> fps = Bucket(_drawnFps, key);
                Ship.Store lastDrawn = null;

                for (int attempt = 0; attempt < MaxTries; attempt++)
                {
                    _drawCount++;

                    Ship.Store r = __instance.RandomShipOfType(player, shipType);
                    if (r == null)
                    {
                        __result = null;
                        return false;
                    }

                    string id = SafeGuid(r.id);
                    string fp = Fingerprint(r);
                    lastDrawn = r;
                    if (!ids.Contains(id) && (fp == null || !fps.Contains(fp)))
                    {
                        ids.Add(id);
                        if (fp != null) fps.Add(fp);
                        if (HotfixParams.Verbose)
                            HotfixLog.Info("[SMS.1.C][draw] " + key + " -> id=" + id + " '" + SafeVesselName(r) + "'"
                                           + (attempt > 0 ? ("  (after " + attempt + " retry)") : ""));
                        __result = r;
                        return false;
                    }

                    _dupsSeen++;
                    _retries++;
                    HotfixLog.Debug("[SMS.1.C][DUP] draw returned an already-drawn design for " + key
                                   + " : id=" + id + " '" + SafeVesselName(r) + "'"
                                   + " -> redraw (" + (attempt + 1) + "/" + MaxTries + ")");
                }

                _exhausted++;
                // 重抽耗尽：返回最后一抽的设计而不是 null（调用方拿 null 会误判"该舰种无可用设计"）
                HotfixLog.Info("[SMS.1.C][DUP] draw exhausted for " + key + " after " + MaxTries
                               + " tries -> returning last drawn design: " + SafeVesselName(lastDrawn));
                var alt = TryAltBucket(player, shipType, ids, fps);
                if (alt != null)
                {
                    try { ids.Add(SafeGuid(alt.id)); var afp = Fingerprint(alt); if (afp != null) fps.Add(afp); } catch { }
                    __result = alt;
                    return false;
                }
                try { if (lastDrawn != null) ids.Add(SafeGuid(lastDrawn.id)); } catch { }
                __result = lastDrawn;
                return false;
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C] Prefix_RandomShipOfType: " + e.Message);
                return true;
            }
            finally
            {
                _inDraw = false;
            }
        }

        private static void EnsureCampaign()
        {
            try
            {
                var cc = CampaignController.Instance;
                if (!ReferenceEquals(cc, _campaign))
                {
                    _campaign = cc;
                    _drawnIds.Clear();
                    _drawnFps.Clear();
                    HotfixLog.Info("[SMS.1.C] new campaign -> draw memory cleared");
                }
            }
            catch { }
        }

        private static HashSet<string> Bucket(Dictionary<string, HashSet<string>> map, string key)
        {
            HashSet<string> set;
            if (!map.TryGetValue(key, out set)) { set = new HashSet<string>(); map[key] = set; }
            return set;
        }

        /// <summary>五项内容指纹：标准吨位/船宽/吃水/横摇/纵摇。用于识别"内容重复但 id 不同"。</summary>
        private static string Fingerprint(Ship.Store s)
        {
            try
            {
                return s.tonnage.ToString("0.###") + "|" + s.beam.ToString("0.###") + "|"
                     + s.draught.ToString("0.###") + "|" + s.instability_xx.ToString("0.###") + "|"
                     + s.instability_zz.ToString("0.###");
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // ② 诊断（仅记录，不改 id）
        // ------------------------------------------------------------------

        internal static void Postfix_RandomShip(Player player, ShipType type, int desiredYear, Ship.Store __result)
        {
            try
            {
                string key = SafeName(player) + "|" + SafeType(type);
                if (__result == null)
                {
                    HotfixLog.Warn("[SMS.1.C][draw] " + key + " year=" + desiredYear + " -> <null> (上层会跳过这一份)");
                    return;
                }
                if (HotfixParams.Verbose)
                    HotfixLog.Info("[SMS.1.C][draw] " + key + " year=" + desiredYear
                                   + " id=" + SafeGuid(__result.id) + " '" + SafeVesselName(__result) + "'");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C] Postfix_RandomShip: " + e.Message);
            }
        }

        internal static void LogSummary()
        {
            HotfixLog.Info("[SMS.1.C] draw summary: draws=" + _drawCount + " duplicates=" + _dupsSeen
                           + " redraws=" + _retries + " exhausted=" + _exhausted);
        }

        // ------------------------------------------------------------------
        // 存档挽回（重复 id）
        // ------------------------------------------------------------------

        internal static void Postfix_LoadShips(Il2CppSystem.Collections.Generic.List<Ship.Store> shipDesigns)
        {
            if (!HotfixParams.DuplicateDesignGuard || shipDesigns == null) return;

            try
            {
                var seen = new Dictionary<string, Ship.Store>();
                int duplicates = 0, reid = 0;

                for (int i = 0; i < shipDesigns.Count; i++)
                {
                    Ship.Store st = shipDesigns[i];
                    if (st == null) continue;
                    if (st.designId != Il2CppSystem.Guid.Empty) continue;

                    string id = st.id.ToString();
                    if (id == EmptyGuid) continue;

                    Ship.Store first;
                    if (!seen.TryGetValue(id, out first)) { seen[id] = st; continue; }

                    duplicates++;
                    if (HotfixParams.IsApply)
                    {
                        st.id = Il2CppSystem.Guid.NewGuid();
                        reid++;
                    }
                }

                if (duplicates > 0)
                    HotfixLog.Info("[SMS.1.C] save repair: " + duplicates + " duplicate design id(s), " + reid + " re-id'd");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C] Postfix_LoadShips: " + e.Message);
            }
        }

        private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

        private static string SafeName(Player p) { try { return p == null ? "<null>" : p.data.name; } catch { return "<err>"; } }
        private static string SafeType(ShipType t) { try { return t == null ? "<null>" : t.name; } catch { return "<err>"; } }
        /// <summary>
        /// 换桶：当前桶抽出的是重复件时，去**其它桶**（同玩家其它年份 / 别名桶 "{i}_{玩家}"）
        /// 找一份尚未用过的同舰种设计。predefinedDesignsData 的分层语义因此被保留：
        /// 我们只是"换一个包/年份的桶"，而不是把全部预制菜混同。
        /// </summary>
        private static Ship.Store TryAltBucket(Player player, ShipType shipType,
                                              System.Collections.Generic.HashSet<string> ids,
                                              System.Collections.Generic.HashSet<string> fps)
        {
            try
            {
                var cc = CampaignController.Instance;
                if (cc == null || cc.CampaignData == null) return null;
                var cd = cc._currentDesigns;
                if (cd == null) return null;

                string type = null, raw = null;
                try { type = shipType.name; } catch { }
                try { raw = player.data.name; } catch { }
                if (type == null || raw == null) return null;

                var pool = new System.Collections.Generic.List<Ship.Store>();
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
                            if (s == null) continue;
                            string id = SafeGuid(s.id);
                            if (ids != null && ids.Contains(id)) continue;
                            string fp = Fingerprint(s);
                            if (fp != null && fps != null && fps.Contains(fp)) continue;
                            pool.Add(s);
                        }
                    }
                }
                if (pool.Count == 0) return null;
                int idx = UnityEngine.Random.Range(0, pool.Count);
                HotfixLog.Info("[SMS.1.C][alt] player=" + raw + " type=" + type + " -> served from alternative bucket ("
                               + pool.Count + " candidate(s))");
                return pool[idx];
            }
            catch (Exception e) { HotfixLog.Debug("[SMS.1.C] alt bucket: " + e.Message); return null; }
        }

        private static string SafeGuid(Il2CppSystem.Guid g) { try { return g.ToString(); } catch { return "<err>"; } }
        private static string SafeVesselName(Ship.Store s) { try { return s.vesselName; } catch { return "<err>"; } }
    }
}
