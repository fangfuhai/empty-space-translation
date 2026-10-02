using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceTranslate;

internal static class Native
{
    internal const nuint ExtraSignature = 0x5354524E; // "STRN" - SpaceTranslate Injected
    internal const uint WM_HOOK_RESET = 0x8001;
    internal delegate nint HookProc(int code, nint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wparam, nint lparam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wp, nint lp);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint id, ref GuiThreadInfo info);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("imm32.dll")] internal static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] internal static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)] internal static extern int ImmGetCompositionString(nint context, uint index, nint buffer, uint length);
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
    { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public RECT CaretRect; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    internal static bool ModifiersDown() => new[] { 16, 17, 18, 91, 92 }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    internal static string FocusStamp()
    {
        var win = GetForegroundWindow(); var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(GetWindowThreadProcessId(win, out uint process), ref info);
        return $"{win}:{info.Focus}:{process}";
    }
    internal static bool OurForeground()
    { GetWindowThreadProcessId(GetForegroundWindow(), out uint p); return p == (uint)Environment.ProcessId; }
    internal static bool Composing()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(GetWindowThreadProcessId(GetForegroundWindow(), out _), ref info);
        var h = ImmGetContext(info.Focus);
        if (h == 0) return false;
        try { return ImmGetCompositionString(h, 8, 0, 0) > 0; }
        finally { ImmReleaseContext(info.Focus, h); }
    }
    private static INPUT Key(int vk, bool up) => new() { Type = 1, Union = new InputUnion { Keyboard = new KEYBDINPUT { Vk = (ushort)vk, Flags = up ? 2u : 0u, Extra = ExtraSignature } } };
    internal static void Send(int key, int modifier = 0, int times = 1)
    {
        List<INPUT> keys = [];
        if (modifier != 0) keys.Add(Key(modifier, false));
        for (int i = 0; i < times; ++i) { keys.Add(Key(key, false)); keys.Add(Key(key, true)); }
        if (modifier != 0) keys.Add(Key(modifier, true));
        var array = keys.ToArray();
        if (SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>()) != array.Length)
            throw new IOException("目标窗口未接受按键。请在普通权限的输入框中使用。");
    }
}

internal sealed class InputHooks : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim installed = new();
    private Native.HookProc? keyboardProc, mouseProc;
    private nint keyboard, mouse;
    private uint threadId;
    private Exception? error;
    private readonly DoubleSpaceDetector detector;
    private readonly HashSet<int> held = [];
    private readonly Action activity, doubleSpace;
    internal volatile bool Enabled;
    internal InputHooks(int interval, Action activity, Action doubleSpace)
    {
        detector = new(interval); this.activity = activity; this.doubleSpace = doubleSpace;
        thread = new Thread(Run) { IsBackground = true, Name = "SpaceTranslate input" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!installed.Wait(3000)) throw new IOException("无法启动键盘监听，请重启程序。");
        if (error != null) throw error;
    }
    internal void RequestReset()
    {
        if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_HOOK_RESET, 0, 0);
    }
    private void Run()
    {
        threadId = Native.GetCurrentThreadId();
        keyboardProc = OnKeyboard; mouseProc = OnMouse;
        try
        {
            keyboard = Native.SetWindowsHookEx(13, keyboardProc, Native.GetModuleHandle(null), 0);
            mouse = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
            if (keyboard == 0 || mouse == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监听快捷键。");
            installed.Set();
            while (true)
            {
                bool b = GetMessage(out var msg, 0, 0, 0);
                if (!b) break;
                if (msg.message == Native.WM_HOOK_RESET)
                {
                    detector.Reset();
                    held.Clear();
                }
                else if (msg.message == 0x12) break; // WM_QUIT
            }
        }
        catch (Exception e) { error = e; installed.Set(); }
        finally { if (keyboard != 0) Native.UnhookWindowsHookEx(keyboard); if (mouse != 0) Native.UnhookWindowsHookEx(mouse); }
    }
    [DllImport("user32.dll")] private static extern bool GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int pt_x, pt_y; }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0)
            {
                var k = Marshal.PtrToStructure<Native.KeyboardData>(data);
                bool injected = k.Extra == Native.ExtraSignature;
                if (!injected)
                {
                    bool down = message is 0x100 or 0x104;
                    bool up = message is 0x101 or 0x105;
                    int key = (int)k.Vk;
                    if (down) { held.Add(key); activity(); }
                    else if (up) activity();
                    bool mods = held.Any(x => x is 16 or 17 or 18 or 91 or 92 or >= 160 and <= 165);
                    if (!Enabled) detector.Reset();
                    else if (down) detector.KeyDown(key == 32, mods);
                    else if (up && detector.KeyUp(key == 32, mods, Environment.TickCount64)) doubleSpace();
                    if (up) held.Remove(key);
                }
            }
        }
        catch { detector.Reset(); }
        return Native.CallNextHookEx(keyboard, code, message, data);
    }
    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && message is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            var m = Marshal.PtrToStructure<Native.MouseData>(data);
            bool injected = m.Extra == Native.ExtraSignature;
            if ((m.Flags & 1) == 0 && !injected) { detector.Reset(); activity(); }
        }
        else if (code >= 0 && message is 0x20A or 0x20E)
        {
            var m = Marshal.PtrToStructure<Native.MouseData>(data);
            bool injected = m.Extra == Native.ExtraSignature;
            if ((m.Flags & 1) == 0 && !injected) detector.Reset();
        }
        return Native.CallNextHookEx(mouse, code, message, data);
    }
    public void Dispose() { Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(1000); installed.Dispose(); }
}

