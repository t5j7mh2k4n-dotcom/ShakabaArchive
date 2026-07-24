using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Forms;

public sealed class EventEditForm : Form
{
    private readonly int _personId;
    private readonly int? _eventId;
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, ShowCheckBox = true };
    private readonly TextBox _place = AppTheme.Field();
    private readonly TextBox _title = AppTheme.Field();
    private readonly TextBox _related = AppTheme.Field();
    private readonly TextBox _details = AppTheme.Field();
    private readonly TextBox _source = AppTheme.Field();

    public EventEditForm(int personId, int? eventId = null)
    {
        _personId = personId;
        _eventId = eventId;
        AppTheme.ApplyForm(this);
        Text = eventId is null ? "إضافة مناسبة" : "تعديل مناسبة";
        Size = new Size(540, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        foreach (var (value, label) in EventTypeLabels.All)
            _type.Items.Add(new TypeItem(value, label));
        _type.DisplayMember = nameof(TypeItem.Label);
        _type.SelectedIndex = 0;
        _place.Text = "الشكابة شاع الدين";
        _details.Multiline = true;
        _details.Height = 80;
        _details.ScrollBars = ScrollBars.Vertical;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (var i = 0; i < 7; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 5 ? 90 : 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        AddRow(layout, 0, "نوع المناسبة", _type);
        AddRow(layout, 1, "التاريخ", _date);
        AddRow(layout, 2, "المكان", _place);
        AddRow(layout, 3, "العنوان", _title);
        AddRow(layout, 4, "الطرف الآخر / ذو صلة", _related);
        AddRow(layout, 5, "التفاصيل", _details);
        AddRow(layout, 6, "مصدر المعلومة", _source);

        var save = AppTheme.PrimaryButton("حفظ المناسبة");
        save.Dock = DockStyle.Fill;
        save.Click += (_, _) => Save();
        layout.SetColumnSpan(save, 2);
        layout.Controls.Add(save, 0, 7);

        Controls.Add(layout);
        if (eventId is not null)
            LoadEvent(eventId.Value);
        else
            _title.Text = EventTypeLabels.ToArabic(EventType.Birth);
        _type.SelectedIndexChanged += (_, _) =>
        {
            if (_type.SelectedItem is TypeItem item && string.IsNullOrWhiteSpace(_title.Text))
                _title.Text = item.Label;
        };
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

    private void LoadEvent(int id)
    {
        using var db = DatabaseService.CreateContext();
        var e = db.LifeEvents.Find(id);
        if (e is null) return;

        for (var i = 0; i < _type.Items.Count; i++)
        {
            if (_type.Items[i] is TypeItem t && t.Value == e.Type)
            {
                _type.SelectedIndex = i;
                break;
            }
        }

        if (e.EventDate is { } d)
        {
            _date.Checked = true;
            _date.Value = d;
        }
        else _date.Checked = false;

        _place.Text = e.Place;
        _title.Text = e.Title;
        _related.Text = e.RelatedPersonName;
        _details.Text = e.Details;
        _source.Text = e.SourceNote;
    }

    private void Save()
    {
        if (_type.SelectedItem is not TypeItem typeItem)
            return;

        using var db = DatabaseService.CreateContext();
        LifeEvent ev;
        if (_eventId is null)
        {
            ev = new LifeEvent { PersonId = _personId, CreatedAt = DateTime.UtcNow };
            db.LifeEvents.Add(ev);
        }
        else
        {
            ev = db.LifeEvents.Find(_eventId.Value)!;
            if (ev is null) return;
        }

        ev.Type = typeItem.Value;
        ev.EventDate = _date.Checked ? _date.Value.Date : null;
        ev.Place = _place.Text.Trim();
        ev.Title = string.IsNullOrWhiteSpace(_title.Text) ? typeItem.Label : _title.Text.Trim();
        ev.RelatedPersonName = _related.Text.Trim();
        ev.Details = _details.Text.Trim();
        ev.SourceNote = _source.Text.Trim();

        db.SaveChanges();
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record TypeItem(EventType Value, string Label);
}
