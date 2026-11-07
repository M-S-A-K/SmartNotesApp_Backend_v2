using Microsoft.EntityFrameworkCore;
using SmartNotes.Data;
using SmartNotes.Data.Entities;

namespace SmartNotes.Services;

public class NotesService(AppDbContext db)
{
    private async Task<Notes> GetAccessible(int userId, int noteId)
    {
        var note = await db.Notes.FindAsync(noteId) ?? throw new AppException(404, "Note not found");
        
        var isMember = await db.NoteMembers.AnyAsync(m => m.note_id == noteId && m.user_id == userId);
        
        if (note.owner_id != userId && !isMember) throw new AppException(404, "Note not found");
        
        return note;
    }

    // -- Notes CURD ---
   public async Task<Notes> Create(int userId, string title, string? content, string noteType)
   {
       if (string.IsNullOrWhiteSpace(title)) throw new AppException(400, "Title is required");
       if (noteType != "personal" && noteType != "group") throw new AppException(400, "noteType must be personal or group");
   
    var now = DateTime.UtcNow;
       var note = new Notes { owner_id = userId, title = title, content = content ?? "",
                              note_type = noteType, created_at = now, updated_at = now };
       db.Notes.Add(note);
       await db.SaveChangesAsync();
   
    if (noteType == "group")
       {
           db.NoteMembers.Add(new NoteMembers { note_id = note.id, user_id = userId, joined_at = now });
           await db.SaveChangesAsync();
       }
       return note;
   }
   

   public async Task<object> List(int userId, string? tag, string? type)
   {
       var q = db.Notes.Where(n => n.owner_id == userId ||
                                   db.NoteMembers.Any(m => m.note_id == n.id && m.user_id == userId));

       if (tag != null)
           q = q.Where(n => db.NoteTags.Any(nt => nt.note_id == n.id &&
                                                  db.Tags.Any(t => t.id == nt.tag_id && t.name == tag)));

       if (type != null) q = q.Where(n => n.note_type == type);

       return await q.OrderByDescending(n => n.updated_at)
           .Select(n => new { n.id, n.title, n.note_type, n.is_locked, n.created_at, n.updated_at,
               snippet = n.content.Length > 100 ? n.content.Substring(0, 100) : n.content })
           .ToListAsync();
   }

   public async Task<object> Get(int userId, int noteId)
   {
       var n = await GetAccessible(userId, noteId);

       var tags = await (from nt in db.NoteTags
           join t in db.Tags on nt.tag_id equals t.id
           where nt.note_id == noteId
           select new { t.id, t.name }).ToListAsync();

       var reminders = await db.Reminders
           .Where(r => r.note_id == noteId)
           .OrderBy(r => r.remind_at)
           .Select(r => new { r.id, r.title, r.remind_at, r.is_done, r.created_by, r.contribution_id })
           .ToListAsync();

       var locations = await db.Locations
           .Where(l => l.note_id == noteId)
           .Select(l => new { l.id, l.place_name, l.sub_location, l.latitude, l.longitude, l.created_by, l.contribution_id })
           .ToListAsync();
       
       var voice_notes = await db.VoiceNotes
           .Where(v => v.note_id == noteId && v.contribution_id == null)
           .OrderBy(v => v.created_at)
           .Select(v => new { v.id, v.display_name, v.file_path, v.duration_seconds, v.transcript, v.language, v.created_by, v.created_at })
           .ToListAsync();

       return new
       {
           n.id, n.owner_id, n.title, n.content, n.note_type,
           n.is_locked, n.created_at, n.updated_at,
           tags, reminders, locations , voice_notes
       };
   }

   public async Task Update(int userId, int noteId, string title, string? content)
   {
       var n = await GetAccessible(userId, noteId);
       if (n.owner_id != userId) throw new AppException(403, "Only the owner can edit this note");
       n.title = title; n.content = content ?? ""; n.updated_at = DateTime.UtcNow;
       await db.SaveChangesAsync();
   }                                 

    public async Task Delete(int userId, int noteId)
    {
        var n = await GetAccessible(userId, noteId);
        if (n.owner_id != userId) throw new AppException(403, "Only the owner can delete this note");
        db.Notes.Remove(n);                                
        await db.SaveChangesAsync();
    }
    
    // --- TAGS ---
    
    public async Task<object> GetUserTags(int userId) =>
        await db.Tags.Where(t => t.user_id == userId)
            .OrderBy(t => t.name)
            .Select(t => new { t.id, t.name })
            .ToListAsync();
    
    public async Task<object> AddTagToNote(int userId, int noteId, string tagName)
    {
        var n = await GetAccessible(userId, noteId);
        if (n.owner_id != userId) throw new AppException(403, "Only the owner can tag this note");
        if (string.IsNullOrWhiteSpace(tagName)) throw new AppException(400, "Tag name is required");

        tagName = tagName.Trim();

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.user_id == userId && t.name == tagName);
        if (tag == null)
        {
            tag = new Tags { user_id = userId, name = tagName };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
        }

        var exists = await db.NoteTags.AnyAsync(nt => nt.note_id == noteId && nt.tag_id == tag.id);
        if (!exists)
        {
            db.NoteTags.Add(new NoteTags { note_id = noteId, tag_id = tag.id });
            await db.SaveChangesAsync();
        }

