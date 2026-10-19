using DailyMacro.Models;
using System.Diagnostics;

namespace DailyMacro.Services;

internal static class InputSender
{
    public const int MouseHoldMs = 80;
    private static readonly int InputBytes = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
    private static readonly object Gate = new();
    private static readonly HashSet<ushort> DownKeys = [];
    private static readonly HashSet<string> DownMice = new(StringComparer.OrdinalIgnoreCase);

    public static void ReleaseAll(string? keepMouse = null)
    {
        lock (Gate)
        {
            foreach (var vk in DownKeys.ToArray())
                KeyEventCore(vk, false);
            DownKeys.Clear();

            foreach (var button in DownMice.ToArray())
            {
                if (string.Equals(button, keepMouse, StringComparison.OrdinalIgnoreCase))
                    continue;
                MouseEventCore(button, false, false, 0, 0);
                DownMice.Remove(button);
            }

            ReleaseModifiersCore();
        }
    }

    public static void ReleaseModifiers()
    {
        lock (Gate)
            ReleaseModifiersCore();
    }

    private static void ReleaseModifiersCore(IReadOnlyCollection<string>? keepNames = null)
    {
        var keep = ModifierVks(keepNames);
        foreach (var vk in new ushort[] { 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5, 0x5B, 0x5C })
        {
            if (keep.Contains(vk))
                continue;
            KeyEventCore(vk, false);
        }
    }

    private static HashSet<ushort> ModifierVks(IReadOnlyCollection<string>? names)
    {
        var keep = new HashSet<ushort>();
        if (names is null)
            return keep;

        foreach (var name in names)
        {
            if (!KeyNames.IsModifierName(name))
                continue;
            switch (name)
            {
                case "Shift":
                    keep.Add(0x10);
                    keep.Add(0xA0);
                    keep.Add(0xA1);
                    break;
                case "Ctrl":
                    keep.Add(0x11);
                    keep.Add(0xA2);
                    keep.Add(0xA3);
                    break;
                case "Alt":
                    keep.Add(0x12);
                    keep.Add(0xA4);
                    keep.Add(0xA5);
                    break;
                case "Win":
                    keep.Add(0x5B);
                    keep.Add(0x5C);
                    break;
            }
        }

        return keep;
    }

    public static void SendEvent(MacroEvent ev)
    {
        switch (ev.Type)
        {
            case "key_press":
                KeyEventByName(ev.Key, true);
                break;
            case "key_release":
                KeyEventByName(ev.Key, false);
                break;
            case "mouse_down":
                MouseEvent(string.IsNullOrWhiteSpace(ev.Button) ? "Left" : ev.Button, true, false, 0, 0);
                break;
            case "mouse_up":
                MouseEvent(string.IsNullOrWhiteSpace(ev.Button) ? "Left" : ev.Button, false, false, 0, 0);
                break;
        }
    }

    public static void SendStep(MacroStep step, CancellationToken cancellationToken = default, MacroStep? next = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (step.Type)
        {
            case StepType.Text:
                TypeText(step.Text, cancellationToken);
                break;
            case StepType.Click:
                SendMouse(step.MouseButton, step.HasPosition, step.X, step.Y, HoldFor(step, hasMouse: true), cancellationToken);
                break;
            case StepType.Combo:
                SendCombo(step, cancellationToken, next);
                break;
        }
    }

