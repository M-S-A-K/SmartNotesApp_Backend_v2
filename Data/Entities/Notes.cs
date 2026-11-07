namespace SmartNotes.Data.Entities;

public class Notes
{
    public int id { get; set; }
    public int owner_id { get; set; }
    public string title { get; set; } = string.Empty;
    public string content { get; set; } = string.Empty;
    public string note_type { get; set; } = string.Empty; // personal or group
    public bool is_locked { get; set; }
    public string? lock_password { get; set; }
    public DateTime created_at { get; set; }
    public DateTime updated_at { get; set; }
}