// Messages/TaskMessage.cs
using Common.Models;

namespace Common.Messages;

public sealed class TaskMessage : MessageBase
{
    public TaskType TaskType { get; init; }

    /// <summary>
    /// Исходное имя wav-файла
    /// </summary>
    public string SourceFileName { get; init; } = string.Empty;

    /// <summary>
    /// Список файлов (wav или txt)
    /// </summary>
    public List<FilePayload> Files { get; init; } = new();

    public TaskMessage()
    {
        Type = MessageType.ExtractTask;
    }
}
