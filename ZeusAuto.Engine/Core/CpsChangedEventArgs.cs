namespace ZeusAuto.Engine.Core;

/// <summary>
/// Argumentos do evento <see cref="MacroEngine.CpsChanged"/>.
/// Fornece o novo CPS efetivo e o IntervalMs correspondente para persistência.
/// </summary>
public sealed class CpsChangedEventArgs : EventArgs
{
    /// <param name="newCps">Novo CPS calculado após o ajuste (1–50).</param>
    /// <param name="newIntervalMs">IntervalMs equivalente: <c>round(1000 / newCps)</c>.</param>
    public CpsChangedEventArgs(double newCps, int newIntervalMs)
    {
        NewCps        = newCps;
        NewIntervalMs = newIntervalMs;
    }

    /// <summary>Novo CPS efetivo após o ajuste. Sempre dentro de 1–50.</summary>
    public double NewCps { get; }

    /// <summary>IntervalMs equivalente ao novo CPS. Sempre >= 1.</summary>
    public int NewIntervalMs { get; }
}
