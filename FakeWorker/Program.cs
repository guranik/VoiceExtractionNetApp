using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Networking;
using Common.Models;

var ct = CancellationToken.None;
var tasks = new List<Task>();

const int workerCount = 5000; // сколько воркеров имитировать

for (int i = 0; i < workerCount; i++)
{
    tasks.Add(RunFakeWorker(i, ct));
}

await Task.WhenAll(tasks);

static async Task RunFakeWorker(int id, CancellationToken ct)
{
    using var client = new TcpClient();
    await client.ConnectAsync("127.0.0.1", 5000);
    var reader = new TcpMessageReader(client);
    var writer = new TcpMessageWriter(client);

    // Регистрация
    await writer.SendAsync(new WorkerReadyMessage
    {
        ExtractThreads = 2,
        TranscribeThreads = 2
    }, ct);

    _ = await reader.ReadAsync(ct);

    _ = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            await writer.SendAsync(new HeartBeatMessage(), ct);
            await Task.Delay(2000, ct);
        }
    }, ct);

    while (!ct.IsCancellationRequested)
    {
        var msg = await reader.ReadAsync(ct);
        if (msg is TaskMessage task)
        {
            Console.WriteLine($"[Worker {id}] Получил задачу: {task.TaskType} {task.SourceFileName}");

            var fakeResult = new TaskMessage
            {
                TaskType = task.TaskType,
                SourceFileName = task.SourceFileName,
                Files = { new FilePayload { FileName = task.SourceFileName, Base64Content = "UkE=" } }
            };

            await writer.SendAsync(fakeResult, ct);
            Console.WriteLine($"[Worker {id}] Отправил результат");
        }
    }
}