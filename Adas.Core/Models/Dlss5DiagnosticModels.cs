namespace RenoDXCommander.Models;

internal sealed record Dlss5DiagnosticReport(
    bool HasProblems,
    bool IsWorking,
    string Summary,
    IReadOnlyList<string> Findings)
{
    public string ToDisplayText()
        => Findings.Count == 0
            ? Summary
            : Summary + Environment.NewLine + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", Findings);
}
