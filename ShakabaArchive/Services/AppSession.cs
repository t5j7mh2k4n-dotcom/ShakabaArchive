using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

public static class AppSession
{
    public static AppUser? CurrentUser { get; private set; }
    public static bool IsLoggedIn => CurrentUser is not null;

    public static void SignIn(AppUser user) => CurrentUser = user;
    public static void SignOut() => CurrentUser = null;
}
