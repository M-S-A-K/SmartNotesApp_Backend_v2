namespace SmartNotes.Data.Entities;

public class Messages
{
    public int id { get; set; }
    public int sender_id { get; set; }
    public int receiver_id { get; set; }
    public string? message_text { get; set; }
    public int? shared_note_id { get; set; }
    public string? shared_title { get; set; }
    public string? shared_content { get; set; }
    public bool is_read { get; set; }
    public DateTime sent_at { get; set; }
}