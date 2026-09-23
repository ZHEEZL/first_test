using System;
using System.Runtime.InteropServices;

namespace first
{
    /// <summary>
    /// Обеспечивает временную аппаратную изоляцию рабочего потока на выделенном ядре CPU
    /// без блокировки UI-потока и композитора Avalonia.
    /// </summary>
    public static class ThreadAffinityScope
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

        public static IDisposable PinToDedicatedCore(int preferredCore = 1)
        {
            if (!OperatingSystem.IsWindows() || Environment.ProcessorCount < 2)
                return null;

            try
            {
                int targetCore = Math.Min(Environment.ProcessorCount - 1, Math.Max(0, preferredCore));
                IntPtr mask = (IntPtr)(1L << targetCore);
                IntPtr oldMask = SetThreadAffinityMask(GetCurrentThread(), mask);
                if (oldMask == IntPtr.Zero)
                    return null;

                return new RestoreScope(oldMask);
            }
            catch
            {
                return null;
            }
        }

        private sealed class RestoreScope : IDisposable
        {
            private readonly IntPtr _oldMask;
            public RestoreScope(IntPtr oldMask) => _oldMask = oldMask;

            public void Dispose()
            {
                if (OperatingSystem.IsWindows() && _oldMask != IntPtr.Zero)
                {
                    try { SetThreadAffinityMask(GetCurrentThread(), _oldMask); } catch { }
                }
            }
        }
    }
}
