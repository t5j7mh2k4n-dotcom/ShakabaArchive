using System.Text.RegularExpressions;

namespace ShakabaArchive.Web;

public static class WhatsAppShare
{
    public const string PublicSiteUrl = "https://shakabaarchive.onrender.com";

    public static string SiteShareUrl()
    {
        var text =
            "أرشيف الشكابة شاع الدين\n" +
            "إهداء إلى روح المرحوم — عبدالمحمود محمد علي\n" +
            "تصميم عمر عبدالمحمود\n" +
            PublicSiteUrl;
        return Build(text);
    }

    public static string InviteShareUrl(string inviteCode, string registerUrl)
    {
        var text =
            "دعوة للانضمام إلى أرشيف الشكابة شاع الدين\n\n" +
            $"رقم الدعوة: {inviteCode}\n" +
            "سجّل من الرابط التالي بالإيميل ورقم الهاتف:\n" +
            registerUrl;
        return Build(text);
    }

    public static string PersonShareUrl(string fullName, string detailsUrl)
    {
        var text =
            $"سجل من أرشيف الشكابة شاع الدين\n" +
            $"{fullName}\n" +
            detailsUrl;
        return Build(text);
    }

    /// <summary>رسالة واتساب لإكمال البيانات الناقصة بعد الترحيل إلى السجل.</summary>
    public static string CompleteDataReminderUrl(string? phone, string personName, string completeUrl)
    {
        var text =
            $"السلام عليكم {personName}\n\n" +
            "تم اعتماد بياناتك مبدئياً في أرشيف الشكابة شاع الدين وحفظها في السجل.\n" +
            "يرجى إكمال البيانات الناقصة عبر الرابط التالي (بعد تسجيل الدخول):\n" +
            completeUrl + "\n\n" +
            "شكراً لتعاونكم.";

        var digits = NormalizePhone(phone);
        if (!string.IsNullOrEmpty(digits))
            return $"https://wa.me/{digits}?text=" + Uri.EscapeDataString(text);

        return Build(text);
    }

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "";

        var digits = Regex.Replace(phone, @"\D", "");
        if (digits.Length == 0)
            return "";

        // تحويل 09xxxxxxxx السوداني إلى 2499xxxxxxxx
        if (digits.StartsWith('0') && digits.Length >= 9)
            digits = "249" + digits[1..];
        else if (digits.Length == 9 && digits.StartsWith('9'))
            digits = "249" + digits;

        return digits;
    }

    private static string Build(string text) =>
        "https://wa.me/?text=" + Uri.EscapeDataString(text);
}
