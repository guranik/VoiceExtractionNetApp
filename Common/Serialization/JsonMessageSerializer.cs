using System.Text;
using System.Text.Json;
using Common.Messages;

namespace Common.Serialization;

public static class JsonMessageSerializer
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] Serialize(MessageBase message)
    {
        var json = JsonSerializer.Serialize(message, message.GetType(), Options);
        return Utf8NoBom.GetBytes(json);
    }

    public static MessageBase Deserialize(ReadOnlySpan<byte> data)
    {
        var json = Encoding.UTF8.GetString(data);
        using var doc = JsonDocument.Parse(json);

        var type = (MessageType)doc.RootElement.GetProperty("type").GetInt32();

        return type switch
        {
            MessageType.WorkerHello =>
                JsonSerializer.Deserialize<WorkerHelloMessage>(json, Options)!,

            MessageType.HeartBeat =>
                JsonSerializer.Deserialize<HeartBeatMessage>(json, Options)!,

            MessageType.Ack =>
                JsonSerializer.Deserialize<AckMessage>(json, Options)!,

            MessageType.ExtractTask or MessageType.TranscribeTask =>
                JsonSerializer.Deserialize<TaskMessage>(json, Options)!,

            MessageType.ClientInput =>
                JsonSerializer.Deserialize<ClientInputMessage>(json, Options)!,

            _ => throw new InvalidOperationException(
                $"Unknown message type {type}")
        };
    }
}
