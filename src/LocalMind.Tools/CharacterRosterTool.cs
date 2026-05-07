using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LocalMind.Tools;

public sealed class CharacterRosterTool(
    ILogger<CharacterRosterTool> logger,
    NpgsqlDataSource db) : ITool
{
    private sealed record CharacterRow(
        int Id,
        string Codename,
        string? RealName,
        string? Status,
        string[]? PowerClass,
        string? FirstIssue,
        string? Notes);

    public string Name => "query_character_roster";

    public string Description => """
        Queries the structured X-Men character registry in PostgreSQL.
        Use this for precise, filterable facts: team memberships by year, 
        power classifications, character status, and known relationships.
        Prefer this over the knowledge base search when the question is 
        structured ("list all telepaths", "who are Jean Grey's siblings") 
        rather than narrative ("what happened to Jean Grey at Alkali Lake").
        """;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["codename"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional name filter",
            },
            ["status"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("active", "deceased", "villain", "depowered"),
            },
            ["power_class"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "List of power classes (e.g., telepath, telekinetic)",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["uniqueItems"] = true,
                ["minItems"] = 1,
            },
            ["team"] = new JsonObject
            {
                ["type"] = "string",
            },
            ["active_in_year"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 0,
            },
            ["relation_to"] = new JsonObject
            {
                ["type"] = "string",
            },
            ["relation_type"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("sibling", "nemesis", "mentor", "lover", "parent", "child", "clone", "spouse")
            },
        },
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var codename = GetOptionalString(input, "codename");
            var status = GetOptionalString(input, "status");
            var powerClass = GetOptionalStringArray(input, "power_class");
            var team = GetOptionalString(input, "team");
            var activeInYear = GetOptionalInt(input, "active_in_year");
            var relationTo = GetOptionalString(input, "relation_to");
            var relationType = GetOptionalString(input, "relation_type");

            var where = new List<string>();
            var cmdParams = new List<NpgsqlParameter>();

            string AddParam(string baseName, object? value)
            {
                var name = $"{baseName}{cmdParams.Count}";
                cmdParams.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
                return "@" + name;
            }

            // Name filter (codename or real name)
            if (!string.IsNullOrWhiteSpace(codename))
            {
                var p1 = AddParam("p", $"%{codename.Trim()}%");
                var p2 = AddParam("p", $"%{codename.Trim()}%");
                where.Add($"(c.codename ILIKE {p1} OR c.real_name ILIKE {p2})");
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var p = AddParam("p", status.Trim());
                where.Add($"c.status = {p}");
            }

            // Require the character to have ALL listed power classes.
            if (powerClass is { Length: > 0 })
            {
                var p = AddParam("p", powerClass);
                where.Add($"c.power_class @> {p}");
            }

            // Team membership / active year constraints via EXISTS.
            if (!string.IsNullOrWhiteSpace(team) || activeInYear is not null)
            {
                var existsParts = new List<string> { "tm.character_id = c.id" };

                if (!string.IsNullOrWhiteSpace(team))
                {
                    var p = AddParam("p", team.Trim());
                    existsParts.Add($"tm.team = {p}");
                }

                if (activeInYear is not null)
                {
                    var p = AddParam("p", activeInYear.Value);
                    existsParts.Add($"tm.joined_year <= {p}");
                    existsParts.Add($"(tm.left_year IS NULL OR tm.left_year >= {p})");
                }

                where.Add($"""
                    EXISTS (
                        SELECT 1
                        FROM team_memberships tm
                        WHERE {string.Join(" AND ", existsParts)}
                    )
                    """);
            }

            // Relationships via EXISTS. Requires both relation_to and relation_type to avoid vague scans.
            if (!string.IsNullOrWhiteSpace(relationTo) || !string.IsNullOrWhiteSpace(relationType))
            {
                if (string.IsNullOrWhiteSpace(relationTo) || string.IsNullOrWhiteSpace(relationType))
                    return ToolResult.Fail(Name, "Provide both 'relation_to' and 'relation_type' together.", sw.Elapsed);

                var toP = AddParam("p", relationTo.Trim());
                var typeP = AddParam("p", relationType.Trim());

                // Match either direction in relationships (a->b or b->a).
                where.Add($"""
                    EXISTS (
                        SELECT 1
                        FROM relationships r
                        JOIN characters other
                          ON other.id = CASE
                            WHEN r.character_a = c.id THEN r.character_b
                            ELSE r.character_a
                          END
                        WHERE r.relation = {typeP}
                          AND (r.character_a = c.id OR r.character_b = c.id)
                          AND (other.codename ILIKE {toP} OR other.real_name ILIKE {toP})
                    )
                    """);
            }

            if (where.Count == 0)
            {
                return ToolResult.Fail(Name, "At least one filter is required.", sw.Elapsed);
            }

            var sql = $"""
                SELECT
                    c.id,
                    c.codename,
                    c.real_name,
                    c.status,
                    c.power_class,
                    c.first_issue,
                    c.notes
                FROM characters c
                WHERE {string.Join(" AND ", where)}
                ORDER BY c.codename
                LIMIT 200;
                """;

            await using var conn = await db.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in cmdParams)
                cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<CharacterRow>();

            var ordId         = reader.GetOrdinal("id");
            var ordCodename   = reader.GetOrdinal("codename");
            var ordRealName   = reader.GetOrdinal("real_name");
            var ordStatus     = reader.GetOrdinal("status");
            var ordPowerClass = reader.GetOrdinal("power_class");
            var ordFirstIssue = reader.GetOrdinal("first_issue");
            var ordNotes      = reader.GetOrdinal("notes");
            
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new CharacterRow(
                    Id:         reader.GetInt32(ordId),
                    Codename:   reader.GetString(ordCodename),
                    RealName:   reader.IsDBNull(ordRealName)    ? null : reader.GetString(ordRealName),
                    Status:     reader.IsDBNull(ordStatus)      ? null : reader.GetString(ordStatus),
                    PowerClass: reader.IsDBNull(ordPowerClass)  ? null : reader.GetFieldValue<string[]>(ordPowerClass),
                    FirstIssue: reader.IsDBNull(ordFirstIssue)  ? null : reader.GetString(ordFirstIssue),
                    Notes:      reader.IsDBNull(ordNotes)       ? null : reader.GetString(ordNotes)
                ));
            }

            var results = new JsonArray();
            foreach (var r in rows)
            {
                var obj = new JsonObject
                {
                    ["id"] = r.Id,
                    ["codename"] = r.Codename,
                    ["real_name"] = r.RealName,
                    ["status"] = r.Status,
                    ["first_issue"] = r.FirstIssue,
                    ["notes"] = r.Notes,
                };

                if (r.PowerClass is { Length: > 0 })
                {
                    var arr = new JsonArray();
                    foreach (var s in r.PowerClass)
                        arr.Add(s);
                    obj["power_class"] = arr;
                }
                else
                {
                    obj["power_class"] = null;
                }
                results.Add(obj);
            }

            logger.LogInformation(
                "Character roster: {Count} result(s) [codename={C} status={S} team={T} year={Y}]",
                rows.Count, codename, status, team, activeInYear);

            return ToolResult.Ok(Name, results.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            }), sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Fail(Name, "Tool execution was cancelled.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Character roster query failed.");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
        finally
        {
            sw.Stop();
        }
    }

    static string? GetOptionalString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            return null;
        return node.GetValue<string?>();
    }

    static int? GetOptionalInt(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            return null;
        return node.GetValue<int?>();
    }

    static string[]? GetOptionalStringArray(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            return null;

        if (node is not JsonArray arr)
        {
            return [];
        }

        var list = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            if (item is null)
                continue;
            list.Add(item.GetValue<string>());
        }

        return list.Count == 0 ? null : list.ToArray();
    }
}