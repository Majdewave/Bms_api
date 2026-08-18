namespace Clienta.Api.Entities;

public static class ImagingStudyStorageStatuses
{
    public const string Local = "Local";
    public const string LocalAndS3 = "LocalAndS3";
    public const string UploadFailed = "UploadFailed";

    public static readonly string[] All =
    {
        Local,
        LocalAndS3,
        UploadFailed
    };
}