        return new { tag.id, tag.name };
    }
    
    public async Task RemoveTagFromNote(int userId, int noteId, int tagId)
    {
        var n = await GetAccessible(userId, noteId);
        if (n.owner_id != userId) throw new AppException(403, "Only the owner can remove tags");

        var link = await db.NoteTags.FirstOrDefaultAsync(nt => nt.note_id == noteId && nt.tag_id == tagId)
                   ?? throw new AppException(404, "Tag not on this note");

        db.NoteTags.Remove(link);
        await db.SaveChangesAsync();
    }
    
    // --- LOCK / UNLOCK / VERIFY ---
    public async Task Lock(int userId, int noteId, string password)
    {
        var n = await GetAccessible(userId, noteId);
        if (n.owner_id != userId) throw new AppException(403, "Only the owner can lock this note");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            throw new AppException(400, "Password must be at least 4 characters");

        n.is_locked = true;
        n.lock_password = password;
        n.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task Unlock(int userId, int noteId, string password)
    {
        var n = await GetAccessible(userId, noteId);
        if (n.owner_id != userId) throw new AppException(403, "Only the owner can remove the lock");
        if (n.is_locked && n.lock_password != password)
            throw new AppException(400, "Wrong lock password");

        n.is_locked = false;
        n.lock_password = null;
        n.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task VerifyLock(int userId, int noteId, string password)
    {
        var n = await GetAccessible(userId, noteId);
        if (!n.is_locked) return;
        if (n.lock_password != password) throw new AppException(400, "Wrong lock password");
    }
    
    // --- REMINDERS ---
    public async Task<object> AddReminder(int userId, int noteId, string title, DateTime remindAt)
    {
        await GetAccessible(userId, noteId);
        if (string.IsNullOrWhiteSpace(title)) throw new AppException(400, "Reminder title is required");

        var r = await db.Reminders.FirstOrDefaultAsync(x => x.note_id == noteId && x.contribution_id == null);
        if (r == null)
        {
            r = new Reminders { note_id = noteId, created_by = userId, created_at = DateTime.UtcNow };
            db.Reminders.Add(r);
        }

        r.title = title.Trim();
        r.remind_at = remindAt;
        r.is_done = false;
        await db.SaveChangesAsync();
        return new { r.id, r.title, r.remind_at, r.is_done, r.created_by };
    }

    public async Task ToggleReminder(int userId, int noteId, int reminderId)
    {
        await GetAccessible(userId, noteId);
        var r = await db.Reminders.FirstOrDefaultAsync(x => x.id == reminderId && x.note_id == noteId)
            ?? throw new AppException(404, "Reminder not found");

        r.is_done = !r.is_done;
        await db.SaveChangesAsync();
    }

    public async Task DeleteReminder(int userId, int noteId, int reminderId)
    {
        var n = await GetAccessible(userId, noteId);
        var r = await db.Reminders.FirstOrDefaultAsync(x => x.id == reminderId && x.note_id == noteId)
            ?? throw new AppException(404, "Reminder not found");

        if (n.owner_id != userId && r.created_by != userId)
            throw new AppException(403, "Not allowed");

        db.Reminders.Remove(r);
        await db.SaveChangesAsync();
    }

    // --- LOCATIONS ---
    public async Task<object> AddLocation(int userId, int noteId, string placeName, string? subLocation, decimal? lat, decimal? lng)
    {
        await GetAccessible(userId, noteId);
        if (string.IsNullOrWhiteSpace(placeName)) throw new AppException(400, "Place name is required");

        var loc = await db.Locations.FirstOrDefaultAsync(x => x.note_id == noteId && x.contribution_id == null);
        if (loc == null)
        {
            loc = new Locations { note_id = noteId, created_by = userId, created_at = DateTime.UtcNow };
            db.Locations.Add(loc);
        }

        loc.place_name = placeName.Trim();
        loc.sub_location = subLocation;
        loc.latitude = lat;
        loc.longitude = lng;
        await db.SaveChangesAsync();
        return new { loc.id, loc.place_name, loc.sub_location, loc.latitude, loc.longitude, loc.created_by };
    }

    public async Task DeleteLocation(int userId, int noteId, int locationId)
    {
        var n = await GetAccessible(userId, noteId);
        var loc = await db.Locations.FirstOrDefaultAsync(x => x.id == locationId && x.note_id == noteId)
            ?? throw new AppException(404, "Location not found");

        if (n.owner_id != userId && loc.created_by != userId)
            throw new AppException(403, "Not allowed");

        db.Locations.Remove(loc);
        await db.SaveChangesAsync();
    }

    // --- CALENDAR ---
    public async Task<object> GetCalendar(int userId) =>
        await (from r in db.Reminders
               join n in db.Notes on r.note_id equals n.id
               where n.owner_id == userId || db.NoteMembers.Any(m => m.note_id == n.id && m.user_id == userId)
               orderby r.remind_at
               select new { r.id, r.note_id, note_title = n.title, r.title, r.remind_at, r.is_done }).ToListAsync();
    
}