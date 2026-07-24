using ShakabaArchive.Forms;
using ShakabaArchive.Services;

namespace ShakabaArchive;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error);

        try
        {
            LocalUserService.Initialize();
            DatabaseService.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذر تهيئة قاعدة البيانات.\nيمكنك ضبط التخزين لاحقاً من الشاشة الرئيسية.\n\n" + ex.Message,
                "تنبيه قاعدة البيانات",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK || !AppSession.IsLoggedIn)
            return;

        Application.Run(new MainForm());
    }
}
