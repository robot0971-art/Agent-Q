using System.Text;

namespace AgentQ.Desktop.Services;

public static class DesktopWorkspaceAnalysisReportBuilder
{
    public static string Build(WorkspaceAnalysis analysis)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Workspace Analysis Report");
        builder.AppendLine();
        builder.AppendLine($"Workspace: {analysis.WorkspaceRoot}");
        builder.AppendLine($"Updated: {analysis.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Type: {analysis.ProjectType}");
        builder.AppendLine($"- Framework: {analysis.Framework}");
        builder.AppendLine($"- Git: {analysis.GitBranch}");
        builder.AppendLine($"- Size: {analysis.FileCount:0} files / {analysis.DirectoryCount:0} folders");
        AppendSection(builder, "Verification Commands", analysis.VerificationCommands);
        AppendSection(builder, "Project Map", analysis.ProjectMap);
        AppendSection(builder, "Key Files", analysis.KeyFiles);
        AppendSection(builder, "Key Symbols", analysis.KeySymbols);
        AppendSection(builder, "Key Dependencies", analysis.KeyDependencies);
        AppendSection(builder, "Evidence And Hints", analysis.Hints);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        foreach (var item in items)
        {
            builder.AppendLine($"- {item}");
        }
    }
}
