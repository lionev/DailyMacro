using DailyMacro.Models;
using System.Diagnostics;

namespace DailyMacro.Services;

internal sealed class MacroPlayer
{
    public bool IsPlaying { get; private set; }
    public int ResumeIndex { get; private set; }
    public int ResumeCycle { get; private set; } = 1;

    public async Task PlayAsync(
        MacroDefinition macro,
        AppSettings settings,
        CancellationToken cancellationToken,
        IProgress<string>? status = null,
        IntPtr focusWindow = default,
        int startIndex = 0,
        int startCycle = 1)
    {
        if (macro.Events.Count == 0 && macro.Steps.Count == 0)
            return;

        var cycleCount = macro.EffectiveCycleCount();
        IsPlaying = true;
        ResumeIndex = Math.Max(0, startIndex);
        ResumeCycle = Math.Clamp(startCycle <= 0 ? 1 : startCycle, 1, cycleCount);
        try
        {
            var speed = Math.Clamp(settings.Speed, 0.25, 3.0);
            var loops = settings.InfiniteLoop || settings.Repeat == RepeatMode.UntilStopped
                ? int.MaxValue
                : settings.Repeat switch
                {
                    RepeatMode.Count => Math.Max(1, settings.RepeatCount),
                    _ => 1
                };

            var index = ResumeIndex;
            var cycle = ResumeCycle;
            for (var loop = 1; loop <= loops; loop++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (loop == 1)
                    ProcessHelper.FocusWindow(focusWindow);
                var loopLabel = cycleCount > 1
                    ? $"ciclo {cycle}/{cycleCount}"
                    : (loops == int.MaxValue ? loop.ToString() : $"{loop}/{loops}");
                var from = loop == 1 ? index : 0;

                if (macro.Steps.Count > 0)
                    index = await Task.Run(() => PlaySteps(macro, speed, loopLabel, cancellationToken, status, from, cycle, cycleCount), cancellationToken);
                else
                    index = await Task.Run(() => PlayEvents(PrepareEvents(macro.Events), macro.TriggerMouse, speed, loopLabel, cancellationToken, status, from), cancellationToken);

                ResumeIndex = 0;
                index = 0;
                cycle = cycle >= cycleCount ? 1 : cycle + 1;
                ResumeCycle = cycle;

                if (loop < loops)
                {
                    InputSender.ReleaseAll(macro.TriggerMouse);
                    await Task.Delay(LoopGapMs(macro, settings), cancellationToken);
                }
            }

            ResumeIndex = 0;
            ResumeCycle = 1;
        }
        finally
        {
            try { InputSender.ReleaseAll(macro.TriggerMouse); } catch { /* ignore */ }
            IsPlaying = false;
        }
    }

    private static List<MacroEvent> PrepareEvents(List<MacroEvent> events)
    {
        var list = events.ToList();
        while (list.Count >= 2)
        {
            var last = list[^1];
            var prev = list[^2];
            if (last.Timestamp - prev.Timestamp < 1.5)
                break;
            if (last.Type != "key_release" || !KeyNames.IsModifierName(last.Key))
                break;
            list.RemoveAt(list.Count - 1);
        }

        return list;
    }

