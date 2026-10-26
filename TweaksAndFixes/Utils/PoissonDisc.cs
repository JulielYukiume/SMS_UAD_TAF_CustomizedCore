using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.RH] Poisson-disk 采样（Bridson）—— 甲板装饰件布点用。
    /// 3.22 里由 `ModUtils.GeneratePoissonDiscPoints` 提供；我们的 3.21.1 树没有该成员，这里按同样语义补上。
    /// </summary>
    internal static class PoissonDisc
    {
        public static List<Vector3> Generate(Bounds area, float separation)
        {
            var result = new List<Vector3>();
            if (separation <= 0.0001f) separation = 0.0001f;

            // ★ [SMS.1.PROP] 与 duke 的用法对齐：调用方传入的 size 是**半宽/半深**
            //   （例如 `(deckBounds.size.x - sep/4) / 2`），所以采样范围是 center ± size。
            //   之前按 size 视为全宽（center ± size/2），导致布点只覆盖中间一半宽度 ⇒
            //   装饰物全挤在船舯一条竖线上（port50 实测）。
            float minX = area.center.x - area.size.x;
            float maxX = area.center.x + area.size.x;
            float minZ = area.center.z - area.size.z;
            float maxZ = area.center.z + area.size.z;
            float y = area.center.y;

            float width = maxX - minX;
            float depth = maxZ - minZ;
            if (width <= 0f || depth <= 0f) return result;

            float cell = separation / Mathf.Sqrt(2f);
            int gridW = Mathf.Max(1, Mathf.CeilToInt(width / cell));
            int gridH = Mathf.Max(1, Mathf.CeilToInt(depth / cell));
            var grid = new int[gridW * gridH];
            for (int i = 0; i < grid.Length; i++) grid[i] = -1;

            var active = new List<Vector2>();
            var points = new List<Vector2>();

            var first = new Vector2(Random.Range(minX, maxX), Random.Range(minZ, maxZ));
            active.Add(first); points.Add(first);
            grid[Index(first, minX, minZ, cell, gridW, gridH)] = 0;

            int attempts = 30;
            while (active.Count > 0)
            {
                int idx = Random.Range(0, active.Count);
                Vector2 origin = active[idx];
                bool accepted = false;

                for (int k = 0; k < attempts; k++)
                {
                    float angle = Random.value * Mathf.PI * 2f;
                    float radius = Random.Range(separation, separation * 2f);
                    var cand = new Vector2(origin.x + Mathf.Cos(angle) * radius,
                                           origin.y + Mathf.Sin(angle) * radius);
                    if (cand.x < minX || cand.x > maxX || cand.y < minZ || cand.y > maxZ) continue;

                    int gi = Index(cand, minX, minZ, cell, gridW, gridH);
                    int gx = gi % gridW, gz = gi / gridW;

                    bool ok = true;
                    for (int zz = Mathf.Max(0, gz - 2); zz <= Mathf.Min(gridH - 1, gz + 2) && ok; zz++)
                    {
                        for (int xx = Mathf.Max(0, gx - 2); xx <= Mathf.Min(gridW - 1, gx + 2); xx++)
                        {
                            int pi = grid[xx + zz * gridW];
                            if (pi < 0) continue;
                            if (Vector2.Distance(points[pi], cand) < separation) { ok = false; break; }
                        }
                    }
                    if (!ok) continue;

                    active.Add(cand);
                    points.Add(cand);
                    grid[gi] = points.Count - 1;
                    accepted = true;
                    break;
                }

                if (!accepted) active.RemoveAt(idx);
            }

            foreach (var q in points)
                result.Add(new Vector3(q.x, y, q.y));
            return result;
        }

        private static int Index(Vector2 p, float minX, float minZ, float cell, int gridW, int gridH)
        {
            int gx = Mathf.Clamp((int)((p.x - minX) / cell), 0, gridW - 1);
            int gz = Mathf.Clamp((int)((p.y - minZ) / cell), 0, gridH - 1);
            return gx + gz * gridW;
        }
    }
}
