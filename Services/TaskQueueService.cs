using System.Threading.Channels;

public class TaskQueueService
{
    private readonly Channel<Func<CancellationToken, Task>> _queue =
        Channel.CreateBounded<Func<CancellationToken, Task>>(1000);
    public async Task EnqueueTaskAsync(Func<CancellationToken, Task> task)
    {
        await _queue.Writer.WriteAsync(task);
    }

    public async Task StartProcessingAsync(CancellationToken token)
    {
        await foreach (var task in _queue.Reader.ReadAllAsync(token))
        {
            try
            {
                await task(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QUEUE ERROR] {ex.Message}");
            }
        }
    }
}