using System.Collections.Generic;

namespace OpenFga.Language.Validation;

/// <summary>Type → relations already visited on the current path. Copied on every branch, as JS deep-copies it.</summary>
internal sealed class VisitedRelations
{
    private readonly Dictionary<string, HashSet<string>> _visited;

    public VisitedRelations()
        : this([])
    {
    }

    private VisitedRelations(Dictionary<string, HashSet<string>> visited)
    {
        _visited = visited;
    }

    public VisitedRelations Clone()
    {
        var copy = new Dictionary<string, HashSet<string>>(_visited.Count);
        foreach (var entry in _visited)
        {
            copy[entry.Key] = new HashSet<string>(entry.Value);
        }

        return new VisitedRelations(copy);
    }

    public void Add(string type, string relation)
    {
        if (!_visited.TryGetValue(type, out var relations))
        {
            relations = [];
            _visited[type] = relations;
        }

        relations.Add(relation);
    }

    public bool Contains(string type, string relation) => _visited.TryGetValue(type, out var relations) && relations.Contains(relation);
}
