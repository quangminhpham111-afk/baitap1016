namespace LabEquipmentCheckout;

/// <summary>Một phiếu mượn thiết bị.</summary>
public class Checkout
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string Equipment { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }

    /// <summary>Tình trạng được tự suy ra: Đã trả / Quá hạn / Đang mượn.</summary>
    public string Status =>
        ReturnDate.HasValue ? "Đã trả"
        : DueDate.Date < DateTime.Today ? "Quá hạn"
        : "Đang mượn";
}
