namespace SmartNotes.Data.Entities;

public class Locations
{
    public int id { get; set; }
    public int note_id { get; set; }
    public int? contribution_id { get; set; }
    public int created_by { get; set; }
    public string place_name { get; set; } = string.Empty;
    public string? sub_location { get; set; }
    public decimal? latitude { get; set; }
    public decimal? longitude { get; set; }
    public DateTime created_at { get; set; }
}

