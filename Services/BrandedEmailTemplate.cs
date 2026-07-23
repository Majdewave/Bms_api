namespace Clienta.Api.Services;

public sealed record BrandedEmailModel(
    string Language,
    string Preheader,
    string Headline,
    string Greeting,
    IReadOnlyList<string> Paragraphs,
    IReadOnlyList<string> PrimaryBullets,
    IReadOnlyList<string> MilestoneBullets,
    string InfoBoxText,
    string CtaText,
    string CtaUrl,
    string FooterTitle,
    string FooterSubtitle,
    string WebsiteUrl,
    string SupportEmail
);

public static class BrandedEmailTemplate
{
    public static string Render(BrandedEmailModel model, string logoUrl)
    {
        var rtl = model.Language is "he" or "ar";
        var dir = rtl ? "rtl" : "ltr";
        var align = rtl ? "right" : "left";

        var paragraphsHtml = string.Join(string.Empty, model.Paragraphs.Select(p =>
            $"<p style='margin:0 0 12px 0;font-size:16px;line-height:1.6;color:#334155'>{Escape(p)}</p>"));

        var primaryBulletsHtml = RenderBullets(model.PrimaryBullets, "&#8226;", "#2563eb", align);
        var milestoneBulletsHtml = RenderBullets(model.MilestoneBullets, "&#10003;", "#0f766e", align);

        var safeFooterTitle = Escape(model.FooterTitle);
        var safeFooterSubtitle = Escape(model.FooterSubtitle);
        var safeWebsite = Escape(model.WebsiteUrl);
        var safeSupportEmail = Escape(model.SupportEmail);
        var safeCtaText = Escape(model.CtaText);
        var safeCtaUrl = Escape(model.CtaUrl);

        return $@"<!doctype html>
<html lang='{model.Language}' dir='{dir}'>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <title>{Escape(model.Headline)}</title>
</head>
<body style='margin:0;padding:0;background:#eff6ff;font-family:Arial,Helvetica,sans-serif;'>
  <div style='display:none;max-height:0;overflow:hidden;opacity:0'>{Escape(model.Preheader)}</div>
  <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='background:#eff6ff;padding:24px 12px'>
    <tr>
      <td align='center'>
        <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='max-width:640px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 10px 28px rgba(37,99,235,0.16)'>
          <tr>
            <td style='background:#2563eb;padding:28px 24px;text-align:center'>
              <img src='{Escape(logoUrl)}' alt='Clienta' style='height:36px;max-width:180px;display:block;margin:0 auto 14px auto' />
              <div style='font-size:24px;line-height:1.25;font-weight:700;color:#ffffff'>{Escape(model.Headline)}</div>
            </td>
          </tr>
          <tr>
            <td style='padding:28px 24px 24px 24px;text-align:{align}'>
              <p style='margin:0 0 16px 0;font-size:18px;line-height:1.5;font-weight:600;color:#0f172a'>{Escape(model.Greeting)}</p>
              {paragraphsHtml}
              {primaryBulletsHtml}
              {milestoneBulletsHtml}
              <div style='margin:18px 0 0 0;padding:14px 16px;border:1px solid #bfdbfe;background:#eff6ff;border-radius:10px;font-size:14px;line-height:1.6;color:#1e3a8a'>
                {Escape(model.InfoBoxText)}
              </div>
              <div style='margin:24px 0 8px 0;text-align:center'>
                <a href='{safeCtaUrl}' style='display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:10px;font-weight:600;font-size:15px'>
                  {safeCtaText}
                </a>
              </div>
            </td>
          </tr>
          <tr>
            <td style='border-top:1px solid #e2e8f0;padding:18px 24px 24px 24px;background:#f8fafc;text-align:center'>
              <div style='font-size:16px;font-weight:700;color:#0f172a'>{safeFooterTitle}</div>
              <div style='font-size:13px;color:#475569;margin-top:4px'>{safeFooterSubtitle}</div>
              <div style='margin-top:10px'>
                <a href='{safeWebsite}' style='font-size:13px;color:#2563eb;text-decoration:none'>{safeWebsite}</a>
              </div>
              <div style='margin-top:8px;font-size:13px;color:#475569'>Need help?</div>
              <div style='margin-top:4px'>
                <a href='mailto:{safeSupportEmail}' style='font-size:13px;color:#2563eb;text-decoration:none'>{safeSupportEmail}</a>
              </div>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string RenderBullets(IReadOnlyList<string> items, string icon, string iconColor, string align)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var rows = string.Join(string.Empty, items.Select(item =>
            $"<tr><td style='width:22px;vertical-align:top;color:{iconColor};font-weight:700'>{icon}</td><td style='font-size:15px;line-height:1.55;color:#0f172a;padding:0 0 8px 0'>{Escape(item)}</td></tr>"));

        return $@"<table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='margin:8px 0 12px 0;text-align:{align}'>{rows}</table>";
    }

    private static string Escape(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
