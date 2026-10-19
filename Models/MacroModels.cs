using System.Text.Json.Serialization;

namespace DailyMacro.Models;

public enum StepType
{
    Combo,
    Text,
    Wait,
    Click
}

public enum RepeatMode
{
    Once,
    UntilStopped,
    Count
}

public sealed class MacroLibrary
{
    public List<MacroDefinition> Macros { get; set; } = [];
}

public sealed class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Novo macro";
    public List<MacroStep> Steps { get; set; } = [];
    public List<MacroEvent> Events { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string TriggerKey { get; set; } = "";
    public string TriggerMouse { get; set; } = "";
    public int LoopDelayMs { get; set; } = 250;
    public bool RepeatWhileHeld { get; set; } = true;
    public bool ResumeStages { get; set; }
    public string TargetProcess { get; set; } = "";
    public bool? Enabled { get; set; }
    /// <summary>How many rotations before the sequence restarts at cycle 1. 1 = every loop plays every step.</summary>
    public int CycleCount { get; set; } = 1;

    [JsonIgnore]
    public bool IsOn => Enabled != false;

    public int EffectiveCycleCount() => Math.Clamp(CycleCount <= 0 ? 1 : CycleCount, 1, 8);

    public string TriggerLabel()
    {
        if (!string.IsNullOrWhiteSpace(TriggerKey))
            return TriggerKey;
        if (!string.IsNullOrWhiteSpace(TriggerMouse))
            return TriggerMouse switch
            {
                "Middle" => "Meio",
                "X1" => "Lateral 4",
                "X2" => "Lateral 5",
                "Right" => "Direito",
                _ => KeyNames.MouseTriggerDisplay(TriggerMouse)
            };
        return "";
    }

    [JsonIgnore]
    public double DurationSeconds
    {
        get
        {
            if (Events.Count > 0)
                return Math.Max(0, Events[^1].Timestamp);

            var ms = 0;
            foreach (var step in Steps)
            {
                if (step.Type == StepType.Wait)
                {
                    ms += Math.Max(0, step.WaitMs);
                    continue;
                }

                if (step.Type is StepType.Combo or StepType.Click)
                    ms += Math.Max(0, step.HoldMs);
                ms += Math.Max(0, step.DelayAfterMs);
            }

            return ms / 1000.0;
        }
    }
    [JsonIgnore]
    public int EventCount => Events.Count > 0 ? Events.Count : Steps.Count;

    public override string ToString()
    {
        var bind = TriggerLabel();
        return string.IsNullOrEmpty(bind) ? Name : $"{Name}   [{bind}]";
    }
}

public sealed class MacroEvent
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Button { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public double Timestamp { get; set; }
}

public sealed class MacroStep
{
    public StepType Type { get; set; } = StepType.Combo;

    /// <summary>Virtual keys that make up a combination, e.g. Shift, E.</summary>
    public List<string> Keys { get; set; } = [];

    public string MouseButton { get; set; } = "";
    public List<string> MouseButtons { get; set; } = [];
    public bool HasPosition { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Text { get; set; } = "";
    public int HoldMs { get; set; } = 40;
    public int WaitMs { get; set; } = 200;
    public int DelayAfterMs { get; set; } = 80;
    /// <summary>If greater than 0, tap the keys every N ms during HoldMs instead of holding.</summary>
    public int TapIntervalMs { get; set; }
    /// <summary>1-based cycles this step plays on. Empty means every cycle.</summary>
    public List<int> Cycles { get; set; } = [];

    public MacroStep Clone() => new()
    {
        Type = Type,
        Keys = [.. Keys],
        MouseButton = MouseButton,
        MouseButtons = [.. MouseButtons],
        HasPosition = HasPosition,
        X = X,
        Y = Y,
        Text = Text,
        HoldMs = HoldMs,
        WaitMs = WaitMs,
        DelayAfterMs = DelayAfterMs,
        TapIntervalMs = TapIntervalMs,
        Cycles = [.. Cycles]
    };

    public string ActionText()
    {
        return Type switch
        {
            StepType.Text => string.IsNullOrWhiteSpace(Text)
                ? "Digitar texto"
                : $"Digitar: {TrimPreview(Text)}",
            StepType.Wait => $"Esperar {Math.Max(0, WaitMs)} ms",
            StepType.Click => ClickText(),
            _ => ComboText()
        };
    }

    public string HoldText()
    {
        if (Type is StepType.Wait or StepType.Text)
            return "—";
        if (TapIntervalMs > 0)
            return $"{Math.Max(0, HoldMs)} ms · clique {TapIntervalMs} ms";
        return $"{Math.Max(0, HoldMs)} ms";
    }

    public string AfterText() => Type == StepType.Wait
        ? "—"
        : $"{Math.Max(0, DelayAfterMs)} ms";

    public bool PlaysOnCycle(int cycle, int cycleCount)
    {
        if (cycleCount <= 1)
            return true;
        var selected = ActiveCycles(cycleCount);
        return selected.Count == 0 || selected.Count >= cycleCount || selected.Contains(cycle);
    }

    public string CycleText(int cycleCount)
    {
        if (cycleCount <= 1)
            return "—";
        var selected = ActiveCycles(cycleCount);
        if (selected.Count == 0 || selected.Count >= cycleCount)
            return "Todos";
        return string.Join("+", selected);
    }

    private List<int> ActiveCycles(int cycleCount) =>
        Cycles
            .Where(cycle => cycle >= 1 && cycle <= cycleCount)
            .Distinct()
            .OrderBy(cycle => cycle)
            .ToList();

    private string ComboText()
    {
        var parts = new List<string>();
        parts.AddRange(Keys.Where(k => !string.IsNullOrWhiteSpace(k)));
        parts.AddRange(AllMouseButtons().Select(KeyNames.MouseDisplay));
        return parts.Count == 0 ? "(vazio)" : string.Join(" + ", parts);
    }

    public List<string> AllMouseButtons()
    {
        if (MouseButtons.Count > 0)
            return MouseButtons
                .Where(button => !string.IsNullOrWhiteSpace(button))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (string.IsNullOrWhiteSpace(MouseButton))
            return [];

        return MouseButton
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(button => button.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ClickText()
    {
        var button = KeyNames.MouseDisplay(string.IsNullOrWhiteSpace(MouseButton) ? "Left" : MouseButton);
        return HasPosition
            ? $"{button} em ({X}, {Y})"
            : $"{button} na posição atual";
    }

    private static string TrimPreview(string text)
    {
        var oneLine = text.Replace("\r", " ").Replace("\n", " ");
        return oneLine.Length <= 42 ? oneLine : oneLine[..42] + "…";
    }
}

public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; }
    public int CountdownSeconds { get; set; }
    public double Speed { get; set; } = 1.0;
    public RepeatMode Repeat { get; set; } = RepeatMode.UntilStopped;
    public int RepeatCount { get; set; } = 5;
    public int DelayBetweenLoopsMs { get; set; } = 250;
    public bool InfiniteLoop { get; set; } = true;
    public bool DarkTheme { get; set; }
    public bool ShowWatermark { get; set; } = true;
    public int WatermarkX { get; set; } = int.MinValue;
    public int WatermarkY { get; set; } = int.MinValue;
}
