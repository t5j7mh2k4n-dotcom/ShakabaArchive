using ShakabaArchive.Services;

namespace ShakabaArchive.Forms;

public sealed class LoginForm : Form
{
    private readonly TextBox _user = AppTheme.Field();
    private readonly TextBox _pass = AppTheme.Field();

    public LoginForm()
    {
        AppTheme.ApplyForm(this);
        Text = "أرشيف الشكابة شاع الدين — دخول";
        Size = new Size(460, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var title = new Label
        {
            Text = "أرشيف الشكابة شاع الدين",
            Font = AppTheme.TitleFont,
            ForeColor = AppTheme.Accent,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var sub = new Label
        {
            Text = "الدخول بالبريد أو رقم الهاتف — المستخدمون على هذا الجهاز",
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.Muted,
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(40, 20, 40, 20),
            BackColor = AppTheme.Panel
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(new Label { Text = "البريد أو الهاتف", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight }, 0, 0);
        _user.Dock = DockStyle.Fill;
        _user.Text = "abohosam@shukaba.local";
        layout.Controls.Add(_user, 0, 1);

        layout.Controls.Add(new Label { Text = "كلمة المرور", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight }, 0, 2);
        _pass.Dock = DockStyle.Fill;
        _pass.UseSystemPasswordChar = true;
        _pass.Text = "Om123456@";
        layout.Controls.Add(_pass, 0, 3);

        var loginBtn = AppTheme.PrimaryButton("دخول إلى الأرشيف");
        loginBtn.Dock = DockStyle.Fill;
        loginBtn.Click += (_, _) => TryLogin();
        layout.Controls.Add(loginBtn, 0, 5);

        panel.Controls.Add(layout);
        Controls.Add(panel);
        Controls.Add(sub);
        Controls.Add(title);

        AcceptButton = loginBtn;
    }

    private void TryLogin()
    {
        try
        {
            LocalUserService.Initialize();
            var user = LocalUserService.FindByLogin(_user.Text);
            if (user is null || !PasswordHasher.Verify(_pass.Text, user.PasswordHash))
            {
                MessageBox.Show("البريد/الهاتف أو كلمة المرور غير صحيحة.", "تنبيه",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSession.SignIn(user);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر تسجيل الدخول:\n" + ex.Message, "خطأ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
