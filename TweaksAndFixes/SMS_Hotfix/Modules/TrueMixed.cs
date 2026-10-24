using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// G：真混合设计来源。
    ///
    /// 反汇编依据（1.7.0.0，CampaignController._AiManageFleet_d__201::MoveNext，RVA 0x881930）：
    ///   state==0：
    ///     if (player.isPlayer) 整体跳过
    ///     ScrapOldAiShips(player)
    ///     if (!prewarming) { SetAiShipRoles(player); DeleteOldDesigns(player); }
    ///     if (designsUsage==Mixed(1) && prewarming) || designsUsage==FullPredefined(2)
    ///            → GetPredefinedDesign(player, ·)          // 快速/混合
    ///     else → GenerateRandomDesigns(player, ·)          // 慢速
    ///   state==1：
    ///     cash = CashAndIncome(player); BuildNewShips(player, cash); BuildNewSubmarines(player, cash)
    ///
    /// 因此"分支"只由 designsUsage 与 prewarming 决定，而 prewarming 在原版里同时控制"是否删设计/设角色"。
    /// 我们的做法：
    ///   ① 在 state==0 的 Prefix 里，若为 AI 玩家且本回合尚未处理：
    ///        - 先自己执行 SetAiShipRoles + DeleteOldDesigns（保持原版语义，K 继续兜底）
    ///        - 再临时把 prewarming 置 true，使原版走 GetPredefinedDesign 分支
    ///   ② 在 Postfix 里（state 已经推进到 1）恢复 prewarming=false，避免影响后续逻辑
    /// </summary>
    internal static class TrueMixed
    {
        internal const int UsageFullGenerated = 0;
        internal const int UsageMixed = 1;
        internal const int UsageFullPredefined = 2;

        private static int _lastTurn = -1;
        private static readonly System.Collections.Generic.HashSet<string> _doneThisTurn =
            new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.Dictionary<string, bool> _forced =
            new System.Collections.Generic.Dictionary<string, bool>();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.TrueMixed && HotfixParams.DesignCapPerType <= 0)
            {
                HotfixLog.Info("[SMS.1.G] TrueMixed: disabled");
                return;
            }
            try
            {
                var mn = AccessTools.Method(typeof(CampaignController._AiManageFleet_d__201), "MoveNext");
                if (mn == null) { HotfixLog.Warn("[SMS.1.G] MoveNext not found"); return; }
                harmony.Patch(mn,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(TrueMixed), nameof(Prefix_MoveNext))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(TrueMixed), nameof(Postfix_MoveNext))));
                HotfixLog.Info("[SMS.1.G] TrueMixed v5: armed on _AiManageFleet.MoveNext  true_mixed=" + HotfixParams.TrueMixed
                               + " cap=" + HotfixParams.DesignCapPerType + "  mode=" + HotfixParams.Mode);
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.G] install failed: " + e); }
        }

        internal static void Prefix_MoveNext(CampaignController._AiManageFleet_d__201 __instance)
        {
            try
            {
                if (!HotfixParams.IsApply) return;
                if (ReadState(__instance) != 0) return;

                Player player = null;
                try { player = __instance.player; } catch { }
                if (player == null) return;
                if (IsHuman(player)) return;

                int usage = GetUsage();
                string key = PlayerKey(player) + "|" + Turn();

                // 只处理 Mixed 模式；FullPredefined 原版已走预制菜，FullGenerated 是玩家明确选择慢速
                if (!HotfixParams.TrueMixed || usage != UsageMixed) return;

                if (!_doneThisTurn.Add(key)) return;   // 每人每回合一次

                var cc = CampaignController.Instance;
                if (cc == null) return;

                bool prewarm = false;
                try { prewarm = __instance.prewarming; } catch { }

                int minCount, typeCount;
                string capDetail = CapDetail(player, out minCount, out typeCount);
                int cap = HotfixParams.DesignCapPerType;
                if (cap > 0 && typeCount > 0 && minCount >= cap)
                {
                    HotfixLog.InfoOnce("cap|" + PlayerName(player) + "|" + Turn(), "[SMS.1.G] cap reached: player=" + PlayerName(player) + " (" + ShowName(player) + ") turn=" + Turn()
                                   + " cap=" + cap + " designs:" + capDetail + " -> skip forcing predef branch");
                    return;
                }
                if (HotfixParams.Verbose)
                    HotfixLog.Debug("[SMS.1.G] cap status: player=" + PlayerName(player) + " cap=" + cap + " designs:" + capDetail);

                int before = CountDesigns(player);
                if (!prewarm)
                {
                    // 原版在 prewarming==true 时会跳过这两步，因此我们自己补上以保持语义
                    try { cc.SetAiShipRoles(player); } catch (Exception e1) { HotfixLog.Debug("[SMS.1.G] SetAiShipRoles: " + e1.Message); }
                    try { cc.DeleteOldDesigns(player); } catch (Exception e2) { HotfixLog.Debug("[SMS.1.G] DeleteOldDesigns: " + e2.Message); }
                }

                // 强制走预制菜分支
                try { __instance.prewarming = true; _forced[key] = true; } catch { }

                HotfixLog.Debug("[SMS.1.G] forced predef branch: player=" + PlayerName(player) + " (" + ShowName(player) + ") turn=" + Turn()
                               + " usage=" + usage + " prewarmWas=" + prewarm + " designs=" + before + " global=" + CountAllDesigns());
            }
            catch (Exception e) { HotfixLog.Debug("[SMS.1.G] prefix: " + e.Message); }
        }

        internal static void Postfix_MoveNext(CampaignController._AiManageFleet_d__201 __instance, bool __result)
        {
            try
            {
                if (!HotfixParams.IsApply) return;
                int turnNow = Turn();
                if (turnNow != _lastTurn)
                {
                    _lastTurn = turnNow;
                    _doneThisTurn.Clear();
                    _forced.Clear();
                }
                if (ReadState(__instance) == -1) return;   // 单个协程结束：不清空（其它玩家可能还在跑）

                Player player = null;
                try { player = __instance.player; } catch { }
                if (player == null) return;
                string key = PlayerKey(player) + "|" + Turn();
                if (!_forced.ContainsKey(key) && _forced.Count > 0 && HotfixParams.Verbose)
                    HotfixLog.Info("[SMS.1.G] done key miss: key=" + key + " pending=" + string.Join(",", _forced.Keys));
                if (_forced.ContainsKey(key))
                {
                    try { __instance.prewarming = false; } catch { }
                    _forced.Remove(key);
                    HotfixLog.Info("[SMS.1.G] predef branch done: player=" + PlayerName(player)
                                   + " (" + ShowName(player) + ") designs=" + CountDesigns(player)
                                   + " global=" + CountAllDesigns());
                }
            }
            catch (Exception e) { HotfixLog.Debug("[SMS.1.G] postfix: " + e.Message); }
        }

        /// <summary>读档后按存档模式决定是否预热（由 B2 完成点调用）。</summary>
        internal static void PrewarmIfNeeded()
        {
            int usage = GetUsage();
            bool want = (usage == UsageFullPredefined) || (usage == UsageMixed && HotfixParams.TrueMixed);
            HotfixLog.Info("[SMS.1.G] designsUsage=" + usage + " (0=slow,1=mixed,2=fast)  true_mixed=" + HotfixParams.TrueMixed
                           + "  prewarm=" + want);
            if (!want || !HotfixParams.IsApply) return;
            try
            {
                CampaignController.Instance.CheckPredefinedDesigns(true);
                HotfixLog.Info("[SMS.1.G] prewarmed predefined designs at load (usage=" + usage + ")");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.G] prewarm failed: " + e.Message); }
        }

        // ---------- helpers ----------

        private static int ReadState(object inst)
        {
            try
            {
                var t = inst.GetType();
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var p in t.GetProperties(F))
                    if (p.Name != null && p.Name.IndexOf("__1__state", StringComparison.Ordinal) >= 0)
                        return Convert.ToInt32(p.GetValue(inst));
                foreach (var fi in t.GetFields(F))
                    if (fi.Name != null && fi.Name.IndexOf("__1__state", StringComparison.Ordinal) >= 0)
                        return Convert.ToInt32(fi.GetValue(inst));
            }
            catch { }
            return -99;
        }

        private static int GetUsage()
        {
            try { return (int)CampaignController.Instance.designsUsage; } catch { }
            return -1;
        }

        private static bool IsHuman(Player p)
        {
            try
            {
                var pi = typeof(Player).GetProperty("isPlayer",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null) return Convert.ToBoolean(pi.GetValue(p));
            }
            catch { }
            return false;
        }

        /// <summary>该玩家各舰种设计数（上限判定 + 日志）。</summary>
        private static string CapDetail(Player player, out int minCount, out int types)
        {
            var sb = new System.Text.StringBuilder();
            minCount = int.MaxValue; types = 0;
            try
            {
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                string want = PlayerName(player);
                var vs = CampaignController.Instance.CampaignData.Vessels;
                for (int i = 0; i < vs.Count; i++)
                {
                    var v = vs[i] as Ship;
                    if (v == null) continue;
                    try
                    {
                        var st = v.ToStore();
                        if (st == null || st.designId != Il2CppSystem.Guid.Empty) continue;
                        string pn = null, ty = null;
                        try { pn = st.playerName; } catch { }
                        try { ty = st.shipType; } catch { }
                        if (pn == null || ty == null || pn != want) continue;
                        int n; counts.TryGetValue(ty, out n); counts[ty] = n + 1;
                    }
                    catch { }
                }
                foreach (var kv in counts)
                {
                    sb.Append(' ').Append(kv.Key).Append(':').Append(kv.Value);
                    if (kv.Value < minCount) minCount = kv.Value;
                    types++;
                }
            }
            catch { }
            if (types == 0) minCount = 0;
            return sb.ToString();
        }

        private static int CountAllDesigns()
        {
            int n = 0;
            try
            {
                var vs = CampaignController.Instance.CampaignData.Vessels;
                for (int i = 0; i < vs.Count; i++)
                {
                    var v = vs[i] as Ship;
                    if (v == null) continue;
                    try { var st = v.ToStore(); if (st != null && st.designId == Il2CppSystem.Guid.Empty) n++; } catch { }
                }
            }
            catch { }
            return n;
        }

        private static int CountDesigns(Player player)
        {
            int n = 0;
            try
            {
                var vs = CampaignController.Instance.CampaignData.Vessels;
                for (int i = 0; i < vs.Count; i++)
                {
                    var v = vs[i] as Ship;
                    if (v == null) continue;
                    try
                    {
                        var st = v.ToStore();
                        if (st == null || st.designId != Il2CppSystem.Guid.Empty) continue;
                        string pn = null;
                        try { pn = st.playerName; } catch { }
                        string want = PlayerName(player);
                        if (pn != null && want != "?" && pn == want) n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        private static int Turn()
        {
            try { return CampaignController.Instance.CurrentDate.turn; } catch { }
            return 0;
        }

        /// <summary>键必须稳定：Il2Cpp 包装对象的 GetHashCode 每次可能不同，改用原始国名。</summary>
        private static string PlayerKey(Player p) { return PlayerName(p); }
        /// <summary>原始国名（存档里的 playerName / playerOriginal 用的就是这个）。</summary>
        private static string PlayerName(Player p)
        {
            try
            {
                var pd = p.data;
                if (pd != null && !string.IsNullOrEmpty(pd.name)) return pd.name;
            }
            catch { }
            try
            {
                var nm = p.Name(false);
                if (!string.IsNullOrEmpty(nm)) return nm;
            }
            catch { }
            return "?";
        }

        /// <summary>显示名（本地化，仅用于日志）。</summary>
        private static string ShowName(Player p)
        {
            try { var nm = p.Name(false); if (!string.IsNullOrEmpty(nm)) return nm; } catch { }
            return PlayerName(p);
        }
    }
}
