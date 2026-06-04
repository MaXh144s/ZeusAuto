namespace ZeusAuto.Engine.Core;

/// <summary>
/// Argumentos do evento <see cref="MacroEngine.CpsChanged"/>.
/// Fornece o novo CPS efetivo, o IntervalMs correspondente, o delta do ajuste
/// e o step configurado — usados pelo toast flutuante de CPS.
/// </summary>
public sealed class CpsChangedEventArgs : EventArgs
{
    /// <param name="newCps">Novo CPS calculado após o ajuste (1–50).</param>
    /// <param name="newIntervalMs">IntervalMs equivalente: <c>round(1000 / newCps)</c>.</param>
    /// <param name="deltaCps">Delta aplicado neste ajuste (+step ou -step).</param>
    /// <param name="step">Step configurado no MacroConfig (padrão 0.5).</param>
    public CpsChangedEventArgs(double newCps, int newIntervalMs, double deltaCps = 0, double step = 0.5)
    {
        NewCps        = newCps;
        NewIntervalMs = newIntervalMs;
        DeltaCps      = deltaCps;
        Step          = step;
    }

    /// <summary>Novo CPS efetivo após o ajuste. Sempre dentro de 1–50.</summary>
    public double NewCps { get; }

    /// <summary>IntervalMs equivalente ao novo CPS. Sempre >= 1.</summary>
    public int NewIntervalMs { get; }

    /// <summary>Delta aplicado neste ajuste: +step (incremento) ou -step (decremento).</summary>
    public double DeltaCps { get; }

    /// <summary>Step configurado (padrão 0.5 CPS por pressão).</summary>
    public double Step { get; }
}