internal sealed class WindowsClipboard : IClipboardPort
{
    private sealed class SnapshotData : IDisposable
    {
        public DataObject Data = new();
        public bool Empty;
        public List<IDisposable> Owned = [];
        public void Dispose() { foreach (var item in Owned) item.Dispose(); }
    }
    public uint Sequence => Native.GetClipboardSequenceNumber();
    public object Snapshot()
    {
        var result = new SnapshotData();
        try
        {
            var source = Clipboard.GetDataObject();
            if (source == null) { result.Empty = true; return result; }
            foreach (var format in source.GetFormats(false))
            {
                // OS cloud/history flags need not be copied; request local-only handling for temporary text.
                if (format is "CanIncludeInClipboardHistory" or "CanUploadToCloudClipboard" or "ExcludeClipboardContentFromMonitorProcessing") continue;
                object? v = source.GetData(format, false);
                if (v == null) throw new IOException("剪贴板包含无法保存的格式，请先复制一段普通文字再试。");
                object cloned = v switch
                {
                    string s => s,
                    string[] a => a.Clone(),
                    byte[] b => b.Clone(),
                    Image i => i.Clone(),
                    MemoryStream m => new MemoryStream(m.ToArray()),
                    StringCollection c => CopyFiles(c),
                    ICloneable c => c.Clone(),
                    _ when v.GetType().IsValueType => v,
                    _ => throw new IOException("当前剪贴板格式不支持可靠恢复，请先复制普通文字。")
                };
                if (cloned is IDisposable d) result.Owned.Add(d);
                result.Data.SetData(format, false, cloned);
            }
            result.Empty = result.Data.GetFormats(false).Length == 0;
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static StringCollection CopyFiles(StringCollection c) { var copy = new StringCollection(); foreach (string? s in c) if (s != null) copy.Add(s); return copy; }
    public void WriteText(string value)
    {
        if (value == "") { Clipboard.Clear(); return; }
        var data = new DataObject(); data.SetText(value, TextDataFormat.UnicodeText);
        using var noHistory = new MemoryStream(new byte[4]);
        using var noCloud = new MemoryStream(new byte[4]);
        data.SetData("CanIncludeInClipboardHistory", false, noHistory);
        data.SetData("CanUploadToCloudClipboard", false, noCloud);
        Clipboard.SetDataObject(data, true, 3, 20);
    }
    public string? ReadText() => Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
    public void Restore(object snapshot)
    {
        var s = (SnapshotData)snapshot;
        if (s.Empty) Clipboard.Clear(); else Clipboard.SetDataObject(s.Data, true, 4, 25);
    }
}
