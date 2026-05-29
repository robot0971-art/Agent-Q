using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentQ.Desktop.Services;

public sealed class TaskContextSelector
{
    public async Task<string> BuildTaskContextAsync(
        TaskStep step,
        WorkspaceAnalysis analysis,
        WorkspaceSymbolIndex symbolIndex,
        string workspaceRoot,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Task Context ===");
        sb.AppendLine($"Goal: {step.Description}");
        sb.AppendLine($"Task Type: {step.Kind}");
        if (step.RelevantFiles.Count > 0)
        {
            sb.AppendLine("Relevant Files:");
            foreach (var file in step.RelevantFiles)
            {
                var fullPath = Path.IsPathRooted(file) ? file : Path.Combine(workspaceRoot, file);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(fullPath, ct);
                        // If file is very large, truncate it to save token space
                        if (content.Length > 15000)
                        {
                            content = content.Substring(0, 10000) + "\n\n... [TRUNCATED] ...\n\n" + content.Substring(content.Length - 3000);
                        }
                        sb.AppendLine($"\n--- File: {file} ---");
                        sb.AppendLine(content);
                        sb.AppendLine("--------------------");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"[Error reading {file}: {ex.Message}]");
                    }
                }
                else
                {
                    sb.AppendLine($"[File not found: {file}]");
                }
            }
        }

        // Add matching symbols context from the symbolIndex
        var stepFileNames = step.RelevantFiles.Select(Path.GetFileName).ToList();
        var relevantSymbols = symbolIndex.Symbols
            .Where(sym => stepFileNames.Contains(Path.GetFileName(sym.RelativePath)))
            .Take(30)
            .ToList();

        if (relevantSymbols.Count > 0)
        {
            sb.AppendLine("\nRelevant Code Symbols Defined in these files:");
            foreach (var sym in relevantSymbols)
            {
                sb.AppendLine($"- {sym.DisplayName}");
            }
        }

        return sb.ToString();
    }
}
