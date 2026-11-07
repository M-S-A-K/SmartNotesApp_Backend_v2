namespace SmartNotes.Data.Entities;

public class VoiceNotes
{
    public int id { get; set; }
    public int note_id { get; set; }
    public int? contribution_id { get; set; }
    public int created_by { get; set; }
    public string display_name { get; set; } = string.Empty;
    public string file_path { get; set; } = string.Empty;
    public int? duration_seconds { get; set; }
    public string? transcript { get; set; }
    public string? language { get; set; }
    public DateTime created_at { get; set; }
}