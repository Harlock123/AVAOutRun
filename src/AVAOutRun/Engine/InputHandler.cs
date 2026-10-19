using Avalonia.Input;

namespace AVAOutRun.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, up and down (up the screen is -1), and a button.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right (or A and D) to steer; up, W, X or Ctrl to accelerate; down or S to brake; Space, Z or Shift to
    /// change gear. On a gamepad: the stick to steer, A or the right trigger to accelerate, B or the left trigger to
    /// brake, X or Y to change gear.
    /// </summary>
    public Controls Player => Read(Key.Left, Key.Right, Key.Up, Key.Down, [Key.X, Key.LeftCtrl, Key.RightCtrl], _pad, also: true);

    private Controls Read(Key left, Key right, Key up, Key down, Key[] fire, ControllerManager pad, bool also = false)
    {
        int dx = (Any(right) || also && Any(Key.D, Key.NumPad6) ? 1 : 0) - (Any(left) || also && Any(Key.A, Key.NumPad4) ? 1 : 0);
        if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
        // Down the screen is +1.
        int dy = (Any(down) || also && Any(Key.S, Key.NumPad2) ? 1 : 0) - (Any(up) || also && Any(Key.W, Key.NumPad8) ? 1 : 0);
        if (dy == 0) dy = pad.DPadUp ? -1 : pad.DPadDown || pad.ButtonB || pad.LeftTrigger > 0.5f ? 1 : 0;
        bool act = Any(Key.Space, Key.Z, Key.LeftShift, Key.RightShift) || pad.ButtonX || pad.ButtonY;
        bool button = Any(fire) || pad.ButtonA || pad.RightTrigger > 0.5f;
        return new Controls(dx, dy, button, act);
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
