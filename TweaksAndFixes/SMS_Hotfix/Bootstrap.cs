using MelonLoader;

namespace TAFHotfixBatch1
{
    /// <summary>SMS 集成入口：由 TAF 主类在 Harmony PatchAll 之后调用。</summary>
    internal static class Bootstrap
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            HotfixParams.Load();
            HotfixLog.Init();

            HotfixLog.Info("==================================================");
            HotfixLog.Info("SMS hotfix (integrated into TAF)  Mode=" + HotfixParams.Mode);
            HotfixLog.Info("==================================================");

            Modules.RefitChainFix.Install(harmony);
            Modules.RefitCostRefresh.Install(harmony);
            Modules.RefitCostDisplay.Install(harmony);
            Modules.AiRefitRestore.Install(harmony);
            Modules.DebugAiRefit.Install(harmony);
            Modules.KeepLastDesign.Install(harmony);
            Modules.SharedBackfillProbe.Install(harmony);
            Modules.ReparationCopyFix.Install(harmony);
            Modules.ReparationMothball.Install(harmony);
            Modules.ShipLossGuard.Install(harmony);
            Modules.DuplicateDesignGuard.Install(harmony);
            Modules.ZeroedShipRepair.Install(harmony);
            Modules.RepairNotifier.Install(harmony);
            Modules.PredefPoolProbe.Install(harmony);
            Modules.PredefForceOff.Install(harmony);
            Modules.LogSuppress.Install(harmony);
            Modules.CheatTechFix.Install(harmony);
            Modules.ShipTypeProbe.Install(harmony);
            Modules.ShipTypeSupportApply.Install(harmony);
            Modules.TonnageCheck.Install(harmony);
            Modules.DesignStatusAudit.Install(harmony);
            global::TweaksAndFixes.HullDataApply.Install(harmony);
            Modules.IsPartAvailableProbe.Install(harmony);
            Modules.HullTypeDump.Install(harmony);
            Modules.ConstructorTypeProbe.Install(harmony);
            Modules.TrueMixed.Install(harmony);
            Modules.ShipDesignRelink.Install(harmony);
            Modules.SunkGraceWindow.Install(harmony);

            HotfixLog.Info("SMS hotfix install complete.");
        }
    }
}