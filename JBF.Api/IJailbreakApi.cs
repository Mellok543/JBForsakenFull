namespace JBF.Api;

public interface IJailbreakApi
{
    JailbreakRoundState RoundState { get; }

    bool IsRoundActive { get; }
}
