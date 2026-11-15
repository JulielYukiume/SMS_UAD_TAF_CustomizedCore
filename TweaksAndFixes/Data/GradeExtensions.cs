//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using MelonLoader;
using Il2Cpp;

#pragma warning disable CS0649
#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class GradeExtensions
    {
        public class GunDataExtension : Serializer.IPostProcess
        {
            [Serializer.Field] string name;
            [Serializer.Field] string param;

            public void PostProcess()
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(param))
                    return;

                if (!G.GameData.guns.TryGetValue(name, out var gd))
                    return;

                // To make life easier for modders, let them use the column names
                // in the csv rather than the names of the fields.
                param = param.Replace("firerate(", "firerates(");
                param = param.Replace("barrelW(", "barrelWeights(");
                param = param.Replace("firerate(", "firerates(");
                param = param.Replace("shellW(", "shellWeights(");
                param = param.Replace("shellV(", "shellVelocities(");
                param = param.Replace("range(", "ranges(");
                param = param.Replace("accuracy(", "accuracies(");
                param = param.Replace("penetration(", "penetrations(");

                if (!Serializer.Human.FillIndexedDicts(gd, param, true))
                    Melon<TweaksAndFixes>.Logger.Msg($"Failed to parse params for gun data {gd.name}!");

                int max = GetMaxGrade(gd);
                Config.MaxGunGrade = Math.Max(Config.MaxGunGrade, max);
            }

            public static int GetMaxGrade(GunData gd)
            {
                int max = 6;
                for (; ; ++max)
                {
                    if (gd.accuracies.ContainsKey(max)
                        || gd.barrelWeights.ContainsKey(max)
                        || gd.firerates.ContainsKey(max)
                        || gd.penetrations.ContainsKey(max)
                        || gd.ranges.ContainsKey(max)
                        || gd.shellVelocities.ContainsKey(max)
                        || gd.shellWeights.ContainsKey(max)
                        || gd.accuracies.ContainsKey(max))
                        continue;

                    break;
                }
                return max - 1;
            }

            public static void LoadData()
            {
                var text = Serializer.CSV.GetTextFromFileOrAsset("guns");
                if (text != null)
                {
                    List<GunDataExtension> list = new List<GunDataExtension>();
                    Serializer.CSV.Read<List<GunDataExtension>, GunDataExtension>(text, list, true, true);
                }

                // We need to ensure that everything goes up to the max grade.
                foreach (var gd in G.GameData.guns.Values)
                {
                    ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.barrelWeights, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.firerates, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.penetrations, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.ranges, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.shellVelocities, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.shellWeights, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                }
                if (Config.MaxGunGrade > 5)
                {
                    GunData.gradesTotal = Config.MaxGunGrade;
                    Melon<TweaksAndFixes>.Logger.Msg("Max gun grade is now " + Config.MaxGunGrade);
                }
            }
        }

        public class PartModelExtension : Serializer.IPostProcess
        {
            [Serializer.Field] string name;
            [Serializer.Field] string param;

            public void PostProcess()
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(param))
                    return;

                if (!G.GameData.partModels.TryGetValue(name, out var pm))
                    return;

                // To make life easier for modders, let them use the column names
                // in the csv rather than the names of the fields.
                param = param.Replace("model(", "models(");
                param = param.Replace("scale(", "scales(");
                param = param.Replace("max_scale(", "maxScales(");
                param = param.Replace("weight_modifier(", "weightModifiers(");
                param = param.Replace("caliber_length_modifier(", "caliberLengthModifiers(");

                Serializer.Human.FillIndexedDicts(pm, param, true);
            }

            public static void LoadData()
            {
                var text = Serializer.CSV.GetTextFromFileOrAsset("partModels");
                if (text != null)
                {
                    List<PartModelExtension> list = new List<PartModelExtension>();
                    Serializer.CSV.Read<List<PartModelExtension>, PartModelExtension>(text, list, true, true);
                }

                // We need to ensure that everything goes up to the max grade.
                foreach (var pm in G.GameData.partModels.Values)
                {
                    // Skip partmodels that aren't guns
                    if (pm.caliberLengthModifiers.count < 5)
                        continue;

                    ModUtils.FillGradeData(pm.caliberLengthModifiers, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.maxScales, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.models, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.scales, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.weightModifiers, Config.MaxGunGrade);
                }
            }
        }

        /// <summary>
        /// [SMS3-20c] 抬高 `Config.MaxGunGrade` 之后**逐列补齐**（把 rc39c 的"整级回滚"升级为"按列回滚"）：
        ///
        /// 1) **先**看 `partModels`：某 mk 的 `models` 为空、或 `scales`/`maxScales`/`weightModifiers`/
        ///    `caliberLengthModifiers` 为 0 / -1 ⇒ 该列"无有效数据"，单独回滚到最近一个有效级；
        /// 2) **再**看 `guns` 射表：某 mk 的 accuracy/barrelW/firerate/penetration/range/shellV/shellW
        ///    为 0 或缺失 ⇒ 该列单独回滚；
        /// 3) 只有所有列都缺的等级才算整级无效（日志会点出来）。
        ///
        /// 这是 NathanKell 时代 `FillGradeData` 的意图（Mk6 ← Mk5 安全回滚），旧实现按整级回滚且不看"值为空"。
        /// </summary>
        public static void ReapplyGradeData()
        {
            if (Config.MaxGunGrade <= 5)
                return;

            int max = Config.MaxGunGrade;
            int pmCount = 0, pmReal = 0, gunCount = 0, gunReal = 0;

            // ---- 1) partModels 先行（模型会影响炮重与倍径修正） ----
            foreach (var pm in G.GameData.partModels.Values)
            {
                if (pm.caliberLengthModifiers.count < 5)
                    continue;   // 只处理火炮类 partModel

                pmCount++;

                bool real = pm.models.ContainsKey(max) && ModUtils.IsValidGradeValue(pm.models[max]);
                if (real) pmReal++;

                ModUtils.FillGradeData(pm.models, max);
                ModUtils.FillGradeData(pm.scales, max);
                ModUtils.FillGradeData(pm.maxScales, max);
                ModUtils.FillGradeData(pm.caliberLengthModifiers, max);
                ModUtils.FillGradeData(pm.weightModifiers, max);

                if (Config.Param("verbose", 0f) > 0f && !real)
                    Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] partModel " + pm.name
                        + ": model_" + max + " empty -> per-column fallback");
            }

            // ---- 2) guns 射表 ----
            foreach (var gd in G.GameData.guns.Values)
            {
                gunCount++;

                bool real = gd.accuracies.ContainsKey(max) && ModUtils.IsValidGradeValue(gd.accuracies[max]);
                if (real) gunReal++;

                ModUtils.FillGradeData(gd.accuracies, max);
                ModUtils.FillGradeData(gd.barrelWeights, max);
                ModUtils.FillGradeData(gd.firerates, max);
                ModUtils.FillGradeData(gd.penetrations, max);
                ModUtils.FillGradeData(gd.ranges, max);
                ModUtils.FillGradeData(gd.shellVelocities, max);
                ModUtils.FillGradeData(gd.shellWeights, max);
            }

            GunData.gradesTotal = Config.MaxGunGrade;

            Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] per-column grade fill up to " + max
                + ": partModels " + pmCount + " (real model at grade " + max + ": " + pmReal + "), "
                + "guns " + gunCount + " (real table at grade " + max + ": " + gunReal + ")");

            if (pmReal == 0 || gunReal == 0)
                Melon<TweaksAndFixes>.Logger.Warning("[SMS.3.GRADE] grade " + max
                    + " has no "
                    + (pmReal == 0 ? "real model" : "")
                    + (pmReal == 0 && gunReal == 0 ? " / " : "")
                    + (gunReal == 0 ? "real gun table" : "")
                    + " in mod data -> columns copied from the nearest valid grade (normal for Mk6-on-Mk5 mods)");
        }

        public static void LoadData()
        {
            GunDataExtension.LoadData();
            PartModelExtension.LoadData();
        }
    }
}