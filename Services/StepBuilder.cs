using DailyMacro.Models;

namespace DailyMacro.Services;

internal static class StepBuilder
{
    public static List<MacroStep> FromMacroEvents(IReadOnlyList<MacroEvent> events)
    {
        var steps = new List<MacroStep>();
        if (events.Count == 0)
            return steps;

        var sorted = events.OrderBy(ev => ev.Timestamp).ToList();
        var keysDown = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var miceDown = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var chordKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chordMice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double? chordBegan = null;
        var lastEnd = 0.0;

        void Flush(double at)
        {
            if (keysDown.Count > 0 || miceDown.Count > 0)
                return;
            if (chordKeys.Count == 0 && chordMice.Count == 0)
                return;

            var began = chordBegan ?? at;
            if (steps.Count > 0)
                steps[^1].DelayAfterMs = Math.Max(0, (int)Math.Round((began - lastEnd) * 1000));

            var hold = (int)Math.Round((at - began) * 1000);
            var mice = chordMice.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            steps.Add(new MacroStep
            {
                Type = mice.Count > 0 && chordKeys.Count == 0 ? StepType.Click : StepType.Combo,
                Keys = [.. chordKeys],
                MouseButtons = mice,
                MouseButton = string.Join(",", mice),
                HoldMs = Math.Clamp(hold, 15, 10000)
            });

            lastEnd = at;
            chordKeys.Clear();
            chordMice.Clear();
            chordBegan = null;
        }

        foreach (var ev in sorted)
        {
            switch (ev.Type)
            {
                case "key_press":
                    if (string.IsNullOrWhiteSpace(ev.Key))
                        break;
                    keysDown[ev.Key] = ev.Timestamp;
                    chordKeys.Add(ev.Key);
                    chordBegan ??= ev.Timestamp;
                    break;
                case "key_release":
                    if (!string.IsNullOrWhiteSpace(ev.Key))
                        keysDown.Remove(ev.Key);
                    Flush(ev.Timestamp);
                    break;
                case "mouse_down":
                {
                    var button = string.IsNullOrWhiteSpace(ev.Button) ? "Left" : ev.Button;
                    miceDown[button] = ev.Timestamp;
                    chordMice.Add(button);
                    chordBegan ??= ev.Timestamp;
                    break;
                }
                case "mouse_up":
                {
                    var button = string.IsNullOrWhiteSpace(ev.Button) ? "Left" : ev.Button;
                    miceDown.Remove(button);
                    Flush(ev.Timestamp);
                    break;
                }
            }
        }

        if (chordKeys.Count > 0 || chordMice.Count > 0)
            Flush(sorted[^1].Timestamp + 0.04);

        if (steps.Count > 0 && steps[^1].DelayAfterMs <= 0)
            steps[^1].DelayAfterMs = 1;

        return steps;
    }
}
