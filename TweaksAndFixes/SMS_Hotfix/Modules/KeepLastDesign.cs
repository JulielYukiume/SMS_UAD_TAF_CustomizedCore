using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    internal static class KeepLastDesign
    {
        private static int _keptLast, _keptInUse;
        private static int _lastTriggerTurn = -1;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.KeepLastDesign) { HotfixLog.Info("[SMS.1.K] KeepLastDesign: disabled"); return; }
            try
            {
                var m = AccessTools.Method(typeof(PlayerController), "CanDeleteDesign");
                if (m == null) { HotfixLog.Warn("[SMS.1.K] CanDeleteDesign not found"); return; }
                harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(KeepLastDesign), nameof(Prefix))));
                HotfixLog.Info("[SMS.1.K] KeepLastDesign: armed on PlayerController::CanDeleteDesign  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.K] install failed: " + e); }
        }

        internal static bool Prefix(Ship ship, bool demandAtLeastOneOtherDesign, ref bool __result)
        {
            try
            {
                if (ship == null) return true;
                var cc = CampaignController.Instance;
                if (cc == null || cc.CampaignData == null) return true;
                var vessels = cc.CampaignData.Vessels;
                if (vessels == null) return true;

                var targetId = ship.id;
                int sameType = 0, inUse = 0;

                for (int i = 0; i < vessels.Count; i++)
                {
                    var v = vessels[i] as Ship;
                    if (v == null) continue;

                    bool isDesign = false;
                    try { var st = v.ToStore(); isDesign = st != null && st.designId == Il2CppSystem.Guid.Empty; } catch { }

                    if (isDesign)
                    {
                        try { if (v.shipType == ship.shipType && SameOwner(v, ship)) sameType++; } catch { }
                    }
                    else
                    {
                        try { var d = v.design; if (d != null && d.id == targetId && SameOwner(v, ship)) inUse++; } catch { }
                    }
                }

                if (sameType <= 1)
                {
                    _keptLast++;
                    HotfixLog.Info("[SMS.1.K] kept LAST design: owner=" + OwnerName(ship) + " type=" + SafeType(ship) + " design=" + SafeName(ship));
                    __result = false;

                    // K-1 hit: ask the game to fill missing designs once per turn, so the player
                    // does not stay blocked on the LAST-design guard forever.
                    if (HotfixParams.KeepLastDesignTriggerPredef)
                    {
                        try
                        {
                            int nowTurn = cc.CurrentDate.turn;
                            if (_lastTriggerTurn != nowTurn)
                            {
                                _lastTriggerTurn = nowTurn;
                                cc.CheckPredefinedDesigns(false);
                                HotfixLog.Info("[SMS.1.K] triggered predef fill (turn " + nowTurn + ")");
                            }
                        }
                        catch (Exception ee) { HotfixLog.Debug("[SMS.1.K] predef fill failed: " + ee.Message); }
                    }

                    return false;
                }

                // IN-USE escape hatch: allow deletion when design is old enough (turns config).
                bool allowEscape = false;
                if (HotfixParams.KeepLastDesignAgeTurns > 0)
                {
                    try
                    {
                        int created = ship.dateCreated.turn;
                        int now = cc.CurrentDate.turn;
                        if (now - created >= HotfixParams.KeepLastDesignAgeTurns) allowEscape = true;
                    }
                    catch { }
                }
                // 准入判据：设计已"不可建造"（部件淘汰/超重等）且够老 ⇒ 属于原版合法清理，放行。
                bool notBuildable = false;
                if (HotfixParams.KeepLastDesignAgeTurns > 0)
                {
                    try
                    {
                        int created2 = ship.dateCreated.turn;
                        int now2 = cc.CurrentDate.turn;
                        if (now2 - created2 >= HotfixParams.KeepLastDesignAgeTurns)
                        {
                            string reason = null;
                            bool canBuild = PlayerController.Instance.CanBuildShipsFromDesign(ship, out reason);
                            if (!canBuild) notBuildable = true;
                        }
                    }
                    catch { }
                }
                if (inUse > 0 && !allowEscape && !notBuildable)
                {
                    _keptInUse++;
                    HotfixLog.Info("[SMS.1.K] kept IN-USE design: owner=" + OwnerName(ship) + " design=" + SafeName(ship) + " ships=" + inUse);
                    __result = false;
                    return false;
                }
            }
            catch (Exception e) { HotfixLog.Debug("[SMS.1.K] prefix: " + e.Message); }
            return true;
        }

        private static bool SameOwner(Ship a, Ship b)
        {
            try
            {
                var pa = a.player; var pb = b.player;
                if (pa == null || pb == null) return false;
                return pa.Pointer == pb.Pointer;
            }
            catch { return false; }
        }

        private static string OwnerName(Ship s)
        {
            try { var p = s.player; if (p != null) { var d = p.data; if (d != null) return d.name; } } catch { }
            return "?";
        }

        private static string SafeType(Ship s) { try { return s.shipType.ToString(); } catch { return "?"; } }
        private static string SafeName(Ship s) { try { return s.vesselName; } catch { } try { return s.name; } catch { } return "?"; }

        internal static void LogSummary()
        {
            if (_keptLast + _keptInUse > 0)
                HotfixLog.Info("[SMS.1.K] summary: keptLast=" + _keptLast + " keptInUse=" + _keptInUse);
        }
    }
}
