namespace SmartNotes.Data.Entities;

public class NoteExclusions
{
    public int id { get; set; }
    public int note_id { get; set; }
    public int contribution_id { get; set; }
    public int user_id { get; set; }
    public int created_by { get; set; }
}