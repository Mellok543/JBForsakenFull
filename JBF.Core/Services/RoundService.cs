using JBF.Api;

namespace JBF.Core.Services;

internal sealed class RoundService
{
    public JailbreakRoundState State { get; private set; } = JailbreakRoundState.Waiting;

    public void RoundStart()
    {
        State = JailbreakRoundState.Active;
    }

    public void RoundEnd()
    {
        State = JailbreakRoundState.Ended;
    }
}
