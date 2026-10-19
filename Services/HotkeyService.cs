using System.Runtime.InteropServices;
using DailyMacro.Models;

namespace DailyMacro.Services;

internal sealed class HotkeyService : IDisposable
{
    public event Action<string, bool>? MacroTrigger;
    public event Action<string>? BindCaptured;
    public event Action<string>? Notice;
    public bool HooksReady => _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;

    public bool MacroTriggersEnabled { get; set; } = true;
    public bool CaptureNextBind { get; set; }
    public string? PreferredMacroId { get; set; }
    public IntPtr LastTargetWindow { get; private set; }

    private readonly Control _ui;
    private readonly NativeMethods.LowLevelProc _keyboardProc;
    private readonly NativeMethods.LowLevelProc _mouseProc;
    private readonly HashSet<uint> _heldKeys = [];
    private readonly HashSet<string> _heldMouse = [];
    private readonly List<MacroBind> _binds = [];
    private readonly object _bindLock = new();
    private readonly ManualResetEventSlim _hookReady = new(false);
    private Thread? _hookThread;
    private Control? _pump;
    private ApplicationContext? _hookContext;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private string? _heldMacroBind;
    private string? _heldTriggerKey;

    public HotkeyService(Control ui)
    {
        _ui = ui;
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
        StartHookThread();
    }

    public void Register()
    {
        StartHookThread();
        RunOnHookThread(EnsureHooks);
    }

    public void UpdateMacros(IEnumerable<MacroDefinition> macros)
    {
        lock (_bindLock)
        {
            _binds.Clear();
            foreach (var macro in macros)
            {
                if (!macro.IsOn)
                    continue;
                if (string.IsNullOrWhiteSpace(macro.TriggerKey) && string.IsNullOrWhiteSpace(macro.TriggerMouse))
                    continue;
                _binds.Add(new MacroBind(macro.Id, macro.TriggerKey, macro.TriggerMouse, macro.TargetProcess));
            }
        }
    }

    public void Dispose()
    {
        try
        {
            var pump = _pump;
            var context = _hookContext;
            if (pump is { IsHandleCreated: true } && !pump.IsDisposed)
            {
                pump.BeginInvoke(() =>
                {
                    UnhookAll();
                    context?.ExitThread();
                });
            }
            else
                context?.ExitThread();

            if (_hookThread is { IsAlive: true })
                _hookThread.Join(1500);
        }
        catch
        {
            // ignore shutdown races
        }

        UnhookAll();
        _hookReady.Dispose();
    }

    private void StartHookThread()
    {
        if (_hookThread is { IsAlive: true })
            return;

        _hookReady.Reset();
        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "XoMacro.Hooks"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        _hookReady.Wait(1500);
    }

    private void HookThreadMain()
    {
        try
        {
            _hookContext = new ApplicationContext();
            _pump = new Control();
            _ = _pump.Handle;
            EnsureHooks();
            _hookReady.Set();
            Application.Run(_hookContext);
        }
        catch
        {
            _hookReady.Set();
        }
        finally
        {
            UnhookAll();
            try { _pump?.Dispose(); } catch { /* ignore */ }
            _pump = null;
            _hookContext = null;
        }
    }