    public static void SendCombo(MacroStep step, CancellationToken cancellationToken = default, MacroStep? next = null)
    {
        var names = step.Keys
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var modifiers = names.Where(KeyNames.IsModifierName).ToList();
        var mains = names.Where(name => !KeyNames.IsModifierName(name)).ToList();
        var mice = step.AllMouseButtons();

        if (step.TapIntervalMs > 0)
        {
            TapRepeated(step, modifiers, mains, mice, cancellationToken);
            return;
        }

        var heldKeys = modifiers.Concat(mains).ToList();
        SendChord(heldKeys, mice, down: true);
        try
        {
            HoldPressed(HoldFor(step, mice.Count > 0), mice, heldKeys, cancellationToken);
        }
        finally
        {
            var (keepKeys, keepMice) = KeysToKeep(step, next);
            var releaseKeys = heldKeys
                .Where(key => !keepKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var releaseMice = mice
                .Where(button => !keepMice.Contains(button, StringComparer.OrdinalIgnoreCase))
                .ToList();
            SendChord(releaseKeys, releaseMice, down: false);
            lock (Gate)
                ReleaseModifiersCore(keepKeys);
            if (releaseKeys.Count > 0 || releaseMice.Count > 0)
                Wait(10, cancellationToken);
        }
    }

    private static (List<string> keys, List<string> mice) KeysToKeep(MacroStep current, MacroStep? next)
    {
        if (next is null || current.DelayAfterMs > 40 || next.TapIntervalMs > 0)
            return ([], []);
        if (next.Type is not (StepType.Combo or StepType.Click))
            return ([], []);

        return (
            next.Keys.Where(name => !string.IsNullOrWhiteSpace(name)).ToList(),
            []);
    }

    private static void TapRepeated(
        MacroStep step,
        List<string> modifiers,
        List<string> mains,
        List<string> mice,
        CancellationToken cancellationToken)
    {
        var duration = Math.Max(1, step.HoldMs);
        var cycle = Math.Clamp(step.TapIntervalMs, 10, 500);
        var press = Math.Min(8, Math.Max(4, cycle / 2));
        var gap = Math.Max(1, cycle - press);

        var heldKeys = modifiers.Concat(mains).ToList();
        SendChord(modifiers, [], down: true);

        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.ElapsedMilliseconds < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SendChord(mains, mice, down: true);
                Wait(press, cancellationToken);
                SendChord(mains, mice, down: false);

                var remain = duration - (int)clock.ElapsedMilliseconds;
                if (remain <= 0)
                    break;
                Wait(Math.Min(gap, remain), cancellationToken);
            }
        }
        finally
        {
            SendChord(heldKeys, mice, down: false);
            ReleaseModifiers();
            Wait(10, cancellationToken);
        }
    }

    public static void TypeText(string text, CancellationToken cancellationToken = default)
    {
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ch == '\r')
                continue;

            if (ch == '\n')
            {
                KeyEvent(Keys.Enter, true);
                KeyEvent(Keys.Enter, false);
                continue;
            }

            Unicode(ch, down: true);
            Unicode(ch, down: false);
        }
    }

    public static void SendMouseButtons(IReadOnlyList<string> buttons, bool down)
    {
        if (buttons.Count == 0)
            return;

        lock (Gate)
        {
            var inputs = new List<NativeMethods.INPUT>();
            AppendMouseChord(inputs, buttons, down);
            SendMany(inputs);
        }
    }

    private static void SendChord(IReadOnlyList<string> keys, IReadOnlyList<string> mice, bool down, bool force = false)
    {
        lock (Gate)
        {
            var inputs = new List<NativeMethods.INPUT>();
            if (down)
            {
                foreach (var key in keys)
                    AppendKey(inputs, key, down: true, force);
                AppendMouseChord(inputs, mice, down: true);
            }
            else
            {
                AppendMouseChord(inputs, mice, down: false);
                for (var i = keys.Count - 1; i >= 0; i--)
                    AppendKey(inputs, keys[i], down: false);
            }

            SendMany(inputs);
        }
    }

    private static void AppendMouseChord(List<NativeMethods.INPUT> dest, IReadOnlyList<string> buttons, bool down)
    {
        uint flags = 0;
        foreach (var raw in buttons)
        {
            var button = string.IsNullOrWhiteSpace(raw) ? "Left" : raw;
            if (down)
            {
                if (DownMice.Contains(button))
                {
                    dest.Add(MouseInput(button, down: false));
                    DownMice.Remove(button);
                }

                DownMice.Add(button);
            }
            else
            {
                DownMice.Remove(button);
            }

            if (button is "X1" or "X2")
            {
                dest.Add(MouseInput(button, down));
                continue;
            }

            flags |= MouseFlags(button, down).flags;
        }

        if (flags == 0)
            return;

        dest.Add(new NativeMethods.INPUT
        {
            Type = NativeMethods.InputMouse,
            Mi = new NativeMethods.MOUSEINPUT
            {
                DwFlags = flags
            }
        });
    }

    private static int HoldFor(MacroStep step, bool hasMouse = false)
    {
        if (hasMouse)
            return Math.Max(MouseHoldMs, step.HoldMs);
        return Math.Max(1, step.HoldMs);
    }

    private static void SendMouse(string button, bool hasPosition, int x, int y, int holdMs, CancellationToken cancellationToken)
    {
        MouseEvent(string.IsNullOrWhiteSpace(button) ? "Left" : button, true, hasPosition, x, y);
        HoldPressed(Math.Max(1, holdMs), [string.IsNullOrWhiteSpace(button) ? "Left" : button], [], cancellationToken);
        MouseEvent(string.IsNullOrWhiteSpace(button) ? "Left" : button, false, false, 0, 0);
    }

    private static void Wait(int ms, CancellationToken cancellationToken)
    {
        if (ms <= 0)
            return;
        if (cancellationToken.WaitHandle.WaitOne(ms))
            cancellationToken.ThrowIfCancellationRequested();
    }

    private static void HoldPressed(
        int ms,
        IReadOnlyList<string> mice,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var remain = Math.Max(0, ms);
        while (remain > 0)
        {
            var slice = Math.Min(30, remain);
            if (cancellationToken.WaitHandle.WaitOne(slice))
                cancellationToken.ThrowIfCancellationRequested();
            remain -= slice;
            if (remain <= 0)
                break;
            if (mice.Count > 0)
                KeepMouseDown(mice);
            if (keys.Count > 0)
                KeepKeysDown(keys);
        }
    }

    private static void KeepKeysDown(IReadOnlyList<string> keys)
    {
        var digits = keys
            .Where(name => KeyNames.TryDigitVirtualKey(name, out _, out _))
            .ToList();
        if (digits.Count == 0)
            return;
        SendChord(digits, [], down: true, force: true);
    }

    private static void KeepMouseDown(IReadOnlyList<string> _)
    {
        lock (Gate)
        {
            Send(new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Mi = new NativeMethods.MOUSEINPUT
                {
                    DwFlags = NativeMethods.MouseeventfMove
                }
            });
        }
    }

    private static void KeyEventByName(string name, bool down)
    {
        lock (Gate)
        {
            var inputs = new List<NativeMethods.INPUT>();
            AppendKey(inputs, name, down);
            SendMany(inputs);
        }
    }

    private static void AppendKey(List<NativeMethods.INPUT> dest, string name, bool down, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (KeyNames.TryDigitVirtualKey(name, out var digitVk, out var digitScan))
        {
            AppendKeyVk(dest, digitVk, digitScan, down, sendScan: true, force);
            return;
        }

        var key = KeyNames.ToKeys(name);
        var vk = (ushort)(key & Keys.KeyCode);
        if (vk == 0)
            return;

        if (vk is 0x10 or 0x11 or 0x12)
        {
            vk = vk switch
            {
                0x10 => 0xA0,
                0x11 => 0xA2,
                _ => 0xA4
            };
        }

        var scan = (ushort)NativeMethods.MapVirtualKey(vk, 0);
        AppendKeyVk(dest, vk, scan, down, sendScan: false, force);
    }

    private static void AppendKeyVk(
        List<NativeMethods.INPUT> dest,
        ushort vk,
        ushort scan,
        bool down,
        bool sendScan,
        bool force = false)
    {
        if (vk == 0 && scan == 0)
            return;

        if (vk != 0 && !force)
        {
            if (down && DownKeys.Contains(vk))
                return;
            if (!down && !DownKeys.Contains(vk))
                return;
        }

        if (vk != 0)
        {
            if (down)
                DownKeys.Add(vk);
            else
                DownKeys.Remove(vk);
        }

        uint flags = down ? 0 : NativeMethods.KeyeventfKeyup;
        if (vk != 0 && IsExtended(vk))
            flags |= NativeMethods.KeyeventfExtendedkey;

        if (vk != 0)
            dest.Add(KeyboardInput(vk, scan, flags));
        if (sendScan && scan != 0)
            dest.Add(KeyboardInput(0, scan, flags | NativeMethods.KeyeventfScancode));
    }

    private static void KeyEvent(Keys key, bool down)
    {
        var vk = (ushort)(key & Keys.KeyCode);
        if (vk == 0)
            return;

        if (vk is 0x10 or 0x11 or 0x12)
        {
            vk = vk switch
            {
                0x10 => 0xA0,
                0x11 => 0xA2,
                _ => 0xA4
            };
        }

        var scan = (ushort)NativeMethods.MapVirtualKey(vk, 0);
        KeyEventVk(vk, scan, down, sendScan: false);
    }

    private static void KeyEventVk(ushort vk, ushort scan, bool down, bool sendScan)
    {
        lock (Gate)
            KeyEventVkCore(vk, scan, down, sendScan);
    }

    private static void KeyEventVkCore(ushort vk, ushort scan, bool down, bool sendScan)
    {
        if (vk == 0 && scan == 0)
            return;

        if (vk != 0)
        {
            if (down)
                DownKeys.Add(vk);
            else
                DownKeys.Remove(vk);
        }

        uint flags = down ? 0 : NativeMethods.KeyeventfKeyup;
        if (vk != 0 && IsExtended(vk))
            flags |= NativeMethods.KeyeventfExtendedkey;

        if (vk != 0)
            Send(KeyboardInput(vk, scan, flags));
        if (sendScan && scan != 0)
            Send(KeyboardInput(0, scan, flags | NativeMethods.KeyeventfScancode));
    }

    private static NativeMethods.INPUT KeyboardInput(ushort vk, ushort scan, uint flags) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Ki = new NativeMethods.KEYBDINPUT
        {
            WVk = vk,
            WScan = scan,
            DwFlags = flags
        }
    };

    private static void KeyEventCore(ushort vk, bool down)
    {
        var scan = (ushort)NativeMethods.MapVirtualKey(vk, 0);
        KeyEventVkCore(vk, scan, down, sendScan: false);
    }

    private static bool IsExtended(ushort vk) => vk is
        0xA3 or 0xA5 or 0x5B or 0x5C or
        0x21 or 0x22 or 0x23 or 0x24 or
        0x25 or 0x26 or 0x27 or 0x28 or
        0x2D or 0x2E or 0x5D;

    private static void Unicode(char ch, bool down)
    {
        Send(new NativeMethods.INPUT
        {
            Type = NativeMethods.InputKeyboard,
            Ki = new NativeMethods.KEYBDINPUT
            {
                WVk = 0,
                WScan = ch,
                DwFlags = NativeMethods.KeyeventfUnicode | (down ? 0 : NativeMethods.KeyeventfKeyup)
            }
        });
    }

    private static void MouseEvent(string button, bool down, bool moveTo, int x, int y)
    {
        lock (Gate)
            MouseEventCore(button, down, moveTo, x, y);
    }

    private static void MouseEventCore(string button, bool down, bool moveTo, int x, int y)
    {
        if (string.IsNullOrWhiteSpace(button))
            button = "Left";

        if (down)
            DownMice.Add(button);
        else
            DownMice.Remove(button);
        uint flags;
        uint data = 0;
        switch (button)
        {
            case "Right":
                flags = down ? NativeMethods.MouseeventfRightdown : NativeMethods.MouseeventfRightup;
                break;
            case "Middle":
                flags = down ? NativeMethods.MouseeventfMiddledown : NativeMethods.MouseeventfMiddleup;
                break;
            case "X1":
                flags = down ? NativeMethods.MouseeventfXdown : NativeMethods.MouseeventfXup;
                data = 1;
                break;
            case "X2":
                flags = down ? NativeMethods.MouseeventfXdown : NativeMethods.MouseeventfXup;
                data = 2;
                break;
            default:
                flags = down ? NativeMethods.MouseeventfLeftdown : NativeMethods.MouseeventfLeftup;
                break;
        }

        var dx = 0;
        var dy = 0;
        if (moveTo)
        {
            var vx = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
            var vy = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
            var vw = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen));
            var vh = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
            dx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
            dy = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
            NativeMethods.SetCursorPos(x, y);
            Send(new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Mi = new NativeMethods.MOUSEINPUT
                {
                    Dx = dx,
                    Dy = dy,
                    DwFlags = NativeMethods.MouseeventfMove | NativeMethods.MouseeventfAbsolute | NativeMethods.MouseeventfVirtualdesk
                }
            });
        }

        Send(MouseInput(button, down, dx, dy, data, flags));
    }

    private static NativeMethods.INPUT MouseInput(string button, bool down, int dx = 0, int dy = 0, uint data = 0, uint flags = 0)
    {
        if (flags == 0)
            (flags, data) = MouseFlags(button, down);

        return new NativeMethods.INPUT
        {
            Type = NativeMethods.InputMouse,
            Mi = new NativeMethods.MOUSEINPUT
            {
                Dx = dx,
                Dy = dy,
                MouseData = data,
                DwFlags = flags
            }
        };
    }

    private static (uint flags, uint data) MouseFlags(string button, bool down)
    {
        uint data = 0;
        uint flags = button switch
        {
            "Right" => down ? NativeMethods.MouseeventfRightdown : NativeMethods.MouseeventfRightup,
            "Middle" => down ? NativeMethods.MouseeventfMiddledown : NativeMethods.MouseeventfMiddleup,
            "X1" => down ? NativeMethods.MouseeventfXdown : NativeMethods.MouseeventfXup,
            "X2" => down ? NativeMethods.MouseeventfXdown : NativeMethods.MouseeventfXup,
            _ => down ? NativeMethods.MouseeventfLeftdown : NativeMethods.MouseeventfLeftup
        };
        if (button is "X1")
            data = 1;
        else if (button is "X2")
            data = 2;
        return (flags, data);
    }

    private static void Send(NativeMethods.INPUT input)
    {
        NativeMethods.SendInput(1, [input], InputBytes);
    }

    private static void SendMany(List<NativeMethods.INPUT> inputs)
    {
        if (inputs.Count == 0)
            return;
        NativeMethods.SendInput((uint)inputs.Count, [.. inputs], InputBytes);
    }
}
