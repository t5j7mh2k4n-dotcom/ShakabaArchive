using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Forms;

public sealed class MainForm : Form
{
    private readonly TextBox _search = AppTheme.Field();
    private readonly ComboBox _filterNationality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DataGridView _peopleGrid = new();
    private readonly DataGridView _eventsGrid = new();
    private readonly Label _status = new();
    private readonly Label _personTitle = new();

    private int? _selectedPersonId;

    public MainForm()
    {
        AppTheme.ApplyForm(this);
        Text = "أرشيف الشكابة شاع الدين — سجل المواليد والمناسبات";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 650);

        Controls.Add(BuildBody());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildHeader());

        Load += (_, _) =>
        {
            RefreshNationalities();
            ReloadPeople();
        };
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = AppTheme.Accent,
            Padding = new Padding(16, 8, 16, 8)
        };

        var title = new Label
        {
            Text = "أرشيف الشكابة شاع الدين",
            Font = AppTheme.TitleFont,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(16, 8)
        };
        var sub = new Label
        {
            Text = "أرشيف الشكابة شاع الدين — إهداء إلى روح المرحوم — عبدالمحمود محمد علي — تصميم عمر عبدالمحمود",
            Font = AppTheme.SmallFont,
            ForeColor = Color.FromArgb(220, 235, 228),
            AutoSize = true,
            Location = new Point(16, 40)
        };

        var user = new Label
        {
            Text = AppSession.CurrentUser?.DisplayName ?? "",
            Font = AppTheme.BodyFont,
            ForeColor = Color.White,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        user.Location = new Point(header.Width - 200, 24);
        header.Resize += (_, _) => user.Left = Math.Max(16, header.ClientSize.Width - user.Width - 20);

        header.Controls.Add(title);
        header.Controls.Add(sub);
        header.Controls.Add(user);
        return header;
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(10, 8, 10, 8),
            BackColor = AppTheme.Panel,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        _search.Width = 220;
        _search.PlaceholderText = "بحث: رقم وطني / اسم / جنسية / قبيلة / حي";
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ReloadPeople();
                e.SuppressKeyPress = true;
            }
        };

        _filterNationality.Items.Add("كل الجنسيات");
        _filterNationality.SelectedIndex = 0;
        _filterNationality.SelectedIndexChanged += (_, _) => ReloadPeople();

        var searchBtn = AppTheme.SecondaryButton("بحث");
        searchBtn.Width = 80;
        searchBtn.Click += (_, _) => ReloadPeople();

        var addPerson = AppTheme.PrimaryButton("إضافة شخص");
        addPerson.Width = 120;
        addPerson.Click += (_, _) =>
        {
            using var f = new PersonEditForm();
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                RefreshNationalities();
                ReloadPeople();
            }
        };

        var editPerson = AppTheme.SecondaryButton("تعديل");
        editPerson.Width = 80;
        editPerson.Click += (_, _) => EditSelectedPerson();

        var deletePerson = AppTheme.SecondaryButton("حذف");
        deletePerson.Width = 80;
        deletePerson.ForeColor = AppTheme.Danger;
        deletePerson.Click += (_, _) => DeleteSelectedPerson();

        var addEvent = AppTheme.SecondaryButton("مناسبة جديدة");
        addEvent.Width = 120;
        addEvent.Click += (_, _) => AddEventForSelected();

        var exportBtn = AppTheme.SecondaryButton("تصدير JSON");
        exportBtn.Width = 110;
        exportBtn.Click += async (_, _) => await ExportAsync();

        var settingsBtn = AppTheme.SecondaryButton("التخزين");
        settingsBtn.Width = 90;
        settingsBtn.Click += (_, _) =>
        {
            using var f = new SettingsForm();
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                RefreshNationalities();
                ReloadPeople();
                UpdateStatus();
            }
        };

        bar.Controls.Add(addPerson);
        bar.Controls.Add(editPerson);
        bar.Controls.Add(deletePerson);
        bar.Controls.Add(addEvent);
        bar.Controls.Add(exportBtn);
        bar.Controls.Add(settingsBtn);
        bar.Controls.Add(searchBtn);
        bar.Controls.Add(_search);
        bar.Controls.Add(_filterNationality);
        return bar;
    }

    private Control BuildBody()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 620,
            BackColor = AppTheme.Bg
        };

        AppTheme.StyleGrid(_peopleGrid);
        _peopleGrid.Dock = DockStyle.Fill;
        _peopleGrid.SelectionChanged += (_, _) => OnPersonSelected();
        _peopleGrid.CellDoubleClick += (_, _) => EditSelectedPerson();

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(_peopleGrid);

        AppTheme.StyleGrid(_eventsGrid);
        _eventsGrid.Dock = DockStyle.Fill;
        _eventsGrid.CellDoubleClick += (_, _) => EditSelectedEvent();

        _personTitle.Dock = DockStyle.Top;
        _personTitle.Height = 36;
        _personTitle.Font = AppTheme.HeadingFont;
        _personTitle.ForeColor = AppTheme.Accent;
        _personTitle.TextAlign = ContentAlignment.MiddleRight;
        _personTitle.Text = "اختر شخصاً لعرض مناسباته";

        var eventsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4)
        };
        var editEv = AppTheme.SecondaryButton("تعديل المناسبة");
        editEv.Width = 120;
        editEv.Click += (_, _) => EditSelectedEvent();
        var delEv = AppTheme.SecondaryButton("حذف المناسبة");
        delEv.Width = 120;
        delEv.ForeColor = AppTheme.Danger;
        delEv.Click += (_, _) => DeleteSelectedEvent();
        eventsBar.Controls.Add(editEv);
        eventsBar.Controls.Add(delEv);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 28;
        _status.ForeColor = AppTheme.Muted;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.Padding = new Padding(8, 0, 8, 0);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(_eventsGrid);
        right.Controls.Add(eventsBar);
        right.Controls.Add(_personTitle);
        right.Controls.Add(_status);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        UpdateStatus();
        return split;
    }

    private void UpdateStatus()
    {
        _status.Text = $"التخزين: {DatabaseService.ProviderLabel}  |  المجلد: {DatabaseService.DataFolder}";
    }

    private void RefreshNationalities()
    {
        var selected = _filterNationality.SelectedItem?.ToString();
        using var db = DatabaseService.CreateContext();
        var list = db.People.AsNoTracking()
            .Select(p => p.Nationality)
            .Where(n => n != null && n != "")
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        _filterNationality.Items.Clear();
        _filterNationality.Items.Add("كل الجنسيات");
        foreach (var n in list)
            _filterNationality.Items.Add(n);

        var idx = 0;
        if (!string.IsNullOrEmpty(selected))
        {
            for (var i = 0; i < _filterNationality.Items.Count; i++)
            {
                if (_filterNationality.Items[i]?.ToString() == selected)
                {
                    idx = i;
                    break;
                }
            }
        }
        _filterNationality.SelectedIndex = idx;
    }

    private void ReloadPeople()
    {
        var q = _search.Text.Trim();
        var nat = _filterNationality.SelectedItem?.ToString();

        using var db = DatabaseService.CreateContext();
        IQueryable<Person> query = db.People.AsNoTracking();

        if (!string.IsNullOrEmpty(nat) && nat != "كل الجنسيات")
            query = query.Where(p => p.Nationality == nat);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(p =>
                p.NationalId.Contains(q) ||
                p.FullName.Contains(q) ||
                p.FatherName.Contains(q) ||
                p.Nationality.Contains(q) ||
                p.Residence.Contains(q) ||
                p.Tribe.Contains(q) ||
                p.Neighborhood.Contains(q));
        }

        var rows = query
            .OrderBy(p => p.FullName)
            .Select(p => new
            {
                p.Id,
                الرقم_الوطني = p.NationalId,
                الاسم = p.FullName,
                الأب = p.FatherName,
                الجنسية = p.Nationality,
                القبيلة = p.Tribe,
                الحي = p.Neighborhood,
                النوع = p.Gender,
                الميلاد = p.BirthDate,
                الإقامة = p.Residence,
                المناسبات = p.Events.Count
            })
            .ToList();

        _peopleGrid.DataSource = rows;
        if (_peopleGrid.Columns["Id"] is not null)
            _peopleGrid.Columns["Id"]!.Visible = false;

        if (_peopleGrid.Rows.Count > 0)
            _peopleGrid.Rows[0].Selected = true;
        else
        {
            _selectedPersonId = null;
            _eventsGrid.DataSource = null;
            _personTitle.Text = "لا توجد نتائج";
        }
    }

    private void OnPersonSelected()
    {
        if (_peopleGrid.CurrentRow?.DataBoundItem is null)
            return;

        var idProp = _peopleGrid.CurrentRow.DataBoundItem.GetType().GetProperty("Id");
        if (idProp?.GetValue(_peopleGrid.CurrentRow.DataBoundItem) is not int id)
            return;

        _selectedPersonId = id;
        LoadEvents(id);
    }

    private void LoadEvents(int personId)
    {
        using var db = DatabaseService.CreateContext();
        var person = db.People.AsNoTracking().FirstOrDefault(p => p.Id == personId);
        if (person is null) return;

        _personTitle.Text = $"{person.FullName}  —  {person.NationalId}  ({person.Nationality})";

        var events = db.LifeEvents.AsNoTracking()
            .Where(e => e.PersonId == personId)
            .OrderByDescending(e => e.EventDate)
            .ToList()
            .Select(e => new
            {
                e.Id,
                النوع = EventTypeLabels.ToArabic(e.Type),
                التاريخ = e.EventDate,
                المكان = e.Place,
                العنوان = e.Title,
                ذو_صلة = e.RelatedPersonName,
                التفاصيل = e.Details
            })
            .ToList();

        _eventsGrid.DataSource = events;
        if (_eventsGrid.Columns["Id"] is not null)
            _eventsGrid.Columns["Id"]!.Visible = false;
    }

    private int? SelectedPersonId()
    {
        if (_peopleGrid.CurrentRow?.DataBoundItem is null) return null;
        var idProp = _peopleGrid.CurrentRow.DataBoundItem.GetType().GetProperty("Id");
        return idProp?.GetValue(_peopleGrid.CurrentRow.DataBoundItem) is int id ? id : null;
    }

    private int? SelectedEventId()
    {
        if (_eventsGrid.CurrentRow?.DataBoundItem is null) return null;
        var idProp = _eventsGrid.CurrentRow.DataBoundItem.GetType().GetProperty("Id");
        return idProp?.GetValue(_eventsGrid.CurrentRow.DataBoundItem) is int id ? id : null;
    }

    private void EditSelectedPerson()
    {
        var id = SelectedPersonId();
        if (id is null) return;
        using var f = new PersonEditForm(id);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            RefreshNationalities();
            ReloadPeople();
        }
    }

    private void DeleteSelectedPerson()
    {
        var id = SelectedPersonId();
        if (id is null) return;
        if (MessageBox.Show("حذف هذا الشخص وجميع مناسباته؟", "تأكيد",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        using var db = DatabaseService.CreateContext();
        var p = db.People.Find(id.Value);
        if (p is null) return;
        db.People.Remove(p);
        db.SaveChanges();
        RefreshNationalities();
        ReloadPeople();
    }

    private void AddEventForSelected()
    {
        var id = SelectedPersonId();
        if (id is null)
        {
            MessageBox.Show("اختر شخصاً أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new EventEditForm(id.Value);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            ReloadPeople();
            LoadEvents(id.Value);
        }
    }

    private void EditSelectedEvent()
    {
        var personId = SelectedPersonId();
        var eventId = SelectedEventId();
        if (personId is null || eventId is null) return;

        using var f = new EventEditForm(personId.Value, eventId);
        if (f.ShowDialog(this) == DialogResult.OK)
            LoadEvents(personId.Value);
    }

    private void DeleteSelectedEvent()
    {
        var personId = SelectedPersonId();
        var eventId = SelectedEventId();
        if (personId is null || eventId is null) return;

        if (MessageBox.Show("حذف هذه المناسبة؟", "تأكيد",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        using var db = DatabaseService.CreateContext();
        var ev = db.LifeEvents.Find(eventId.Value);
        if (ev is null) return;
        db.LifeEvents.Remove(ev);
        db.SaveChanges();
        ReloadPeople();
        LoadEvents(personId.Value);
    }

    private async Task ExportAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = $"shakaba-archive-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            await ArchiveExportService.ExportJsonAsync(dlg.FileName);
            MessageBox.Show("تم تصدير الأرشيف بنجاح.", "تم",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("فشل التصدير:\n" + ex.Message, "خطأ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
