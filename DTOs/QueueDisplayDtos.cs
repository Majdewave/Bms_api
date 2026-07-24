using Clienta.Api.Entities;

namespace Clienta.Api.DTOs;

public record QueueDisplayPatientDto(
    Guid AppointmentId,
    int? QueueNumber,
    string DisplayName,
    string Status
);

public record QueueDisplayDto(
    string BusinessName,
    string? LogoUrl,
    QueueDisplayTheme Theme,
    QueueDisplayPrivacyMode PrivacyMode,
    string? AdvertisementImageUrl,
    int WaitingCount,
    QueueDisplayPatientDto? Current,
    QueueDisplayPatientDto? Next,
    DateTime LastUpdatedUtc,
    string Version,
    DateTime GeneratedAtUtc
);

public record QueueDisplaySettingsDto(
    string PublicToken,
    QueueDisplayPrivacyMode PrivacyMode,
    QueueDisplayTheme Theme,
    string? LogoOverrideUrl,
    string? AdvertisementImageUrl
);

public record QueueDisplayAccessLinkDto(
    string PublicToken
);

public record UpdateQueueDisplaySettingsRequest(
    QueueDisplayPrivacyMode PrivacyMode,
    QueueDisplayTheme Theme,
    string? LogoOverrideUrl,
    string? AdvertisementImageUrl
);
