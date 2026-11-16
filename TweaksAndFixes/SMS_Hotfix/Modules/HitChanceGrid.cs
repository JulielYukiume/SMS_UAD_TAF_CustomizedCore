using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HC] "命定之死"：把 `Ship::HitChance` 末尾 `Mathf.Clamp(chance, 0.01f, …)` 的 **1% 下限**
    /// 改成可调（默认 0.1%）。采用**字节级定点补丁**（不是 Harmony）：
    ///
    ///   · Harmony 挂不上：`HitChance` 参数含 `Nullable<…>` 且被 `il2cpp_runtime_invoke` 调用（rc7/rc8 崩）；
    ///   · `Mathf.Clamp` 是全局热路径，patch 后启动即退（rc11/rc12）；
    ///   · 因此：**直接改那一条指令的 disp32**，把常量指到我们自己分配的 float。
    ///
    /// 识别方式（不依赖方法指针，rc13 失败原因：`GetFunctionPointer()` 返回的是 thunk）：
    ///   1) 取 GameAssembly 模块基址，枚举 `.text` / `il2cpp` 段；
    ///   2) 扫描 `F3 0F 10 0D disp32` 且后随 `0F 28 C6 E8`（movss xmm1,[..]; movaps xmm0,xmm6; call）；
    ///   3) 只保留 disp 指向 **0.01f** 的那几处，取**最后一处**（1.7 实测即 HitChance 的 Clamp 下限）；
    ///   4) `VirtualProtect` → 改 disp32 → 恢复。
    /// </summary>
    internal static class HitChanceGrid
    {
        private const float VanillaFloor = 0.01f;
        private static float _floor = 0.001f;
        private static IntPtr _constPtr = IntPtr.Zero;
        private static bool _installed;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, PAGE_READWRITE = 0x04, PAGE_EXECUTE_READWRITE = 0x40;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                _floor = HotfixParams.HitChanceGrid;
                if (_floor <= 0f || _floor >= VanillaFloor) { HotfixLog.Info("[SMS.2.HC] floor patch disabled (floor=" + _floor + ")"); return; }

                IntPtr baseAddr = GetModuleHandle("GameAssembly.dll");
                if (baseAddr == IntPtr.Zero) { HotfixLog.Warn("[SMS.2.HC] GameAssembly handle null"); return; }

                IntPtr best = IntPtr.Zero;
                foreach (var sec in Sections(baseAddr))
                {
                    long p = sec.Item1.ToInt64(), end = p + sec.Item2;
                    for (long cur = p; cur + 13 < end; cur++)
                    {
                        IntPtr q = new IntPtr(cur);
                        if (Marshal.ReadByte(q) != 0xF3) continue;
                        if (Marshal.ReadByte(q + 1) != 0x0F) continue;
                        if (Marshal.ReadByte(q + 2) != 0x10) continue;
                        if (Marshal.ReadByte(q + 3) != 0x0D) continue;
                        if (Marshal.ReadByte(q + 8) != 0x0F || Marshal.ReadByte(q + 9) != 0x28 ||
                            Marshal.ReadByte(q + 10) != 0xC6 || Marshal.ReadByte(q + 11) != 0xE8) continue;
                        int disp = Marshal.ReadInt32(q + 4);
                        IntPtr tgt = q + 8 + disp;
                        float v;
                        try { v = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(tgt)), 0); }
                        catch { continue; }
                        if (Math.Abs(v - VanillaFloor) < 1e-9f) best = q;   // 取最后一处
                    }
                }
                if (best == IntPtr.Zero) { HotfixLog.Warn("[SMS.2.HC] clamp site not found (scan)"); return; }

                IntPtr mem = AllocNear(best, out long delta);
                if (mem == IntPtr.Zero) { HotfixLog.Warn("[SMS.2.HC] alloc near code failed"); return; }
                Marshal.WriteInt32(mem, BitConverter.ToInt32(BitConverter.GetBytes(_floor), 0));

                uint old;
                if (!VirtualProtect(best, (UIntPtr)8, PAGE_EXECUTE_READWRITE, out old)) { HotfixLog.Warn("[SMS.2.HC] VirtualProtect failed"); return; }
                Marshal.WriteInt32(best + 4, (int)delta);
                uint d2; VirtualProtect(best, (UIntPtr)8, old, out d2);

                _constPtr = mem; _installed = true;
                HotfixLog.Info("[SMS.2.HC] floor byte-patched: site=" + best.ToString("X") + " newConst=" + mem.ToString("X")
                    + " floor=" + _floor.ToString("F6") + " (vanilla 0.01)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HC] install failed: " + e); }
        }


        /// <summary>在目标指令 ±2GB 内分配一页（Windows 会按 64KB 粒度就近分配）。</summary>
        private static IntPtr AllocNear(IntPtr near, out long delta)
        {
            delta = 0;
            long baseAddr = near.ToInt64() & ~0xFFFFL;      // 64KB 对齐
            const long step = 0x10000L;
            const long maxSteps = 0x7000;                    // ±~448MB 搜索
            for (long k = 1; k <= maxSteps; k++)
            {
                foreach (long dir in new long[] { +1, -1 })
                {
                    long hint = baseAddr + dir * k * step;
                    if (hint <= 0x10000) continue;
                    IntPtr mem = VirtualAlloc(new IntPtr(hint), (UIntPtr)0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                    if (mem == IntPtr.Zero) continue;
                    long d = (long)mem - (near.ToInt64() + 8);
                    if (d >= int.MinValue && d <= int.MaxValue) { delta = d; return mem; }
                    VirtualFree(mem, UIntPtr.Zero, 0x8000);      // MEM_RELEASE，换下一个
                }
            }
            // 最后再试系统随意分配（可能不满足 ±2GB，但值得一试）
            IntPtr any = VirtualAlloc(IntPtr.Zero, (UIntPtr)0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (any != IntPtr.Zero)
            {
                long d = (long)any - (near.ToInt64() + 8);
                if (d >= int.MinValue && d <= int.MaxValue) { delta = d; return any; }
                VirtualFree(any, UIntPtr.Zero, 0x8000);
            }
            return IntPtr.Zero;
        }

        /// <summary>枚举可执行段（按 PE 头），返回 (起始地址, 长度)。</summary>
        private static System.Collections.Generic.List<Tuple<IntPtr, int>> Sections(IntPtr mod)
        {
            var list = new System.Collections.Generic.List<Tuple<IntPtr, int>>();
            int lfanew = Marshal.ReadInt32(mod + 0x3C);
            IntPtr nt = mod + lfanew;
            short nsec = Marshal.ReadInt16(nt + 6);
            short optSize = Marshal.ReadInt16(nt + 20);
            IntPtr secTab = nt + 24 + optSize;
            for (int i = 0; i < nsec; i++)
            {
                IntPtr s = secTab + i * 40;
                byte b0 = Marshal.ReadByte(s), b1 = Marshal.ReadByte(s + 1);
                string name = new string(new char[] { (char)b0, (char)b1 });
                int vsize = Marshal.ReadInt32(s + 8);
                int vaddr = Marshal.ReadInt32(s + 12);
                if (name != ".t" && name != "il") continue;      // .text / il2cpp
                if (vsize <= 0) continue;
                list.Add(Tuple.Create(mod + vaddr, vsize));
            }
            return list;
        }

        internal static void SetFloor(float v)
        {
            if (!_installed || v <= 0f || v >= VanillaFloor) return;
            _floor = v;
            try { Marshal.WriteInt32(_constPtr, BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); } catch { }
        }
    }
}
