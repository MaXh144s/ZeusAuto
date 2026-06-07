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
    private string[] _pauseHotkey = [];
    private string[] _overlayHotkey = [];
    private string[] _bipHotkey = [];
    private string[] _encerrarHotkey = [];
    private bool _startHotkeyLatched;
    private bool _stopHotkeyLatched;
    private bool _cpsIncrementLatched;
    private bool _cpsDecrementLatched;
    private bool _pauseHotkeyLatched;
    private bool _overlayHotkeyLatched;
    private bool _bipHotkeyLatched;
    private bool _encerrarHotkeyLatched;
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

    public event EventHandler? PauseHotkeyPressed;

    public event EventHandler? OverlayHotkeyPressed;

    public event EventHandler? BipHotkeyPressed;

    public event EventHandler? EncerrarHotkeyPressed;

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
            _pauseHotkey          = ParseHotkey(config.PauseHotkey);
            _overlayHotkey        = ParseHotkey(config.OverlayHotkey);
            _bipHotkey            = ParseHotkey(config.BipToggleHotkey);
            _encerrarHotkey       = ParseHotkey(config.EncerrarHotkey);
            _startHotkeyLatched   = false;
            _stopHotkeyLatched    = false;
            _cpsIncrementLatched  = false;
            _cpsDecrementLatched  = false;
            _pauseHotkeyLatched   = false;
            _overlayHotkeyLatched = false;
            _bipHotkeyLatched     = false;
            _encerrarHotkeyLatched = false;
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
                        _pauseHotkeyLatched      = IsHotkeyPart(keyName, _pauseHotkey)        && _pauseHotkeyLatched      && IsHotkeyPressed(_pauseHotkey);
                        _overlayHotkeyLatched    = IsHotkeyPart(keyName, _overlayHotkey)      && _overlayHotkeyLatched    && IsHotkeyPressed(_overlayHotkey);
                        _bipHotkeyLatched        = IsHotkeyPart(keyName, _bipHotkey)          && _bipHotkeyLatched        && IsHotkeyPressed(_bipHotkey);
                        _encerrarHotkeyLatched   = IsHotkeyPart(keyName, _encerrarHotkey)     && _encerrarHotkeyLatched   && IsHotkeyPressed(_encerrarHotkey);
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
                _pauseHotkeyLatched = false;
                _overlayHotkeyLatched = false;
                _bipHotkeyLatched = false;
                _encerrarHotkeyLatched = false;
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
        EventHandler? pause      = null;
        EventHandler? overlay    = null;
        EventHandler? bip        = null;
        EventHandler? encerrar   = null;

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

            // Atalhos globais — comportamento toggle (dispara uma vez por pressão)
            if (_pauseHotkey.Length > 0 && IsHotkeyPressed(_pauseHotkey))
            {
                if (!_pauseHotkeyLatched)
                {
                    _pauseHotkeyLatched = true;
                    pause = PauseHotkeyPressed;
                }
            }
            else
            {
                _pauseHotkeyLatched = false;
            }

            if (_overlayHotkey.Length > 0 && IsHotkeyPressed(_overlayHotkey))
            {
                if (!_overlayHotkeyLatched)
                {
                    _overlayHotkeyLatched = true;
                    overlay = OverlayHotkeyPressed;
                }
            }
            else
            {
                _overlayHotkeyLatched = false;
            }

            if (_bipHotkey.Length > 0 && IsHotkeyPressed(_bipHotkey))
            {
                if (!_bipHotkeyLatched)
                {
                    _bipHotkeyLatched = true;
                    bip = BipHotkeyPressed;
                }
            }
            else
            {
                _bipHotkeyLatched = false;
            }

            if (_encerrarHotkey.Length > 0 && IsHotkeyPressed(_encerrarHotkey))
            {
                if (!_encerrarHotkeyLatched)
                {
                    _encerrarHotkeyLatched = true;
                    encerrar = EncerrarHotkeyPressed;
                }
            }
            else
            {
                _encerrarHotkeyLatched = false;
            }
        }

        start?.Invoke(this, EventArgs.Empty);
        stop?.Invoke(this, EventArgs.Empty);
        cpsInc?.Invoke(this, EventArgs.Empty);
        cpsDec?.Invoke(this, EventArgs.Empty);
        pause?.Invoke(this, EventArgs.Empty);
        overlay?.Invoke(this, EventArgs.Empty);
        bip?.Invoke(this, EventArgs.Empty);
        encerrar?.Invoke(this, EventArgs.Empty);
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
        // Normaliza nomes de modificadores e garante que caracteres produzidos
        // por Shift (ex: "+" que é Shift+"=") sejam mapeados para o VK físico
        // equivalente ("="). Isso é necessário porque o JS pode gravar o char
        // produzido pelo evento, mas o hook recebe sempre o VK físico.
        return keyName.Trim().ToUpperInvariant() switch
        {
            "CONTROL" or "CTRL" or "LCONTROL" or "RCONTROL" => "Ctrl",
            "SHIFT" or "LSHIFT" or "RSHIFT" => "Shift",
            "ALT" or "MENU" or "LMENU" or "RMENU" => "Alt",
            "WIN" or "LWIN" or "RWIN" => "Win",
            // Caracteres produzidos por Shift → mapeados para o VK físico
            // que o hook de baixo nível sempre reporta:
            "+" => "=",   // Shift+"=" → VK 0xBB, hook retorna "="
            "_" => "-",   // Shift+"-" → VK 0xBD, hook retorna "-"
            "?" => "/",   // Shift+"/" → VK 0xBF, hook retorna "/"
            ":" => ";",   // Shift+";" → VK 0xBA, hook retorna ";"
            "\"" => "'",  // Shift+"'" → VK 0xDE, hook retorna "'"
            "|" => "\\",  // Shift+"\" → VK 0xDC, hook retorna "\"
            "~" => "`",   // Shift+"`" → VK 0xC0, hook retorna "`"
            "{" => "[",   // Shift+"[" → VK 0xDB, hook retorna "["
            "}" => "]",   // Shift+"]" → VK 0xDD, hook retorna "]"
            "<" => ",",   // Shift+"," → VK 0xBC, hook retorna ","
            ">" => ".",   // Shift+"." → VK 0xBE, hook retorna "."
            // Dígitos com Shift (teclado US)
            "!" => "1", "@" => "2", "#" => "3", "$" => "4", "%" => "5",
            "^" => "6", "&" => "7", "*" => "8", "(" => "9", ")" => "0",
            var key when key.Length == 1 => key,
            var key => key
        };
    }

    private static string? NormalizeVirtualKey(uint virtualKey)
    {
        // IMPORTANTE: o hook de baixo nível recebe SEMPRE o VK físico da tecla,
        // independente de modificadores (Shift, Caps Lock, etc.).
        // Exemplos:
        //   Shift pressionado → VK 0xA0/0xA1 → "Shift"
        //   Tecla "=" física (VK 0xBB) → sempre "="  (mesmo com Shift pressionado,
        //   que no teclado US produziria "+")
        //   Tecla "-" física (VK 0xBD) → sempre "-"  (mesmo com Shift → "_")
        //
        // Por isso, NUNCA mapeamos pelo caractere produzido — sempre pelo VK físico.
        // O JS (normalizeKey) também deve gravar o VK físico, não o char com Shift.
        // Isso garante que "Shift+=" funcione como AND: ambas as teclas devem estar
        // pressionadas simultaneamente, sem que "Shift" sozinho dispare o combo.
        return virtualKey switch
        {
            // ── Modificadores ────────────────────────────────────────────────
            0xA0 or 0xA1 => "Shift",
            0xA2 or 0xA3 => "Ctrl",
            0xA4 or 0xA5 => "Alt",
            0x5B or 0x5C => "Win",
            // ── Letras A–Z ───────────────────────────────────────────────────
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            // ── Dígitos 0–9 (fila superior) ──────────────────────────────────
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            // ── Teclas de função F1–F24 ───────────────────────────────────────
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
            // ── Numpad ───────────────────────────────────────────────────────
            >= 0x60 and <= 0x69 => $"Num{virtualKey - 0x60}",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            // ── Controles ────────────────────────────────────────────────────
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
            // ── Pontuação / símbolos (VK FÍSICO — não o char produzido com Shift) ──
            // VK 0xBB = tecla "=" / "+" no US  → sempre "="
            // VK 0xBD = tecla "-" / "_"         → sempre "-"
            // VK 0xBC = tecla "," / "<"          → sempre ","
            // VK 0xBE = tecla "." / ">"          → sempre "."
            // VK 0xBF = tecla "/" / "?"          → sempre "/"
            // VK 0xBA = tecla ";" / ":"          → sempre ";"
            // VK 0xDE = tecla "'" / """          → sempre "'"
            // VK 0xDC = tecla "\" / "|"          → sempre "\"
            // VK 0xC0 = tecla "`" / "~"          → sempre "`"
            // VK 0xDB = tecla "[" / "{"          → sempre "["
            // VK 0xDD = tecla "]" / "}"          → sempre "]"
            0xBB => "=",
            0xBD => "-",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0xBA => ";",
            0xDE => "'",
            0xDC => "\\",
            0xC0 => "`",
            0xDB => "[",
            0xDD => "]",
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
