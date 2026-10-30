using Clienta.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clienta.Api.IntegrationTests;

[TestClass]
public sealed class ProjectProposalUploadValidationTests
{
    private static IClientFileService CreateService() => new ClientFileService(null!, null!, null!);

    private static FormFile File(string fileName, long length = 1)
    {
        var stream = new MemoryStream();
        stream.SetLength(length);
        return new FormFile(stream, 0, length, "file", fileName);
    }

    [DataTestMethod]
    [DataRow("proposal.pdf")]
    [DataRow("proposal.DOC")]
    [DataRow("proposal.docx")]
    [DataRow("proposal.xls")]
    [DataRow("proposal.XLSX")]
    public void ValidateProposalUpload_AllowsProposalExtensions(string fileName)
    {
        Assert.IsNull(CreateService().ValidateProposalUpload(File(fileName)));
    }

    [DataTestMethod]
    [DataRow("notes.txt")]
    [DataRow("image.png")]
    [DataRow("proposal.exe")]
    [DataRow("no-extension")]
    public void ValidateProposalUpload_RejectsExtensionsOutsideProposalAllowList(string fileName)
    {
        Assert.IsNotNull(CreateService().ValidateProposalUpload(File(fileName)));
    }

    [TestMethod]
    public void ValidateProposalUpload_PreservesGenericFileSupportAndExistingSizeLimit()
    {
        var service = CreateService();
        Assert.IsNull(service.ValidateUpload(File("notes.txt")));

        var tooLarge = File("proposal.pdf", ClientFileService.MaxFileSize + 1);
        Assert.IsNotNull(service.ValidateProposalUpload(tooLarge));
    }
}