using System.Runtime.InteropServices;
using System.Text;

namespace DailyMacro.Services;

internal static class NativeMethods
{
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmRButtonDown = 0x0204;
    public const int WmRButtonUp = 0x0205;
    public const int WmMButtonDown = 0x0207;
    public const int WmMButtonUp = 0x0208;
    public const int WmXButtonDown = 0x020B;
    public const int WmXButtonUp = 0x020C;
    public const int XButton1 = 1;
    public const int XButton2 = 2;
    public const int InputMouse = 0;
    public const int InputKeyboard = 1;
    public const uint KeyeventfExtendedkey = 0x0001;
    public const uint KeyeventfKeyup = 0x0002;
    public const uint KeyeventfUnicode = 0x0004;
    public const uint KeyeventfScancode = 0x0008;
    public const uint MouseeventfMove = 0x0001;
    public const uint MouseeventfLeftdown = 0x0002;
    public const uint MouseeventfLeftup = 0x0004;
    public const uint MouseeventfRightdown = 0x0008;
    public const uint MouseeventfRightup = 0x0010;
    public const uint MouseeventfMiddledown = 0x0020;
    public const uint MouseeventfMiddleup = 0x0040;
    public const uint MouseeventfXdown = 0x0080;
    public const uint MouseeventfXup = 0x0100;
    public const uint MouseeventfAbsolute = 0x8000;
    public const uint MouseeventfVirtualdesk = 0x4000;
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;
    public const uint LlkhfInjected = 0x10;
    public const uint LlkhfLowerIlInjected = 0x02;
    public const uint LlmhfInjected = 0x01;

    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    public static IntPtr HookLowLevel(int idHook, LowLevelProc proc)
    {
        var user32 = LoadLibrary("user32.dll");
        var hook = SetWindowsHookEx(idHook, proc, user32, 0);
        if (hook != IntPtr.Zero)
            return hook;
        return SetWindowsHookEx(idHook, proc, GetModuleHandle(null), 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public static readonly IntPtr HwndTopMost = new(-1);
    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoactivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static void MoveNoActivate(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero)
            return;
        SetWindowPos(hwnd, HwndTopMost, x, y, 0, 0, SwpNosize | SwpNoactivate);
    }

    public static void KeepTopMost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
    }

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const uint ProcessQueryLimitedInformation = 0x1000;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    public const uint GaRoot = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int WmSetRedraw = 0x000B;
    public const int LvmGetHeader = 0x101F;
    public const uint RdwInvalidate = 0x0001;
    public const uint RdwErase = 0x0004;
    public const uint RdwAllChildren = 0x0080;
    public const uint RdwFrame = 0x0400;
    public const uint RdwUpdateNow = 0x0100;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    public static void BeginRedrawFreeze(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        SendMessage(hwnd, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
    }

    public static void EndRedrawFreeze(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        SendMessage(hwnd, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
        RedrawWindow(
            hwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwFrame | RdwUpdateNow);
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    public static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    public static extern int AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    public static void ApplyControlTheme(IntPtr hwnd, bool dark, string? themeName = null)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try { SetPreferredAppMode(dark ? 2 : 0); } catch { /* older Windows */ }
        try { AllowDarkModeForWindow(hwnd, dark); } catch { /* older Windows */ }
        SetWindowTheme(hwnd, themeName ?? (dark ? "DarkMode_Explorer" : "Explorer"), null);
        var useDark = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public MOUSEINPUT Mi;
        [FieldOffset(8)] public KEYBDINPUT Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
