namespace SmartNotes.Data.Entities;

public class Reminders
{
    public int id { get; set; }
    public int note_id { get; set; }
    public int? contribution_id { get; set; }
    public int created_by { get; set; }
    public string title { get; set; } = string.Empty;
    public DateTime remind_at { get; set; }
    public bool is_done { get; set; }
    public DateTime created_at { get; set; }

}