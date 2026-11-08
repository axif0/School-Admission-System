namespace SchoolAdmission.Api.Domain;

public class Hold
{
    public string Id { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Status { get; set; } = HoldStatus.Held;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
