namespace CornWatch.UI.Overlay;

// A global hotkey on its own message-only window. MainForm's handle is recreated when it
// moves to and from the tray (ShowInTaskbar), which would silently drop a hotkey bound to it.
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xC0;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    public event Action? Pressed;
    public bool IsRegistered { get; }

    public HotkeyWindow(uint modifiers, Keys key)
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        IsRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key);
        if (!IsRegistered)
            SessionLog.Write($"[HOTKEY] could not register {key} (another app probably owns it), error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && (int)m.WParam == HotkeyId) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        if (IsRegistered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }
}
