using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace RepairServiceTracker;

public class MainForm : Form
{
    static readonly string[] Statuses = { "Mới tiếp nhận", "Đang sửa", "Chờ linh kiện", "Hoàn tất", "Đã hủy" };
    static readonly string[] DeviceTypes = { "Điện thoại", "Laptop", "Máy tính bảng", "Máy in", "Tivi", "Khác" };
    static readonly CultureInfo Vi = new("vi-VN");   // 1.500.000 (dấu . ngăn cách hàng nghìn)

    readonly BindingList<RepairOrder> _all = new();
    readonly BindingList<RepairOrder> _view = new();
    readonly BindingSource _bs = new();
    readonly ErrorProvider _err = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

    TextBox txtId, txtName, txtPhone, txtCost, txtDesc, txtSearch;
    ComboBox cboDevice, cboStatus, cboFilter;
    DateTimePicker dtpReceived;
    PictureBox pic;
    DataGridView grid;
    ToolStripStatusLabel lblCount, lblTotal;

    RepairOrder _selected;
    string _imagePath = "";   // ảnh đang chọn trên form
    bool _loading;

    public MainForm()
    {
        Text = "Repair Service Tracker";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1150, 760);
        MinimumSize = new Size(950, 650);

        BuildUi();
        SeedData();
        ApplyFilter();
    }

    // ================= GIAO DIỆN =================
    void BuildUi()
    {
        var menu = new MenuStrip();
        var mFile = new ToolStripMenuItem("&Tệp");
        mFile.DropDownItems.Add(new ToolStripMenuItem("Xuất CSV (danh sách đang hiển thị)...", null, (s, e) => ExportCsv()));
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add(new ToolStripMenuItem("Thoát", null, (s, e) => Application.Exit()));
        menu.Items.Add(mFile);
        MainMenuStrip = menu;

        var status = new StatusStrip();
        lblCount = new ToolStripStatusLabel();
        lblTotal = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        status.Items.Add(lblCount);
        status.Items.Add(lblTotal);

        // ---- Khu vực trên: bên trái nhập liệu, bên phải ảnh (TableLayoutPanel) ----
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 310, ColumnCount = 2, RowCount = 1, Padding = new Padding(5) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // -- Bên trái --
        var input = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(5) };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        txtId = new TextBox();
        txtName = new TextBox();
        txtPhone = new TextBox();
        txtCost = new TextBox { TextAlign = HorizontalAlignment.Right, Text = "0" };
        txtDesc = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical };
        cboDevice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboDevice.Items.AddRange(DeviceTypes);
        cboDevice.SelectedIndex = 0;
        cboStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboStatus.Items.AddRange(Statuses);
        cboStatus.SelectedIndex = 0;
        dtpReceived = new DateTimePicker { Format = DateTimePickerFormat.Short };

        AddField(input, "Mã đơn:", txtId, 0, 0);
        AddField(input, "Tên khách hàng:", txtName, 2, 0);
        AddField(input, "Số điện thoại:", txtPhone, 0, 1);
        AddField(input, "Loại thiết bị:", cboDevice, 2, 1);
        AddField(input, "Ngày tiếp nhận:", dtpReceived, 0, 2);
        AddField(input, "Chi phí dự kiến (đ):", txtCost, 2, 2);
        AddField(input, "Trạng thái:", cboStatus, 0, 3);

        // Hàng mô tả lỗi chiếm phần chiều cao còn lại
        input.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        input.Controls.Add(new Label { Text = "Mô tả lỗi:", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, 0, 4);
        txtDesc.Dock = DockStyle.Fill;
        input.Controls.Add(txtDesc, 1, 4);
        input.SetColumnSpan(txtDesc, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left };
        buttons.Controls.Add(MakeButton("Thêm", 80, (s, e) => AddOrder()));
        buttons.Controls.Add(MakeButton("Cập nhật", 90, (s, e) => UpdateOrder()));
        buttons.Controls.Add(MakeButton("Xóa", 80, (s, e) => DeleteOrder()));
        buttons.Controls.Add(MakeButton("Làm mới", 90, (s, e) => ClearInputs()));
        input.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.Controls.Add(buttons, 1, 5);
        input.SetColumnSpan(buttons, 3);

        // -- Bên phải: ảnh thiết bị --
        var picPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(5) };
        picPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        picPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,     // giữ đúng tỷ lệ ảnh
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.WhiteSmoke
        };
        var picButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        picButtons.Controls.Add(MakeButton("Chọn ảnh...", 110, (s, e) => ChooseImage()));
        picButtons.Controls.Add(MakeButton("Bỏ ảnh", 90, (s, e) => { _imagePath = ""; ShowImage(""); }));
        picPanel.Controls.Add(pic, 0, 0);
        picPanel.Controls.Add(picButtons, 0, 1);

        top.Controls.Add(input, 0, 0);
        top.Controls.Add(picPanel, 1, 0);

        // ---- Tìm kiếm / lọc ----
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        txtSearch = new TextBox { Width = 240, PlaceholderText = "Mã đơn, tên khách hàng hoặc SĐT" };
        cboFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        cboFilter.Items.Add("Tất cả");
        cboFilter.Items.AddRange(Statuses);
        cboFilter.SelectedIndex = 0;
        filter.Controls.Add(MakeLabel("Tìm kiếm:"));
        filter.Controls.Add(txtSearch);
        filter.Controls.Add(MakeLabel("Trạng thái:"));
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
        grid.Columns.Add(Col("Id", "Mã đơn", 9));
        grid.Columns.Add(Col("CustomerName", "Khách hàng", 16));
        grid.Columns.Add(Col("Phone", "Số điện thoại", 12));
        grid.Columns.Add(Col("DeviceType", "Thiết bị", 11));
        grid.Columns.Add(Col("Description", "Mô tả lỗi", 22));
        var colDate = Col("ReceivedDate", "Ngày tiếp nhận", 11);
        colDate.DefaultCellStyle.Format = "dd/MM/yyyy";
        grid.Columns.Add(colDate);
        var colCost = Col("EstimatedCost", "Chi phí dự kiến", 12);
        colCost.DefaultCellStyle.Format = "N0";
        colCost.DefaultCellStyle.FormatProvider = Vi;
        colCost.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        grid.Columns.Add(colCost);
        grid.Columns.Add(Col("Status", "Trạng thái", 11));
        _bs.DataSource = _view;
        grid.DataSource = _bs;
        grid.SelectionChanged += (s, e) => LoadSelected();

        Controls.Add(grid);
        Controls.Add(filter);
        Controls.Add(top);
        Controls.Add(status);
        Controls.Add(menu);
    }

    static DataGridViewTextBoxColumn Col(string prop, string header, float weight) => new()
    {
        DataPropertyName = prop, HeaderText = header, Name = "col" + prop, FillWeight = weight
    };

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
        _all.Add(new RepairOrder { Id = "SC001", CustomerName = "Nguyễn Thị Hoa", Phone = "0912345678", DeviceType = "Điện thoại", Description = "Vỡ màn hình, cần thay mới", ReceivedDate = t.AddDays(-2), EstimatedCost = 1500000, Status = "Đang sửa" });
        _all.Add(new RepairOrder { Id = "SC002", CustomerName = "Trần Văn Bình", Phone = "0987654321", DeviceType = "Laptop", Description = "Không lên nguồn", ReceivedDate = t.AddDays(-1), EstimatedCost = 800000, Status = "Mới tiếp nhận" });
        _all.Add(new RepairOrder { Id = "SC003", CustomerName = "Lê Minh Tuấn", Phone = "0901122334", DeviceType = "Máy in", Description = "Kẹt giấy liên tục", ReceivedDate = t.AddDays(-6), EstimatedCost = 300000, Status = "Hoàn tất" });
        _all.Add(new RepairOrder { Id = "SC004", CustomerName = "Phạm Thu Trang", Phone = "0933445566", DeviceType = "Tivi", Description = "Chờ đặt bo mạch nguồn", ReceivedDate = t.AddDays(-4), EstimatedCost = 2200000, Status = "Chờ linh kiện" });
    }

    // ================= LỌC / TÌM KIẾM / THỐNG KÊ =================
    void ApplyFilter()
    {
        if (grid == null) return;
        var keep = _selected;
        string kw = txtSearch.Text.Trim().ToLowerInvariant();

        IEnumerable<RepairOrder> q = _all;
        if (kw.Length > 0)
            q = q.Where(o => o.Id.ToLowerInvariant().Contains(kw)
                          || o.CustomerName.ToLowerInvariant().Contains(kw)
                          || o.Phone.Contains(kw.Replace(" ", "")));
        if (cboFilter.SelectedIndex > 0)
            q = q.Where(o => o.Status == (string)cboFilter.SelectedItem);

        var list = q.OrderByDescending(o => o.ReceivedDate).ThenBy(o => o.Id).ToList();

        _loading = true;
        _view.RaiseListChangedEvents = false;
        _view.Clear();
        foreach (var o in list) _view.Add(o);
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

        // Tổng chi phí các đơn chưa hoàn tất (không tính đơn đã hủy vì không phát sinh chi phí)
        decimal pending = _all.Where(o => o.Status != "Hoàn tất" && o.Status != "Đã hủy").Sum(o => o.EstimatedCost);
        lblCount.Text = $"Số đơn theo bộ lọc: {_view.Count} / {_all.Count}";
        lblTotal.Text = $"Tổng chi phí dự kiến (đơn chưa hoàn tất): {pending.ToString("N0", Vi)} đ";
    }

    // ================= CHỌN DÒNG → NẠP LÊN FORM =================
    void LoadSelected()
    {
        if (_loading || grid.SelectedRows.Count == 0) return;
        if (grid.SelectedRows[0].DataBoundItem is not RepairOrder o) return;

        _selected = o;
        _loading = true;
        txtId.Text = o.Id;
        txtName.Text = o.CustomerName;
        txtPhone.Text = o.Phone;
        cboDevice.SelectedItem = o.DeviceType;
        txtDesc.Text = o.Description;
        dtpReceived.Value = o.ReceivedDate;
        txtCost.Text = o.EstimatedCost.ToString("N0", Vi);
        cboStatus.SelectedItem = o.Status;
        _imagePath = o.ImagePath ?? "";
        ShowImage(_imagePath);
        _loading = false;
        _err.Clear();
    }

    void ClearInputs()
    {
        _selected = null;
        grid.ClearSelection();
        txtId.Clear(); txtName.Clear(); txtPhone.Clear(); txtDesc.Clear();
        txtCost.Text = "0";
        cboDevice.SelectedIndex = 0;
        cboStatus.SelectedIndex = 0;
        dtpReceived.Value = DateTime.Today;
        _imagePath = "";
        ShowImage("");
        _err.Clear();
        txtId.Focus();
    }

    // ================= ẢNH THIẾT BỊ =================
    void ChooseImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Chọn ảnh thiết bị",
            Filter = "Ảnh (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _imagePath = dlg.FileName;
        ShowImage(_imagePath);
    }

    void ShowImage(string path)
    {
        var old = pic.Image;
        pic.Image = null;
        old?.Dispose();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            // Đọc vào bộ nhớ rồi đóng file ngay để không khóa file ảnh
            using var fs = File.OpenRead(path);
            using var tmp = Image.FromStream(fs);
            pic.Image = new Bitmap(tmp);
        }
        catch
        {
            MessageBox.Show("Không thể mở tệp ảnh này.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ================= KIỂM TRA DỮ LIỆU =================
    /// <summary>Đọc chi phí theo định dạng Việt Nam; trả về false nếu không phải số.</summary>
    bool TryParseCost(out decimal cost) =>
        decimal.TryParse(txtCost.Text.Trim(), NumberStyles.Number, Vi, out cost);

    bool ValidateForm(RepairOrder editing)
    {
        _err.Clear();
        bool ok = true;
        void Fail(Control c, string msg) { _err.SetError(c, msg); ok = false; }

        string id = txtId.Text.Trim();
        if (id.Length == 0) Fail(txtId, "Mã đơn không được để trống.");
        else if (_all.Any(o => o != editing && o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            Fail(txtId, "Mã đơn đã tồn tại.");

        if (txtName.Text.Trim().Length == 0) Fail(txtName, "Tên khách hàng không được để trống.");
        if (txtPhone.Text.Trim().Length == 0) Fail(txtPhone, "Số điện thoại không được để trống.");
        if (txtDesc.Text.Trim().Length == 0) Fail(txtDesc, "Vui lòng mô tả lỗi của thiết bị.");

        if (!TryParseCost(out decimal cost)) Fail(txtCost, "Chi phí dự kiến phải là một số (VD: 1.500.000).");
        else if (cost < 0) Fail(txtCost, "Chi phí dự kiến không được âm.");

        if (cboDevice.SelectedIndex < 0) Fail(cboDevice, "Vui lòng chọn loại thiết bị.");
        if (cboStatus.SelectedIndex < 0) Fail(cboStatus, "Vui lòng chọn trạng thái.");
        return ok;
    }

    void Fill(RepairOrder o)
    {
        TryParseCost(out decimal cost);
        o.Id = txtId.Text.Trim();
        o.CustomerName = txtName.Text.Trim();
        o.Phone = txtPhone.Text.Trim();
        o.DeviceType = (string)cboDevice.SelectedItem;
        o.Description = txtDesc.Text.Trim();
        o.ReceivedDate = dtpReceived.Value.Date;
        o.EstimatedCost = cost;
        o.Status = (string)cboStatus.SelectedItem;
        o.ImagePath = _imagePath;
    }

    // ================= THÊM / SỬA / XÓA =================
    void AddOrder()
    {
        if (!ValidateForm(null)) return;
        var o = new RepairOrder();
        Fill(o);
        _all.Add(o);
        _selected = o;
        ApplyFilter();
        MessageBox.Show("Đã thêm đơn sửa chữa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void UpdateOrder()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một đơn trong danh sách.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ValidateForm(_selected)) return;
        if (MessageBox.Show("Cập nhật đơn này?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Fill(_selected);
        ApplyFilter();
    }

    void DeleteOrder()
    {
        if (_selected == null)
        {
            MessageBox.Show("Hãy chọn một đơn để xóa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"Xóa đơn {_selected.Id} của {_selected.CustomerName}?", "Xác nhận xóa",
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
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"don_sua_chua_{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var lines = new List<string> { "Mã đơn,Khách hàng,Số điện thoại,Loại thiết bị,Mô tả lỗi,Ngày tiếp nhận,Chi phí dự kiến,Trạng thái" };
        foreach (var o in _view)
            lines.Add(string.Join(",", Csv(o.Id), Csv(o.CustomerName), Csv(o.Phone), Csv(o.DeviceType), Csv(o.Description),
                o.ReceivedDate.ToString("dd/MM/yyyy"), o.EstimatedCost.ToString("0", CultureInfo.InvariantCulture), Csv(o.Status)));
        File.WriteAllLines(dlg.FileName, lines, new UTF8Encoding(true));
        MessageBox.Show($"Đã xuất {_view.Count} đơn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
