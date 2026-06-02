namespace ZeusAuto.Engine.Core.Interfaces;

public interface IMouseSimulator
{
    void Click(string buttonName);

    void ClickLeft();

    void ClickRight();

    void ClickMiddle();

    void ClickX1();

    void ClickX2();

    void PressLeft();

    void ReleaseLeft();

    void PressRight();

    void ReleaseRight();

    void PressMiddle();

    void ReleaseMiddle();

    void PressX1();

    void ReleaseX1();

    void PressX2();

    void ReleaseX2();
}
