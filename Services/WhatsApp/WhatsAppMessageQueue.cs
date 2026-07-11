using System.Collections.Concurrent;

namespace Clienta.Api.Services.WhatsApp;

public class WhatsAppMessageQueue
{
    private readonly ConcurrentQueue<Guid> _queue = new();

    public void Enqueue(Guid messageId)
    {
        _queue.Enqueue(messageId);
    }

    public bool TryDequeue(out Guid messageId)
    {
        return _queue.TryDequeue(out messageId);
    }
}
