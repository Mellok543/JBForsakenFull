namespace JBF.Api;

public interface ISpecialDaysApi
{
    bool IsActive { get; }

    bool HasPending { get; }

    string? ActiveDayName { get; }

    string? PendingDayName { get; }

    IDisposable RegisterDay(ISpecialDay day);

    bool TryStop();
}
