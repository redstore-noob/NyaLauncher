using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia.Pages;

internal sealed record SystemMemorySnapshot(
    int TotalMemoryMb,
    int AvailableMemoryMb);

internal sealed record GameMemoryDecision(
    bool IsAutomatic,
    int MaximumMemoryMb,
    int TotalMemoryMb,
    int AvailableMemoryMb,
    int ReservedMemoryMb);

/// <summary>
/// Persists the global memory policy and resolves the effective JVM -Xmx at
/// launch time. Automatic decisions always use a fresh physical-memory sample.
/// </summary>
internal static class GameMemorySettings
{
    private const string MaximumMemoryKey = "globalMaximumMemoryMb";
    private const string AutomaticAdjustmentKey = "automaticMemoryAdjustment";
    private const int MinimumSelectableMemoryMb = 512;
    private const int MemoryStepMb = 256;
    private const int DefaultMaximumMemoryMb = 4096;

    public static bool IsAutomaticAdjustmentEnabled
    {
        get => bool.TryParse(
                   LauncherConfig.GetValue(AutomaticAdjustmentKey),
                   out var enabled) &&
               enabled;
        set => LauncherConfig.SetValue(
            AutomaticAdjustmentKey,
            value.ToString(CultureInfo.InvariantCulture));
    }

    public static int GetSliderMaximumMemoryMb() =>
        Math.Max(
            MinimumSelectableMemoryMb,
            RoundDown(GetSystemMemory().TotalMemoryMb, MemoryStepMb));

    public static int GetManualMaximumMemoryMb()
    {
        var sliderMaximum = GetSliderMaximumMemoryMb();
        var configured = int.TryParse(
            LauncherConfig.GetValue(MaximumMemoryKey),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : Math.Min(DefaultMaximumMemoryMb, sliderMaximum);
        return ClampAndRound(configured, sliderMaximum);
    }

    public static void SaveManualMaximumMemoryMb(int memoryMb)
    {
        var normalized = ClampAndRound(memoryMb, GetSliderMaximumMemoryMb());
        LauncherConfig.SetValue(
            MaximumMemoryKey,
            normalized.ToString(CultureInfo.InvariantCulture));
    }

    public static GameMemoryDecision ResolveForLaunch(int? instanceMaximumMemoryMb = null)
    {
        var memory = GetSystemMemory();
        var systemMaximum = Math.Max(
            MinimumSelectableMemoryMb,
            RoundDown(memory.TotalMemoryMb, MemoryStepMb));
        if (!IsAutomaticAdjustmentEnabled)
        {
            var policyMaximum = ClampAndRound(GetManualMaximumMemoryMb(), systemMaximum);
            return new GameMemoryDecision(
                false,
                ApplyInstanceLimit(policyMaximum, instanceMaximumMemoryMb, systemMaximum),
                memory.TotalMemoryMb,
                memory.AvailableMemoryMb,
                0);
        }

        // Keep enough memory for the OS and launcher while preventing a game
        // from taking more than 75% of physical RAM, even on an idle system.
        var reserve = Math.Max(2048, RoundUp(memory.TotalMemoryMb * 15 / 100, MemoryStepMb));
        var availableForGame = Math.Max(
            MinimumSelectableMemoryMb,
            memory.AvailableMemoryMb - reserve);
        var totalMemoryCap = Math.Max(
            MinimumSelectableMemoryMb,
            RoundDown(memory.TotalMemoryMb * 75 / 100, MemoryStepMb));
        var automaticMaximum = ClampAndRound(
            Math.Min(availableForGame, totalMemoryCap),
            systemMaximum);

        return new GameMemoryDecision(
            true,
            ApplyInstanceLimit(automaticMaximum, instanceMaximumMemoryMb, systemMaximum),
            memory.TotalMemoryMb,
            memory.AvailableMemoryMb,
            reserve);
    }

    public static SystemMemorySnapshot GetSystemMemory()
    {
        if (OperatingSystem.IsWindows() && TryReadWindowsMemory(out var windows))
            return windows;
        if (OperatingSystem.IsLinux() && TryReadLinuxMemory(out var linux))
            return linux;
        return ReadRuntimeMemoryFallback();
    }

    private static int ClampAndRound(int value, int maximum)
    {
        var clamped = Math.Clamp(value, MinimumSelectableMemoryMb, maximum);
        return Math.Max(MinimumSelectableMemoryMb, RoundDown(clamped, MemoryStepMb));
    }

    private static int ApplyInstanceLimit(
        int policyMaximum,
        int? instanceMaximumMemoryMb,
        int systemMaximum) =>
        instanceMaximumMemoryMb is { } requested
            ? Math.Min(policyMaximum, ClampAndRound(requested, systemMaximum))
            : policyMaximum;

    private static int RoundDown(int value, int step) => value / step * step;

    private static int RoundUp(int value, int step) => (value + step - 1) / step * step;

    private static bool TryReadWindowsMemory(out SystemMemorySnapshot snapshot)
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (GlobalMemoryStatusEx(ref status))
        {
            snapshot = new SystemMemorySnapshot(
                BytesToMb(status.TotalPhysical),
                BytesToMb(status.AvailablePhysical));
            return snapshot.TotalMemoryMb > 0;
        }

        snapshot = null!;
        return false;
    }

    private static bool TryReadLinuxMemory(out SystemMemorySnapshot snapshot)
    {
        try
        {
            long totalKb = 0;
            long availableKb = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    totalKb = ReadLinuxKilobytes(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    availableKb = ReadLinuxKilobytes(line);
            }

            if (totalKb > 0)
            {
                snapshot = new SystemMemorySnapshot(
                    ClampMegabytes(totalKb / 1024),
                    ClampMegabytes(availableKb > 0 ? availableKb / 1024 : totalKb / 2 / 1024));
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        snapshot = null!;
        return false;
    }

    private static long ReadLinuxKilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(
            parts[1],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static SystemMemorySnapshot ReadRuntimeMemoryFallback()
    {
        var info = GC.GetGCMemoryInfo();
        var totalBytes = Math.Max(
            info.TotalAvailableMemoryBytes,
            1L * MinimumSelectableMemoryMb * 1024 * 1024);
        var availableBytes = info.MemoryLoadBytes > 0
            ? Math.Max(totalBytes - info.MemoryLoadBytes, totalBytes / 8)
            : totalBytes / 2;
        return new SystemMemorySnapshot(
            BytesToMb((ulong)totalBytes),
            BytesToMb((ulong)availableBytes));
    }

    private static int BytesToMb(ulong bytes) =>
        ClampMegabytes((long)(bytes / 1024 / 1024));

    private static int ClampMegabytes(long megabytes) =>
        (int)Math.Clamp(megabytes, MinimumSelectableMemoryMb, int.MaxValue);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
