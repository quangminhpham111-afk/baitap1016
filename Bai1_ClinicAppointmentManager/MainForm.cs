using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicAppointmentManager;

public class MainForm : Form
{
    static readonly string[] Statuses = { "Chờ khám", "Đang khám", "Hoàn tất", "Đã hủy" };
    static readonly string[] Doctors =
        { "BS. Nguyễn Văn An", "BS. Trần Thị Bình", "BS. Lê Minh Cường", "BS. Phạm Thu Dung" };

    // Danh sách gốc và danh sách đang hiển thị (sau khi lọc)
    readonly BindingList<Appointment> _all = new();
    readonly BindingList<Appointment> _view = new();
    readonly BindingSource _bs = new();
    readonly ErrorProvider _err = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

    TextBox txtId, txtName, txtPhone, txtReason, txtSearch;
    DateTimePicker dtpTime, dtpFilter;
    ComboBox cboDoctor, cboStatus, cboFilterStatus;
    DataGridView grid;
    ToolStripStatusLabel lblCount;

    Appointment _selected;   // lịch hẹn đang chọn
    bool _loading;           // chặn sự kiện khi đang nạp dữ liệu

    public MainForm()
    {
        Text = "Clinic Appointment Manager";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1050, 680);
        MinimumSize = new Size(850, 550);

