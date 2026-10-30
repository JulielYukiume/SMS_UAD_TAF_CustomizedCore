using Il2Cpp;
using MelonLoader;
using System.Text;
using UnityEngine;

namespace TweaksAndFixes
{
    internal class HullData : Serializer.IPostProcess
    {
        public static Dictionary<string, HullData> _Data = new();

        [Serializer.Field] public string name = string.Empty;
        [Serializer.Field] public float waterline = 0;
        [Serializer.Field] public float stabilityForward = 0;
        [Serializer.Field] public string sectioning = string.Empty;
        public List<List<int>> sectioningParsed = new();

        /// <summary>
        /// 按**部件**的自定义段序：partName -> (middleCount -> 段索引列表)。
        /// 来源：parts.csv 里的 `sect_<n>(...)` 参数（duke 的 `sectioningParsedPart`）。
        /// </summary>
        public Dictionary<string, Dictionary<int, List<int>>> sectioningParsedPart = new();
        public bool useDefaultSectioning = true;

        public void PostProcess()
        {
            // Melon<TweaksAndFixes>.Logger.Msg($"{name}: {waterline}");

            if (sectioning == "cyclical")
            {
                useDefaultSectioning = true;
            }
            else
            {
                useDefaultSectioning = false;

                var entries = sectioning.Trim().Split(',');

                Melon<TweaksAndFixes>.Logger.Msg($"{entries.Length}");

                foreach (var entry in entries)
                {
                    var list = entry.Trim().Split(';');
                    List<int> parsedList = new();

                    foreach (var index in list)
                    {
                        if (index.Length == 0)
                            continue;

                        if (!ModUtils.TryParse(index, out int parsed))
                        {
                            continue;
                        }

                        parsedList.Add(parsed);
                    }

                    sectioningParsed.Add(parsedList);
                }
            }

            _Data.Add(name, this);
        }

        public static bool ModifiedSectionOrder(string key)
        {
            if (!_Data.ContainsKey(key))
                return false;

            return !_Data[key].useDefaultSectioning;
        }

        public static void LoadData()
        {
            FilePath fp = new FilePath(FilePath.DirType.ModsDir, "hullData.csv");
            if (!fp.Exists)
            {
                return;
            }

            List<HullData> list = new();
            string? text = Serializer.CSV.GetTextFromFile(fp.path);

            if (text == null)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Failed to load `hullData.csv`.");
                return;
            }

            _Data.Clear();

            Serializer.CSV.Read<List<HullData>, HullData>(text, list, true, true);

            foreach (var d in G.GameData.parts)
            {
                foreach (var p in d.Value.paramx)
                {
                    if (!p.Key.Contains("sect"))
                        continue;

                    if (!p.key.Contains('_'))
                    {
                        // ERROR
                        continue;
                    }

                    if (!ModUtils.TryParse(p.Key.Split('_')[1], out int idx))
                    {
                        // ERROR
                        continue;
                    }

                    if (!_Data.ContainsKey(d.Value.model))
                    {
                        _Data[d.Value.model] = new HullData();
                        _Data[d.Value.model].name = d.Value.model;
                    }

                    var entry = _Data[d.Value.model];

                    // ★ 与 duke 对齐（docs/75）：`sect_*` 参数属于**按部件**的自定义段序，
                    //   应写入 sectioningParsedPart（partName -> middleCount -> 段索引列表），
                    //   而不是塞进 sectioningParsed（后者是 hullData.csv `sectioning` 列专用，
                    //   按 middleCount 索引全局段序）。
                    //
                    //   同时用"部件名"和"参数键"两个 key 存同一份数据：
                    //   · 部件名：更新时按 `hull.data.name` 查询（duke 的 UpdateSections 用法）
                    //   · 参数键：兼容按 `sect_<n>` 查询的写法
                    string partName = d.Value.name;
                    if (string.IsNullOrEmpty(partName))
                        partName = d.Value.model;

                    if (!entry.sectioningParsedPart.ContainsKey(partName))
                        entry.sectioningParsedPart[partName] = new Dictionary<int, List<int>>();
                    if (!entry.sectioningParsedPart.ContainsKey(p.Key))
                        entry.sectioningParsedPart[p.Key] = new Dictionary<int, List<int>>();

                    List<int> parsedList = new();

                    foreach (var index in p.Value)
                    {
                        if (index.Length == 0)
                            continue;

                        if (!ModUtils.TryParse(index, out int parsed))
                        {
                            continue;
                        }

                        parsedList.Add(parsed);
                        entry.useDefaultSectioning = false;
                    }

                    entry.sectioningParsedPart[partName][idx] = parsedList;
                    entry.sectioningParsedPart[p.Key][idx] = parsedList;
                }
            }

            Melon<TweaksAndFixes>.Logger.Msg($"Loaded {_Data.Count} hull data entries.");
        }
    }
}
