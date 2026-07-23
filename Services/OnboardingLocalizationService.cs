using System.Globalization;

namespace Clienta.Api.Services;

public interface IOnboardingLocalizationService
{
    string ResolveLanguage(string? preferredLanguage, string? acceptLanguageHeader);
    string GetMessage(string key, string language);
    (string Subject, string HtmlBody) BuildPendingApprovalWelcomeEmail(string language, string fullName);
    (string Subject, string HtmlBody) BuildAccountApprovedEmail(string language, string fullName, string loginUrl, DateTime trialEndsAtUtc);
}

public class OnboardingLocalizationService : IOnboardingLocalizationService
{
    private readonly string _baseUrl;
    private readonly string _logoUrl;
    private readonly string _supportEmail;

    public OnboardingLocalizationService(IConfiguration configuration)
    {
        _baseUrl = (configuration["App:BaseUrl"] ?? "https://clienta.digitalpenpro.com").TrimEnd('/');
        _logoUrl = $"{_baseUrl}/clienta-logo.png";
        _supportEmail = configuration["Support:Email"] ?? "support@clienta.digitalpenpro.com";
    }

    public string ResolveLanguage(string? preferredLanguage, string? acceptLanguageHeader)
    {
        var direct = NormalizeLanguageCode(preferredLanguage);
        if (direct != null)
        {
            return direct;
        }

        if (!string.IsNullOrWhiteSpace(acceptLanguageHeader))
        {
            foreach (var part in acceptLanguageHeader.Split(','))
            {
                var token = part.Split(';')[0].Trim();
                var normalized = NormalizeLanguageCode(token);
                if (normalized != null)
                {
                    return normalized;
                }
            }
        }

        return "en";
    }

    public string GetMessage(string key, string language)
    {
        language = ResolveLanguage(language, null);

        return key switch
        {
            "PENDING_APPROVAL_LOGIN" => language switch
            {
                "he" => "החשבון שלך ממתין לאישור.\nצוות Clienta ייצור איתך קשר בקרוב.",
                "ar" => "حسابك بانتظار الموافقة.\nسيتواصل معك فريق Clienta قريبًا.",
                _ => "Your account is waiting for approval.\nOur team will contact you shortly."
            },
            "ACCOUNT_SUSPENDED" => language switch
            {
                "he" => "החשבון שלך הושעה.\nאנא פנה לתמיכה.",
                "ar" => "تم تعليق حسابك.\nيرجى التواصل مع الدعم.",
                _ => "Your account has been suspended.\nPlease contact support."
            },
            "BUSINESS_NAME_REQUIRED" => language switch
            {
                "he" => "שם העסק הוא שדה חובה",
                "ar" => "اسم النشاط التجاري مطلوب",
                _ => "Business name is required"
            },
            "FULL_NAME_REQUIRED" => language switch
            {
                "he" => "שם מלא הוא שדה חובה",
                "ar" => "الاسم الكامل مطلوب",
                _ => "Full name is required"
            },
            "EMAIL_REQUIRED" => language switch
            {
                "he" => "אימייל הוא שדה חובה",
                "ar" => "البريد الإلكتروني مطلوب",
                _ => "Email is required"
            },
            "PHONE_REQUIRED" => language switch
            {
                "he" => "מספר טלפון הוא שדה חובה",
                "ar" => "رقم الهاتف مطلوب",
                _ => "Phone number is required"
            },
            "PHONE_INVALID" => language switch
            {
                "he" => "מספר טלפון לא תקין",
                "ar" => "رقم هاتف غير صالح",
                _ => "Invalid phone number"
            },
            "PASSWORD_REQUIRED" => language switch
            {
                "he" => "סיסמה היא שדה חובה",
                "ar" => "كلمة المرور مطلوبة",
                _ => "Password is required"
            },
            "PASSWORD_TOO_SHORT" => language switch
            {
                "he" => "הסיסמה חייבת להכיל לפחות 6 תווים",
                "ar" => "يجب أن تتكون كلمة المرور من 6 أحرف على الأقل",
                _ => "Password must be at least 6 characters"
            },
            "PASSWORDS_DO_NOT_MATCH" => language switch
            {
                "he" => "הסיסמאות אינן תואמות",
                "ar" => "كلمتا المرور غير متطابقتين",
                _ => "Passwords do not match"
            },
            "USER_ALREADY_EXISTS" => language switch
            {
                "he" => "משתמש עם האימייל הזה כבר קיים",
                "ar" => "يوجد مستخدم بهذا البريد الإلكتروني بالفعل",
                _ => "User with this email already exists"
            },
            "BUSINESS_ALREADY_EXISTS" => language switch
            {
                "he" => "שם העסק כבר תפוס",
                "ar" => "اسم النشاط التجاري مستخدم بالفعل",
                _ => "Business name already taken"
            },
            "REGISTER_PENDING_APPROVAL_SUCCESS" => language switch
            {
                "he" => "ההרשמה התקבלה בהצלחה. החשבון שלך ממתין לאישור.",
                "ar" => "تم استلام التسجيل بنجاح. حسابك بانتظار الموافقة.",
                _ => "Registration received successfully. Your account is pending approval."
            },
            _ => key
        };
    }

