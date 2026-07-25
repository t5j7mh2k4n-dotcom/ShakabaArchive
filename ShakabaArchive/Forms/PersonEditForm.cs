using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Forms;

public sealed class PersonEditForm : Form
{
    private readonly int? _id;
    private string _existingDoc = "";
    private readonly TextBox _nationalId = AppTheme.Field();
    private readonly TextBox _fullName = AppTheme.Field();
    private readonly TextBox _father = AppTheme.Field();
    private readonly TextBox _mother = AppTheme.Field();
    private readonly ComboBox _gender = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DateTimePicker _birth = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, ShowCheckBox = true };
    private readonly TextBox _birthPlace = AppTheme.Field();
    private readonly TextBox _residence = AppTheme.Field();
    private readonly TextBox _neighborhood = AppTheme.Field();
    private readonly TextBox _phone = AppTheme.Field();
    private readonly TextBox _notes = AppTheme.Field();
    private readonly TextBox _docPath = AppTheme.Field();
    private readonly Button _browseDoc = AppTheme.SecondaryButton("اختيار صورة وثيقة");

    public PersonEditForm(int? id = null)
    {
        _id = id;
        AppTheme.ApplyForm(this);
        Text = id is null ? "إضافة سجل شخص" : "تعديل سجل شخص";
        Size = new Size(580, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        _gender.Items.AddRange(["ذكر", "أنثى"]);
        _gender.SelectedIndex = 0;
        _birthPlace.Text = "الشكابة شاع الدين";
        _residence.Text = "الشكابة شاع الدين";
        _notes.Multiline = true;
        _notes.Height = 60;
        _notes.ScrollBars = ScrollBars.Vertical;
        _docPath.ReadOnly = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 14
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (var i = 0; i < 13; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 10 or 11 ? 56 : 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        AddRow(layout, 0, "الرقم الوطني / الهوية", _nationalId);
        AddRow(layout, 1, "الاسم الكامل", _fullName);
        AddRow(layout, 2, "اسم الأب", _father);
        AddRow(layout, 3, "اسم الأم", _mother);
        AddRow(layout, 4, "النوع", _gender);
        AddRow(layout, 5, "تاريخ الميلاد", _birth);
        AddRow(layout, 6, "مكان الميلاد", _birthPlace);
        AddRow(layout, 7, "مكان الإقامة", _residence);
        AddRow(layout, 8, "الحي / الحلة", _neighborhood);
        AddRow(layout, 9, "الهاتف", _phone);
        AddRow(layout, 10, "ملاحظات", _notes);
        AddRow(layout, 11, "صورة الوثيقة", _docPath);
        _browseDoc.Dock = DockStyle.Fill;
        _browseDoc.Click += (_, _) => BrowseDocument();
        layout.SetColumnSpan(_browseDoc, 2);
        layout.Controls.Add(_browseDoc, 0, 12);

        var save = AppTheme.PrimaryButton("حفظ السجل");
        save.Dock = DockStyle.Fill;
        save.Click += (_, _) => Save();
        layout.SetColumnSpan(save, 2);
        layout.Controls.Add(save, 0, 13);

        Controls.Add(layout);
        if (id is not null)
            LoadPerson(id.Value);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control field)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, row);
        field.Dock = DockStyle.Fill;
        layout.Controls.Add(field, 1, row);
    }

    private void BrowseDocument()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "صور ووثائق|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf|الكل|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var fs = File.OpenRead(dlg.FileName);
            _existingDoc = DatabaseService.SaveDocumentImage(fs, dlg.FileName);
            _docPath.Text = _existingDoc;
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر حفظ الصورة:\n" + ex.Message, "خطأ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadPerson(int id)
    {
        using var db = DatabaseService.CreateContext();
        var p = db.People.Find(id);
        if (p is null) return;

        _nationalId.Text = p.NationalId;
        _fullName.Text = p.FullName;
        _father.Text = p.FatherName;
        _mother.Text = p.MotherName;
        _gender.SelectedItem = p.Gender;
        if (p.BirthDate is { } d)
        {
            _birth.Checked = true;
            _birth.Value = d;
        }
        else _birth.Checked = false;
        _birthPlace.Text = p.BirthPlace;
        _residence.Text = p.Residence;
        _neighborhood.Text = p.Neighborhood;
        _phone.Text = p.Phone;
        _notes.Text = p.Notes;
        _existingDoc = p.DocumentImagePath;
        _docPath.Text = p.DocumentImagePath;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_fullName.Text))
        {
            MessageBox.Show("الاسم مطلوب.", "تنبيه",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var db = DatabaseService.CreateContext();
        Person person;
        if (_id is null)
        {
            person = new Person
            {
                HierarchyLevel = 1,
                RegistryCode = PersonRegistryService.AllocateCodeAsync(db, 1, null)
                    .GetAwaiter().GetResult()
            };
            db.People.Add(person);
        }
        else
        {
            person = db.People.Find(_id.Value)!;
            if (person is null) return;
        }

        person.NationalId = _nationalId.Text.Trim();
        person.FirstName = _fullName.Text.Trim();
        person.FatherName = _father.Text.Trim();
        person.MotherName = _mother.Text.Trim();
        person.Nationality = "";
        person.Gender = _gender.SelectedItem?.ToString() ?? "ذكر";
        person.BirthDate = _birth.Checked ? _birth.Value.Date : null;
        person.BirthPlace = _birthPlace.Text.Trim();
        person.Residence = _residence.Text.Trim();
        person.Neighborhood = _neighborhood.Text.Trim();
        person.Phone = _phone.Text.Trim();
        person.Notes = _notes.Text.Trim();
        person.DocumentImagePath = _existingDoc;
        person.RefreshFullName();
        if (string.IsNullOrWhiteSpace(person.FullName))
            person.FullName = _fullName.Text.Trim();
        person.UpdatedAt = DateTime.UtcNow;
        if (_id is null) person.CreatedAt = DateTime.UtcNow;

        db.SaveChanges();
        DialogResult = DialogResult.OK;
        Close();
    }
}
