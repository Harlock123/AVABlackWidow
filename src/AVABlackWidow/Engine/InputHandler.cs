using Avalonia.Input;

namespace AVABlackWidow.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to twin-stick controls: W A S D walk and the arrow keys shoot
/// (eight ways, or the numeric keypad); on a gamepad the left stick walks and the right stick shoots
/// in any direction, with the face buttons as a four-way fallback.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            double mx = (Any(Key.D) ? 1 : 0) - (Any(Key.A) ? 1 : 0);
            double my = (Any(Key.S) ? 1 : 0) - (Any(Key.W) ? 1 : 0);
            double fx = (Any(Key.Right, Key.NumPad6, Key.NumPad9, Key.NumPad3) ? 1 : 0) - (Any(Key.Left, Key.NumPad4, Key.NumPad7, Key.NumPad1) ? 1 : 0);
            double fy = (Any(Key.Down, Key.NumPad2, Key.NumPad1, Key.NumPad3) ? 1 : 0) - (Any(Key.Up, Key.NumPad8, Key.NumPad7, Key.NumPad9) ? 1 : 0);
            if (mx == 0 && my == 0)
            {
                mx = _pad.LeftStickX != 0 ? _pad.LeftStickX : _pad.DPadLeft ? -1 : _pad.DPadRight ? 1 : 0;
                my = _pad.LeftStickY != 0 ? _pad.LeftStickY : _pad.DPadUp ? -1 : _pad.DPadDown ? 1 : 0;
            }
            if (fx == 0 && fy == 0)
            {
                fx = _pad.RightStickX;
                fy = _pad.RightStickY;
                if (fx * fx + fy * fy < 0.25)
                {
                    fx = (_pad.ButtonB ? 1 : 0) - (_pad.ButtonX ? 1 : 0);
                    fy = (_pad.ButtonA ? 1 : 0) - (_pad.ButtonY ? 1 : 0);
                }
            }
            return new Controls(mx, my, fx, fy);
        }
    }

    public bool Start => Any(Key.Enter, Key.Space) || _pad.ButtonStart;

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
