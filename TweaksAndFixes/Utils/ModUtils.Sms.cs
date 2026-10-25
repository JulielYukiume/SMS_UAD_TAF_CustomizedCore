using System.Collections.Generic;
using System.Globalization;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.PORT] `ModUtils` 的 3.22 补充部分（Beta6 独有方法）。
    ///
    /// 来源：Beta6 反编译 `TweaksAndFixes/ModUtils.cs`
    ///   · `NumToString(int/float/double, string format)`（L422-435）
    ///   · `SplitSmart(string)`（L1089-1120）
    ///
    /// 这两个是 3.22 其它模块（UI 数值显示、CSV 解析）依赖的工具函数，我们先补齐。
    /// </summary>
    public static partial class ModUtils
    {
        /// <summary>按不变文化格式化整数（避免本地化把小数点变成逗号）。</summary>
        public static string NumToString(int num, string format)
        {
            return num.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>按不变文化格式化浮点数。</summary>
        public static string NumToString(float num, string format)
        {
            return num.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>按不变文化格式化双精度数。</summary>
        public static string NumToString(double num, string format)
        {
            return num.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// "智能"逗号分割：尊重双引号内的逗号与反斜杠转义。
        ///
        /// 与 Beta6 实现逐行一致：
        ///   · `\` 转义下一个字符（该次不参与引号/逗号判定）；
        ///   · `"` 切换"在引号内"状态（引号本身不进入结果）；
        ///   · 只有在**引号外**遇逗号才切分。
        /// </summary>
        public static List<string> SplitSmart(string s)
        {
            var list = new List<string>();
            if (s == null) return list;

            int start = 0;      // 当前字段起点
            bool inQuotes = false;
            bool escaped = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (!escaped && c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;   // 引号本身不进入结果
                }

                escaped = false;

                if (!inQuotes && c == ',')
                {
                    list.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }

            list.Add(s.Substring(start));
            return list;
        }
    }
}