    private void RunOnHookThread(Action action)
    {
        var pump = _pump;
        if (pump is null || !pump.IsHandleCreated || pump.IsDisposed)
        {
            if (Thread.CurrentThread == _hookThread)
                action();
            return;
        }

        try
        {
            if (pump.InvokeRequired)
                pump.Invoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void UnhookAll()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private void EnsureHooks()
    {
        if (_keyboardHook == IntPtr.Zero)
            _keyboardHook = NativeMethods.HookLowLevel(NativeMethods.WhKeyboardLl, _keyboardProc);

        if (_mouseHook == IntPtr.Zero)
            _mouseHook = NativeMethods.HookLowLevel(NativeMethods.WhMouseLl, _mouseProc);
    }

    private MacroBind[] BindSnapshot()
    {
        lock (_bindLock)
            return _binds.Count == 0 ? [] : [.. _binds];
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = unchecked((int)(long)wParam);
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if ((info.Flags & (NativeMethods.LlkhfInjected | NativeMethods.LlkhfLowerIlInjected)) == 0)
            {
                if (msg is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp)
                {
                    _heldKeys.Remove(info.VkCode);
                    if (_heldMacroBind is string held && MatchesKey(_heldTriggerKey ?? "", info.VkCode))
                    {
                        _heldMacroBind = null;
                        _heldTriggerKey = null;
                        if (MacroTriggersEnabled)
                            DispatchMacro(held, false);
                    }
                }
                else if (msg is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
                {
                    var name = KeyNames.FromKeys((Keys)info.VkCode);
                    if (CaptureNextBind && _heldKeys.Add(info.VkCode))
                    {
                        CaptureNextBind = false;
                        InvokeUi(() => BindCaptured?.Invoke(name));
                        return 1;
                    }

                    var macroId = MatchMacroKey(info.VkCode);
                    if (macroId is not null && MacroTriggersEnabled && _heldKeys.Add(info.VkCode))
                    {
                        LastTargetWindow = NativeMethods.GetForegroundWindow();
                        _heldMacroBind = macroId;
                        _heldTriggerKey = name;
                        DispatchMacro(macroId, true);
                        return 1;
                    }

                    if (macroId is not null && MacroTriggersEnabled)
                        return 1;

                    _heldKeys.Add(info.VkCode);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = unchecked((int)(long)wParam);
            if (msg is not (
                NativeMethods.WmRButtonDown or NativeMethods.WmRButtonUp or
                NativeMethods.WmMButtonDown or NativeMethods.WmMButtonUp or
                NativeMethods.WmXButtonDown or NativeMethods.WmXButtonUp))
                return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if ((info.Flags & NativeMethods.LlmhfInjected) == 0)
            {
                var button = MouseName(msg, info.MouseData);
                var down = msg is NativeMethods.WmMButtonDown or NativeMethods.WmRButtonDown or NativeMethods.WmXButtonDown;
                var up = msg is NativeMethods.WmMButtonUp or NativeMethods.WmRButtonUp or NativeMethods.WmXButtonUp;

                if (button is not null && down && CaptureNextBind)
                {
                    CaptureNextBind = false;
                    InvokeUi(() => BindCaptured?.Invoke(button));
                    return 1;
                }

                if (!MacroTriggersEnabled)
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

                if (button is not null && !CaptureNextBind)
                {
                    if (up && _heldMouse.Remove(button) && _heldMacroBind is string heldMacro)
                    {
                        _heldMacroBind = null;
                        DispatchMacro(heldMacro, false);
                        return 1;
                    }

                    var macroId = MatchMacroMouse(button, info.Pt, out var blocked);
                    if (macroId is not null)
                    {
                        if (down && _heldMouse.Add(button))
                        {
                            LastTargetWindow = ProcessHelper.RootFromPoint(info.Pt);
                            _heldMacroBind = macroId;
                            DispatchMacro(macroId, true);
                        }

                        return 1;
                    }

                    if (down && blocked is not null)
                        InvokeUi(() => Notice?.Invoke("Trigger só dispara em " + blocked + ". Clique nessa janela e segure o botão."));
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static bool MatchesKey(string bind, uint vk)
    {
        if (string.IsNullOrWhiteSpace(bind) || KeyNames.IsMouseBind(bind))
            return false;
        var key = KeyNames.ToKeys(bind);
        return key != Keys.None && (uint)(key & Keys.KeyCode) == vk;
    }

    private string? MatchMacroKey(uint vk)
    {
        string? preferred = null;
        string? specific = null;
        string? fallback = null;
        foreach (var bind in BindSnapshot())
        {
            if (string.IsNullOrWhiteSpace(bind.Key))
                continue;
            if ((uint)(KeyNames.ToKeys(bind.Key) & Keys.KeyCode) != vk)
                continue;
            var processOk = string.IsNullOrWhiteSpace(bind.Process) || ProcessHelper.IsTargetForeground(bind.Process);
            if (!processOk)
                continue;
            if (IsPreferred(bind.Id))
                preferred = bind.Id;
            else if (!string.IsNullOrWhiteSpace(bind.Process))
                specific ??= bind.Id;
            else
                fallback ??= bind.Id;
        }

        return preferred ?? specific ?? fallback;
    }

    private string? MatchMacroMouse(string button, NativeMethods.POINT point, out string? blockedProcess)
    {
        blockedProcess = null;
        string? preferred = null;
        string? specific = null;
        string? fallback = null;
        foreach (var bind in BindSnapshot())
        {
            if (!string.Equals(bind.Mouse, button, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(bind.Process))
            {
                if (IsPreferred(bind.Id))
                    preferred = bind.Id;
                else
                    fallback ??= bind.Id;
                continue;
            }

            if (ProcessHelper.IsTarget(bind.Process, point))
            {
                if (IsPreferred(bind.Id))
                    preferred = bind.Id;
                else
                    specific ??= bind.Id;
            }
            else
                blockedProcess = bind.Process;
        }

        var id = preferred ?? specific ?? fallback;
        if (id is not null)
            blockedProcess = null;
        return id;
    }

    private bool IsPreferred(string id) =>
        !string.IsNullOrWhiteSpace(PreferredMacroId) &&
        string.Equals(PreferredMacroId, id, StringComparison.Ordinal);

    private static string? MouseName(int msg, uint mouseData)
    {
        if (msg is NativeMethods.WmMButtonDown or NativeMethods.WmMButtonUp)
            return "Middle";
        if (msg is NativeMethods.WmRButtonDown or NativeMethods.WmRButtonUp)
            return "Right";
        if (msg is NativeMethods.WmXButtonDown or NativeMethods.WmXButtonUp)
        {
            var x = (int)((mouseData >> 16) & 0xFFFF);
            return x == NativeMethods.XButton2 ? "X2" : "X1";
        }

        return null;
    }

    private void DispatchMacro(string id, bool down) =>
        InvokeUi(() => MacroTrigger?.Invoke(id, down));

    private void InvokeUi(Action action)
    {
        if (_ui.IsHandleCreated)
            _ui.BeginInvoke(action);
    }

    private sealed record MacroBind(string Id, string Key, string Mouse, string Process);
}
