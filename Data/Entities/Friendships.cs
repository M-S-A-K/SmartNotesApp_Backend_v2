namespace SmartNotes.Data.Entities;

public class Friendships
{
    public int user_id { get; set; }
    public int friend_id { get; set; }
    public DateTime created_at { get; set; }
}