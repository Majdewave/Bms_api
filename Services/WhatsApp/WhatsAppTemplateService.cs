namespace Clienta.Api.Services.WhatsApp;

public class WhatsAppTemplateService
{
    private readonly ILogger<WhatsAppTemplateService> _logger;

    public WhatsAppTemplateService(ILogger<WhatsAppTemplateService> logger)
    {
        _logger = logger;
    }

    public Task SyncTemplatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WhatsApp template sync invoked (stage 1 skeleton)");
        return Task.CompletedTask;
    }
}
