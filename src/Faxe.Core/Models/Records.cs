namespace Faxe.Core.Models;

public sealed class TaskRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Dfs { get; set; } = string.Empty;
    public GraphDefinition? Definition { get; set; }
    public long Changed { get; set; }
    public long? LastStart { get; set; }
    public long? LastStop { get; set; }
    public bool Permanent { get; set; }
    public bool IsRunning { get; set; }
    public Dictionary<string, object?> TemplateVars { get; set; } = new(StringComparer.Ordinal);
    public string Template { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string? Group { get; set; }
    public bool GroupLeader { get; set; }
}

public sealed class TemplateRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Dfs { get; set; } = string.Empty;
    public GraphDefinition? Definition { get; set; }
    public long Changed { get; set; }
    public List<string> Vars { get; set; } = new();
}

public sealed class UserRecord
{
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "admin";
}

public sealed class GraphDefinition
{
    public List<GraphNodeDef> Nodes { get; set; } = new();
    public List<GraphEdgeDef> Edges { get; set; } = new();
}

public sealed class GraphNodeDef
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?> Options { get; set; } = new(StringComparer.Ordinal);
}

public sealed class GraphEdgeDef
{
    public string Source { get; set; } = string.Empty;
    public int OutPort { get; set; } = 1;
    public string Dest { get; set; } = string.Empty;
    public int InPort { get; set; } = 1;
}