    public (string Subject, string HtmlBody) BuildPendingApprovalWelcomeEmail(string language, string fullName)
    {
        language = ResolveLanguage(language, null);
        var safeName = string.IsNullOrWhiteSpace(fullName) ?
            (language == "he" ? "שלום" : language == "ar" ? "مرحبًا" : "Hello") :
            fullName.Trim();

        var content = language switch
        {
            "he" => new BrandedEmailModel(
                language,
                "ההרשמה ל-Clienta התקבלה בהצלחה וממתינה לאישור.",
                "Welcome to Clienta!",
                $"שלום {safeName},",
                new[]
                {
                    "תודה שנרשמת ל-Clienta.",
                    "ההרשמה שלך התקבלה בהצלחה.",
                    "החשבון שלך ממתין כרגע לאישור."
                },
                new[]
                {
                    "להגדיר איתך את העסק שלך",
                    "לעזור בהקמה הראשונית",
                    "לענות על כל שאלה",
                    "לוודא שתקבל/י את החוויה הטובה ביותר מ-Clienta"
                },
                new[]
                {
                    "תקופת הניסיון המודרכת ל-21 יום תתחיל.",
                    "יישלח אליך מייל נוסף.",
                    "ניתן יהיה להתחבר מיד ולהתחיל לעבוד עם Clienta."
                },
                "אין צורך לבצע שום פעולה בשלב זה. נעדכן אותך מיד כשהחשבון יאושר.",
                "לאתר Clienta",
                _baseUrl,
                "Clienta",
                "Business Management Platform",
                _baseUrl,
                _supportEmail),
            "ar" => new BrandedEmailModel(
                language,
                "تم استلام تسجيلك في Clienta وهو بانتظار الموافقة.",
                "Welcome to Clienta!",
                $"مرحبًا {safeName}،",
                new[]
                {
                    "شكرًا لتسجيلك في Clienta.",
                    "تم استلام تسجيلك بنجاح.",
                    "حسابك بانتظار الموافقة حاليًا."
                },
                new[]
                {
                    "تهيئة نشاطك التجاري",
                    "المساعدة في الإعداد الأولي",
                    "الإجابة عن أسئلتك",
                    "التأكد من حصولك على أفضل تجربة مع Clienta"
                },
                new[]
                {
                    "ستبدأ تجربتك الإرشادية لمدة 21 يومًا.",
                    "ستتلقى رسالة بريد إلكتروني إضافية.",
                    "ستتمكن من تسجيل الدخول فورًا والبدء باستخدام Clienta."
                },
                "لا يلزم منك أي إجراء في هذه المرحلة. سنبلغك فور اعتماد حسابك.",
                "زيارة موقع Clienta",
                _baseUrl,
                "Clienta",
                "Business Management Platform",
                _baseUrl,
                _supportEmail),
            _ => new BrandedEmailModel(
                language,
                "Your Clienta registration was received and is waiting for approval.",
                "Welcome to Clienta!",
                $"Hello {safeName},",
                new[]
                {
                    "Thank you for registering with Clienta.",
                    "Your registration has been received successfully.",
                    "Your account is currently waiting for approval."
                },
                new[]
                {
                    "Configure your business",
                    "Help with the initial setup",
                    "Answer your questions",
                    "Make sure you get the best experience from Clienta"
                },
                new[]
                {
                    "Your 21-Day Guided Trial will begin.",
                    "You will receive another email.",
                    "You can immediately log in and start using Clienta."
                },
                "No action is required from you at this stage. We will notify you as soon as your account is approved.",
                "Visit Clienta Website",
                _baseUrl,
                "Clienta",
                "Business Management Platform",
                _baseUrl,
                _supportEmail)
        };

        return (GetPendingApprovalSubject(language), BrandedEmailTemplate.Render(content, _logoUrl));
    }

