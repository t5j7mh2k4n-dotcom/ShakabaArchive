using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ShakabaArchive.Web.Services;

/// <summary>تسجيل ودخول وتأكيد البريد عبر Firebase Identity Toolkit REST API.</summary>
public class FirebaseAuthService(HttpClient http, IOptions<FirebaseOptions> options)
{
    private readonly FirebaseOptions _opt = options.Value;

    public bool IsEnabled => _opt.IsConfigured;

    public async Task<(bool Ok, string Error, string? ErrorCode, string? LocalId, string? IdToken)> SignUpAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
            return (true, "", null, null, null);

        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={Uri.EscapeDataString(_opt.ApiKey)}";
        using var res = await http.PostAsJsonAsync(url, new FirebaseAuthRequest
        {
            Email = email.Trim(),
            Password = password,
            ReturnSecureToken = true
        }, ct);

        var body = await res.Content.ReadFromJsonAsync<FirebaseAuthResponse>(cancellationToken: ct);
        if (res.IsSuccessStatusCode && body?.LocalId is { Length: > 0 })
            return (true, "", null, body.LocalId, body.IdToken);

        var code = body?.Error?.Message;
        return (false, MapError(code), code, null, null);
    }

    public async Task<(bool Ok, string Error, string? LocalId, string? IdToken, bool EmailVerified)> SignInAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
            return (false, "", null, null, false);

        var url =
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={Uri.EscapeDataString(_opt.ApiKey)}";
        using var res = await http.PostAsJsonAsync(url, new FirebaseAuthRequest
        {
            Email = email.Trim(),
            Password = password,
            ReturnSecureToken = true
        }, ct);

        var body = await res.Content.ReadFromJsonAsync<FirebaseAuthResponse>(cancellationToken: ct);
        if (!res.IsSuccessStatusCode || body?.LocalId is not { Length: > 0 } || string.IsNullOrEmpty(body.IdToken))
            return (false, MapError(body?.Error?.Message), null, null, false);

        var verified = await IsEmailVerifiedAsync(body.IdToken, ct);
        return (true, "", body.LocalId, body.IdToken, verified);
    }

    /// <summary>يرسل رابط تأكيد الحساب إلى بريد المستخدم.</summary>
    public async Task<(bool Ok, string Error)> SendEmailVerificationAsync(
        string idToken,
        string? continueUrl = null,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
            return (true, "");

        if (string.IsNullOrWhiteSpace(idToken))
            return (false, "تعذر إرسال رابط التأكيد (لا يوجد رمز جلسة).");

        var url =
            $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={Uri.EscapeDataString(_opt.ApiKey)}";

        var payload = new Dictionary<string, object?>
        {
            ["requestType"] = "VERIFY_EMAIL",
            ["idToken"] = idToken
        };
        if (!string.IsNullOrWhiteSpace(continueUrl))
            payload["continueUrl"] = continueUrl.Trim();

        using var res = await http.PostAsJsonAsync(url, payload, ct);
        var body = await res.Content.ReadFromJsonAsync<FirebaseAuthResponse>(cancellationToken: ct);
        if (res.IsSuccessStatusCode)
            return (true, "");

        return (false, MapError(body?.Error?.Message));
    }

    public async Task<bool> IsEmailVerifiedAsync(string idToken, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(idToken))
            return false;

        var url =
            $"https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={Uri.EscapeDataString(_opt.ApiKey)}";
        using var res = await http.PostAsJsonAsync(url, new { idToken }, ct);
        var body = await res.Content.ReadFromJsonAsync<FirebaseLookupResponse>(cancellationToken: ct);
        if (!res.IsSuccessStatusCode || body?.Users is not { Count: > 0 })
            return false;

        return body.Users[0].EmailVerified == true;
    }

    private static string MapError(string? code) => code switch
    {
        "EMAIL_EXISTS" => "هذا البريد مسجّل مسبقاً في Firebase.",
        "OPERATION_NOT_ALLOWED" => "تسجيل البريد/كلمة المرور غير مفعّل في Firebase Console.",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "محاولات كثيرة. حاول لاحقاً.",
        "WEAK_PASSWORD : Password should be at least 6 characters" or "WEAK_PASSWORD"
            => "كلمة المرور ضعيفة — 6 أحرف على الأقل.",
        "INVALID_EMAIL" => "البريد الإلكتروني غير صالح.",
        "EMAIL_NOT_FOUND" => "لا يوجد حساب بهذا البريد في Firebase.",
        "INVALID_PASSWORD" => "كلمة المرور غير صحيحة.",
        "INVALID_LOGIN_CREDENTIALS" => "البريد أو كلمة المرور غير صحيحة.",
        "USER_DISABLED" => "هذا الحساب معطّل في Firebase.",
        "INVALID_ID_TOKEN" => "انتهت جلسة التأكيد. سجّل الدخول مجدداً ثم أعد إرسال الرابط.",
        null or "" => "تعذر الاتصال بـ Firebase. تحقق من الإعدادات أو أعد المحاولة.",
        _ when code.Contains("WEAK_PASSWORD", StringComparison.OrdinalIgnoreCase)
            => "كلمة المرور ضعيفة — 6 أحرف على الأقل.",
        _ => $"خطأ Firebase: {code}"
    };

    private sealed class FirebaseAuthRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("returnSecureToken")]
        public bool ReturnSecureToken { get; set; }
    }

    private sealed class FirebaseAuthResponse
    {
        [JsonPropertyName("localId")]
        public string? LocalId { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("idToken")]
        public string? IdToken { get; set; }

        [JsonPropertyName("error")]
        public FirebaseErrorBody? Error { get; set; }
    }

    private sealed class FirebaseLookupResponse
    {
        [JsonPropertyName("users")]
        public List<FirebaseUserInfo>? Users { get; set; }
    }

    private sealed class FirebaseUserInfo
    {
        [JsonPropertyName("localId")]
        public string? LocalId { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("emailVerified")]
        public bool? EmailVerified { get; set; }
    }

    private sealed class FirebaseErrorBody
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
