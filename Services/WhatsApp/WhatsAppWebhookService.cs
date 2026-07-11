namespace Clienta.Api.Services.WhatsApp;

public class WhatsAppWebhookService
{
    private readonly ILogger<WhatsAppWebhookService> _logger;

    public WhatsAppWebhookService(ILogger<WhatsAppWebhookService> logger)
    {
        _logger = logger;
    }

    public Task HandleWebhookAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WhatsApp webhook handler invoked (stage 1 skeleton)");
        return Task.CompletedTask;
    }
}
