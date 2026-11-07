namespace SmartNotes.Data.Entities;

public class CommandKeywords
{
    public int id { get; set; }
    public string language { get; set; } = string.Empty;
    public string keyword { get; set; } = string.Empty;
    public string action { get; set; } = string.Empty; 
}