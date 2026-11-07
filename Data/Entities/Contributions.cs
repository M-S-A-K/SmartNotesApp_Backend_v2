namespace SmartNotes.Data.Entities;

public class Contributions
{
    public int id { get; set; }
    public int note_id { get; set; }
    public int user_id { get; set; }
    public string type { get; set; } = string.Empty; 
    public string? text { get; set; }
    public DateTime created_at { get; set; }
}