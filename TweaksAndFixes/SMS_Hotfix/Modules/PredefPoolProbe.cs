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


        /// <summary>[SMS.2.G2] 预制包元数据（来自 Mods/predefinedDesignsData.csv）。</summary>
        private sealed class PackInfo
        {
            internal string name;
            internal string filename;
            internal float skipChance;
            internal int yearRange;
        }

        private static System.Collections.Generic.List<PackInfo> _packs;
        private static int _packsBaseIndex = -1;

        private static void LoadPacks()
        {
            if (_packs != null) return;
            _packs = new System.Collections.Generic.List<PackInfo>();
            try
            {
                string dir = null;
                try { dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Mods"); } catch { }
                string file = null;
                if (dir != null)
                {
                    string f1 = System.IO.Path.Combine(dir, "predefinedDesignsData.csv");
                    if (System.IO.File.Exists(f1)) file = f1;
                }
                if (file == null)
                {
                    // 兜底：游戏根目录（AppContext.BaseDirectory 已是游戏目录时，Mods 就在其下）
                    try
                    {
                        string f2 = System.IO.Path.Combine(AppContext.BaseDirectory, "Mods", "predefinedDesignsData.csv");
                        if (System.IO.File.Exists(f2)) file = f2;
                    }
                    catch { }
                }
                if (file == null) { _packsBaseIndex = 0; return; }

                var lines = System.IO.File.ReadAllLines(file);
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (raw.TrimStart().StartsWith("@")) continue;
                    var parts = raw.Split(',');
                    if (parts.Length < 4) continue;
                    var pi = new PackInfo();
                    pi.name = parts[0].Trim();
                    pi.filename = parts[1].Trim();
                    float sc = 0f; int.TryParse(parts[3].Trim(), out pi.yearRange);
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out sc);
                    pi.skipChance = sc;
                    if (pi.filename == "--" || pi.filename == "-") _packsBaseIndex = _packs.Count;
                    _packs.Add(pi);
                }
                if (_packsBaseIndex < 0 && _packs.Count > 0) _packsBaseIndex = _packs.Count - 1;
                HotfixLog.Info("[SMS.2.G2] predef packs loaded: " + _packs.Count + " (base=" + _packsBaseIndex + ")");
            }
            catch (Exception e) { HotfixLog.Dbg("[SMS.2.G2] LoadPacks: " + e.Message); }
        }

        /// <summary>
        /// [SMS.2.G2] 按 **predefinedDesignsData.csv 的优先级链**抽设计：
        ///   包序 = CSV 顺序（先 = 高优先）；每包先掷 skipChance，再在该包内按 yearRange 取"离目标年最近"的年份集。
        /// 返回 null 表示整条链都抽不到（交回 vanilla）。
        /// </summary>

        /// <summary>
        /// [SMS.2.G2][T1] 科技匹配闸：只数两个数
        ///   n_design = 设计 techs 数；n_player = 玩家已完成科技数（progress>=100）
        ///   容忍上限 = tech_tolerance + floor((当前年 - year_base) / year_step)
        /// </summary>
        // ---------------- [T3] 兜底 ----------------

        private static readonly System.Collections.Generic.Dictionary<string, int> _catchupTurn =
            new System.Collections.Generic.Dictionary<string, int>();

        private static int CurTurn()
        {
            try
            {
                var d = CampaignController.Instance.CurrentDate.AsDate();
                return (int)d.Month + 12 * d.Year;   // 每回合 +1（月份步进）
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// [T3] 从 (当前年 - catchup_back_min..max) 的节点取一份设计，并把该设计缺失的科技补给玩家。
        /// 冷却 catchup_cooldown_turns 回合；返回 null 表示未触发/无候选。
        /// </summary>
        private static Ship.Store TryCatchup(Player player, string raw, string type, int cur,
            System.Collections.Generic.HashSet<string> ids, System.Collections.Generic.HashSet<string> fps,
            ref string debug)
        {
            try
            {
                if (!HotfixParams.TrueMixedCatchup) return null;
                if (player == null || cur <= 0) return null;

                int turn = CurTurn();
                int last;
                if (_catchupTurn.TryGetValue(raw, out last)
                    && HotfixParams.TrueMixedCatchupCooldownTurns > 0
                    && turn - last < HotfixParams.TrueMixedCatchupCooldownTurns)
                {
                    debug += " [T3]cooldown";
                    return null;
                }

                int lo = Math.Min(HotfixParams.TrueMixedCatchupBackMin, HotfixParams.TrueMixedCatchupBackMax);
                int hi = Math.Max(HotfixParams.TrueMixedCatchupBackMin, HotfixParams.TrueMixedCatchupBackMax);
                if (hi < 1) hi = 1;
                int back = UnityEngine.Random.Range(lo, hi + 1);
                int target = cur - back;

                // 年份上限：复用 TAF 参数（默认 1950）
                int maxYear = 1950;
                try { maxYear = (int)TweaksAndFixes.Config.Param("taf_campaign_end_retirement_date", 1950f); } catch { }
                if (maxYear < cur) maxYear = cur + 20;

                var cd = CampaignController.Instance._currentDesigns;
                if (cd == null) return null;

                var triedFps = new System.Collections.Generic.HashSet<string>();
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    Ship.Store best = null;
                    int bestDist = int.MaxValue;
                    foreach (var spy in cd.shipsPerYear)
                    {
                        var spp2 = spy.Value;
                        if (spp2 == null) continue;
                        foreach (var spp in spp2.shipsPerPlayer)
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
                                int y = 0;
                                try { y = s.YearCreated; } catch { continue; }
                                if (y > maxYear) continue;                 // 超过退休年上限
                                int d2 = Math.Abs(y - target);
                                if (d2 >= bestDist) continue;
                                string cid = null;
                                try { cid = s.id.ToString(); } catch { }
                                if (cid != null && ids != null && ids.Contains(cid)) continue;
                                string cfp = DesignFp(s);
                                if (cfp != null && triedFps.Contains(cfp)) continue;
                                best = s; bestDist = d2;
                            }
                        }
                    }
                    if (best == null) { debug += " [T3]no-candidate(target=" + target + ")"; break; }

                    string fp = DesignFp(best);
                    string dinfo;
                    if (DedupeAccept(best, fp, out dinfo))
                    {
                        if (fp != null) triedFps.Add(fp);
                        int granted = GrantMissingTechs(player, best);
                        _catchupTurn[raw] = turn;
                        debug += " [T3]catchup year=" + SafeYearStr(best) + " back=" + back
                                 + " target=" + target + " granted=" + granted;
                        HotfixLog.Info("[SMS.2.G2][T3] catchup: player=" + raw + " type=" + type
                                       + " target=" + target + " pickedYear=" + SafeYearStr(best)
                                       + " grantedTechs=" + granted);
                        return best;
                    }

                    // 命中"仍有效"的设计 ⇒ target 前移一年重试
                    if (fp != null) triedFps.Add(fp);
                    debug += " [T3]dup(" + dinfo + ")->target+1(" + (target + 1) + ") ";
                    target++;
                    if (target > maxYear) { debug += " [T3]hit-max"; break; }
                }
                return null;
            }
            catch (Exception e) { debug += " [T3]err:" + e.Message; return null; }
        }

        private static string SafeYearStr(Ship.Store s)
        {
            try { return s.YearCreated.ToString(); } catch { return "?"; }
        }

        /// <summary>把设计所需、而该玩家尚未完成的科技补给玩家（真 clobber）。</summary>
        private static int GrantMissingTechs(Player player, Ship.Store store)
        {
            int granted = 0;
            try
            {
                var have = new System.Collections.Generic.HashSet<string>();
                var list = player.technologies;
                for (int i = 0; i < list.Count; i++)
                {
                    var tech = list[i];
                    if (tech == null || tech.progress < 100f) continue;
                    try { if (tech.data != null) have.Add(tech.data.name); } catch { }
                }

                foreach (var name in store.techs)
                {
                    if (string.IsNullOrEmpty(name) || have.Contains(name)) continue;
                    Il2Cpp.TechnologyData data;
                    if (!G.GameData.technologies.TryGetValue(name, out data) || data == null) continue;
                    var nt = new Il2Cpp.Technology();
                    nt.data = data;
                    nt.progress = 100f;
                    player.technologies.Add(nt);
                    have.Add(name);
                    granted++;
                }
            }
            catch (Exception e) { HotfixLog.Dbg("[SMS.2.G2][T3] grant failed: " + e.Message); }
            return granted;
        }

        // ---------------- [T6] 设计对设计 指纹去重 ----------------

        private static readonly System.Collections.Generic.Dictionary<string, Il2Cpp.Ship> _designByFp =
            new System.Collections.Generic.Dictionary<string, Il2Cpp.Ship>();
        private static int _fpTurn = -1;

        /// <summary>[T6] 是否有实船把该设计当作 design 引用（判断"废弃设计能否安全删除"）。</summary>
        private static bool IsReferencedByShip(Il2Cpp.Ship design)
        {
            try
            {
                var data = CampaignController.Instance.CampaignData;
                var vessels = data != null ? data.Vessels : null;
                if (vessels == null) return false;
                for (int i = 0; i < vessels.Count; i++)
                {
                    Ship v = null;
                    try { v = vessels[i] as Ship; } catch { }
                    if (v == null) continue;
                    if (ReferenceEquals(v, design)) continue;
                    try { if (v.design != null && ReferenceEquals(v.design, design)) return true; } catch { }
                }
            }
            catch { }
            return false;
        }

        internal static string DesignFp(Ship.Store s)
        {
            try
            {
                string core = DuplicateDesignGuard.Fingerprint(s);
                if (core == null) return null;
                return core + "|" + s.hullName + "|" + s.shipType;
            }
            catch { return null; }
        }

        /// <summary>每回合（或首次）重建"设计 → 指纹"缓存。只收设计槽（status ∈ {0,1,7} 且 designId 为空）。</summary>
        private static void RefreshDesignCache(int turn)
        {
            if (_fpTurn == turn && _designByFp.Count > 0) return;
            _fpTurn = turn;
            _designByFp.Clear();
            try
            {
                var data = CampaignController.Instance.CampaignData;
                var vessels = data != null ? data.Vessels : null;
                if (vessels == null) return;
                for (int i = 0; i < vessels.Count; i++)
                {
                    Ship sh = null;
                    try { sh = vessels[i] as Ship; } catch { }
                    if (sh == null) continue;
                    int st = -1;
                    try { st = (int)sh.status; } catch { continue; }
                    if (st != 0 && st != 1 && st != 7) continue;      // 只收设计槽
                    Ship.Store store = null;
                    try { store = sh.ToStore(); } catch { }
                    if (store == null) continue;
                    try { if (store.designId != Il2CppSystem.Guid.Empty) continue; } catch { }
                    string fp = DesignFp(store);
                    if (fp == null) continue;
                    if (!_designByFp.ContainsKey(fp)) _designByFp[fp] = sh;
                }
            }
            catch (Exception e) { HotfixLog.Dbg("[SMS.2.G2][T6] cache build: " + e.Message); }
        }

        /// <summary>
        /// [T6] 命中已存在设计时：
        ///   · 已废弃(status==7) ⇒ 让旧设计进入"删船池"（status=Scrapped，下回合 OnNewTurn 清理），本次视为成功
        ///   · 仍有效 ⇒ 返回 false（本次失败，由调用方顺延/回退年份）
        /// </summary>
        private static bool DedupeAccept(Ship.Store incoming, string fp, out string info)
        {
            info = "";
            if (fp == null) return true;
            Il2Cpp.Ship existing;
            if (!_designByFp.TryGetValue(fp, out existing) || existing == null) return true;

            int st = -1;
            try { st = (int)existing.status; } catch { }
            if (st == 7)
            {
                // ① 若仍有实船引用该设计，onNewTurn 的清理会跳过它（hasDesign=true），
                //    此时"废弃"不等于"可删"，按"仍有效"处理（本次失败，换候选）。
                if (IsReferencedByShip(existing))
                {
                    info = "dup obsolete-but-referenced";
                    return false;
                }

                // ② 已被引用过也没关系：先断链（id 置空），再进删船池。
                //    id 置空后即便过渡期落盘、读档被 C 模块重赋 id，也已与旧设计完全脱钩。
                try { existing.id = Il2CppSystem.Guid.Empty; } catch { }
                try { existing.status = VesselEntity.Status.Scrapped; } catch { }
                _designByFp.Remove(fp);
                info = "dup+scrap old(7)";
                HotfixLog.Dbg("[SMS.2.G2][T6] duplicate & obsolete -> id cleared, marked Scrapped, new design accepted");
                return true;
            }
            info = "dup alive(status=" + st + ")";
            return false;
        }

        internal static bool TechOk(Ship.Store s, Player player, int cur, out string info)
        {
            info = "";
            try
            {
                if (s == null || player == null) return true;

                int nDesign = 0;
                try { if (s.techs != null) nDesign = s.techs.Count; } catch { }

                int nPlayer = 0;
                try
                {
                    var list = player.technologies;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var tech = list[i];
                        if (tech != null && tech.progress >= 100f) nPlayer++;
                    }
                }
                catch { }

                int bonus = 0;
                if (HotfixParams.TrueMixedTechYearStep > 0)
                {
                    // ★ base 之前的年份不倒扣：与 0 取最大值
                    int raw = (cur - HotfixParams.TrueMixedTechYearBase) / HotfixParams.TrueMixedTechYearStep;
                    if (raw > 0) bonus = raw;
                }

                int cap = HotfixParams.TrueMixedTechTolerance + bonus;
                bool ok = nDesign <= nPlayer + cap;
                info = "nDesign=" + nDesign + " nPlayer=" + nPlayer + " cap=" + cap
                       + (ok ? " ok" : " REJECT");
                return ok;
            }
            catch { return true; }
        }

        internal static Ship.Store PickFromRegistry(Player player, ShipType viaType,
            System.Collections.Generic.HashSet<string> ids,
            System.Collections.Generic.HashSet<string> fps,
            out string debug)
        {
            debug = "";
            try
            {
                if (player == null || viaType == null) return null;
                string type = null;
                try { type = viaType.name; } catch { }
                if (string.IsNullOrEmpty(type)) return null;
                string raw = RawName(player);
                if (string.IsNullOrEmpty(raw)) return null;

                var cd = CampaignController.Instance._currentDesigns;
                if (cd == null) return null;

                LoadPacks();
                RefreshDesignCache(CurTurn());

                int cur = 0;
                try { cur = CampaignController.Instance.CurrentDate.AsDate().Year; } catch { }
                if (cur <= 0) return null;

                // 逐包尝试（CSV 顺序 = 优先级）
                int packCount = (_packs != null && _packs.Count > 0) ? _packs.Count : 1;
                for (int pi = 0; pi < packCount; pi++)
                {
                    var info = (_packs != null && pi < _packs.Count) ? _packs[pi] : null;
                    if (info != null && info.skipChance > 0f)
                    {
                        if (UnityEngine.Random.value < info.skipChance)
                        {
                            debug += "[" + pi + "]skip ";
                            continue;
                        }
                    }

                    int range = info != null ? info.yearRange : -1;
                    // 该包接受的年份集合：|year - 目标| <= range；range<0 时按 vanilla 规则（目标年 + 最近更早年份）
                    // 玩家可见的别名键：非基座包为 "{i}_{raw}"，基座包为 raw
                    string key = (pi == _packsBaseIndex) ? raw : (pi + "_" + raw);

                    var byYear = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Ship.Store>>();
                    var aliasByYear = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Ship.Store>>();
                    foreach (var spy in cd.shipsPerYear)
                    {
                        var spp2 = spy.Value;
                        if (spp2 == null) continue;
                        foreach (var spp in spp2.shipsPerPlayer)
                        {
                            string k = spp.Key;
                            if (k == null) continue;
                            Il2CppSystem.Collections.Generic.List<Ship.Store> list;
                            if (!spp.Value.shipsPerType.TryGetValue(type, out list) || list == null) continue;

                            bool isExact = (k == key);
                            bool isAlias = !isExact && (k == raw || k.EndsWith("_" + raw, StringComparison.Ordinal))
                                           && (pi == _packsBaseIndex ? k == raw : true);
                            if (!isExact && !isAlias) continue;

                            for (int i = 0; i < list.Count; i++)
                            {
                                var s = list[i];
                                if (s == null) continue;
                                int y = 0;
                                try { y = spy.Key; } catch { continue; }
                                var target = isExact ? byYear : aliasByYear;
                                System.Collections.Generic.List<Ship.Store> bucket;
                                if (!target.TryGetValue(y, out bucket))
                                    target[y] = bucket = new System.Collections.Generic.List<Ship.Store>();
                                bucket.Add(s);
                            }
                        }
                    }

                    var yearsAvail = new System.Collections.Generic.List<int>();
                    foreach (var kv in byYear) yearsAvail.Add(kv.Key);
                    foreach (var kv in aliasByYear) if (!yearsAvail.Contains(kv.Key)) yearsAvail.Add(kv.Key);
                    if (yearsAvail.Count == 0) { debug += "[" + pi + "]none "; continue; }

                    // 按离目标年的距离排序（越近越优先）
                    yearsAvail.Sort((a, b) => Math.Abs(a - cur).CompareTo(Math.Abs(b - cur)));

                    for (int yi = 0; yi < yearsAvail.Count; yi++)
                    {
                        int y = yearsAvail[yi];
                        if (range >= 0 && Math.Abs(y - cur) > range) continue;

                        var bucket = byYear.ContainsKey(y) ? byYear[y] : aliasByYear[y];
                        if (bucket == null || bucket.Count == 0) continue;

                        int startIdx = UnityEngine.Random.Range(0, bucket.Count);
                        for (int step = 0; step < bucket.Count; step++)
                        {
                            var cand = bucket[(startIdx + step) % bucket.Count];
                            if (cand == null) continue;
                            string cid = null;
                            try { cid = cand.id.ToString(); } catch { }
                            if (cid != null && ids != null && ids.Contains(cid)) continue;

                            // [T1] 科技匹配闸
                            string tinfo;
                            if (!TechOk(cand, player, cur, out tinfo))
                            {
                                debug += "techfail(" + tinfo + ") ";
                                continue;
                            }

                            // [T6] 设计对设计指纹去重
                            string dinfo;
                            string cfp = DesignFp(cand);
                            if (!DedupeAccept(cand, cfp, out dinfo))
                            {
                                debug += "dup(" + dinfo + ") ";
                                continue;
                            }
                            if (dinfo.Length > 0) debug += "[" + dinfo + "] ";
                            if (fps != null)
                            {
                                string fp = null;
                                try { fp = DuplicateDesignGuard.Fingerprint(cand); } catch { }
                                if (fp != null && fps.Contains(fp)) continue;
                            }
                            debug += "pack=" + pi + " year=" + y + " dist=" + Math.Abs(y - cur)
                                     + " pool=" + bucket.Count + " " + tinfo;
                            return cand;
                        }
                    }
                    debug += "[" + pi + "]drawn ";
                }
                debug += "chain exhausted";

                // ★ [SMS.2.G2][T3] 兜底"大跃进"：包链抽不到时，取 3~5 年前的节点设计，并补齐其缺失科技
                var catchup = TryCatchup(player, raw, type, cur, ids, fps, ref debug);
                if (catchup != null) return catchup;

                return null;
            }
            catch (Exception e) { debug = "err:" + e.Message; return null; }
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

                // [SMS.2.G2] 注册表级查找：同玩家（真名 + 别名桶），按"年份窗口"收集，并记录最新年份边界
                var pool = new System.Collections.Generic.List<Ship.Store>();
                int newestEnd = 0;          // 池中"最新可用年份(<= curYear+ahead)"的队尾序号
                int curYear = 0, maxYear = 0, minYear = 0;
                bool g2 = false;
                try
                {
                    g2 = HotfixParams.TrueMixed && (int)CampaignController.Instance.designsUsage == 1;
                    if (g2)
                    {
                        try { curYear = CampaignController.Instance.CurrentDate.AsDate().Year; } catch { curYear = 0; }
                        if (curYear > 0)
                        {
                            maxYear = curYear + HotfixParams.TrueMixedYearAhead;
                            minYear = maxYear - HotfixParams.TrueMixedYearWindow;
                        }
                        else g2 = false;
                    }
                }
                catch { g2 = false; }

                try
                {
                    var cd = CampaignController.Instance._currentDesigns;
                    if (cd != null)
                    {
                        // 按年份升序（旧→新），使"靠后的序号 = 更新的年份"
                        var years = new System.Collections.Generic.List<int>();
                        foreach (var spy in cd.shipsPerYear) years.Add(spy.Key);
                        years.Sort();

                        foreach (int yr in years)
                        {
                            // G2：只收窗口内的年份
                            if (g2 && (yr < minYear || yr > maxYear)) continue;

                            ShipsPerYear spy;
                            if (!cd.shipsPerYear.TryGetValue(yr, out spy) || spy == null) continue;

                            foreach (var spp in spy.shipsPerPlayer)
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
                catch (Exception e1) { HotfixLog.Debug("[SMS.2.G2] registry scan: " + e1.Message); }

                if (g2 && pool.Count > 0)
                {
                    // ★ 按设计自身的 YearCreated 降序：池首 = 最新设计
                    try { pool.Sort((a, b) => b.YearCreated.CompareTo(a.YearCreated)); }
                    catch (Exception es) { HotfixLog.Dbg("[SMS.2.G2] sort failed: " + es.Message); }

                    int topYear = -1;
                    try { topYear = pool[0].YearCreated; } catch { }

                    // newestEnd = "最新那一年"全部设计的队尾序号
                    newestEnd = 0;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        int y = 0;
                        try { y = pool[i].YearCreated; } catch { }
                        if (y == topYear) newestEnd = i + 1; else break;
                    }
                    HotfixLog.Dbg("[SMS.2.G2] pool built: player=" + raw + " type=" + type
                                  + " window=" + minYear + ".." + maxYear + " size=" + pool.Count
                                  + " newestYear=" + topYear + " newestEnd=" + newestEnd);
                }

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
                // [SMS.2.G2] 若抽到比"窗口最新年"更旧的设计，则折半逼近新端（舍弃较老的一半）
                // [SMS.2.G2] 只折半一次：把"落在更老区段"的抽中概率折到较新区段，
                // 但保留窗口内较新但在 newestEnd 之前的年份的原有权重（不反复折叠）
                if (g2 && newestEnd > 0 && idx >= newestEnd)
                {
                    idx /= 2;
                    HotfixLog.Dbg("[SMS.2.G2] idx halved once -> " + idx + " (newestEnd=" + newestEnd + ")");
                }
                if (idx < 0 || idx >= pool.Count) idx = 0;
                __result = pool[idx];
                _served++;
                HotfixLog.Dbg("[SMS.1.P] alias fallback: player=" + raw + " type=" + type
                               + (g2 ? (" [SMS.2.G2] window=" + minYear + ".." + maxYear + " idx=" + idx + "/" + pool.Count)
                                     : " (pool=" + pool.Count + ")")
                               + " total served=" + _served);
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