        BuildUi();
        SeedData();
        ApplyFilter();
    }

    // ================= GIAO DIỆN =================
    void BuildUi()
    {
        // ---- MenuStrip ----
        var menu = new MenuStrip();
        var mFile = new ToolStripMenuItem("&Tệp");
        mFile.DropDownItems.Add(new ToolStripMenuItem("Xuất CSV...", null, (s, e) => ExportCsv()));
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add(new ToolStripMenuItem("Thoát", null, (s, e) => Close()));
        menu.Items.Add(mFile);
        MainMenuStrip = menu;

        // ---- StatusStrip ----
        var status = new StatusStrip();
        lblCount = new ToolStripStatusLabel();
        status.Items.Add(lblCount);

        // ---- Khu vực nhập liệu (TableLayoutPanel) ----
        var input = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(10)
        };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        txtId = new TextBox();
        txtName = new TextBox();
        txtPhone = new TextBox();
        txtReason = new TextBox();
        dtpTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm" };
        cboDoctor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboDoctor.Items.AddRange(Doctors);
        cboDoctor.SelectedIndex = 0;
        cboStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboStatus.Items.AddRange(Statuses);
        cboStatus.SelectedIndex = 0;

        AddField(input, "Mã lịch hẹn:", txtId, 0, 0);
        AddField(input, "Họ tên bệnh nhân:", txtName, 2, 0);
        AddField(input, "Số điện thoại:", txtPhone, 0, 1);
        AddField(input, "Ngày giờ hẹn:", dtpTime, 2, 1);
        AddField(input, "Bác sĩ:", cboDoctor, 0, 2);
        AddField(input, "Trạng thái:", cboStatus, 2, 2);
        AddField(input, "Lý do khám:", txtReason, 0, 3);
        input.SetColumnSpan(txtReason, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left };
        buttons.Controls.Add(MakeButton("Thêm", (s, e) => AddAppointment()));
        buttons.Controls.Add(MakeButton("Cập nhật", (s, e) => UpdateAppointment()));
        buttons.Controls.Add(MakeButton("Xóa", (s, e) => DeleteAppointment()));
        buttons.Controls.Add(MakeButton("Làm mới", (s, e) => ClearInputs()));
        input.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.Controls.Add(buttons, 1, 4);
        input.SetColumnSpan(buttons, 3);

        // ---- Thanh tìm kiếm / lọc ----
        var filter = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 5, 10, 5)
        };
        txtSearch = new TextBox { Width = 200, PlaceholderText = "Tên bệnh nhân hoặc SĐT" };
        dtpFilter = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Width = 120
        };
        cboFilterStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        cboFilterStatus.Items.Add("Tất cả");
        cboFilterStatus.Items.AddRange(Statuses);
        cboFilterStatus.SelectedIndex = 0;

        filter.Controls.Add(MakeLabel("Tìm kiếm:"));
        filter.Controls.Add(txtSearch);
        filter.Controls.Add(MakeLabel("Ngày hẹn:"));
        filter.Controls.Add(dtpFilter);
        filter.Controls.Add(MakeLabel("Trạng thái:"));
        filter.Controls.Add(cboFilterStatus);

        txtSearch.TextChanged += (s, e) => ApplyFilter();           // tìm kiếm tức thời
        dtpFilter.ValueChanged += (s, e) => ApplyFilter();
        cboFilterStatus.SelectedIndexChanged += (s, e) => ApplyFilter();

        // ---- DataGridView ----
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window
        };
        grid.Columns.Add(Col("Id", "Mã lịch hẹn", 12));
        grid.Columns.Add(Col("PatientName", "Họ tên", 22));
        grid.Columns.Add(Col("Phone", "Số điện thoại", 14));
        var colTime = Col("Time", "Ngày giờ hẹn", 16);
        colTime.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
        grid.Columns.Add(colTime);
        grid.Columns.Add(Col("Doctor", "Bác sĩ", 18));
        grid.Columns.Add(Col("Reason", "Lý do khám", 25));
        grid.Columns.Add(Col("Status", "Trạng thái", 12));
        _bs.DataSource = _view;
        grid.DataSource = _bs;
        grid.SelectionChanged += (s, e) => LoadSelected();

        // Thứ tự Add quyết định thứ tự docking (control Fill phải được Add đầu tiên)
        Controls.Add(grid);
        Controls.Add(filter);
        Controls.Add(input);
        Controls.Add(status);
        Controls.Add(menu);
    }

    static DataGridViewTextBoxColumn Col(string prop, string header, float weight) => new()
    {
        DataPropertyName = prop, HeaderText = header, Name = "col" + prop, FillWeight = weight
    };

    static Label MakeLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };

    static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, Width = 90, Height = 30 };
        b.Click += onClick;
        return b;
    }

    static void AddField(TableLayoutPanel t, string label, Control c, int col, int row)
    {
        if (col == 0) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, col, row);
        c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        t.Controls.Add(c, col + 1, row);
    }

    void SeedData()
    {
        var d = DateTime.Today.AddDays(1);
        _all.Add(new Appointment { Id = "LH001", PatientName = "Nguyễn Văn Nam", Phone = "0912345678", Time = d.AddHours(8), Doctor = Doctors[0], Reason = "Khám tổng quát", Status = "Chờ khám" });
        _all.Add(new Appointment { Id = "LH002", PatientName = "Trần Thị Lan", Phone = "0987654321", Time = d.AddHours(9.5), Doctor = Doctors[1], Reason = "Đau đầu kéo dài", Status = "Đang khám" });
        _all.Add(new Appointment { Id = "LH003", PatientName = "Lê Hoàng Minh", Phone = "0901122334", Time = d.AddHours(14), Doctor = Doctors[2], Reason = "Tái khám tim mạch", Status = "Hoàn tất" });
        _all.Add(new Appointment { Id = "LH004", PatientName = "Phạm Thu Hà", Phone = "0933445566", Time = d.AddDays(1).AddHours(10), Doctor = Doctors[3], Reason = "Khám da liễu", Status = "Đã hủy" });
    }

    // ================= LỌC / TÌM KIẾM =================
    void ApplyFilter()
    {
        if (grid == null) return;
        var keep = _selected;
        string kw = txtSearch.Text.Trim().ToLowerInvariant();

        IEnumerable<Appointment> q = _all;
        if (kw.Length > 0)
            q = q.Where(a => a.PatientName.ToLowerInvariant().Contains(kw) || a.Phone.Contains(kw.Replace(" ", "")));
        if (dtpFilter.Checked)
            q = q.Where(a => a.Time.Date == dtpFilter.Value.Date);
        if (cboFilterStatus.SelectedIndex > 0)
            q = q.Where(a => a.Status == (string)cboFilterStatus.SelectedItem);

        var list = q.OrderBy(a => a.Time).ToList();

        _loading = true;
        _view.RaiseListChangedEvents = false;
        _view.Clear();
        foreach (var a in list) _view.Add(a);
        _view.RaiseListChangedEvents = true;
        _view.ResetBindings();
        _loading = false;

        // Giữ lại dòng đang chọn nếu còn trong kết quả lọc
        grid.ClearSelection();
        int idx = keep == null ? -1 : _view.IndexOf(keep);
        if (idx >= 0)
        {
            grid.Rows[idx].Selected = true;
            grid.CurrentCell = grid.Rows[idx].Cells[0];
            _selected = keep;
        }
        else _selected = null;

        lblCount.Text = $"Số lịch hẹn sau khi lọc: {_view.Count} / {_all.Count}";
    }

    // ================= CHỌN DÒNG → NẠP LÊN FORM =================
    void LoadSelected()
    {
        if (_loading || grid.SelectedRows.Count == 0) return;
        if (grid.SelectedRows[0].DataBoundItem is not Appointment a) return;

        _selected = a;
        _loading = true;
        txtId.Text = a.Id;
        txtName.Text = a.PatientName;
        txtPhone.Text = a.Phone;
        dtpTime.Value = a.Time;
        cboDoctor.SelectedItem = a.Doctor;
        cboStatus.SelectedItem = a.Status;
        txtReason.Text = a.Reason;
        _loading = false;
        _err.Clear();
    }

    void ClearInputs()
    {
        _selected = null;
        grid.ClearSelection();
        txtId.Clear(); txtName.Clear(); txtPhone.Clear(); txtReason.Clear();
        dtpTime.Value = DateTime.Now.AddHours(1);
        cboDoctor.SelectedIndex = 0;
        cboStatus.SelectedIndex = 0;
        _err.Clear();
        txtId.Focus();
    }

    // ================= KIỂM TRA DỮ LIỆU =================
    static readonly Regex PhoneRegex = new(@"^(0|\+84)\d{9}$");

    DateTime PickedTime
    {
        get { var v = dtpTime.Value; return new DateTime(v.Year, v.Month, v.Day, v.Hour, v.Minute, 0); }
    }

    /// <param name="editing">Lịch hẹn đang sửa (null khi thêm mới).</param>
    bool ValidateForm(Appointment editing)
    {
        _err.Clear();
        bool ok = true;
        void Fail(Control c, string msg) { _err.SetError(c, msg); ok = false; }

        string id = txtId.Text.Trim();
        if (id.Length == 0) Fail(txtId, "Mã lịch hẹn không được để trống.");
        else if (_all.Any(a => a != editing && a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            Fail(txtId, "Mã lịch hẹn đã tồn tại.");

        if (txtName.Text.Trim().Length == 0) Fail(txtName, "Vui lòng nhập họ tên bệnh nhân.");

        string phone = txtPhone.Text.Trim().Replace(" ", "");
        if (phone.Length == 0) Fail(txtPhone, "Vui lòng nhập số điện thoại.");
        else if (!PhoneRegex.IsMatch(phone)) Fail(txtPhone, "Số điện thoại không hợp lệ (VD: 0912345678).");

        if (txtReason.Text.Trim().Length == 0) Fail(txtReason, "Vui lòng nhập lý do khám.");

        // Ngày hẹn không nằm trong quá khứ. Khi SỬA mà không đổi ngày giờ thì bỏ qua
        // (để vẫn cập nhật được trạng thái của lịch hẹn cũ).
        bool dateChanged = editing == null || PickedTime != editing.Time;
        if (dateChanged && PickedTime.Date < DateTime.Today)
            Fail(dtpTime, "Ngày hẹn không được nằm trong quá khứ.");

        if (cboDoctor.SelectedIndex < 0) Fail(cboDoctor, "Vui lòng chọn bác sĩ.");
        if (cboStatus.SelectedIndex < 0) Fail(cboStatus, "Vui lòng chọn trạng thái.");
        return ok;
    }

    void Fill(Appointment a)
    {
        a.Id = txtId.Text.Trim();
        a.PatientName = txtName.Text.Trim();
        a.Phone = txtPhone.Text.Trim().Replace(" ", "");
        a.Time = PickedTime;
        a.Doctor = (string)cboDoctor.SelectedItem;
        a.Reason = txtReason.Text.Trim();
        a.Status = (string)cboStatus.SelectedItem;
    }

    // ================= THÊM / SỬA / XÓA =================
    void AddAppointment()
    {
        if (!ValidateForm(null)) return;
        var a = new Appointment();
        Fill(a);
        _all.Add(a);
        _selected = a;
        ApplyFilter();
        MessageBox.Show("Đã thêm lịch hẹn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void UpdateAppointment()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một lịch hẹn trong danh sách.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ValidateForm(_selected)) return;
        if (MessageBox.Show("Cập nhật lịch hẹn này?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Fill(_selected);
        ApplyFilter();
    }

    void DeleteAppointment()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một lịch hẹn để xóa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"Xóa lịch hẹn {_selected.Id} của {_selected.PatientName}?", "Xác nhận xóa",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _all.Remove(_selected);
        _selected = null;
        ApplyFilter();
        ClearInputs();
    }

    // ================= XUẤT CSV =================
    static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    void ExportCsv()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"lich_hen_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var lines = new List<string> { "Mã lịch hẹn,Họ tên,Số điện thoại,Ngày giờ hẹn,Bác sĩ,Lý do khám,Trạng thái" };
        foreach (var a in _view)
            lines.Add(string.Join(",", Csv(a.Id), Csv(a.PatientName), Csv(a.Phone),
                Csv(a.Time.ToString("dd/MM/yyyy HH:mm")), Csv(a.Doctor), Csv(a.Reason), Csv(a.Status)));
        File.WriteAllLines(dlg.FileName, lines, new UTF8Encoding(true)); // UTF-8 BOM để Excel đọc đúng tiếng Việt
        MessageBox.Show($"Đã xuất {_view.Count} lịch hẹn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
