namespace LocalMind.Application.Agents;

public static class KnownAgents
{
    public const string Knowledge = "knowledge";

    public static bool Exists(string name) =>
        string.Equals(name, Knowledge, StringComparison.OrdinalIgnoreCase);
}
