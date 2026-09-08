using Amazon.S3;
using Amazon.S3.Model;
using Clienta.Api.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Cryptography;

namespace Clienta.Api.Services;

public sealed class InterpretationPdfService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InterpretationPdfService> _logger;

    public InterpretationPdfService(
        IAmazonS3 s3,
        IConfiguration configuration,
        ILogger<InterpretationPdfService> logger)
    {
        _s3 = s3;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<InterpretationPdfResult> GenerateAsync(
        InterpretationRequest request,
        InterpretationReport report,
        Tenant tenant,
        Client client,
        User interpreter,
        string language,
        CancellationToken cancellationToken)
    {
        var labels = InterpretationPdfLabels.ForLanguage(language);
        var isRtl = labels.IsRtl;

        var logoBytes = await TryLoadUrlBytesAsync(
            tenant.LogoUrl,
            "tenant logo",
            cancellationToken);

        byte[]? stampBytes = null;

        if (interpreter.UseStamp &&
            !string.IsNullOrWhiteSpace(interpreter.StampUrl))
        {
            stampBytes = await TryLoadStorageBytesAsync(
                interpreter.StampUrl,
                "interpreter stamp",
                cancellationToken);
        }

        var order = request.ImagingOrder;
        var completionTime = request.CompletedAt ?? DateTime.UtcNow;
        var producedAt = DateTime.Now;

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);

                page.DefaultTextStyle(style =>
                    style
                        .FontFamily(labels.FontFamily)
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken4));

                // FOOTER — same visual language as Prescription PDF
                page.Footer().Row(row =>
                {
                    row.RelativeItem()
                        .AlignLeft()
                        .Text($"{labels.Produced}: {producedAt:dd/MM/yyyy HH:mm}")
                        .FontFamily(labels.FontFamily)
                        .FontSize(8)
                        .FontColor(Colors.Grey.Medium);

                    row.RelativeItem()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span(labels.Page + " ")
                                .FontSize(9)
                                .FontFamily(labels.FontFamily)
                                .FontColor(Colors.Grey.Medium);

                            text.CurrentPageNumber()
                                .FontSize(9)
                                .FontFamily(labels.FontFamily)
                                .FontColor(Colors.Grey.Medium);

                            text.Span(" " + labels.Of + " ")
                                .FontSize(9)
                                .FontFamily(labels.FontFamily)
                                .FontColor(Colors.Grey.Medium);

                            text.TotalPages()
                                .FontSize(9)
                                .FontFamily(labels.FontFamily)
                                .FontColor(Colors.Grey.Medium);
                        });

                    row.RelativeItem()
                        .AlignRight()
                        .Text("CLIENTA")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(col =>
                {
                    // LOGO
                    if (logoBytes != null && logoBytes.Length > 0)
                    {
                        col.Item()
                            .AlignCenter()
                            .Height(90)
                            .Image(logoBytes)
                            .FitArea();
                    }

                    // BUSINESS NAME
                    var businessName =
                        tenant.LegalBusinessName ?? tenant.Name ?? string.Empty;

                    var businessNameText = col.Item()
                        .AlignCenter()
                        .Text(businessName)
                        .FontFamily(labels.FontFamily)
                        .FontSize(18)
                        .Bold();

                    if (isRtl)
                        businessNameText.DirectionFromRightToLeft();

                    col.Item()
                        .PaddingTop(5)
                        .LineHorizontal(1)
                        .LineColor(Colors.Grey.Lighten2);

                    // BUSINESS CONTACT DETAILS
                    var contactParts = new List<string>();

                    if (!string.IsNullOrWhiteSpace(tenant.Phone))
                        contactParts.Add(tenant.Phone);

                    if (!string.IsNullOrWhiteSpace(tenant.WhatsApp))
                        contactParts.Add($"WhatsApp {tenant.WhatsApp}");

                    if (!string.IsNullOrWhiteSpace(tenant.OwnerUser?.Email))
                        contactParts.Add(tenant.OwnerUser.Email);

                    if (contactParts.Count > 0)
                    {
                        col.Item()
                            .PaddingTop(5)
                            .AlignCenter()
                            .Text(string.Join(" | ", contactParts))
                            .FontFamily(labels.FontFamily)
                            .FontColor(Colors.Grey.Medium)
                            .FontSize(10);
                    }

                    col.Item().PaddingBottom(5);

                    // DOCUMENT TITLE
                    var titleText = col.Item()
                        .AlignCenter()
                        .Text(labels.Title)
                        .FontFamily(labels.FontFamily)
                        .FontSize(26)
                        .Bold();

                    if (isRtl)
                        titleText.DirectionFromRightToLeft();

                    col.Item().PaddingVertical(10);

                    // MAIN DOCUMENT FRAME
                    col.Item()
                        .Border(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .Padding(15)
                        .Column(details =>
                        {
                            // PATIENT DETAILS
                            SectionTitle(
                                details,
                                labels.PatientSection,
                                labels);

                            details.Item()
                                .PaddingTop(8)
                                .Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn();
                                        columns.RelativeColumn();
                                    });

                                    void Cell(string label, string? value)
                                    {
                                        table.Cell()
                                            .PaddingBottom(8)
                                            .Column(c =>
                                            {
                                                var labelText = c.Item()
                                                    .AlignRight()
                                                    .Text(label)
                                                    .FontFamily(labels.FontFamily)
                                                    .FontSize(10)
                                                    .Bold();

                                                if (isRtl)
                                                    labelText.DirectionFromRightToLeft();

                                                var valueText = c.Item()
                                                    .AlignRight()
                                                    .Text(value ?? string.Empty)
                                                    .FontFamily(labels.FontFamily)
                                                    .FontSize(12);

                                                if (isRtl)
                                                    valueText.DirectionFromRightToLeft();
                                            });
                                    }

                                    if (isRtl)
                                    {
                                        Cell(labels.FullName, client.FullName);
                                        Cell(labels.IdNumber, client.IdNumber);
                                        Cell(labels.Phone, client.Phone);
                                        Cell(
                                            labels.BirthDate,
                                            client.BirthDate?.ToString("dd/MM/yyyy"));
                                    }
                                    else
                                    {
                                        Cell(labels.FullName, client.FullName);
                                        Cell(labels.IdNumber, client.IdNumber);
                                        Cell(labels.Phone, client.Phone);
                                        Cell(
                                            labels.BirthDate,
                                            client.BirthDate?.ToString("dd/MM/yyyy"));
                                    }
                                });

                            details.Item().PaddingVertical(8);

                            // EXAM DETAILS
                            details.Item()
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Padding(10)
                                .Column(exam =>
                                {
                                    SectionTitle(
                                        exam,
                                        labels.ExamSection,
                                        labels);

                                    exam.Item()
                                        .PaddingTop(8)
                                        .Table(table =>
                                        {
                                            table.ColumnsDefinition(columns =>
                                            {
                                                columns.RelativeColumn();
                                                columns.RelativeColumn();
                                            });

                                            void ExamCell(
                                                string label,
                                                string? value)
                                            {
                                                table.Cell()
                                                    .PaddingBottom(8)
                                                    .Column(c =>
                                                    {
                                                        var labelText = c.Item()
                                                            .AlignRight()
                                                            .Text(label)
                                                            .FontFamily(labels.FontFamily)
                                                            .FontSize(9)
                                                            .Bold();

                                                        if (isRtl)
                                                            labelText.DirectionFromRightToLeft();

                                                        var valueText = c.Item()
                                                            .AlignRight()
                                                            .Text(value ?? string.Empty)
                                                            .FontFamily(labels.FontFamily)
                                                            .FontSize(11);

                                                        if (isRtl)
                                                            valueText.DirectionFromRightToLeft();
                                                    });
                                            }

                                            ExamCell(
                                                labels.Exam,
                                                order.Service?.Name);

                                            ExamCell(
                                                labels.Modality,
                                                order.Modality);

                                            ExamCell(
                                                labels.ExamDate,
                                                order.ScheduledStartTime
                                                    .ToLocalTime()
                                                    .ToString("dd/MM/yyyy HH:mm"));

                                            ExamCell(
                                                labels.AccessionNumber,
                                                order.AccessionNumber);
                                        });
                                });

                            details.Item().PaddingVertical(8);

                            // REFERRING DOCTOR
                            details.Item()
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Padding(10)
                                .Column(referral =>
                                {
                                    SectionTitle(
                                        referral,
                                        labels.ReferralSection,
                                        labels);

                                    referral.Item().PaddingTop(8);

                    var referringDoctorName = string.IsNullOrWhiteSpace(order.ReferringDoctorName)
                        ? labels.NotProvided
                        : order.ReferringDoctorName;

                    var referringDoctorLabel = referral.Item()
                        .AlignRight()
                        .Text(labels.ReferringDoctor)
                        .FontFamily(labels.FontFamily)
                        .FontSize(9)
                        .Bold();

                    if (isRtl)
                        referringDoctorLabel.DirectionFromRightToLeft();

                    var referringDoctorValue = referral.Item()
                        .AlignRight()
                        .Text(referringDoctorName)
                        .FontFamily(labels.FontFamily)
                        .FontSize(11);

                    if (isRtl)
                        referringDoctorValue.DirectionFromRightToLeft();
                                });

                            details.Item().PaddingVertical(8);

                            // INTERPRETATION
                            details.Item()
                                .Border(1)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Padding(12)
                                .Column(interpretation =>
                                {
                                    SectionTitle(
                                        interpretation,
                                        labels.InterpretationSection,
                                        labels);

                                    interpretation.Item()
                                        .PaddingTop(12);

                                    var interpretationText =
                                        interpretation.Item()
                                            .MinHeight(130)
                                            .AlignRight()
                                            .Text(report.Content ?? string.Empty)
                                            .FontFamily(labels.FontFamily)
                                            .FontSize(12)
                                            .LineHeight(1.45f);

                                    if (isRtl)
                                        interpretationText.DirectionFromRightToLeft();
                                });

                            details.Item().PaddingVertical(10);

                            // INTERPRETER + SIGNATURE
                            details.Item().Row(row =>
                            {
                                row.Spacing(40);

                                // SIGNATURE / STAMP
                                row.RelativeItem()
                                    .AlignCenter()
                                    .Column(signature =>
                                    {
                                        var signatureTitle =
                                            signature.Item()
                                                .AlignCenter()
                                                .Text(labels.Signature)
                                                .FontFamily(labels.FontFamily)
                                                .Bold();

                                        if (isRtl)
                                            signatureTitle.DirectionFromRightToLeft();

                                        signature.Item().PaddingTop(5);

                                        if (stampBytes != null &&
                                            stampBytes.Length > 0)
                                        {
                                            signature.Item()
                                                .AlignCenter()
                                                .Width(140)
                                                .Height(70)
                                                .Image(stampBytes)
                                                .FitArea();
                                        }
                                        else
                                        {
                                            signature.Item()
                                                .PaddingTop(40)
                                                .AlignCenter()
                                                .Width(120)
                                                .LineHorizontal(1);
                                        }
                                    });

                                // INTERPRETER DETAILS
                                row.RelativeItem()
                                    .AlignCenter()
                                    .Column(doctor =>
                                    {
                                        var doctorTitle = doctor.Item()
                                            .AlignCenter()
                                            .Text(labels.InterpreterSection)
                                            .FontFamily(labels.FontFamily)
                                            .Bold();

                                        if (isRtl)
                                            doctorTitle.DirectionFromRightToLeft();

                                        doctor.Item().PaddingTop(6);

                                        var doctorName = doctor.Item()
                                            .AlignCenter()
                                            .Text(interpreter.FullName ?? string.Empty)
                                            .FontFamily(labels.FontFamily)
                                            .FontSize(12)
                                            .Bold();

                                        if (isRtl)
                                            doctorName.DirectionFromRightToLeft();

                                        if (!string.IsNullOrWhiteSpace(
                                                interpreter.LicenseNumber))
                                        {
                                            doctor.Item()
                                                .PaddingTop(3)
                                                .AlignCenter()
                                                .Text(
                                                    $"{labels.LicenseNumber}: {interpreter.LicenseNumber}")
                                                .FontFamily(labels.FontFamily)
                                                .FontSize(10);
                                        }

                                        if (!string.IsNullOrWhiteSpace(
                                                interpreter.Phone))
                                        {
                                            doctor.Item()
                                                .PaddingTop(3)
                                                .AlignCenter()
                                                .Text(
                                                    $"{labels.Phone}: {interpreter.Phone}")
                                                .FontFamily(labels.FontFamily)
                                                .FontSize(10);
                                        }

                                        doctor.Item()
                                            .PaddingTop(3)
                                            .AlignCenter()
                                            .Text(
                                                $"{labels.CompletedAt}: " +
                                                completionTime
                                                    .ToLocalTime()
                                                    .ToString("dd/MM/yyyy HH:mm"))
                                            .FontFamily(labels.FontFamily)
                                            .FontSize(9);
                                    });
                            });

                            details.Item().PaddingVertical(10);

                            // DOCUMENT PRODUCTION INFO
                            var productionText = details.Item()
                                .AlignCenter()
                                .Text(
                                    $"{labels.Produced}: " +
                                    producedAt.ToString("dd/MM/yyyy HH:mm"))
                                .FontFamily(labels.FontFamily)
                                .FontSize(9)
                                .FontColor(Colors.Grey.Medium);

                            if (isRtl)
                                productionText.DirectionFromRightToLeft();

                            details.Item()
                                .PaddingTop(3)
                                .AlignCenter()
                                .Text(
                                    $"{labels.AccessionNumber}: " +
                                    (order.AccessionNumber ?? string.Empty))
                                .FontFamily(labels.FontFamily)
                                .FontSize(8)
                                .FontColor(Colors.Grey.Medium);
                        });
                });
            });
        }).GeneratePdf();

        return new InterpretationPdfResult(
            pdf,
            Convert.ToHexString(
                SHA256.HashData(pdf))
                .ToLowerInvariant());
    }

    private static void SectionTitle(
        ColumnDescriptor column,
        string title,
        InterpretationPdfLabels labels)
    {
        var text = column.Item()
            .AlignRight()
            .Text(title)
            .FontFamily(labels.FontFamily)
            .FontSize(16)
            .Bold();

        if (labels.IsRtl)
            text.DirectionFromRightToLeft();
    }

    private async Task<byte[]?> TryLoadStorageBytesAsync(
        string url,
        string description,
        CancellationToken cancellationToken)
    {
        // First try the URL directly, exactly like the existing Prescription PDF.
        var directBytes = await TryLoadUrlBytesAsync(
            url,
            description,
            cancellationToken);

        if (directBytes != null && directBytes.Length > 0)
            return directBytes;

        // S3 fallback for private/internal storage URLs.
        try
        {
            var bucket = _configuration["AWS:BucketName"];
            var key = ExtractS3Key(url, bucket);

            if (string.IsNullOrWhiteSpace(bucket) ||
                string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            using var response = await _s3.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                },
                cancellationToken);

            await using var responseStream = response.ResponseStream;
            using var memory = new MemoryStream();

            await responseStream.CopyToAsync(
                memory,
                cancellationToken);

            return memory.ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not load optional {Description}; continuing without it.",
                description);

            return null;
        }
    }

    private async Task<byte[]?> TryLoadUrlBytesAsync(
        string? url,
        string description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            using var httpClient = new HttpClient();

            return await httpClient.GetByteArrayAsync(
                url,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not load optional {Description}; continuing without it.",
                description);

            return null;
        }
    }

    private static string? ExtractS3Key(
        string url,
        string? bucket)
    {
        if (string.IsNullOrWhiteSpace(bucket) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = Uri.UnescapeDataString(
            uri.AbsolutePath.TrimStart('/'));

        if (uri.Host.StartsWith(
                bucket + ".",
                StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var bucketPrefix = bucket + "/";

        if (path.StartsWith(
                bucketPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return path[bucketPrefix.Length..];
        }

        return path;
    }
}

public sealed record InterpretationPdfResult(
    byte[] Bytes,
    string Sha256);

internal sealed record InterpretationPdfLabels(
    string FontFamily,
    bool IsRtl,
    string Title,
    string PatientSection,
    string FullName,
    string IdNumber,
    string BirthDate,
    string Phone,
    string ExamSection,
    string Exam,
    string Modality,
    string ExamDate,
    string AccessionNumber,
    string ReferralSection,
    string ReferringDoctor,
    string InterpretationSection,
    string InterpreterSection,
    string LicenseNumber,
    string CompletedAt,
    string Signature,
    string Produced,
    string Page,
    string Of,
    string NotProvided)
{
    public static InterpretationPdfLabels ForLanguage(string language)
    {
        var normalized =
            (language ?? "he").Trim().ToLowerInvariant();

        if (normalized.StartsWith("ar"))
        {
            return new InterpretationPdfLabels(
                "Noto Sans Arabic",
                true,
                "تقرير تفسير فحص الألتراساوند",
                "بيانات المريض",
                "الاسم الكامل",
                "رقم الهوية",
                "تاريخ الميلاد",
                "الهاتف",
                "بيانات الفحص",
                "الفحص",
                "نوع التصوير",
                "تاريخ الفحص",
                "رقم الفحص",
                "الإحالة",
                "الطبيب المُحيل",
                "التفسير",
                "الطبيب المفسر",
                "رقم الترخيص",
                "تاريخ ووقت الإكمال",
                "التوقيع والختم",
                "تم إصدار المستند",
                "صفحة",
                "من",
                "غير متوفر");
        }

        if (normalized.StartsWith("en"))
        {
            return new InterpretationPdfLabels(
                "Noto Sans Hebrew",
                false,
                "Ultrasound Interpretation Report",
                "Patient Details",
                "Full name",
                "ID number",
                "Date of birth",
                "Phone",
                "Exam Details",
                "Exam",
                "Modality",
                "Exam date",
                "Accession number",
                "Referral",
                "Referring doctor",
                "Interpretation",
                "Interpreter",
                "License number",
                "Completed at",
                "Signature / Stamp",
                "Produced",
                "Page",
                "of",
                "Not provided");
        }

        return new InterpretationPdfLabels(
            "Noto Sans Hebrew",
            true,
            "פענוח בדיקת אולטרסאונד",
            "פרטי המטופל",
            "שם המטופל",
            "ת.ז",
            "תאריך לידה",
            "טלפון",
            "פרטי הבדיקה",
            "סוג הבדיקה",
            "מודליות",
            "תאריך הבדיקה",
            "מספר בדיקה",
            "הפניה",
            "רופא מפנה",
            "פענוח",
            "רופא מפענח",
            "מספר רישיון",
            "תאריך ושעת הפענוח",
            "חתימה / חותמת",
            "הופק בתאריך",
            "עמוד",
            "מתוך",
            "לא צוין");
    }
}