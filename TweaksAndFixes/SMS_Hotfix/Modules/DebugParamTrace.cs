using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    // Debug: trace which global params the game reads, with a short managed stack.
    // Static RE of the string literal failed (metadata-usage indirection), so we observe at runtime.
    internal static class DebugParamTrace
    {
        private static int _n;
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.DebugParamTrace) { HotfixLog.Info("[SMS.1.P] DebugParamTrace: disabled"); return; }
            try
            {
                var m = AccessTools.Method(typeof(CampaignController), "Param");
                if (m == null) { HotfixLog.Warn("[SMS.1.P] CampaignController::Param not found"); return; }
                harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(DebugParamTrace), nameof(Prefix))));
                HotfixLog.Info("[SMS.1.P] DebugParamTrace: armed on CampaignController::Param");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.1.P] install failed: " + e); }
        }

        internal static void Prefix(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return;
                if (_n++ > 400) return;
                bool interesting = name.IndexOf("generation", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("fleet", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!interesting) return;
                string st = "";
                try
                {
                    var frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
                    int take = frames == null ? 0 : Math.Min(6, frames.Length);
                    for (int i = 0; i < take; i++) st += " <- " + frames[i].GetMethod().DeclaringType.Name + "." + frames[i].GetMethod().Name;
                }
                catch { }
                HotfixLog.Info("[SMS.1.P] Param('" + name + "')" + st);
            }
            catch { }
        }
    }
}