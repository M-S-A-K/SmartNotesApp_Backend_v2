using Microsoft.EntityFrameworkCore;
using SmartNotes.Data.Entities;

namespace SmartNotes.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CommandKeywords> CommandKeywords => Set<CommandKeywords>();
    public DbSet<Contributions> Contributions => Set<Contributions>();
    public DbSet<Friendships> Friendships => Set<Friendships>();
    public DbSet<Locations> Locations => Set<Locations>();
    public DbSet<Messages> Messages => Set<Messages>();
    public DbSet<NoteExclusions> NoteExclusions => Set<NoteExclusions>();
    public DbSet<NoteMembers> NoteMembers => Set<NoteMembers>();
    public DbSet<Notes> Notes => Set<Notes>();
    public DbSet<NoteTags> NoteTags => Set<NoteTags>();
    public DbSet<Reminders> Reminders => Set<Reminders>();
    public DbSet<Tags> Tags => Set<Tags>();
    public DbSet<Users> Users => Set<Users>();
    public DbSet<VoiceNotes> VoiceNotes => Set<VoiceNotes>();
    
    
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Friendships>().HasKey(x => new { x.user_id, x.friend_id });
        b.Entity<NoteTags>().HasKey(x => new { x.note_id, x.tag_id });
        b.Entity<NoteMembers>().HasKey(x => new { x.note_id, x.user_id });
       
        foreach (var e in b.Model.GetEntityTypes())
            e.SetTableName(e.ClrType.Name == "User" ? "Users" : e.ClrType.Name);
    }
}