using System.Text;
using AgentQ.Core.Models;

namespace AgentQ.Core.Providers;

/// <summary>
/// 스트리밍 중 분할된 tool call delta를 누적하고 완성된 청크로 변환한다.
/// </summary>
public sealed class ToolCallDeltaBuffer
{
    private readonly Dictionary<int, Entry> _entries = new();

    /// <summary>
    /// 지정된 인덱스의 tool id를 갱신한다.
    /// </summary>
    public void SetToolId(int index, string? toolId)
    {
        if (!string.IsNullOrEmpty(toolId))
        {
            GetOrCreate(index).ToolId = toolId;
        }
    }

    /// <summary>
    /// 지정된 인덱스의 tool name을 갱신한다.
    /// </summary>
    public void SetToolName(int index, string? toolName)
    {
        if (!string.IsNullOrEmpty(toolName))
        {
            GetOrCreate(index).ToolName = toolName;
        }
    }

    /// <summary>
    /// 지정된 인덱스의 인수 조각을 누적한다.
    /// </summary>
    public void AppendArguments(int index, string? partialArguments)
    {
        if (!string.IsNullOrEmpty(partialArguments))
        {
            GetOrCreate(index).Arguments.Append(partialArguments);
        }
    }

    /// <summary>
    /// 현재까지 누적된 상태를 부분 청크로 반환한다.
    /// </summary>
    public ToolUseChunk? BuildPartialChunk(int index, string? partialArguments = null)
    {
        if (!_entries.TryGetValue(index, out var entry) || string.IsNullOrEmpty(entry.ToolId))
        {
            return null;
        }

        return new ToolUseChunk
        {
            ToolId = entry.ToolId,
            ToolName = entry.ToolName ?? "unknown",
            PartialInput = partialArguments ?? entry.Arguments.ToString(),
            IsComplete = false
        };
    }

    /// <summary>
    /// 지정된 인덱스의 누적 결과를 완료 청크로 반환하고 버퍼에서 제거한다.
    /// </summary>
    public ToolUseChunk? Complete(int index)
    {
        if (!_entries.TryGetValue(index, out var entry) || string.IsNullOrEmpty(entry.ToolId))
        {
            _entries.Remove(index);
            return null;
        }

        _entries.Remove(index);
        return new ToolUseChunk
        {
            ToolId = entry.ToolId,
            ToolName = entry.ToolName ?? "unknown",
            PartialInput = entry.Arguments.ToString(),
            IsComplete = true
        };
    }

    /// <summary>
    /// 현재까지 누적된 모든 tool call을 인덱스 순서대로 완료 청크로 반환하고 버퍼를 비운다.
    /// </summary>
    public IReadOnlyList<ToolUseChunk> CompleteAll()
    {
        var result = _entries
            .OrderBy(pair => pair.Key)
            .Select(pair => Complete(pair.Key))
            .Where(chunk => chunk != null)
            .Cast<ToolUseChunk>()
            .ToArray();

        return result;
    }

    private Entry GetOrCreate(int index)
    {
        if (!_entries.TryGetValue(index, out var entry))
        {
            entry = new Entry();
            _entries[index] = entry;
        }

        return entry;
    }

    private sealed class Entry
    {
        public string? ToolId { get; set; }

        public string? ToolName { get; set; }

        public StringBuilder Arguments { get; } = new();
    }
}
