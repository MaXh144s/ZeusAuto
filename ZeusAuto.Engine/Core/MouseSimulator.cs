using System.ComponentModel;
using System.Runtime.InteropServices;
using ZeusAuto.Engine.Core.Interfaces;

namespace ZeusAuto.Engine.Core;

public sealed class MouseSimulator : IMouseSimulator
{
    private const uint InputMouse = 0;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFMiddleDown = 0x0020;
    private const uint MouseEventFMiddleUp = 0x0040;
    private const uint MouseEventFXDown = 0x0080;
    private const uint MouseEventFXUp = 0x0100;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;

    public void Click(string buttonName)
    {
        switch (NormalizeButton(buttonName))
        {
            case "MouseLeft":
                ClickLeft();
                break;
            case "MouseRight":
                ClickRight();
                break;
            case "MouseMiddle":
                ClickMiddle();
                break;
            case "MouseX1":
                ClickX1();
                break;
            case "MouseX2":
                ClickX2();
                break;
            default:
                throw new NotSupportedException($"Unsupported click button: {buttonName}");
        }
    }

    public void ClickLeft() => Click(MouseEventFLeftDown, MouseEventFLeftUp);

    public void ClickRight() => Click(MouseEventFRightDown, MouseEventFRightUp);

    public void ClickMiddle() => Click(MouseEventFMiddleDown, MouseEventFMiddleUp);

    public void ClickX1() => Click(MouseEventFXDown, MouseEventFXUp, XButton1);

    public void ClickX2() => Click(MouseEventFXDown, MouseEventFXUp, XButton2);

    public void PressLeft() => SendMouse(MouseEventFLeftDown);

    public void ReleaseLeft() => SendMouse(MouseEventFLeftUp);

    public void PressRight() => SendMouse(MouseEventFRightDown);

    public void ReleaseRight() => SendMouse(MouseEventFRightUp);

    public void PressMiddle() => SendMouse(MouseEventFMiddleDown);

    public void ReleaseMiddle() => SendMouse(MouseEventFMiddleUp);

    public void PressX1() => SendMouse(MouseEventFXDown, XButton1);

    public void ReleaseX1() => SendMouse(MouseEventFXUp, XButton1);

    public void PressX2() => SendMouse(MouseEventFXDown, XButton2);

    public void ReleaseX2() => SendMouse(MouseEventFXUp, XButton2);

    private static void Click(uint downFlag, uint upFlag, uint mouseData = 0)
    {
        SendMouse(downFlag, mouseData);
        SendMouse(upFlag, mouseData);
    }

    private static void SendMouse(uint flags, uint mouseData = 0)
    {
        INPUT[] inputs =
        [
            new()
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = flags,
                        mouseData = mouseData
                    }
                }
            }
        ];

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed.");
        }
    }

    private static string NormalizeButton(string? buttonName)
    {
        return buttonName?.Trim().ToUpperInvariant() switch
        {
            "MOUSELEFT" or "LEFT" or "TECLA ESQUERDA" => "MouseLeft",
            "MOUSERIGHT" or "RIGHT" or "TECLA DIREITA" => "MouseRight",
            "MOUSEMIDDLE" or "MIDDLE" or "TECLA SCROLL" => "MouseMiddle",
            "MOUSEX1" or "X1" or "XBUTTON1" or "TECLA XBUTTON4" => "MouseX1",
            "MOUSEX2" or "X2" or "XBUTTON2" or "TECLA XBUTTON5" => "MouseX2",
            _ => buttonName ?? string.Empty
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
