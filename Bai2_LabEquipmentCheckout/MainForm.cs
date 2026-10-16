using System.ComponentModel;
using System.Text;

namespace LabEquipmentCheckout;

public class MainForm : Form
{
    static readonly string[] Equipments =
        { "Laptop Dell", "Máy chiếu", "Oscilloscope", "Bộ Arduino Uno", "Đồng hồ vạn năng", "Router Cisco" };
    static readonly string[] FilterItems = { "Tất cả", "Đang mượn", "Quá hạn", "Đã trả" };

    readonly BindingList<Checkout> _all = new();
    readonly BindingList<Checkout> _view = new();
    readonly BindingSource _bs = new();
    readonly ErrorProvider _err = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

    TextBox txtId, txtStudentId, txtStudentName, txtSearch, txtStatus;
    ComboBox cboEquipment, cboFilter;
    NumericUpDown numQty;
    DateTimePicker dtpBorrow, dtpDue, dtpReturn;
    DataGridView grid;
    ToolStripStatusLabel lblTotal, lblShown;

    Checkout _selected;
    bool _loading;

    public MainForm()
    {
        Text = "Lab Equipment Checkout";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 700);
        MinimumSize = new Size(900, 580);

        BuildUi();
        SeedData();
        ApplyFilter();
    }

    // ================= GIAO DIỆN =================
    void BuildUi()
    {
        var menu = new MenuStrip();
        var mFile = new ToolStripMenuItem("&Tệp");
        mFile.DropDownItems.Add(new ToolStripMenuItem("Xuất CSV (phiếu đang lọc)...", null, (s, e) => ExportCsv()));
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add(new ToolStripMenuItem("Thoát", null, (s, e) => Close()));
        menu.Items.Add(mFile);
        MainMenuStrip = menu;

        var status = new StatusStrip();
        lblTotal = new ToolStripStatusLabel();
        lblShown = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        status.Items.Add(lblTotal);
        status.Items.Add(lblShown);

        // ---- Khu vực nhập phiếu mượn ----
        var input = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(10)
        };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        txtId = new TextBox();
        txtStudentId = new TextBox();
        txtStudentName = new TextBox();
        cboEquipment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboEquipment.Items.AddRange(Equipments);
        numQty = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = 1 };
        dtpBorrow = new DateTimePicker { Format = DateTimePickerFormat.Short };
        dtpDue = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(7) };
        // Ô tick = đã trả (có ngày trả thực tế); bỏ tick = chưa trả
        dtpReturn = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
        txtStatus = new TextBox { ReadOnly = true, TabStop = false };

        AddField(input, "Mã phiếu:", txtId, 0, 0);
        AddField(input, "Mã sinh viên:", txtStudentId, 2, 0);
        AddField(input, "Họ tên sinh viên:", txtStudentName, 0, 1);
        AddField(input, "Thiết bị:", cboEquipment, 2, 1);
        AddField(input, "Số lượng:", numQty, 0, 2);
        AddField(input, "Ngày mượn:", dtpBorrow, 2, 2);
        AddField(input, "Hạn trả:", dtpDue, 0, 3);
        AddField(input, "Ngày trả thực tế:", dtpReturn, 2, 3);
        AddField(input, "Tình trạng:", txtStatus, 0, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left };
        buttons.Controls.Add(MakeButton("Thêm", 80, (s, e) => AddCheckout()));
        buttons.Controls.Add(MakeButton("Cập nhật", 90, (s, e) => UpdateCheckout()));
        buttons.Controls.Add(MakeButton("Xóa", 80, (s, e) => DeleteCheckout()));
        buttons.Controls.Add(MakeButton("Đánh dấu đã trả", 130, (s, e) => MarkReturned()));
        buttons.Controls.Add(MakeButton("Làm mới", 90, (s, e) => ClearInputs()));
        input.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.Controls.Add(buttons, 1, 5);
        input.SetColumnSpan(buttons, 3);

        // ---- Tìm kiếm / lọc ----
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        txtSearch = new TextBox { Width = 220, PlaceholderText = "Mã sinh viên hoặc tên thiết bị" };
        cboFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        cboFilter.Items.AddRange(FilterItems);
        cboFilter.SelectedIndex = 0;
        filter.Controls.Add(MakeLabel("Tìm kiếm:"));
        filter.Controls.Add(txtSearch);
        filter.Controls.Add(MakeLabel("Lọc phiếu:"));
        filter.Controls.Add(cboFilter);
        txtSearch.TextChanged += (s, e) => ApplyFilter();
        cboFilter.SelectedIndexChanged += (s, e) => ApplyFilter();

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
        grid.Columns.Add(Col("Id", "Mã phiếu", 9));
        grid.Columns.Add(Col("StudentId", "Mã SV", 10));
        grid.Columns.Add(Col("StudentName", "Họ tên", 18));
        grid.Columns.Add(Col("Equipment", "Thiết bị", 17));
        grid.Columns.Add(Col("Quantity", "SL", 5));
        grid.Columns.Add(DateCol("BorrowDate", "Ngày mượn"));
        grid.Columns.Add(DateCol("DueDate", "Hạn trả"));
        grid.Columns.Add(DateCol("ReturnDate", "Ngày trả thực tế"));
        grid.Columns.Add(Col("Status", "Tình trạng", 10));
        _bs.DataSource = _view;
        grid.DataSource = _bs;
        grid.SelectionChanged += (s, e) => LoadSelected();
        // Tô màu đỏ nhạt cho phiếu quá hạn
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count &&
                grid.Rows[e.RowIndex].DataBoundItem is Checkout c && c.Status == "Quá hạn")
                e.CellStyle.BackColor = Color.MistyRose;
        };

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

    static DataGridViewTextBoxColumn DateCol(string prop, string header)
    {
        var c = Col(prop, header, 12);
        c.DefaultCellStyle.Format = "dd/MM/yyyy";
        c.DefaultCellStyle.NullValue = "";   // ngày trả thực tế chưa có → để trống
        return c;
    }

    static Label MakeLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };

    static Button MakeButton(string text, int width, EventHandler onClick)
    {
        var b = new Button { Text = text, Width = width, Height = 30 };
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
        var t = DateTime.Today;
        _all.Add(new Checkout { Id = "PM001", StudentId = "SV2021001", StudentName = "Nguyễn Văn An", Equipment = "Laptop Dell", Quantity = 1, BorrowDate = t.AddDays(-3), DueDate = t.AddDays(4) });
        _all.Add(new Checkout { Id = "PM002", StudentId = "SV2021002", StudentName = "Trần Thị Bích", Equipment = "Máy chiếu", Quantity = 1, BorrowDate = t.AddDays(-10), DueDate = t.AddDays(-3) });
        _all.Add(new Checkout { Id = "PM003", StudentId = "SV2021003", StudentName = "Lê Quang Huy", Equipment = "Bộ Arduino Uno", Quantity = 2, BorrowDate = t.AddDays(-8), DueDate = t.AddDays(-1), ReturnDate = t.AddDays(-2) });
        _all.Add(new Checkout { Id = "PM004", StudentId = "SV2021004", StudentName = "Phạm Mai Anh", Equipment = "Oscilloscope", Quantity = 1, BorrowDate = t.AddDays(-5), DueDate = t.AddDays(-2) });
    }

    // ================= LỌC / TÌM KIẾM / THỐNG KÊ =================
    void ApplyFilter()
    {
        if (grid == null) return;
        var keep = _selected;
        string kw = txtSearch.Text.Trim().ToLowerInvariant();

        IEnumerable<Checkout> q = _all;
        if (kw.Length > 0)
            q = q.Where(c => c.StudentId.ToLowerInvariant().Contains(kw) || c.Equipment.ToLowerInvariant().Contains(kw));
        if (cboFilter.SelectedIndex > 0)
            q = q.Where(c => c.Status == (string)cboFilter.SelectedItem);

        var list = q.OrderByDescending(c => c.BorrowDate).ThenBy(c => c.Id).ToList();

        _loading = true;
        _view.RaiseListChangedEvents = false;
        _view.Clear();
        foreach (var c in list) _view.Add(c);
        _view.RaiseListChangedEvents = true;
        _view.ResetBindings();
        _loading = false;

        grid.ClearSelection();
        int idx = keep == null ? -1 : _view.IndexOf(keep);
        if (idx >= 0)
        {
            grid.Rows[idx].Selected = true;
            grid.CurrentCell = grid.Rows[idx].Cells[0];
            _selected = keep;
        }
        else _selected = null;

        int overdue = _all.Count(c => c.Status == "Quá hạn");
        lblTotal.Text = $"Tổng số phiếu: {_all.Count}   |   Quá hạn: {overdue}";
        lblShown.Text = $"Đang hiển thị: {_view.Count}";
    }

    // ================= CHỌN DÒNG → NẠP LÊN FORM =================
    void LoadSelected()
    {
        if (_loading || grid.SelectedRows.Count == 0) return;
        if (grid.SelectedRows[0].DataBoundItem is not Checkout c) return;

        _selected = c;
        _loading = true;
        txtId.Text = c.Id;
        txtStudentId.Text = c.StudentId;
        txtStudentName.Text = c.StudentName;
        cboEquipment.SelectedItem = c.Equipment;
        numQty.Value = Math.Clamp(c.Quantity, (int)numQty.Minimum, (int)numQty.Maximum);
        dtpBorrow.Value = c.BorrowDate;
        dtpDue.Value = c.DueDate;
        if (c.ReturnDate.HasValue) { dtpReturn.Value = c.ReturnDate.Value; dtpReturn.Checked = true; }
        else { dtpReturn.Value = DateTime.Today; dtpReturn.Checked = false; }
        txtStatus.Text = c.Status;
        _loading = false;
        _err.Clear();
    }

    void ClearInputs()
    {
        _selected = null;
        grid.ClearSelection();
        txtId.Clear(); txtStudentId.Clear(); txtStudentName.Clear();
        cboEquipment.SelectedIndex = -1;
        numQty.Value = 1;
        dtpBorrow.Value = DateTime.Today;
        dtpDue.Value = DateTime.Today.AddDays(7);
        dtpReturn.Value = DateTime.Today; dtpReturn.Checked = false;
        txtStatus.Clear();
        _err.Clear();
        txtId.Focus();
    }

    // ================= KIỂM TRA DỮ LIỆU =================
    bool ValidateForm(Checkout editing)
    {
        _err.Clear();
        bool ok = true;
        void Fail(Control c, string msg) { _err.SetError(c, msg); ok = false; }

        string id = txtId.Text.Trim();
        if (id.Length == 0) Fail(txtId, "Mã phiếu không được để trống.");
        else if (_all.Any(c => c != editing && c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            Fail(txtId, "Mã phiếu đã tồn tại.");

        if (txtStudentId.Text.Trim().Length == 0) Fail(txtStudentId, "Mã sinh viên không được để trống.");
        if (txtStudentName.Text.Trim().Length == 0) Fail(txtStudentName, "Vui lòng nhập họ tên sinh viên.");
        if (cboEquipment.SelectedIndex < 0) Fail(cboEquipment, "Vui lòng chọn thiết bị.");
        if (numQty.Value <= 0) Fail(numQty, "Số lượng phải lớn hơn 0.");

        if (dtpDue.Value.Date < dtpBorrow.Value.Date)
            Fail(dtpDue, "Hạn trả không được trước ngày mượn.");
        if (dtpReturn.Checked && dtpReturn.Value.Date < dtpBorrow.Value.Date)
            Fail(dtpReturn, "Ngày trả thực tế không được trước ngày mượn.");
        return ok;
    }

    void Fill(Checkout c)
    {
        c.Id = txtId.Text.Trim();
        c.StudentId = txtStudentId.Text.Trim();
        c.StudentName = txtStudentName.Text.Trim();
        c.Equipment = (string)cboEquipment.SelectedItem;
        c.Quantity = (int)numQty.Value;
        c.BorrowDate = dtpBorrow.Value.Date;
        c.DueDate = dtpDue.Value.Date;
        c.ReturnDate = dtpReturn.Checked ? dtpReturn.Value.Date : null;
    }

    // ================= THÊM / SỬA / XÓA / TRẢ =================
    void AddCheckout()
    {
        if (!ValidateForm(null)) return;
        var c = new Checkout();
        Fill(c);
        _all.Add(c);
        _selected = c;
        ApplyFilter();
        txtStatus.Text = c.Status;
        MessageBox.Show("Đã thêm phiếu mượn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void UpdateCheckout()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một phiếu trong danh sách.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ValidateForm(_selected)) return;
        if (MessageBox.Show("Cập nhật phiếu mượn này?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Fill(_selected);
        ApplyFilter();
        txtStatus.Text = _selected?.Status ?? "";
    }

    void DeleteCheckout()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một phiếu để xóa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"Xóa phiếu {_selected.Id}?", "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _all.Remove(_selected);
        _selected = null;
        ApplyFilter();
        ClearInputs();
    }

    /// <summary>Đánh dấu đã trả: ngày trả thực tế = hôm nay.</summary>
    void MarkReturned()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một phiếu cần đánh dấu đã trả.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_selected.ReturnDate.HasValue)
        {
            MessageBox.Show("Phiếu này đã được trả rồi.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (DateTime.Today < _selected.BorrowDate.Date)
        {
            MessageBox.Show("Ngày trả thực tế không được trước ngày mượn.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _selected.ReturnDate = DateTime.Today;
        var keep = _selected;
        ApplyFilter();                 // cập nhật bảng + thanh trạng thái
        if (keep != null && _view.Contains(keep)) LoadSelected();
        else ClearInputs();            // phiếu biến mất khỏi bộ lọc hiện tại
    }

    // ================= XUẤT CSV =================
    static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    void ExportCsv()
    {
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"phieu_muon_{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var lines = new List<string> { "Mã phiếu,Mã sinh viên,Họ tên,Thiết bị,Số lượng,Ngày mượn,Hạn trả,Ngày trả thực tế,Tình trạng" };
        foreach (var c in _view)
            lines.Add(string.Join(",", Csv(c.Id), Csv(c.StudentId), Csv(c.StudentName), Csv(c.Equipment),
                c.Quantity, c.BorrowDate.ToString("dd/MM/yyyy"), c.DueDate.ToString("dd/MM/yyyy"),
                c.ReturnDate?.ToString("dd/MM/yyyy") ?? "", Csv(c.Status)));
        File.WriteAllLines(dlg.FileName, lines, new UTF8Encoding(true));
        MessageBox.Show($"Đã xuất {_view.Count} phiếu.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
