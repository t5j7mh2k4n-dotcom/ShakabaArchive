namespace ShakabaArchive.Forms;

public static class AppTheme
{
    public static readonly Color Bg = Color.FromArgb(245, 241, 234);
    public static readonly Color Panel = Color.FromArgb(255, 252, 247);
    public static readonly Color Accent = Color.FromArgb(46, 90, 74);
    public static readonly Color AccentSoft = Color.FromArgb(214, 228, 220);
    public static readonly Color Text = Color.FromArgb(32, 40, 36);
    public static readonly Color Muted = Color.FromArgb(95, 105, 98);
    public static readonly Color Danger = Color.FromArgb(140, 55, 48);

    public static readonly Font TitleFont = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font HeadingFont = new("Segoe UI", 12f, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 10.5f, FontStyle.Regular);
    public static readonly Font SmallFont = new("Segoe UI", 9f, FontStyle.Regular);

    public static void ApplyForm(Form form)
    {
        form.RightToLeft = RightToLeft.Yes;
        form.RightToLeftLayout = true;
        form.Font = BodyFont;
        form.BackColor = Bg;
        form.ForeColor = Text;
        form.StartPosition = FormStartPosition.CenterScreen;
    }

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Height = 36,
            Font = HeadingFont,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static Button SecondaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            BackColor = AccentSoft,
            ForeColor = Accent,
            FlatStyle = FlatStyle.Flat,
            Height = 34,
            Font = BodyFont,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static TextBox Field() => new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        Font = BodyFont,
        Height = 28
    };

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Accent,
            ForeColor = Color.White,
            Font = HeadingFont,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Panel,
            ForeColor = Text,
            Font = BodyFont,
            SelectionBackColor = AccentSoft,
            SelectionForeColor = Text
        };
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RightToLeft = RightToLeft.Yes;
    }
}
