namespace DailyMacro.Models;

internal static class KeyNames
{
    public static bool IsModifierName(string? name) =>
        name is "Shift" or "Ctrl" or "Alt" or "Win";

    public static string FromKeys(Keys key)
    {
        return key switch
        {
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "Ctrl",
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "Shift",
            Keys.Menu or Keys.LMenu or Keys.RMenu => "Alt",
            Keys.LWin or Keys.RWin => "Win",
            Keys.Enter => "Enter",
            Keys.Space => "Espaço",
            Keys.Escape => "Esc",
            Keys.Back => "Backspace",
            Keys.Tab => "Tab",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Left => "Esquerda",
            Keys.Right => "Direita",
            Keys.Up => "Cima",
            Keys.Down => "Baixo",
            Keys.Capital => "CapsLock",
            Keys.PrintScreen => "PrintScreen",
            Keys.Pause => "Pause",
            Keys.NumLock => "NumLock",
            Keys.Scroll => "ScrollLock",
            Keys.Apps => "Menu",
            Keys.OemMinus => "-",
            Keys.Oemplus => "+",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Oem1 => ";",
            Keys.Oem7 => "'",
            Keys.OemOpenBrackets => "[",
            Keys.Oem6 => "]",
            Keys.Oem102 => "ABNT",
            Keys.Oemtilde => "`",
            Keys.Decimal => ".",
            Keys.Add => "Num+",
            Keys.Subtract => "Num-",
            Keys.Multiply => "Num*",
            Keys.Divide => "Num/",
            Keys.NumPad0 => "0",
            Keys.NumPad1 => "1",
            Keys.NumPad2 => "2",
            Keys.NumPad3 => "3",
            Keys.NumPad4 => "4",
            Keys.NumPad5 => "5",
            Keys.NumPad6 => "6",
            Keys.NumPad7 => "7",
            Keys.NumPad8 => "8",
            Keys.NumPad9 => "9",
            _ when IsDigitRow(key) => DigitName(key),
            _ => key.ToString()
        };
    }

    public static Keys ToKeys(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Keys.None;

        name = name.Trim();

        if (TryDigitKey(name, out var digit))
            return digit;

        if (name.Length == 1 && char.IsLetter(name[0]))
            return (Keys)char.ToUpperInvariant(name[0]);

        return name switch
        {
            "Ctrl" => Keys.ControlKey,
            "Shift" => Keys.ShiftKey,
            "Alt" => Keys.Menu,
            "Win" => Keys.LWin,
            "Enter" => Keys.Enter,
            "Espaço" or "Space" => Keys.Space,
            "Esc" or "Escape" => Keys.Escape,
            "Backspace" => Keys.Back,
            "Tab" => Keys.Tab,
            "Delete" => Keys.Delete,
            "Insert" => Keys.Insert,
            "Home" => Keys.Home,
            "End" => Keys.End,
            "PageUp" => Keys.PageUp,
            "PageDown" => Keys.PageDown,
            "Esquerda" or "Left" => Keys.Left,
            "Direita" or "Right" => Keys.Right,
            "Cima" or "Up" => Keys.Up,
            "Baixo" or "Down" => Keys.Down,
            "CapsLock" => Keys.Capital,
            "PrintScreen" => Keys.PrintScreen,
            "ScrollLock" or "Scroll" => Keys.Scroll,
            "Menu" => Keys.Apps,
            "-" => Keys.OemMinus,
            "+" => Keys.Oemplus,
            "," => Keys.Oemcomma,
            "." => Keys.OemPeriod,
            "/" => Keys.OemQuestion,
            ";" => Keys.Oem1,
            "'" => Keys.Oem7,
            "[" => Keys.OemOpenBrackets,
            "]" => Keys.Oem6,
            "\\" => Keys.Oem5,
            "ABNT" => Keys.Oem102,
            "`" => Keys.Oemtilde,
            "Num+" => Keys.Add,
            "Num-" => Keys.Subtract,
            "Num*" => Keys.Multiply,
            "Num/" => Keys.Divide,
            "F1" => Keys.F1,
            "F2" => Keys.F2,
            "F3" => Keys.F3,
            "F4" => Keys.F4,
            "F5" => Keys.F5,
            "F6" => Keys.F6,
            "F7" => Keys.F7,
            "F8" => Keys.F8,
            "F9" => Keys.F9,
            "F10" => Keys.F10,
            "F11" => Keys.F11,
            "F12" => Keys.F12,
            _ => ParseNamedKey(name)
        };
    }

    private static bool IsDigitRow(Keys key)
    {
        var code = key & Keys.KeyCode;
        return code is >= Keys.D0 and <= Keys.D9;
    }

    private static string DigitName(Keys key)
    {
        var code = key & Keys.KeyCode;
        return ((char)('0' + (code - Keys.D0))).ToString();
    }

    private static bool TryDigitKey(string name, out Keys key)
    {
        key = Keys.None;
        if (name.Length == 1 && name[0] is >= '0' and <= '9')
        {
            key = (Keys)(byte)name[0];
            return true;
        }

        if (name.Length == 4 &&
            name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            name[3] is >= '0' and <= '9')
        {
            key = (Keys)(byte)name[3];
            return true;
        }

        return false;
    }

    public static bool TryDigitVirtualKey(string name, out ushort vk, out ushort scan)
    {
        vk = 0;
        scan = 0;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        char digit;
        if (name.Length == 1 && name[0] is >= '0' and <= '9')
            digit = name[0];
        else if (name.Length == 4 &&
                 name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
                 name[3] is >= '0' and <= '9')
            digit = name[3];
        else
            return false;

        vk = (ushort)digit;
        scan = digit switch
        {
            '1' => 0x02,
            '2' => 0x03,
            '3' => 0x04,
            '4' => 0x05,
            '5' => 0x06,
            '6' => 0x07,
            '7' => 0x08,
            '8' => 0x09,
            '9' => 0x0A,
            _ => 0x0B
        };
        return true;
    }

    private static Keys ParseNamedKey(string name)
    {
        if (name.Length > 0 && char.IsDigit(name[0]))
            return Keys.None;
        return Enum.TryParse<Keys>(name, true, out var parsed) ? parsed : Keys.None;
    }

    public static string MouseDisplay(string button) => button switch
    {
        "Right" => "Clique direito",
        "Middle" => "Clique do meio",
        "X1" => "Lateral 4",
        "X2" => "Lateral 5",
        _ => "Clique esquerdo"
    };

    public static string MouseTriggerDisplay(string button) => button switch
    {
        "Right" => "Mouse direito",
        "Middle" => "Mouse meio",
        "X1" => "Mouse 4",
        "X2" => "Mouse 5",
        _ => button
    };

    public static bool IsMouseBind(string bind) =>
        bind is "Left" or "Right" or "Middle" or "X1" or "X2";
}
