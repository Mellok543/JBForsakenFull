namespace JBF.Api;

public interface IWeekCycleApi
{
    JailbreakWeekDay CurrentDay { get; }
    string CurrentDayName { get; }
    int RoundIndex { get; }
}
