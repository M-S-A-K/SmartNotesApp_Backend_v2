namespace SmartNotes.Data.Entities;

public class Users
{
    public int id { get; set; }
    public string full_name { get; set; } = "";
    public string email { get; set; } = "";
    public string? phone { get; set; }
    public string password { get; set; } = "";
    public DateTime created_at { get; set; } = DateTime.UtcNow;
}