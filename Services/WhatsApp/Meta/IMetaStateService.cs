namespace Clienta.Api.Services.WhatsApp.Meta;

public interface IMetaStateService
{
    string Create(Guid tenantId, Guid userId, string nonce);
    bool TryValidate(string? protectedState, out MetaStatePayload payload, out string error);
}
