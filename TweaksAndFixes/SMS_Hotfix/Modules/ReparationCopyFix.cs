using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    // A2b v4: REPLACE the vanilla "copy design" entirely.
    // Vanilla CloneDesign makes a full model copy but keeps the SOURCE dateCreated (-> design list
    // still shows the old year) and keeps the reparation flag. We instead build a fresh design:
    //   new id, dateCreated = now, no refit chain, reparation flag cleared, nation = copier (only
    //   when the source is a reparation design). No extra model is left behind (vanilla skipped).
    internal static class ReparationCopyFix
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ReparationCopyFix) { HotfixLog.Info("[SMS.1.A2b] ReparationCopyFix: disabled"); return; }
            try
            {
                var m = AccessTools.Method(typeof(PlayerController), "CloneDesign", new Type[] { typeof(Ship) });
                if (m == null) { HotfixLog.Warn("[SMS.1.A2b] CloneDesign(Ship) not found"); return; }
                harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(ReparationCopyFix), nameof(Prefix))));
                HotfixLog.Info("[SMS.1.A2b] ReparationCopyFix v4: armed (replaces vanilla copy)  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.A2b] install failed: " + e); }
        }

        internal static bool Prefix(Ship ship)
        {
            try
            {
                if (ship == null) return true;
                if (!HotfixParams.IsApply) { HotfixLog.Info("[SMS.1.A2b][log] would rebuild copy of '" + Name(ship) + "'"); return false; }

                var cc = CampaignController.Instance;
                Player p = null;
                try { p = ship.player; } catch { }
                if (cc == null || cc.CampaignData == null || p == null) { HotfixLog.Warn("[SMS.1.A2b] context missing, fallback to vanilla"); return true; }

                bool isRepar = false;
                try { isRepar = ship.isReparationDesign; } catch { }

                var src = ship.ToStore();
                if (src == null) { HotfixLog.Warn("[SMS.1.A2b] source store null"); return true; }

                var st = src;
                var newId = Il2CppSystem.Guid.NewGuid();
                try { st.id = newId; } catch (Exception e1) { HotfixLog.Warn("[SMS.1.A2b] set id failed: " + e1.Message); }
                try { st.designId = Il2CppSystem.Guid.Empty; } catch { }
                try { st.dateCreated = cc.CurrentDate; } catch (Exception e2) { HotfixLog.Warn("[SMS.1.A2b] set dateCreated failed: " + e2.Message); }
                try { st.dateCreatedRefit = default(Il2Cpp.GameDate); } catch { }
                try { st.refitDesignListID = new Il2CppSystem.Collections.Generic.List<string>(); } catch { }
                try { st.isReparationDesign = false; } catch { }

                if (isRepar)
                {
                    PlayerData pd = null;
                    try { pd = p.data; } catch { }
                    if (pd != null) { try { st.playerOriginal = pd.name; } catch { } }
                }

                // 按"新建空设计"的语义清空这三项：名字交由引擎随机生成，不保留改造链/改造名
                try { st.vesselName = ""; } catch { }
                try { st.refitDesignName = null; } catch { }
                try { st.refitDesignListID = new Il2CppSystem.Collections.Generic.List<string>(); } catch { }
                var d = Ship.Create(null, p, false, false, false);
                if (d == null) { HotfixLog.Warn("[SMS.1.A2b] Ship.Create failed"); return true; }

                var keep = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
                keep.value = newId;
                keep.has_value = true;
                if (!d.FromStore(st, keep, null, p, false)) { HotfixLog.Warn("[SMS.1.A2b] FromStore failed"); return true; }

                try { d.SetRandomName(); } catch { }
                try { cc._currentDesigns.AddShip(st); } catch (Exception e3) { HotfixLog.Warn("[SMS.1.A2b] AddShip: " + e3.Message); }
                // 不再 Vessels.Add：设计槽（_currentDesigns）已足够使其出现，二者同时登记会在存档里产生两份
                try { G.ui.ReportNewDesign(d); } catch { }

                // 补全原版复制的第 3 步：进入"只专注这一条船"的编辑模式
                try
                {
                    GameManager.Instance.ToConstructor(false, d, true, null, d.shipType, false, p);
                    HotfixLog.Info("[SMS.1.A2b] entered constructor for '" + Name(d) + "'");
                }
                catch (Exception e5) { HotfixLog.Warn("[SMS.1.A2b] ToConstructor failed: " + e5.Message); }
                HotfixLog.Info("[SMS.1.A2b] copy rebuilt: '" + Name(ship) + "' -> new design '" + Name(d)
                               + "' (date=now, repar=" + isRepar + ")");
                return false;   // skip vanilla copy entirely
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.A2b] Prefix: " + e.Message); return true; }
        }

        private static string Name(Ship s) { try { return s.vesselName; } catch { } try { return s.name; } catch { } return "?"; }
    }
}