    private int PlayEvents(
        List<MacroEvent> events,
        string skipMouse,
        double speed,
        string loopLabel,
        CancellationToken cancellationToken,
        IProgress<string>? status,
        int startIndex)
    {
        events = events
            .Where(ev =>
                string.IsNullOrWhiteSpace(skipMouse) ||
                ev.Type is not ("mouse_down" or "mouse_up") ||
                !string.Equals(ev.Button, skipMouse, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (events.Count == 0)
            return 0;

        var from = startIndex <= 0 || startIndex >= events.Count ? 0 : startIndex;
        status?.Report(from > 0
            ? $"Reproduzindo {loopLabel} · evento {from + 1}/{events.Count}"
            : $"Reproduzindo {loopLabel}");
        var clock = Stopwatch.StartNew();
        var origin = events[from].Timestamp;
        var heldMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heldMains = 0;
        var heldMice = 0;
        for (var i = from; i < events.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeIndex = i;
            var ev = events[i];
            WaitUntil((ev.Timestamp - origin) * 1000 / speed, clock, cancellationToken);

            if (ev.Type is "mouse_down" or "mouse_up")
            {
                var down = ev.Type == "mouse_down";
                var buttons = new List<string> { string.IsNullOrWhiteSpace(ev.Button) ? "Left" : ev.Button };
                while (i + 1 < events.Count)
                {
                    var next = events[i + 1];
                    if (next.Type != ev.Type)
                        break;
                    if ((next.Timestamp - ev.Timestamp) * 1000 / speed > 150)
                        break;
                    buttons.Add(string.IsNullOrWhiteSpace(next.Button) ? "Left" : next.Button);
                    i++;
                }

                var unique = buttons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                InputSender.SendMouseButtons(unique, down);
                heldMice = Math.Max(0, heldMice + (down ? unique.Count : -unique.Count));
                ReleaseIdleModifiers(heldMods, heldMains, heldMice);
                ResumeIndex = i + 1;
                continue;
            }

            if (ShouldDropLateModifierRelease(ev, heldMods, heldMains, heldMice))
            {
                ResumeIndex = i + 1;
                continue;
            }

            InputSender.SendEvent(ev);
            TrackKeys(ev, heldMods, ref heldMains);
            ReleaseIdleModifiers(heldMods, heldMains, heldMice);
            ResumeIndex = i + 1;
        }

        ResumeIndex = 0;
        return 0;
    }

    private static void TrackKeys(MacroEvent ev, HashSet<string> heldMods, ref int heldMains)
    {
        if (ev.Type is not ("key_press" or "key_release") || string.IsNullOrWhiteSpace(ev.Key))
            return;

        var down = ev.Type == "key_press";
        if (KeyNames.IsModifierName(ev.Key))
        {
            if (down)
                heldMods.Add(ev.Key);
            else
                heldMods.Remove(ev.Key);
            return;
        }

        heldMains = Math.Max(0, heldMains + (down ? 1 : -1));
    }

    private static bool ShouldDropLateModifierRelease(
        MacroEvent ev,
        HashSet<string> heldMods,
        int heldMains,
        int heldMice)
    {
        if (ev.Type != "key_release" || !KeyNames.IsModifierName(ev.Key))
            return false;
        if (heldMains > 0 || heldMice > 0)
            return false;
        return !heldMods.Contains(ev.Key);
    }

    private static void ReleaseIdleModifiers(HashSet<string> heldMods, int heldMains, int heldMice)
    {
        if (heldMains > 0 || heldMice > 0 || heldMods.Count == 0)
            return;
        InputSender.ReleaseModifiers();
        heldMods.Clear();
    }

    private int PlaySteps(
        MacroDefinition macro,
        double speed,
        string loopLabel,
        CancellationToken cancellationToken,
        IProgress<string>? status,
        int startIndex,
        int cycle,
        int cycleCount)
    {
        ResumeCycle = cycle;
        if (startIndex <= 0)
            InputSender.ReleaseAll(macro.TriggerMouse);

        var indexed = new List<(int index, MacroStep step)>();
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            if (macro.Steps[i].PlaysOnCycle(cycle, cycleCount))
                indexed.Add((i, macro.Steps[i]));
        }

        var steps = MergeIndexed(indexed);
        if (steps.Count == 0)
            return 0;

        var from = 0;
        if (startIndex > 0)
        {
            from = steps.FindIndex(item => item.index >= startIndex);
            if (from < 0)
                return 0;
        }

        status?.Report(from > 0
            ? $"Reproduzindo {loopLabel} · etapa {steps[from].index + 1}"
            : $"Reproduzindo {loopLabel}");

        for (var n = from; n < steps.Count; n++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (index, step) = steps[n];
            ResumeIndex = index;
            ResumeCycle = cycle;
            status?.Report($"{loopLabel} · etapa {index + 1}/{macro.Steps.Count} · {step.ActionText()}");

            if (step.Type == StepType.Wait)
            {
                WaitMs(step.WaitMs, speed, cancellationToken);
                ResumeIndex = index + 1;
                continue;
            }

            var playStep = step;
            if (!string.IsNullOrWhiteSpace(macro.TriggerMouse))
            {
                var mice = step.AllMouseButtons()
                    .Where(button => !string.Equals(button, macro.TriggerMouse, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (mice.Count != step.AllMouseButtons().Count)
                {
                    playStep = playStep.Clone();
                    playStep.MouseButtons = mice;
                    playStep.MouseButton = string.Join(",", mice);
                }

                if (playStep.Type is StepType.Combo or StepType.Click &&
                    playStep.Keys.Count == 0 &&
                    playStep.AllMouseButtons().Count == 0)
                {
                    ResumeIndex = index + 1;
                    continue;
                }
            }

            InputSender.SendStep(playStep, cancellationToken, NextPlayable(steps, n, macro));
            ResumeIndex = index + 1;
            if (step.DelayAfterMs > 0)
                WaitExact(step.DelayAfterMs, cancellationToken);
        }

        ResumeIndex = 0;
        return 0;
    }

    private static MacroStep? NextPlayable(
        List<(int index, MacroStep step)> steps,
        int current,
        MacroDefinition macro)
    {
        if (current + 1 >= steps.Count)
            return null;

        var next = steps[current + 1].step;
        if (next.Type == StepType.Wait)
            return null;
        if (string.IsNullOrWhiteSpace(macro.TriggerMouse))
            return next;

        var mice = next.AllMouseButtons()
            .Where(button => !string.Equals(button, macro.TriggerMouse, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mice.Count == next.AllMouseButtons().Count)
            return next;

        var clone = next.Clone();
        clone.MouseButtons = mice;
        clone.MouseButton = string.Join(",", mice);
        return clone;
    }

    private static List<(int index, MacroStep step)> MergeIndexed(List<(int index, MacroStep step)> steps)
    {
        var merged = new List<(int index, MacroStep step)>();
        foreach (var (index, step) in steps)
        {
            if (merged.Count > 0 && CanMergeMouse(merged[^1].step, step))
            {
                var prev = merged[^1].step.Clone();
                var mice = prev.AllMouseButtons();
                mice.AddRange(step.AllMouseButtons());
                prev.Type = StepType.Combo;
                prev.Keys = prev.Keys.Concat(step.Keys)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                prev.MouseButtons = mice.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                prev.MouseButton = string.Join(",", prev.MouseButtons);
                prev.HoldMs = Math.Max(prev.HoldMs, step.HoldMs);
                prev.DelayAfterMs = step.DelayAfterMs;
                merged[^1] = (merged[^1].index, prev);
            }
            else
            {
                merged.Add((index, step));
            }
        }

        return merged;
    }

    private static bool CanMergeMouse(MacroStep previous, MacroStep next)
    {
        if (previous.DelayAfterMs > 40)
            return false;
        if (previous.HoldMs > 200 || next.HoldMs > 200)
            return false;
        if (Math.Abs(previous.HoldMs - next.HoldMs) > 80)
            return false;
        if (previous.Type is not (StepType.Combo or StepType.Click))
            return false;
        if (next.Type is not (StepType.Combo or StepType.Click))
            return false;

        var prevMice = previous.AllMouseButtons();
        var nextMice = next.AllMouseButtons();
        if (prevMice.Count == 0 || nextMice.Count == 0)
            return false;

        var union = prevMice
            .Concat(nextMice)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (union.Count <= prevMice.Count)
            return false;

        var prevKeys = previous.Keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var nextKeys = next.Keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (nextKeys.Count == 0)
            return true;
        return prevKeys.SequenceEqual(nextKeys, StringComparer.OrdinalIgnoreCase);
    }

    private static void WaitUntil(double targetMs, Stopwatch clock, CancellationToken cancellationToken)
    {
        var remain = (int)Math.Round(targetMs - clock.Elapsed.TotalMilliseconds);
        if (remain > 0)
            WaitMs(remain, 1, cancellationToken);
    }

    private static int LoopGapMs(MacroDefinition macro, AppSettings settings)
    {
        var gap = macro.LoopDelayMs;
        if (gap < 50)
            gap = settings.DelayBetweenLoopsMs > 0 ? settings.DelayBetweenLoopsMs : 250;
        gap = Math.Max(gap, 250);
        return Math.Max(100, (int)Math.Round(gap * 0.25));
    }

    private static void WaitExact(int ms, CancellationToken cancellationToken)
    {
        if (ms <= 0)
            return;
        if (cancellationToken.WaitHandle.WaitOne(ms))
            cancellationToken.ThrowIfCancellationRequested();
    }

    private static void WaitMs(int ms, double speed, CancellationToken cancellationToken)
    {
        var scaled = (int)Math.Max(0, ms / speed);
        if (scaled <= 0)
            return;
        if (cancellationToken.WaitHandle.WaitOne(scaled))
            cancellationToken.ThrowIfCancellationRequested();
    }
}
