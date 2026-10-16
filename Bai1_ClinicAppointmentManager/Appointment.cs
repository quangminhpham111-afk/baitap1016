namespace ClinicAppointmentManager;

/// <summary>Một lịch hẹn khám.</summary>
public class Appointment
{
    public string Id { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime Time { get; set; }
    public string Doctor { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Chờ khám";
}
