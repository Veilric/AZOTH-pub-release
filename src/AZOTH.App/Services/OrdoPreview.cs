using AZOTH.Core.Localization;
using AZOTH.Core.Ordo;

namespace AZOTH.App.Services;

/// <summary>Read-only presentation snapshot. It contains no executable plan, file access or movement commands.</summary>
public sealed class OrdoPreview
{
    public IReadOnlyList<OrdoPreviewMove> Moves { get; }
    public IReadOnlyList<OrdoPreviewIssue> Issues { get; }
    public int MovingCount => Moves.Count;
    // Planner issues can also describe an unreadable directory. Do not claim to count its unseen contents.
    public int StayingCount => Issues.Select(i => i.Path).Distinct(StringComparer.Ordinal).Count();
    public long TotalBytes { get; }
    public string TotalSize => $"{TotalBytes:N0} B";
    public bool HasMoves => Moves.Count > 0;
    public bool HasIssues => Issues.Count > 0;
    public bool HasOnlyIssues => !HasMoves && HasIssues;
    public bool IsEmpty => !HasMoves && !HasIssues;

    private OrdoPreview(IEnumerable<OrdoPreviewMove> moves, IEnumerable<OrdoPreviewIssue> issues)
    {
        Moves = Array.AsReadOnly(moves.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        foreach (var move in Moves)
            TotalBytes = move.SizeBytes > long.MaxValue - TotalBytes ? long.MaxValue : TotalBytes + move.SizeBytes;
    }

    public static OrdoPreview FromPlan(OrdoPlan plan, OrdoConfiguration configuration, ILocalizer loc)
    {
        var rules = configuration.Rules.Select((rule, index) => (rule.Id, Label:
            loc.Format("ordo.preview_rule", index + 1, loc["ordo.type." + rule.Type])))
            .ToDictionary(r => r.Id, r => r.Label, StringComparer.Ordinal);
        // Flatten the display groups so one virtualized list handles even a large group. The underlying plan is unchanged.
        var rows = plan.Moves.GroupBy(m => (m.RuleId, Directory: Path.GetDirectoryName(m.Destination) ?? ""))
            .SelectMany(group => group.Select((move, index) => new OrdoPreviewMove(
                Path.GetFileName(move.Source.Snapshot.Path), move.Source.Snapshot.Path, move.Destination,
                move.RuleId, rules.GetValueOrDefault(move.RuleId) ?? move.RuleId,
                move.Source.Snapshot.Length, group.Key.Directory, index == 0)));
        return new(rows, plan.Issues.Select(issue => new OrdoPreviewIssue(issue.Path, issue.Detail,
            issue.Retryable, loc[issue.Retryable ? "ordo.preview_attention" : "ordo.preview_skipped"])));
    }
}

public sealed record OrdoPreviewMove(string FileName, string SourcePath, string DestinationPath,
    string RuleId, string MatchedRule, long SizeBytes, string GroupDestination, bool StartsGroup)
{
    public string SizeText => $"{SizeBytes:N0} B";
}

public sealed record OrdoPreviewIssue(string Path, string Explanation, bool NeedsAttention, string Status);
