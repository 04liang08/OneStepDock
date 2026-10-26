using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace OneStepDock
{
    internal static class Elevation
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private const int TokenElevationClass = 20;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<uint, bool> Cache = new Dictionary<uint, bool>();
        private static DateTime _cacheStamp = DateTime.MinValue;

        public static bool Current { get; } = IsProcessElevated((uint)Process.GetCurrentProcess().Id, true);

        public static bool IsProcessElevated(uint processId)
        {
            return IsProcessElevated(processId, false);
        }

        private static bool IsProcessElevated(uint processId, bool bypassCache)
        {
            lock (CacheLock)
            {
                if (!bypassCache)
                {
                    if ((DateTime.UtcNow - _cacheStamp).TotalSeconds > 60)
                    {
                        Cache.Clear();
                        _cacheStamp = DateTime.UtcNow;
                    }

                    bool cached;
                    if (Cache.TryGetValue(processId, out cached))
                    {
                        return cached;
                    }
                }
            }

            bool elevated = QueryElevated(processId);

            lock (CacheLock)
            {
                Cache[processId] = elevated;
            }

            return elevated;
        }

        private static bool QueryElevated(uint processId)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                return true;
            }

            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, TokenQuery, out token))
                {
                    return true;
                }

                int size;
                GetTokenInformation(token, TokenElevationClass, IntPtr.Zero, 0, out size);
                if (size <= 0)
                {
                    return true;
                }

                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenElevationClass, buffer, size, out size))
                    {
                        return true;
                    }
                    return Marshal.ReadInt32(buffer) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
                CloseHandle(process);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr token,
            int informationClass,
            IntPtr information,
            int informationLength,
            out int returnLength);
    }
}
