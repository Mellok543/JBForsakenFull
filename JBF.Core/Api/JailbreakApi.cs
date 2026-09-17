using JBF.Core.Services;

namespace JBF.Core.Api;

internal sealed class JailbreakApi : JBF.Api.IJailbreakApi
{
    private readonly RoundService _roundService;

    public JailbreakApi(RoundService roundService)
    {
        _roundService = roundService;
    }

    public JBF.Api.JailbreakRoundState RoundState => _roundService.State;

    public bool IsRoundActive => _roundService.State == JBF.Api.JailbreakRoundState.Active;
}