    public (string Subject, string HtmlBody) BuildAccountApprovedEmail(string language, string fullName, string loginUrl, DateTime trialEndsAtUtc)
    {
        language = ResolveLanguage(language, null);
        var safeName = string.IsNullOrWhiteSpace(fullName) ?
            (language == "he" ? "שלום" : language == "ar" ? "مرحبًا" : "Hello") :
            fullName.Trim();
        var trialEndDisplay = trialEndsAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var content = language switch
        {
            "he" => new BrandedEmailModel(
                language,
                "החשבון שלך ב-Clienta אושר ומוכן לשימוש.",
                "Welcome to Clienta - Your account is ready!",
                $"שלום {safeName},",
                new[]
                {
                    "ברכות! החשבון שלך אושר.",
                    "תקופת הניסיון המודרכת ל-21 יום התחילה עכשיו.",
                    "אפשר להתחבר ולהתחיל לנהל את העסק שלך מיד."
                },
                Array.Empty<string>(),
                new[]
                {
                    $"סיום תקופת הניסיון: {trialEndDisplay}"
                },
                "זקוק/ה לעזרה? צוות Clienta כאן בשבילך.",
                "התחברות ל-Clienta",
                loginUrl,
                "Clienta",
                "Business Management Platform",
                _baseUrl,
                _supportEmail),
            "ar" => new BrandedEmailModel(
                language,
                "تمت الموافقة على حسابك في Clienta وهو جاهز للاستخدام.",
                "Welcome to Clienta - Your account is ready!",
                $"مرحبًا {safeName}،",
                new[]
                {
                    "تهانينا! تمت الموافقة على حسابك.",
                    "بدأت الآن تجربتك الإرشادية لمدة 21 يومًا.",
                    "يمكنك تسجيل الدخول والبدء في إدارة أعمالك فورًا."
                },
                Array.Empty<string>(),
                new[]
                {
                    $"تنتهي التجربة في: {trialEndDisplay}"
                },
                "هل تحتاج إلى مساعدة؟ فريق Clienta هنا لخدمتك.",
                "تسجيل الدخول إلى Clienta",
                loginUrl,
                "Clienta",
                "Business Management Platform",
                _baseUrl,
                _supportEmail),
            _ => new BrandedEmailModel(
                language,
                "Your Clienta account has been approved and is ready to use.",
                "Welcome to Clienta - Your account is ready!",
                $"Hello {safeName},",
                new[]
                {
                    "Congratulations! Your account has been approved.",
                    "Your 21-Day Guided Trial has now started.",
                    "You can now log in and begin managing your business."
                },
                Array.Empty<string>(),
                new[]
                {
                    $"Trial ends on: {trialEndDisplay}"
                },
                "Need help? The Clienta team is here for you.",
                "Log in to Clienta",
                loginUrl,
                "Clienta",
                "Business Management Platform",
                _baseUrl,
                _supportEmail)
        };

        return (GetApprovedSubject(language), BrandedEmailTemplate.Render(content, _logoUrl));
    }

    private static string GetPendingApprovalSubject(string language)
    {
        return language switch
        {
            "he" => "ברוכים הבאים ל-Clienta",
            "ar" => "مرحبًا بك في Clienta",
            _ => "Welcome to Clienta!"
        };
    }

    private static string GetApprovedSubject(string language)
    {
        return language switch
        {
            "he" => "ברוכים הבאים ל-Clienta - החשבון שלך מוכן!",
            "ar" => "مرحبًا بك في Clienta - حسابك جاهز!",
            _ => "Welcome to Clienta - Your account is ready!"
        };
    }

    private static string? NormalizeLanguageCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lower = value.Trim().ToLowerInvariant();
        if (lower.StartsWith("he")) return "he";
        if (lower.StartsWith("ar")) return "ar";
        if (lower.StartsWith("en")) return "en";
        return null;
    }
}
