using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.SECT] 3.22 的 `hullData.csv` 支持（B4）——**只移植与"舯段分区（sectioning）"相关的部分**。
    ///
    /// 背景（docs/13 §2.2、docs/38 §2）：
    /// 3.21.x 的舰体舯段（middle sections）由游戏按"循环索引"克隆，某些船体因此出现**舯段色差**；
    /// 3.22 引入 `hullData.csv` 的 `sectioning` 列，允许按"每 N 段使用哪些基础段索引"来精确指定，
    /// duke 用它修掉了色差。这里移植该数据层 + 应用逻辑（见 `Harmony/SectionsFix.cs`）。
    ///
    /// CSV 格式（与 Beta6 一致）：
    ///   name,waterline,stabilityForward,sectioning,...
    ///   ship_model,0,0,"0;1;2;3,0;2;1;3"      ← 逗号分组 = 段数档位；分号 = 该档位使用的 base 段索引
    ///   another_model,0,0,"cyclical"          ← 表示沿用原版循环
    /// </summary>
    internal class HullData : Serializer.IPostProcess
    {
        public static readonly Dictionary<string, HullData> _Data =
            new Dictionary<string, HullData>();

        [Serializer.Field]
        public string name = string.Empty;
        public float waterline;
        [Serializer.Field]
        public float stabilityForward;
        public string sectioning = string.Empty;

        /// <summary>每档段数 → 使用的基础段索引列表。</summary>
        public List<List<int>> sectioningParsed = new List<List<int>>();

        public bool useDefaultSectioning = true;

        public void PostProcess()
        {
            try
            {
                if (string.IsNullOrEmpty(sectioning) || sectioning.Trim().ToLowerInvariant() == "cyclical")
                {
                    useDefaultSectioning = true;
                }
                else
                {
                    useDefaultSectioning = false;
                    string[] groups = sectioning.Trim().Split(',');
                    for (int i = 0; i < groups.Length; i++)
                    {
                        string[] parts = groups[i].Trim().Split(';');
                        List<int> list = new List<int>();
                        foreach (string p in parts)
                        {
                            int v;
                            if (p.Length != 0 && int.TryParse(p.Trim(), out v)) list.Add(v);
                        }
                        sectioningParsed.Add(list);
                    }
                }
            }
            catch (System.Exception e)
            {
                HotfixLog.Warn("[SMS.1.SECT] hullData parse '" + name + "': " + e.Message);
                useDefaultSectioning = true;
            }

            if (!string.IsNullOrEmpty(name))
                _Data[name] = this;
        }

        public static bool ModifiedSectionOrder(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            HullData hd;
            if (!_Data.TryGetValue(key, out hd) || hd == null) return false;
            return !hd.useDefaultSectioning;
        }

        public static HullData Get(string key)
        {
            HullData hd;
            return _Data.TryGetValue(key, out hd) ? hd : null;
        }

        public static void LoadData()
        {
            try
            {
                FilePath file = new FilePath(FilePath.DirType.ModsDir, "hullData.csv");
                if (!file.Exists)
                {
                    HotfixLog.Info("[SMS.1.SECT] hullData.csv not found, sectioning stays vanilla");
                    return;
                }

                var output = new List<HullData>();
                Serializer.CSV.Read<List<HullData>, HullData>(output, file, true);
                HotfixLog.Info("[SMS.1.SECT] hullData.csv loaded: " + output.Count + " entrie(s), "
                               + _Data.Count + " in registry");
            }
            catch (System.Exception e)
            {
                HotfixLog.Warn("[SMS.1.SECT] hullData.csv load failed: " + e.Message);
            }
        }
    }
}
