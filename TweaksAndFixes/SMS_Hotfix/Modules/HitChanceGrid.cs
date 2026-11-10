using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HC] "命定之死"：把 `Ship::HitChance` 末尾的 **1% 硬下限**（`Mathf.Clamp(chance, 0.01f, …)`）
    /// 改成可调下限（默认 0.1%）。
    ///
    /// 为什么不用 Harmony：
    ///   · `Ship.HitChance` 参数含 `Nullable<…>`，且被 `il2cpp_runtime_invoke` 调用 ⇒ patch 必崩（rc7/rc8）；
    ///   · `UnityEngine.Mathf.Clamp` 是全局热路径 ⇒ patch 后启动即退（rc11/rc12）。
    ///
    /// 因此改为**字节级定点补丁**：
    ///   在 `HitChance` 体内扫描 `F3 0F 10 0D disp32`（= `movss xmm1, [rip+disp]`），
    ///   找到 disp 指向 **0.01f** 的那一处（最后一处），把 disp32 改成指向我们自己分配的 float。
    ///   → 之后只改我们那块内存里的数值即可实时调整下限，不必再改代码。
    /// </summary>
    internal static class HitChanceGrid
    {
        private const float VanillaFloor = 0.01f;
        private static float _floor = 0.001f;
        private static IntPtr _constPtr = IntPtr.Zero;
        private static IntPtr _dispAddr = IntPtr.Zero;
        private static bool _installed;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, PAGE_READWRITE = 0x04, PAGE_EXECUTE_READWRITE = 0x40;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                _floor = HotfixParams.HitChanceGrid;
                if (_floor <= 0f || _floor >= VanillaFloor) { HotfixLog.Info("[SMS.2.HC] floor patch disabled (floor=" + _floor + ")"); return; }

                var mi = AccessTools.Method(typeof(Ship), "HitChance");
                if (mi == null) { HotfixLog.Warn("[SMS.2.HC] Ship.HitChance not found"); return; }
                IntPtr fn = mi.MethodHandle.GetFunctionPointer();
                if (fn == IntPtr.Zero) { HotfixLog.Warn("[SMS.2.HC] fn ptr null"); return; }

                // 1) 扫描 F3 0F 10 0D disp32（movss xmm1, [rip+disp]），取指向 0.01f 的最后一处
                IntPtr found = IntPtr.Zero; IntPtr target = IntPtr.Zero;
                for (int off = 0; off < 0x9000 - 8; off++)
                {
                    IntPtr p = fn + off;
                    byte b0 = Marshal.ReadByte(p);
                    if (b0 != 0xF3) continue;
                    if (Marshal.ReadByte(p + 1) != 0x0F) continue;
                    if (Marshal.ReadByte(p + 2) != 0x10) continue;
                    if (Marshal.ReadByte(p + 3) != 0x0D) continue;
                    int disp = Marshal.ReadInt32(p + 4);
                    IntPtr tgt = p + 8 + disp;
                    float v;
                    try { v = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(tgt)), 0); }
                    catch { continue; }
                    if (Math.Abs(v - VanillaFloor) < 1e-9f) { found = p; target = tgt; }
                }
                if (found == IntPtr.Zero) { HotfixLog.Warn("[SMS.2.HC] 0.01 floor site not found in HitChance"); return; }

                // 2) 在 ±2GB 内分配一块可写内存存我们的 float
                IntPtr mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (mem == IntPtr.Zero) { HotfixLog.Warn("[SMS.2.HC] VirtualAlloc failed"); return; }
                long delta = (long)mem - ((long)found + 8);
                if (delta < int.MinValue || delta > int.MaxValue)
                {
                    HotfixLog.Warn("[SMS.2.HC] allocated memory out of ±2GB range");
                    return;
                }
                Marshal.WriteInt32(mem, BitConverter.ToInt32(BitConverter.GetBytes(_floor), 0));

                // 3) 改 disp32 指向我们的内存
                uint old;
                if (!VirtualProtect(found, (UIntPtr)8, PAGE_EXECUTE_READWRITE, out old))
                { HotfixLog.Warn("[SMS.2.HC] VirtualProtect failed"); return; }
                Marshal.WriteInt32(found + 4, (int)delta);
                uint dummy;
                VirtualProtect(found, (UIntPtr)8, old, out dummy);

                _constPtr = mem; _dispAddr = found + 4; _installed = true;
                HotfixLog.Info("[SMS.2.HC] HitChanceFloor byte-patched: site=" + found.ToString("X")
                    + " oldConst=" + target.ToString("X") + " newConst=" + mem.ToString("X")
                    + " floor=" + _floor.ToString("F6") + " (vanilla 0.01)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HC] install failed: " + e); }
        }

        /// <summary>运行期调整下限（写入我们的小常量块，立即生效）。</summary>
        internal static void SetFloor(float v)
        {
            if (!_installed || v <= 0f || v >= VanillaFloor) return;
            _floor = v;
            try { Marshal.WriteInt32(_constPtr, BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); } catch { }
        }

        internal static bool Installed { get { return _installed; } }
    }
}
