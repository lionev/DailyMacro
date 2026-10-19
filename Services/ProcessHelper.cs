using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DailyMacro.Services;

internal sealed class ProcessChoice
{
    public string Exe { get; }
    public string Title { get; }

    public ProcessChoice(string exe, string title)
    {
        Exe = exe;
        Title = title;
    }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(Exe))
            return "Qualquer programa";
        return string.IsNullOrEmpty(Title) ? Exe : Exe + "  —  " + Title;
    }
}

internal static class ProcessHelper
{
    private static readonly NativeMethods.EnumWindowsProc EnumCallback = CollectWindow;
    private static readonly ConcurrentDictionary<uint, ExeCacheEntry> ExeByPid = new();
    private static readonly object AppsLock = new();
    private static List<ProcessChoice>? _appsCache;
    private static long _appsCacheAt;
    private const int ExeCacheMs = 2500;
    private const int AppsCacheMs = 8000;

    private static Dictionary<string, ProcessChoice>? _collecting;

    private readonly record struct ExeCacheEntry(string Exe, long Ticks);

    public static string? ForegroundExe() => ExeFromWindow(NativeMethods.GetForegroundWindow());

    public static string? ExeFromPoint(NativeMethods.POINT point)
    {
        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return null;
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        return ExeFromWindow(root != IntPtr.Zero ? root : hwnd);
    }

    public static bool IsTarget(string? targetExe, NativeMethods.POINT point)
    {
        if (string.IsNullOrWhiteSpace(targetExe))
            return true;

        var hwnd = NativeMethods.WindowFromPoint(point);
        var root = hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        var foreground = NativeMethods.GetForegroundWindow();
        if (PidMatches(targetExe, hwnd) || PidMatches(targetExe, root) || PidMatches(targetExe, foreground))
            return true;

        var identified = ExeFromWindow(root != IntPtr.Zero ? root : hwnd) ?? ForegroundExe();
        if (string.IsNullOrWhiteSpace(identified) ||
            identified.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ||
            IsSelfExe(identified))
            return true;

        return Matches(targetExe, identified);
    }

    public static IntPtr RootFromPoint(NativeMethods.POINT point)
    {
        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return NativeMethods.GetForegroundWindow();
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        var target = root != IntPtr.Zero ? root : hwnd;
        var exe = ExeFromWindow(target);
        if (exe is not null && IsSelfExe(exe))
            return NativeMethods.GetForegroundWindow();
        return target;
    }

    public static IntPtr ResolvePlayTarget(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && !IsSelfWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
            return hwnd;

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && !IsSelfWindow(foreground))
            return foreground;
        return IntPtr.Zero;
    }

    private static bool IsSelfWindow(IntPtr hwnd)
    {
        var exe = ExeFromWindow(hwnd);
        return IsSelfExe(exe);
    }

    private static bool IsSelfExe(string? exe) =>
        exe is not null && (
            exe.Equals("XoMacro.exe", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("DailyMacro.exe", StringComparison.OrdinalIgnoreCase));

    public static void FocusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd) || IsSelfWindow(hwnd))
            return;

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
            return;

        var current = NativeMethods.GetCurrentThreadId();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (foregroundThread != 0)
            NativeMethods.AttachThreadInput(current, foregroundThread, true);
        if (targetThread != 0 && targetThread != foregroundThread)
            NativeMethods.AttachThreadInput(current, targetThread, true);
        NativeMethods.SetForegroundWindow(hwnd);
        if (targetThread != 0 && targetThread != foregroundThread)
            NativeMethods.AttachThreadInput(current, targetThread, false);
        if (foregroundThread != 0)
            NativeMethods.AttachThreadInput(current, foregroundThread, false);
    }

    public static bool IsTargetForeground(string? targetExe) =>
        string.IsNullOrWhiteSpace(targetExe)
        || PidMatches(targetExe, NativeMethods.GetForegroundWindow());

    public static bool PidMatches(string targetExe, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(targetExe))
            return false;

        return Matches(targetExe, ExeFromWindow(hwnd));
    }

    public static bool Matches(string? targetExe, string? foregroundExe)
    {
        if (string.IsNullOrWhiteSpace(targetExe))
            return true;
        if (string.IsNullOrWhiteSpace(foregroundExe))
            return false;
        return string.Equals(
            Path.GetFileName(targetExe),
            Path.GetFileName(foregroundExe),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string? ExeFromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        var now = Environment.TickCount64;
        if (ExeByPid.TryGetValue(pid, out var cached) && now - cached.Ticks < ExeCacheMs)
            return cached.Exe;

        var exe = QueryExe(pid);
        if (exe is not null)
        {
            ExeByPid[pid] = new ExeCacheEntry(exe, now);
            if (ExeByPid.Count > 256)
                PruneExeCache(now);
        }

        return exe;
    }

    private static string? QueryExe(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                    return Path.GetFileName(buffer.ToString());
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }
        catch
        {
            return null;
        }
    }

    private static void PruneExeCache(long now)
    {
        foreach (var pair in ExeByPid)
        {
            if (now - pair.Value.Ticks >= ExeCacheMs)
                ExeByPid.TryRemove(pair.Key, out _);
        }
    }

    public static string TitleFromWindow(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    public static void InvalidateAppsCache()
    {
        lock (AppsLock)
        {
            _appsCache = null;
            _appsCacheAt = 0;
        }
    }

    public static List<ProcessChoice> ListApps()
    {
        lock (AppsLock)
        {
            if (_appsCache is not null && Environment.TickCount64 - _appsCacheAt < AppsCacheMs)
                return _appsCache;

            _collecting = new Dictionary<string, ProcessChoice>(StringComparer.OrdinalIgnoreCase);
            NativeMethods.EnumWindows(EnumCallback, IntPtr.Zero);
            var list = _collecting.Values
                .OrderBy(item => item.Exe, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _collecting = null;
            _appsCache = list;
            _appsCacheAt = Environment.TickCount64;
            return list;
        }
    }

    private static bool CollectWindow(IntPtr hwnd, IntPtr lParam)
    {
        if (_collecting is null || !NativeMethods.IsWindowVisible(hwnd))
            return true;

        var title = TitleFromWindow(hwnd);
        if (string.IsNullOrWhiteSpace(title))
            return true;

        var exe = ExeFromWindow(hwnd);
        if (string.IsNullOrWhiteSpace(exe) || IsSelfExe(exe))
            return true;

        _collecting.TryAdd(exe, new ProcessChoice(exe, title));
        return true;
    }
}
