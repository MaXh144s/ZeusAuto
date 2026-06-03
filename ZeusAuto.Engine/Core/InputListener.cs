using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeusAuto.Engine.Core.Interfaces;

namespace ZeusAuto.Engine.Core;

public sealed class InputListener : IInputListener
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int XButton1 = 0x0001;
    private const int XButton2 = 0x0002;

    private readonly object _sync = new();
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Exception? _startupException;
    private string[] _startHotkey = [];
    private string[] _stopHotkey = [];
    private string[] _cpsIncrementHotkey = [];
    private string[] _cpsDecrementHotkey = [];
    private bool _startHotkeyLatched;
    private bool _stopHotkeyLatched;
    private bool _cpsIncrementLatched;
    private bool _cpsDecrementLatched;
    private bool _disposed;

    public InputListener()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public event EventHandler<InputEventArgs>? InputDown;

    public event EventHandler<InputEventArgs>? InputUp;

    public event EventHandler? StartHotkeyPressed;

    public event EventHandler? StopHotkeyPressed;

    public event EventHandler? CpsIncrementPressed;

    public event EventHandler? CpsDecrementPressed;

    public void StartListening()
    {
        ThrowIfDisposed();

        ManualResetEventSlim started = new(false);
        lock (_sync)
        {
            if (_hookThread is { IsAlive: true })
            {
                return;
            }

            _startupException = null;
            _hookThread = new Thread(() => HookThreadMain(started))
            {
                IsBackground = true,
                Name = "ZeusAuto.InputListener"
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        started.Wait();

        if (_startupException is not null)
        {
            throw new InvalidOperationException("Input listener could not start.", _startupException);
        }
    }

    public void StopListening()
    {
        Thread? thread;
        uint threadId;

        lock (_sync)
        {
            thread = _hookThread;
            threadId = _hookThreadId;
        }

        if (thread is { IsAlive: true } && threadId != 0)
        {
            PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            thread.Join();
        }

        lock (_sync)
        {
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    public void UpdateConfig(MacroConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            _startHotkey          = ParseHotkey(config.StartHotkey);
            _stopHotkey           = ParseHotkey(config.StopHotkey);
            _cpsIncrementHotkey   = ParseHotkey(config.CpsIncrementHotkey);
            _cpsDecrementHotkey   = ParseHotkey(config.CpsDecrementHotkey);
            _startHotkeyLatched   = false;
            _stopHotkeyLatched    = false;
            _cpsIncrementLatched  = false;
            _cpsDecrementLatched  = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopListening();
        _disposed = true;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            string? keyName = NormalizeVirtualKey(data.vkCode);

            if (!string.IsNullOrWhiteSpace(keyName))
            {
                if (message is WmKeyDown or WmSysKeyDown)
                {
                    bool added;
                    lock (_sync)
                    {
                        added = _pressedKeys.Add(keyName);
                    }

                    if (added)
                    {
                        InputDown?.Invoke(this, new InputEventArgs(keyName));
                    }

                    EvaluateHotkeys();
                }
                else if (message is WmKeyUp or WmSysKeyUp)
                {
                    lock (_sync)
                    {
                        _pressedKeys.Remove(keyName);
                        _startHotkeyLatched      = IsHotkeyPart(keyName, _startHotkey)        && _startHotkeyLatched      && IsHotkeyPressed(_startHotkey);
                        _stopHotkeyLatched       = IsHotkeyPart(keyName, _stopHotkey)         && _stopHotkeyLatched       && IsHotkeyPressed(_stopHotkey);
                        _cpsIncrementLatched     = IsHotkeyPart(keyName, _cpsIncrementHotkey) && _cpsIncrementLatched     && IsHotkeyPressed(_cpsIncrementHotkey);
                        _cpsDecrementLatched     = IsHotkeyPart(keyName, _cpsDecrementHotkey) && _cpsDecrementLatched     && IsHotkeyPressed(_cpsDecrementHotkey);
                    }

                    InputUp?.Invoke(this, new InputEventArgs(keyName));
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            // Ignora eventos sintéticos gerados pelo próprio SendInput (LLMHF_INJECTED = 0x1)
            // Sem isso, os cliques do macro disparam HandleInputUp e param a engine imediatamente
            bool isInjected = (data.flags & 0x1) != 0;
            if (!isInjected)
            {
                string? inputName = MouseMessageToInputName(wParam.ToInt32(), lParam);
                if (!string.IsNullOrWhiteSpace(inputName))
                {
                    int message = wParam.ToInt32();
                    if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
                    {
                        InputDown?.Invoke(this, new InputEventArgs(inputName));
                    }
                    else
                    {
                        InputUp?.Invoke(this, new InputEventArgs(inputName));
                    }
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HookThreadMain(ManualResetEventSlim started)
    {
        try
        {
            lock (_sync)
            {
                _hookThreadId = GetCurrentThreadId();
                _keyboardHook = SetHook(_keyboardProc, WhKeyboardLl);
                _mouseHook = SetHook(_mouseProc, WhMouseLl);
            }

            started.Set();

            while (GetMessage(out MSG _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        catch (Exception ex)
        {
            _startupException = ex;
            started.Set();
        }
        finally
        {
            lock (_sync)
            {
                if (_keyboardHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHook);
                    _keyboardHook = IntPtr.Zero;
                }

                if (_mouseHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHook);
                    _mouseHook = IntPtr.Zero;
                }

                _pressedKeys.Clear();
                _startHotkeyLatched = false;
                _stopHotkeyLatched = false;
                _hookThreadId = 0;
            }
        }
    }

    private void EvaluateHotkeys()
    {
        EventHandler? start      = null;
        EventHandler? stop       = null;
        EventHandler? cpsInc     = null;
        EventHandler? cpsDec     = null;

        lock (_sync)
        {
            if (_startHotkey.Length > 0 && IsHotkeyPressed(_startHotkey))
            {
                if (!_startHotkeyLatched)
                {
                    _startHotkeyLatched = true;
                    start = StartHotkeyPressed;
                }
            }
            else
            {
                _startHotkeyLatched = false;
            }

            if (_stopHotkey.Length > 0 && IsHotkeyPressed(_stopHotkey))
            {
                if (!_stopHotkeyLatched)
                {
                    _stopHotkeyLatched = true;
                    stop = StopHotkeyPressed;
                }
            }
            else
            {
                _stopHotkeyLatched = false;
            }

            // Hotkeys de CPS: disparam repetidamente enquanto a tecla ficar pressionada
            // porque o unlatch acontece no KeyUp — cada KeyDown gera um novo evento.
            if (_cpsIncrementHotkey.Length > 0 && IsHotkeyPressed(_cpsIncrementHotkey))
            {
                if (!_cpsIncrementLatched)
                {
                    _cpsIncrementLatched = true;
                    cpsInc = CpsIncrementPressed;
                }
            }
            else
            {
                _cpsIncrementLatched = false;
            }

            if (_cpsDecrementHotkey.Length > 0 && IsHotkeyPressed(_cpsDecrementHotkey))
            {
                if (!_cpsDecrementLatched)
                {
                    _cpsDecrementLatched = true;
                    cpsDec = CpsDecrementPressed;
                }
            }
            else
            {
                _cpsDecrementLatched = false;
            }
        }

        start?.Invoke(this, EventArgs.Empty);
        stop?.Invoke(this, EventArgs.Empty);
        cpsInc?.Invoke(this, EventArgs.Empty);
        cpsDec?.Invoke(this, EventArgs.Empty);
    }

    private bool IsHotkeyPressed(IReadOnlyCollection<string> hotkey)
    {
        return hotkey.Count > 0 && hotkey.All(_pressedKeys.Contains);
    }

    private static bool IsHotkeyPart(string keyName, IReadOnlyCollection<string> hotkey)
    {
        return hotkey.Contains(keyName, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ParseHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return [];
        }

        return hotkey
            .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeKeyName)
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeKeyName(string keyName)
    {
        return keyName.Trim().ToUpperInvariant() switch
        {
            "CONTROL" or "CTRL" or "LCONTROL" or "RCONTROL" => "Ctrl",
            "SHIFT" or "LSHIFT" or "RSHIFT" => "Shift",
            "ALT" or "MENU" or "LMENU" or "RMENU" => "Alt",
            "WIN" or "LWIN" or "RWIN" => "Win",
            var key when key.Length == 1 => key,
            var key => key
        };
    }

    private static string? NormalizeVirtualKey(uint virtualKey)
    {
        return virtualKey switch
        {
            0xA0 or 0xA1 => "Shift",
            0xA2 or 0xA3 => "Ctrl",
            0xA4 or 0xA5 => "Alt",
            0x5B or 0x5C => "Win",
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
            0x20 => "Space",
            0x1B => "Escape",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "ArrowLeft",
            0x26 => "ArrowUp",
            0x27 => "ArrowRight",
            0x28 => "ArrowDown",
            0xDC => "\\",
            0xBF => "/",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBD => "-",
            0xBB => "=",
            _ => null
        };
    }

    private static string? MouseMessageToInputName(int message, IntPtr lParam)
    {
        return message switch
        {
            WmLButtonDown or WmLButtonUp => "MouseLeft",
            WmRButtonDown or WmRButtonUp => "MouseRight",
            WmMButtonDown or WmMButtonUp => "MouseMiddle",
            WmXButtonDown or WmXButtonUp => XButtonToInputName(lParam),
            _ => null
        };
    }

    private static string XButtonToInputName(IntPtr lParam)
    {
        MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        int xButton = (int)((data.mouseData >> 16) & 0xffff);
        return xButton == XButton1 ? "MouseX1" : xButton == XButton2 ? "MouseX2" : "MouseX";
    }

    private static IntPtr SetHook(LowLevelProc proc, int hookId)
    {
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        IntPtr hook = SetWindowsHookEx(hookId, proc, GetModuleHandle(module?.ModuleName), 0);

        if (hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
        }

        return hook;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }
}
