using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace JBF.TeamBalance;

public sealed class TeamBalanceConfig : BasePluginConfig
{
    [JsonPropertyName("QuestionNumber")]
    public int QuestionNumber { get; set; } = 5;

    [JsonPropertyName("CtBan")]
    public int CtBan { get; set; } = 30;

    [JsonPropertyName("TerroristsPerGuard")]
    public int TerroristsPerGuard { get; set; } = 3;

    [JsonPropertyName("Connection")]
    public DatabaseConnectionConfig Connection { get; set; } = new();

    [JsonPropertyName("Questions")]
    public List<CtQuestion> Questions { get; set; } = [];
}

public sealed class DatabaseConnectionConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public string Database { get; set; } = "jbforsaken";
    public string User { get; set; } = "jbf";
    public string Password { get; set; } = "change_me";
    public int Port { get; set; } = 3306;
}

public sealed class CtQuestion
{
    [JsonPropertyName("Question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("Answers")]
    public List<string> Answers { get; set; } = [];

    [JsonPropertyName("CorrectAnswer")]
    public string CorrectAnswer { get; set; } = string.Empty;
}
