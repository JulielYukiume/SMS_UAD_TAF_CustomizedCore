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
        /// [SMS3-20b] 在 `Database.FillDatabase()` 把 `Config.MaxGunGrade` 抬高之后**重跑一次等级填充**。
        ///
        /// 背景：`LoadData()` 跑在 `GameData.PostProcessAll` 的 Prefix，那时 `MaxGunGrade` 还是 guns.csv 推出来的值（本 mod 为 5）。
        /// 如果 `technologies.csv` 里有 Mk6 科技（`gun(x;6)`）而 guns.csv 没有 grade-6 列，
        /// 科技表会被我们的 data-driven caps 扩到 6，但 `GunData` / `PartModelData` 的 grade 字典仍只到 5
        /// ⇒ 原生 `Part.GetModelNameScale(..., gunGradeOverride=6)` 取到 null ⇒ `Ui.RefreshParts` 里
        /// `Enumerable.Select/Sum` 抛 `ArgumentNullException`（1950+ 主炮列表打不开，实测 26-11-15_18-37-3.log）。
        ///
        /// `FillGradeData` 的语义正是"缺失等级复制前一级"（duke 的安全回滚：Mk6 沿用 Mk5 数据），所以这里只是把它补齐。
        /// </summary>
        public static void ReapplyGradeData()
        {
            if (Config.MaxGunGrade <= 5)
                return;

            foreach (var gd in G.GameData.guns.Values)
            {
                ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                ModUtils.FillGradeData(gd.barrelWeights, Config.MaxGunGrade);
                ModUtils.FillGradeData(gd.firerates, Config.MaxGunGrade);
                ModUtils.FillGradeData(gd.penetrations, Config.MaxGunGrade);
                ModUtils.FillGradeData(gd.ranges, Config.MaxGunGrade);
                ModUtils.FillGradeData(gd.shellVelocities, Config.MaxGunGrade);
                ModUtils.FillGradeData(gd.shellWeights, Config.MaxGunGrade);
            }

            foreach (var pm in G.GameData.partModels.Values)
            {
                if (pm.caliberLengthModifiers.count < 5)
                    continue;   // 只处理火炮类 partModel

                ModUtils.FillGradeData(pm.caliberLengthModifiers, Config.MaxGunGrade);
                ModUtils.FillGradeData(pm.maxScales, Config.MaxGunGrade);
                ModUtils.FillGradeData(pm.models, Config.MaxGunGrade);
                ModUtils.FillGradeData(pm.scales, Config.MaxGunGrade);
                ModUtils.FillGradeData(pm.weightModifiers, Config.MaxGunGrade);
            }

            GunData.gradesTotal = Config.MaxGunGrade;
            Melon<TweaksAndFixes>.Logger.Msg("[SMS.3.GRADE] re-applied grade data up to " + Config.MaxGunGrade + " (missing grades copy previous, e.g. Mk6<-Mk5)");
        }

        public static void LoadData()
        {
            GunDataExtension.LoadData();
            PartModelExtension.LoadData();
        }
    }
}