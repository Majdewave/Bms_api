namespace Clienta.Api.Services;

public interface IImagingAccessionNumberGenerator
{
    string Generate(string modality, DateTime utcDate);
}
