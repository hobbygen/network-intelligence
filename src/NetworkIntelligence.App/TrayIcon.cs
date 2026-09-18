using System.Runtime.InteropServices;
namespace NetworkIntelligence.App;

internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly IntPtr hwnd;
    private readonly SubclassProc callback;
    private readonly Action<string> command;
    private bool added;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id; public uint Flags; public uint Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    /// <summary>Loaded once from the app's own icon file (Assets/AppIcon.ico, next to the exe); falls back to the
    /// generic system application icon if the file is missing so a bad install never breaks the tray icon.</summary>
    private static readonly IntPtr AppIcon = LoadAppIcon();
    private static IntPtr LoadAppIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        const uint imageIcon = 1, lrLoadFromFile = 0x10;
        return File.Exists(path) ? LoadImage(IntPtr.Zero, path, imageIcon, 16, 16, lrLoadFromFile) : IntPtr.Zero;
    }
    private NotifyData Data() => new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = hwnd, Id = 1, Tip = "Network Intelligence", Info = "", Title = "" };
    public bool Available => added;
    public TrayIcon(IntPtr window, Action<string> onCommand)
    {
        hwnd = window; command = onCommand; callback = Handle;
        if (!SetWindowSubclass(hwnd, callback, 1, 0)) return;
        Add();
    }
    private void Add()
    {
        var data = Data(); data.Flags = 1 | 2 | 4; data.Callback = CallbackMessage; data.Icon = AppIcon != IntPtr.Zero ? AppIcon : LoadIcon(IntPtr.Zero, (IntPtr)32516);
        added = Shell_NotifyIcon(0, ref data);
    }
    public void Update(string text)
    {
        if (!added) return; var data = Data(); data.Flags = 4; data.Tip = text.Length > 127 ? text[..127] : text; Shell_NotifyIcon(1, ref data);
    }
    public void Notify(string title, string body)
    {
        if (!added) return; var data = Data(); data.Flags = 16; data.InfoFlags = 1; data.Title = title[..Math.Min(63, title.Length)]; data.Info = body[..Math.Min(255, body.Length)]; Shell_NotifyIcon(1, ref data);
    }
    public void Hide() => ShowWindow(hwnd, 0);
    public void Show() { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
    private IntPtr Handle(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == taskbarCreated) Add();
        if (message == CallbackMessage)
        {
            if (lParam.ToInt64() is 0x202 or 0x203) command("show");
            else if (lParam.ToInt64() == 0x205)
            {
                var menu = CreatePopupMenu();
                try
                {
                    string[] actions = ["Show Network Intelligence", "Pause / resume monitoring", "Run diagnostics", "Settings", "Exit"];
                    for (int i = 0; i < actions.Length; i++) AppendMenu(menu, 0, (UIntPtr)(i + 1), actions[i]);
                    GetCursorPos(out var point); SetForegroundWindow(hwnd);
                    uint chosen = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, IntPtr.Zero);
                    if (chosen is >= 1 and <= 5) command(new[] { "show", "pause", "diagnostics", "settings", "exit" }[chosen - 1]);
                }
                finally { DestroyMenu(menu); }
            }
            return IntPtr.Zero;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    public void Dispose()
    {
        var data = Data(); if (added) Shell_NotifyIcon(2, ref data); added = false;
        RemoveWindowSubclass(hwnd, callback, 1);
    }
}
