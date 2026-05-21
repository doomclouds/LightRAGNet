using LightRAGNet.Core.Utils;

namespace LightRAGNet.Services.GraphCuration;

public static class GraphCurationVectorIds
{
    public static string Entity(string entityName) =>
        HashUtils.ComputeMd5Hash(entityName, "ent");

    public static string Relation(string sourceEntity, string targetEntity) =>
        HashUtils.ComputeMd5Hash(sourceEntity + targetEntity, "rel");

    public static IEnumerable<string> RelationIds(string sourceEntity, string targetEntity)
    {
        var ordered = NormalizePair(sourceEntity, targetEntity);
        yield return Relation(ordered.Source, ordered.Target);

        var legacy = Relation(ordered.Target, ordered.Source);
        if (!string.Equals(legacy, Relation(ordered.Source, ordered.Target), StringComparison.Ordinal))
        {
            yield return legacy;
        }
    }

    public static (string Source, string Target) NormalizePair(string sourceEntity, string targetEntity) =>
        string.Compare(sourceEntity, targetEntity, StringComparison.Ordinal) <= 0
            ? (sourceEntity, targetEntity)
            : (targetEntity, sourceEntity);
}
