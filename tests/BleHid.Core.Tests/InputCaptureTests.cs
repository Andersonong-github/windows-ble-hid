using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class InputCaptureTests
{
    private const int CapsLock = 0x14;
    private const int LeftControl = 0xA2;
    private const int LeftAlt = 0xA4;

    [Fact]
    public void Hook_timing_separates_event_age_from_callback_duration()
    {
        var timing = new InputCapture.HookTiming();

        timing.Record(100, 110, 20);
        timing.Record(200, 240, 30);

        Assert.Equal(2, timing.Count);
        Assert.Equal(50, timing.TotalEventAgeMs);
        Assert.Equal(40, timing.MaxEventAgeMs);
        Assert.Equal(50, timing.TotalCallbackTicks);
        Assert.Equal(30, timing.MaxCallbackTicks);
    }

    [Fact]
    public void Hook_timing_handles_tick_count_rollover()
    {
        var timing = new InputCapture.HookTiming();

        timing.Record(int.MaxValue - 4, int.MinValue + 5, 0);

        Assert.Equal(10, timing.TotalEventAgeMs);
        Assert.Equal(10, timing.MaxEventAgeMs);
    }

    [Fact]
    public void Hook_timing_clamps_future_event_timestamps()
    {
        var timing = new InputCapture.HookTiming();

        timing.Record(12, 10, 0);

        Assert.Equal(0, timing.TotalEventAgeMs);
        Assert.Equal(0, timing.MaxEventAgeMs);
    }

    [Theory]
    [InlineData(0xA2, 0xA4)]
    [InlineData(0xA2, 0xA5)]
    [InlineData(0xA3, 0xA4)]
    [InlineData(0xA3, 0xA5)]
    public void Switching_out_with_caps_lock_and_back_with_d_c_leaves_no_local_keys_pressed(
        int controlKey, int altKey)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        var localPressedKeys = new HashSet<int>();
        var switches = 0;
        capture.SwitchHostRequested += () =>
        {
            switches++;
            capture.SetPassThrough(!capture.PassThrough);
        };

        void HandleKey(int virtualKey, bool isDown)
        {
            if (capture.HandleKeyboardEvent(virtualKey, isDown)) return;
            if (isDown) localPressedKeys.Add(virtualKey);
            else localPressedKeys.Remove(virtualKey);
        }

        HandleKey(controlKey, true);
        HandleKey(altKey, true);
        HandleKey(CapsLock, true);
        Assert.False(capture.PassThrough);
        HandleKey(CapsLock, false);
        HandleKey(altKey, false);
        HandleKey(controlKey, false);

        HandleKey(controlKey, true);
        HandleKey(0x44, true);
        HandleKey(0x43, true);
        HandleKey(0x43, false);
        HandleKey(0x44, false);
        HandleKey(controlKey, false);

        Assert.True(capture.PassThrough);
        Assert.Equal(2, switches);
        Assert.Empty(localPressedKeys);
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0xA0)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    [InlineData(0x14)]
    [InlineData(0x5B)]
    public void Locally_started_keys_are_released_on_both_targets_when_capture_resumes(int virtualKey)
    {
        using var capture = new InputCapture();
        var reports = new List<(KeyModifiers Modifiers, byte[] Usages)>();
        capture.KeyboardReport += (modifiers, usages) => reports.Add((modifiers, usages));
        capture.SetPassThrough(true);
        Assert.False(capture.HandleKeyboardEvent(virtualKey, true));

        capture.SetPassThrough(false);
        Assert.True(capture.HandleKeyboardEvent(virtualKey, true));
        Assert.False(capture.HandleKeyboardEvent(virtualKey, false));

        Assert.Equal(2, reports.Count);
        Assert.True(reports[0].Modifiers != KeyModifiers.None || reports[0].Usages.Length > 0);
        Assert.Equal(KeyModifiers.None, reports[1].Modifiers);
        Assert.Empty(reports[1].Usages);
        Assert.True(capture.HandleKeyboardEvent(virtualKey, true));
        Assert.True(capture.HandleKeyboardEvent(virtualKey, false));
    }

    [Theory]
    [InlineData(0xA2, 0xA4, false)]
    [InlineData(0xA2, 0xA5, false)]
    [InlineData(0xA3, 0xA4, false)]
    [InlineData(0xA3, 0xA5, false)]
    [InlineData(0xA2, 0xA4, true)]
    [InlineData(0xA2, 0xA5, true)]
    [InlineData(0xA3, 0xA4, true)]
    [InlineData(0xA3, 0xA5, true)]
    public void Ctrl_alt_caps_lock_releases_keys_then_switches_without_forwarding_caps_lock(
        int controlKey, int altKey, bool passThrough)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(passThrough);
        capture.HandleKeyboardEvent(0x41, true);
        capture.HandleKeyboardEvent(controlKey, true);
        capture.HandleKeyboardEvent(altKey, true);
        var events = new List<string>();
        capture.KeyboardReport += (modifiers, usages) =>
        {
            Assert.Equal(KeyModifiers.None, modifiers);
            Assert.Empty(usages);
            events.Add("release");
        };
        capture.SwitchHostRequested += () => events.Add("switch");

        Assert.True(capture.HandleKeyboardEvent(CapsLock, true));
        Assert.True(capture.HandleKeyboardEvent(CapsLock, false));

        Assert.Equal(new[] { "release", "switch" }, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Caps_lock_cycles_once_until_released_even_when_modifiers_are_released_first(bool passThrough)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(passThrough);
        var switches = 0;
        capture.SwitchHostRequested += () => switches++;
        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);

        Assert.True(capture.HandleKeyboardEvent(CapsLock, true));
        Assert.True(capture.HandleKeyboardEvent(CapsLock, true));
        capture.HandleKeyboardEvent(LeftControl, false);
        capture.HandleKeyboardEvent(LeftAlt, false);
        Assert.True(capture.HandleKeyboardEvent(CapsLock, true));
        Assert.Equal(1, switches);
        Assert.True(capture.HandleKeyboardEvent(CapsLock, false));

        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);
        Assert.True(capture.HandleKeyboardEvent(CapsLock, true));
        Assert.True(capture.HandleKeyboardEvent(CapsLock, false));
        Assert.Equal(2, switches);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Caps_lock_without_both_modifiers_keeps_normal_input_behavior(
        bool controlDown, bool altDown, bool passThrough)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(passThrough);
        if (controlDown) capture.HandleKeyboardEvent(LeftControl, true);
        if (altDown) capture.HandleKeyboardEvent(LeftAlt, true);
        var switches = 0;
        var reports = new List<byte[]>();
        capture.SwitchHostRequested += () => switches++;
        capture.KeyboardReport += (_, usages) => reports.Add(usages);

        Assert.Equal(!passThrough, capture.HandleKeyboardEvent(CapsLock, true));
        Assert.Equal(!passThrough, capture.HandleKeyboardEvent(CapsLock, false));

        Assert.Equal(0, switches);
        if (passThrough)
        {
            Assert.Empty(reports);
        }
        else
        {
            Assert.Equal(2, reports.Count);
            Assert.Equal(new byte[] { 0x39 }, reports[0]);
            Assert.Empty(reports[1]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ctrl_d_c_still_cycles_once_until_released(bool passThrough)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(passThrough);
        var switches = 0;
        capture.SwitchHostRequested += () => switches++;
        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(0x44, true);

        Assert.True(capture.HandleKeyboardEvent(0x43, true));
        Assert.True(capture.HandleKeyboardEvent(0x43, true));
        Assert.Equal(1, switches);

        capture.HandleKeyboardEvent(0x43, false);
        Assert.True(capture.HandleKeyboardEvent(0x43, true));
        Assert.Equal(2, switches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ctrl_alt_q_still_stops_without_switching(bool passThrough)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(passThrough);
        var switches = 0;
        var stops = 0;
        capture.SwitchHostRequested += () => switches++;
        capture.StopRequested += () => stops++;
        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);

        Assert.True(capture.HandleKeyboardEvent(0x51, true));

        Assert.Equal(1, stops);
        Assert.Equal(0, switches);
    }

    [Fact]
    public void Shake_gesture_switches_the_target()
    {
        using var capture = new InputCapture();
        var now = 1000;
        capture.SetNow(() => now);
        capture.SetSwitchGesture(SwitchGestureOptions.ForTest(true, 80, 3, 800));

        var switches = 0;
        capture.SwitchHostRequested += () => switches++;

        var sign = 1;
        for (var stroke = 0; stroke < 4; stroke++)
        {
            for (var step = 0; step < 10; step++)
            {
                now += 5;
                capture.TrackGesture(sign * 10, 0);
            }
            sign = -sign;
        }

        Assert.Equal(1, switches);
    }

    [Fact]
    public void Shake_gesture_is_inert_when_it_is_not_enabled()
    {
        using var capture = new InputCapture();
        var now = 1000;
        capture.SetNow(() => now);
        capture.SetSwitchGesture(SwitchGestureOptions.ForTest(false, 80, 3, 800));

        var switches = 0;
        capture.SwitchHostRequested += () => switches++;

        var sign = 1;
        for (var stroke = 0; stroke < 8; stroke++)
        {
            for (var step = 0; step < 10; step++)
            {
                now += 5;
                capture.TrackGesture(sign * 10, 0);
            }
            sign = -sign;
        }

        Assert.Equal(0, switches);
    }

    [Fact]
    public void Shake_gesture_does_not_fire_on_ordinary_pointer_movement()
    {
        using var capture = new InputCapture();
        var now = 1000;
        capture.SetNow(() => now);
        capture.SetSwitchGesture(SwitchGestureOptions.ForTest(true, 80, 3, 800));

        var switches = 0;
        capture.SwitchHostRequested += () => switches++;

        // A long sweep across the screen, then a short correction back - the shape of reaching
        // for something and overshooting slightly.
        for (var step = 0; step < 100; step++)
        {
            now += 5;
            capture.TrackGesture(12, 1);
        }
        for (var step = 0; step < 3; step++)
        {
            now += 5;
            capture.TrackGesture(-6, 0);
        }

        Assert.Equal(0, switches);
    }

    [Theory]
    [InlineData(MouseButtons.Left)]
    [InlineData(MouseButtons.Right)]
    [InlineData(MouseButtons.Middle)]
    public void Switching_target_releases_a_held_mouse_button(MouseButtons button)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);

        var buttonReports = new List<MouseButtons>();
        capture.MouseReport += (buttons, _, _, _) => buttonReports.Add(buttons);
        Assert.True(capture.HandleMouseButtonEvent(button, true));
        Assert.Equal(new[] { button }, buttonReports);
        var switches = 0;
        capture.SwitchHostRequested += () =>
        {
            Assert.Equal(new[] { button, MouseButtons.None }, buttonReports);
            switches++;
        };

        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);
        capture.HandleKeyboardEvent(CapsLock, true);

        Assert.Equal(1, switches);
        Assert.True(capture.HandleMouseButtonEvent(button, false));
        Assert.Equal(MouseButtons.None, buttonReports[^1]);
    }

    [Theory]
    [InlineData(MouseButtons.Left)]
    [InlineData(MouseButtons.Right)]
    [InlineData(MouseButtons.Middle)]
    public void Shaking_away_from_local_preserves_the_local_mouse_release(MouseButtons button)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        capture.SetSwitchGesture(SwitchGestureOptions.ForTest(true, 80, 3, 800));
        var now = 1000;
        capture.SetNow(() => now);
        capture.SwitchHostRequested += () => capture.SetPassThrough(false);
        var buttonReports = new List<MouseButtons>();
        capture.MouseReport += (buttons, _, _, _) => buttonReports.Add(buttons);

        Assert.False(capture.HandleMouseButtonEvent(button, true));
        foreach (var delta in new[] { 100, -100, 100, -100 })
        {
            now += 50;
            capture.TrackGesture(delta, 0);
        }

        Assert.False(capture.PassThrough);
        Assert.Empty(buttonReports);
        Assert.False(capture.HandleMouseButtonEvent(button, false));
        Assert.Equal(new[] { MouseButtons.None }, buttonReports);

        Assert.True(capture.HandleMouseButtonEvent(button, true));
        Assert.True(capture.HandleMouseButtonEvent(button, false));
        Assert.Equal(new[] { MouseButtons.None, button, MouseButtons.None }, buttonReports);
    }

    [Fact]
    public void Local_mouse_releases_remain_independent_when_multiple_buttons_are_held()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        Assert.False(capture.HandleMouseButtonEvent(MouseButtons.Left, true));
        Assert.False(capture.HandleMouseButtonEvent(MouseButtons.Right, true));

        capture.SetPassThrough(false);
        Assert.False(capture.HandleMouseButtonEvent(MouseButtons.Left, false));
        Assert.False(capture.HandleMouseButtonEvent(MouseButtons.Right, false));
        Assert.True(capture.HandleMouseButtonEvent(MouseButtons.Middle, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mouse_releases_are_queued_before_the_target_changes(bool goLocal)
    {
        var queue = new CaptureSession.ReportQueue();
        var targetChange = goLocal ? CaptureSession.Queued.GoLocal : CaptureSession.Queued.SwitchHost;
        queue.EnqueueMouse(MouseButtons.Left, 40, -10, 1);
        queue.EnqueueKeyboard(KeyModifiers.None, []);
        queue.EnqueueMouse(MouseButtons.None, 0, 0, 0);
        queue.EnqueueTargetChange(targetChange);

        Assert.True(queue.TryDequeue(out var keyboard));
        Assert.Equal(CaptureSession.Queued.Report, keyboard.Kind);
        Assert.True(queue.TryDequeue(out var release));
        Assert.Equal(CaptureSession.Queued.MouseReport, release.Kind);
        Assert.Equal(MouseButtons.None, release.Buttons);
        Assert.Equal((40, -10, 1), (release.Dx, release.Dy, release.Wheel));
        Assert.True(queue.TryDequeue(out var change));
        Assert.Equal(targetChange, change.Kind);
        Assert.False(queue.TryDequeue(out _));
        Assert.False(queue.HasMouseReport);
        Assert.False(queue.TryTakeMouseReport(out _));

        queue.EnqueueMouse(MouseButtons.Right, -5, 2, 0);
        Assert.True(queue.TryTakeMouseReport(out var nextHost));
        Assert.Equal(MouseButtons.Right, nextHost.Buttons);
        Assert.Equal((-5, 2, 0), (nextHost.Dx, nextHost.Dy, nextHost.Wheel));
    }

    [Fact]
    public void Mouse_motion_remains_coalesced_between_target_changes()
    {
        var queue = new CaptureSession.ReportQueue();
        queue.EnqueueMouse(MouseButtons.Left, 4, 5, 1);
        queue.EnqueueMouse(MouseButtons.Left, 6, -2, -1);

        Assert.False(queue.TryDequeue(out _));
        Assert.True(queue.HasMouseReport);
        Assert.True(queue.TryTakeMouseReport(out var mouse));
        Assert.Equal(MouseButtons.Left, mouse.Buttons);
        Assert.Equal((10, 3, 0), (mouse.Dx, mouse.Dy, mouse.Wheel));
        Assert.False(queue.HasMouseReport);
    }

    [Fact]
    public void New_mouse_input_cannot_overtake_a_queued_target_change()
    {
        var queue = new CaptureSession.ReportQueue();
        queue.EnqueueMouse(MouseButtons.None, 10, 0, 0);
        queue.EnqueueTargetChange(CaptureSession.Queued.SwitchHost);
        queue.EnqueueMouse(MouseButtons.Right, 20, 0, 0);

        Assert.True(queue.HasMouseReport);
        Assert.False(queue.TryTakeMouseReport(out _));
        Assert.True(queue.TryDequeue(out var outgoing));
        Assert.Equal(CaptureSession.Queued.MouseReport, outgoing.Kind);
        Assert.Equal(10, outgoing.Dx);
        Assert.False(queue.TryTakeMouseReport(out _));
        Assert.True(queue.TryDequeue(out var change));
        Assert.Equal(CaptureSession.Queued.SwitchHost, change.Kind);
        Assert.True(queue.TryTakeMouseReport(out var incoming));
        Assert.Equal(MouseButtons.Right, incoming.Buttons);
        Assert.Equal(20, incoming.Dx);
    }

    [Fact]
    public void U_and_o_survive_a_normal_press_and_release()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);

        capture.HandleKeyboardEvent(0x55, true, 0x16); // U
        Assert.Equal(new byte[] { 0x18 }, reports[^1]);

        capture.HandleKeyboardEvent(0x4F, true, 0x18); // O
        Assert.Equal(new byte[] { 0x18, 0x12 }, reports[^1]);

        capture.HandleKeyboardEvent(0x55, false, 0x16);
        Assert.Equal(new byte[] { 0x12 }, reports[^1]);

        capture.HandleKeyboardEvent(0x4F, false, 0x18);
        Assert.Empty(reports[^1]);
    }

    [Fact]
    public void An_ime_consumed_letter_still_reaches_the_host()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);

        // VK_PROCESSKEY plus the physical scan code for O.
        capture.HandleKeyboardEvent(0xE5, true, 0x18);
        Assert.Equal(new byte[] { 0x12 }, reports[^1]);

        capture.HandleKeyboardEvent(0xE5, false, 0x18);
        Assert.Empty(reports[^1]);
    }

    [Fact]
    public void Auto_repeat_does_not_resend_an_unchanged_report()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var reports = 0;
        capture.KeyboardReport += (_, _) => reports++;

        Assert.True(capture.HandleKeyboardEvent(0x41, true));
        Assert.True(capture.HandleKeyboardEvent(0x41, true));
        Assert.True(capture.HandleKeyboardEvent(0x41, true));

        Assert.Equal(1, reports);
    }

    [Fact]
    public void The_newest_six_keys_win_when_more_than_six_are_held()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        byte[] last = [];
        capture.KeyboardReport += (_, usages) => last = usages;

        // A..G, usages 0x04..0x0A.
        for (var i = 0; i < 7; i++) capture.HandleKeyboardEvent(0x41 + i, true);
        Assert.Equal(new byte[] { 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A }, last);

        // Releasing B makes room and brings the still-held A back into the report.
        capture.HandleKeyboardEvent(0x42, false);
        Assert.Contains((byte)0x04, last);
        Assert.DoesNotContain((byte)0x05, last);
        Assert.Equal(6, last.Length);
    }

    [Fact]
    public void A_lost_key_up_is_recovered_without_losing_the_next_press()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);

        capture.HandleKeyboardEvent(0x55, true, 0x16); // U down
        Assert.Equal(new byte[] { 0x18 }, reports[^1]);

        // The key-up never arrived, but the physical key is already released.
        capture.SetKeyStateProbe(_ => false);
        capture.ResyncHeldState();
        Assert.Empty(reports[^1]);

        // A host still holding U would ignore the next identical report; it must be a press.
        capture.HandleKeyboardEvent(0x55, true, 0x16);
        Assert.Equal(new byte[] { 0x18 }, reports[^1]);
    }

    [Fact]
    public void A_stuck_modifier_is_released_when_its_key_up_was_missed()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var modifiers = KeyModifiers.None;
        capture.KeyboardReport += (value, _) => modifiers = value;

        capture.HandleKeyboardEvent(LeftControl, true);
        Assert.Equal(KeyModifiers.LeftControl, modifiers);

        capture.SetKeyStateProbe(_ => false);
        capture.ResyncHeldState();
        Assert.Equal(KeyModifiers.None, modifiers);
    }

    [Theory]
    [InlineData(0x31, 0)]
    [InlineData(0x35, 4)]
    [InlineData(0x39, 8)]
    public void Ctrl_alt_digit_selects_a_host_directly_and_only_once(int digit, int expectedIndex)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        var selected = new List<int>();
        capture.SelectHostRequested += selected.Add;
        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);

        Assert.True(capture.HandleKeyboardEvent(digit, true));
        Assert.True(capture.HandleKeyboardEvent(digit, true)); // auto-repeat
        capture.HandleKeyboardEvent(digit, false);

        Assert.Equal(new[] { expectedIndex }, selected);
    }

    [Fact]
    public void Ctrl_alt_zero_returns_input_to_this_pc()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var selected = 0;
        var locals = 0;
        capture.SelectHostRequested += _ => selected++;
        capture.SelectLocalRequested += () => locals++;
        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);

        Assert.True(capture.HandleKeyboardEvent(0x30, true));

        Assert.Equal(1, locals);
        Assert.Equal(0, selected);
    }

    [Fact]
    public void Direct_host_selection_releases_held_keys_before_switching()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var events = new List<string>();
        capture.KeyboardReport += (_, usages) =>
        {
            if (usages.Length == 0) events.Add("release");
        };
        capture.SelectHostRequested += _ => events.Add("select");

        capture.HandleKeyboardEvent(0x41, true);
        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(LeftAlt, true);
        capture.HandleKeyboardEvent(0x32, true); // Ctrl+Alt+2

        Assert.Equal(new[] { "release", "select" }, events);
    }

    [Fact]
    public void A_digit_without_both_modifiers_is_typed_normally()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var reports = new List<byte[]>();
        var selected = 0;
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.SelectHostRequested += _ => selected++;

        capture.HandleKeyboardEvent(0x31, true);

        Assert.Equal(new byte[] { 0x1E }, reports[^1]);
        Assert.Equal(0, selected);
    }

    [Theory]
    [InlineData(MouseButtons.Left)]
    [InlineData(MouseButtons.Right)]
    [InlineData(MouseButtons.Middle)]
    public void Shaking_during_a_remote_drag_orders_the_release_before_switching(MouseButtons button)
    {
        using var capture = new InputCapture();
        var queue = new CaptureSession.ReportQueue();
        capture.KeyboardReport += queue.EnqueueKeyboard;
        capture.MouseReport += queue.EnqueueMouse;
        capture.SwitchHostRequested += () => queue.EnqueueTargetChange(CaptureSession.Queued.SwitchHost);
        capture.SetSwitchGesture(SwitchGestureOptions.ForTest(true, 80, 3, 800));
        var now = 1000;
        capture.SetNow(() => now);

        Assert.True(capture.HandleMouseButtonEvent(button, true));
        Assert.True(queue.TryTakeMouseReport(out var pressed));
        Assert.Equal(button, pressed.Buttons);

        foreach (var delta in new[] { 100, -100, 100, -100 })
        {
            now += 50;
            capture.TrackGesture(delta, 0);
        }

        Assert.True(queue.TryDequeue(out var keyboardRelease));
        Assert.Equal(CaptureSession.Queued.Report, keyboardRelease.Kind);
        Assert.True(queue.TryDequeue(out var mouseRelease));
        Assert.Equal(CaptureSession.Queued.MouseReport, mouseRelease.Kind);
        Assert.Equal(MouseButtons.None, mouseRelease.Buttons);
        Assert.True(queue.TryDequeue(out var change));
        Assert.Equal(CaptureSession.Queued.SwitchHost, change.Kind);
        Assert.False(queue.TryTakeMouseReport(out _));
    }
}
