using System.Text;

namespace ShakabaArchive.Web;

public static class WhatsAppShare
{
    public const string PublicSiteUrl = "https://shakabaarchive.onrender.com";

    public static string SiteShareUrl()
    {
        var text =
            "أرشيف الشكابة شاع الدين\n" +
            "أرشيف إلكتروني خاص بمواطني الشكابة شاع الدين\n" +
            "إهداء إلى روح المرحوم عبدالمحمود محمد علي\n" +
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

    private static string Build(string text) =>
        "https://wa.me/?text=" + Uri.EscapeDataString(text);
}
