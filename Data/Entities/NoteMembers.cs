namespace SmartNotes.Data.Entities;

public class NoteMembers
{
    public int note_id { get; set; }
    public int user_id { get; set; }
    public DateTime joined_at { get; set; }
}