using System;
using System.Collections.Generic;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    // S v3: bind design-less ships to an EXISTING same-class design.
    // Fix vs v2: FromStore's 2nd argument is Nullable<Guid> (value type) — passing null
    // throws NullReferenceException. We now pass a non-null nullable carrying the ship's own id.
    internal static class SharedBackfillProbe
    {
        private static readonly HashSet<string> _skip = new HashSet<string>();
        private static int _made, _failed, _noClass;

        internal static int MadeCount { get { return _made; } }
        internal static void ResetCounters() { _made = 0; _failed = 0; _noClass = 0; }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.SharedBackfillAggressive) { HotfixLog.Info("[SMS.1.S] SharedBackfill: disabled"); return; }
            HotfixLog.Info("[SMS.1.S] SharedBackfill v3: ready  max=" + HotfixParams.SharedBackfillMax + "  mode=" + HotfixParams.Mode);
        }

        internal static void RunNow()
        {
            try
            {
                var cc = CampaignController.Instance;
                if (cc == null || cc.CampaignData == null) return;

                var cand = new List<Ship>();
                foreach (var v in ShipDesignRelink.Misses)
                {
                    if (v == null) continue;
                    string id = SafeId(v);
                    if (id == null || _skip.Contains(id)) continue;
                    _skip.Add(id);
                    cand.Add(v);
                }

                int candidates = cand.Count;
                for (int i = 0; i < cand.Count; i++)
                {
                    var v = cand[i];
                    if (HotfixParams.SharedBackfillMax > 0 && _made >= HotfixParams.SharedBackfillMax) break;
                    if (!HotfixParams.IsApply) { HotfixLog.Info("[SMS.1.S][log] would bind '" + Name(v) + "'"); continue; }
                    BindBest(v, cc);
                }
                HotfixLog.Info("[SMS.1.S] backfill done: candidates=" + candidates + " bound=" + _made
                               + " noClass=" + _noClass + " failed=" + _failed);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.S] RunNow: " + e.Message); }
        }

        private static void BindBest(Ship v, CampaignController cc)
        {
            try
            {
                var vs = cc.CampaignData.Vessels;
                var vst = SafeStore(v);
                if (vst == null) { _failed++; return; }

                Ship best = null;
                int bestScore = -1;
                bool ambiguous = false;
                int designs = 0;

                for (int i = 0; i < vs.Count; i++)
                {
                    var d = vs[i] as Ship;
                    if (d == null || ReferenceEquals(d, v)) continue;
                    var ds = SafeStore(d);
                    if (ds == null || ds.designId != Il2CppSystem.Guid.Empty) continue;
                    designs++;

                    int score;
                    if (!Match(vst, ds, out score)) continue;
                    if (score > bestScore) { best = d; bestScore = score; ambiguous = false; }
                    else if (score == bestScore) { ambiguous = true; }
                }

                HotfixLog.Info("[SMS.1.S] diag: '" + Name(v) + "' hull='" + vst.hullName + "' type=" + vst.shipType
                               + " tonn=" + vst.tonnage + " ixx=" + vst.instability_xx + " izz=" + vst.instability_zz
                               + " ow=" + vst.overweight + " | designs=" + designs + " score=" + bestScore);

                if (best == null) { _noClass++; HotfixLog.Warn("[SMS.1.S][no-class] '" + Name(v) + "'"); return; }

                vst.designId = best.id;

                bool ok = false;
                try
                {
                    // uad-nullable-interop: 显式设 value/has_value（只构造可能"不崩但值错"）
                    var keepId = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
                    keepId.value = v.id;
                    keepId.has_value = true;
                    v.FromStore(vst, keepId, null, v.player, false);
                    ok = true;
                }
                catch (Exception e2)
                {
                    HotfixLog.Warn("[SMS.1.S] FromStore(keepId) failed: " + e2.Message);
                }

                if (!ok)
                {
                    try
                    {
                        var anyId = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(Il2CppSystem.Guid.NewGuid());
                        v.FromStore(vst, anyId, null, v.player, false);
                        ok = true;
                    }
                    catch (Exception e3) { HotfixLog.Warn("[SMS.1.S] FromStore(newId) failed: " + e3.Message); }
                }

                try { v.design = best; } catch { }

                if (ok)
                {
                    _made++;
                    HotfixLog.Info("[SMS.1.S] bound '" + Name(v) + "' -> same-class design '" + Name(best) + "'"
                                   + (ambiguous ? "  (AMBIGUOUS)" : ""));
                }
                else
                {
                    _failed++;
                    HotfixLog.Warn("[SMS.1.S] could not write back '" + Name(v) + "' (design ref set only)");
                }
            }
            catch (Exception e) { _failed++; HotfixLog.Warn("[SMS.1.S] BindBest: " + e.Message); }
        }

        private static Ship.Store SafeStore(Ship s) { try { return s.ToStore(); } catch { return null; } }

        private static bool Match(Ship.Store a, Ship.Store b, out int score)
        {
            score = -1;
            try
            {
                if (a.shipType != b.shipType) return false;
                if (a.hullName != b.hullName) return false;
                if (a.survivability != b.survivability) return false;
                if (a.playerOriginal != b.playerOriginal) return false;
            }
            catch { return false; }

            bool numerics = false;
            try
            {
                numerics = Math.Abs(a.tonnage - b.tonnage) <= 0.5f
                        && Math.Abs(a.beam - b.beam) <= 0.01f
                        && Math.Abs(a.draught - b.draught) <= 0.01f;
            }
            catch { numerics = false; }
            if (!numerics) return false;

            bool stability = false;
            try
            {
                stability = Math.Abs(a.instability_xx - b.instability_xx) <= 0.05f
                         && Math.Abs(a.instability_zz - b.instability_zz) <= 0.05f;
            }
            catch { stability = false; }

            if (stability) { score = 2; return true; }
            if (HasDefects(a)) { score = 1; return true; }
            return false;
        }

        private static bool HasDefects(Ship.Store st)
        {
            try
            {
                if (Math.Abs(st.overweight) > 0.001f) return true;
                if (Math.Abs(st.underweight) > 0.001f) return true;
                var sw = st.statWeaponAndHullDefect;
                if (sw != null && sw.Count > 0) return true;
            }
            catch { }
            return false;
        }

        private static string SafeId(Ship s) { try { return s.id.ToString(); } catch { return null; } }
        private static string Name(Ship s) { try { return s.vesselName; } catch { } try { return s.name; } catch { } return "?"; }
        internal static void LogSummary() { if (_made + _noClass + _failed > 0) HotfixLog.Info("[SMS.1.S] summary: bound=" + _made + " noClass=" + _noClass + " failed=" + _failed); }
    }
}
