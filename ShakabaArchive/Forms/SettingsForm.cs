using ShakabaArchive.Services;

namespace ShakabaArchive.Forms;

public sealed class SettingsForm : Form
{
    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _sqlite = AppTheme.Field();
    private readonly TextBox _pg = AppTheme.Field();

    public SettingsForm()
    {
        AppTheme.ApplyForm(this);
        Text = "إعدادات التخزين";
        Size = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var s = DatabaseService.Settings;
        _provider.Items.AddRange(["Sqlite", "PostgreSql"]);
        _provider.SelectedItem = string.Equals(s.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            ? "PostgreSql" : "Sqlite";
        _sqlite.Text = s.SqliteFileName;
        _pg.Text = s.PostgreSqlConnection;
        _pg.Multiline = true;
        _pg.Height = 70;
        _pg.ScrollBars = ScrollBars.Vertical;

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(12),
            Text = "SQLite: مجاني ومحلي على الجهاز.\nPostgreSQL: مجاني أونلاين عبر Neon أو Supabase — الصق سلسلة الاتصال هنا.",
            ForeColor = AppTheme.Muted
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8
        };
        for (var i = 0; i < 7; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 5 ? 80 : 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(LabelOf("نوع التخزين"), 0, 0);
        layout.Controls.Add(_provider, 0, 1);
        layout.Controls.Add(LabelOf("اسم ملف SQLite"), 0, 2);
        _sqlite.Dock = DockStyle.Fill;
        layout.Controls.Add(_sqlite, 0, 3);
        layout.Controls.Add(LabelOf("سلسلة اتصال PostgreSQL (Neon / Supabase)"), 0, 4);
        _pg.Dock = DockStyle.Fill;
        layout.Controls.Add(_pg, 0, 5);

        var save = AppTheme.PrimaryButton("حفظ وإعادة تهيئة");
        save.Dock = DockStyle.Fill;
        save.Click += (_, _) => Save();
        layout.Controls.Add(save, 0, 7);

        Controls.Add(layout);
        Controls.Add(info);
    }

    private static Label LabelOf(string t) => new()
    {
        Text = t,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomRight
    };

    private void Save()
    {
        var next = new AppSettings
        {
            Provider = _provider.SelectedItem?.ToString() ?? "Sqlite",
            SqliteFileName = string.IsNullOrWhiteSpace(_sqlite.Text) ? "shakaba-archive.db" : _sqlite.Text.Trim(),
            PostgreSqlConnection = _pg.Text.Trim()
        };

        if (string.Equals(next.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(next.PostgreSqlConnection))
        {
            MessageBox.Show("أدخل سلسلة اتصال PostgreSQL أو اختر SQLite.", "تنبيه",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            DatabaseService.SaveSettings(next);
            DatabaseService.ReloadSettings();
            DatabaseService.Initialize();
            MessageBox.Show("تم حفظ الإعدادات وتهيئة قاعدة البيانات.", "تم",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("فشل الحفظ:\n" + ex.Message, "خطأ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
