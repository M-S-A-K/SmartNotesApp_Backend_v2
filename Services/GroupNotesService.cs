using Microsoft.EntityFrameworkCore;
using SmartNotes.Data;
using SmartNotes.Data.Entities;

namespace SmartNotes.Services;

public class GroupNotesService(AppDbContext db)
{
    private async Task<Notes> GetGroupNote(int userId, int noteId)
    {
        var note = await db.Notes.FindAsync(noteId) ?? throw new AppException(404, "Note not found");
        if (note.note_type != "group") throw new AppException(400, "This is not a group note");

        var isMember = await db.NoteMembers.AnyAsync(m => m.note_id == noteId && m.user_id == userId);
        if (note.owner_id != userId && !isMember) throw new AppException(404, "Note not found");

        return note;
    }

    // --- MEMBERS ---
    public async Task<object> GetMembers(int userId, int noteId)
    {
        var note = await GetGroupNote(userId, noteId);
        return await (from m in db.NoteMembers
                      join u in db.Users on m.user_id equals u.id
                      where m.note_id == noteId
                      select new
                      {
                          u.id,
                          u.full_name,
                          u.email,
                          m.joined_at,
                          role = u.id == note.owner_id ? "Owner" : "Member"
                      }).ToListAsync();
    }

    public async Task<object> AddMember(int userId, int noteId, int targetUserId)
    {
        var note = await GetGroupNote(userId, noteId);
        if (note.owner_id != userId) throw new AppException(403, "Only the owner can add members");

        var userExists = await db.Users.AnyAsync(u => u.id == targetUserId);
        if (!userExists) throw new AppException(404, "User not found");

        var alreadyMember = await db.NoteMembers.AnyAsync(m => m.note_id == noteId && m.user_id == targetUserId);
        if (!alreadyMember)
        {
            db.NoteMembers.Add(new NoteMembers { note_id = noteId, user_id = targetUserId, joined_at = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await NotifyMembers(noteId);
        return new { note_id = noteId, user_id = targetUserId };
    }

    public async Task RemoveMember(int userId, int noteId, int targetUserId)
    {
        var note = await GetGroupNote(userId, noteId);
        if (note.owner_id != userId) throw new AppException(403, "Only the owner can remove members");
        if (targetUserId == note.owner_id) throw new AppException(400, "Owner cannot be removed");

        var m = await db.NoteMembers.FirstOrDefaultAsync(x => x.note_id == noteId && x.user_id == targetUserId)
            ?? throw new AppException(404, "Member not found");

        db.NoteMembers.Remove(m);
        await db.SaveChangesAsync();
        await NotifyMembers(noteId);
    }

    // --- CONTRIBUTIONS (Text, Location, Reminder entries by members) ---
    public async Task<object> GetContributions(int userId, int noteId)
    {
        await GetGroupNote(userId, noteId);

        // Hide contributions where this user is in NoteExclusions
        var list = await (from c in db.Contributions
                          join u in db.Users on c.user_id equals u.id
                          where c.note_id == noteId &&
                                !db.NoteExclusions.Any(e => e.contribution_id == c.id && e.user_id == userId)
                          orderby c.created_at
                          select new
                          {
                              c.id,
                              c.user_id,
                              author_name = u.full_name,
                              c.type,
                              c.text,
                              c.created_at,
                              location = db.Locations
                                  .Where(l => l.contribution_id == c.id)
                                  .Select(l => new { l.id, l.place_name, l.sub_location, l.latitude, l.longitude })
                                  .FirstOrDefault(),
                              reminder = db.Reminders
                                  .Where(r => r.contribution_id == c.id)
                                  .Select(r => new { r.id, r.title, r.remind_at, r.is_done })
                                  .FirstOrDefault(),
                              voice = db.VoiceNotes
                                  .Where(v => v.contribution_id == c.id)
                                  .Select(v => new { v.id, v.display_name, v.file_path, v.duration_seconds, v.transcript, v.language })
                                  .FirstOrDefault()
                          }).ToListAsync();

        return list;
    }

    public async Task<object> AddContribution(int userId, int noteId, string type, string? text, string? subLocation, DateTime? remindAt ,decimal? lat, decimal? lng)
    {
        var note = await GetGroupNote(userId, noteId);
        if (type != "text" && type != "location" && type != "reminder")
            throw new AppException(400, "Type must be text, location, or reminder");
        if (string.IsNullOrWhiteSpace(text))
            throw new AppException(400, "Text/Title is required");

        var now = DateTime.UtcNow;
        var c = new Contributions
        {
            note_id = noteId,
            user_id = userId,
            type = type,
            text = text.Trim(),
            created_at = now
        };
        db.Contributions.Add(c);
        await db.SaveChangesAsync(); // generates c.id

        if (type == "location")
        {
            db.Locations.Add(new Locations
            {
                note_id = noteId,
                contribution_id = c.id,
                created_by = userId,
                place_name = text.Trim(),
                sub_location = subLocation,
                created_at = now,
                latitude = lat, 
                longitude = lng
            });
        }
        else if (type == "reminder")
        {
            db.Reminders.Add(new Reminders
            {
                note_id = noteId,
                contribution_id = c.id,
                created_by = userId,
                title = text.Trim(),
                remind_at = remindAt ?? now.AddHours(1),
                is_done = false,
                created_at = now
            });
        }

        note.updated_at = now;
        await db.SaveChangesAsync();
        await NotifyMembers(noteId);
        return new { c.id, c.type, c.text, c.user_id, c.created_at };
    }

    public async Task UpdateContribution(int userId, int noteId, int contributionId, string text)
    {
        await GetGroupNote(userId, noteId);
        var c = await db.Contributions.FirstOrDefaultAsync(x => x.id == contributionId && x.note_id == noteId)
            ?? throw new AppException(404, "Contribution not found");

        // Rule: Har member sirf apni hi entry edit kar sakta hai
        if (c.user_id != userId)
            throw new AppException(403, "You can only edit your own contribution");

        c.text = text.Trim();
        await NotifyMembers(noteId);
        await db.SaveChangesAsync();
    }

    public async Task DeleteContribution(int userId, int noteId, int contributionId)
    {
        var note = await GetGroupNote(userId, noteId);
        var c = await db.Contributions.FirstOrDefaultAsync(x => x.id == contributionId && x.note_id == noteId)
            ?? throw new AppException(404, "Contribution not found");
        if (c.user_id != userId && note.owner_id != userId)
            throw new AppException(403, "You can only delete your own contribution");

        // 1. delete what belongs to it first, and save
        db.NoteExclusions.RemoveRange(db.NoteExclusions.Where(e => e.contribution_id == contributionId));
        db.Reminders.RemoveRange(db.Reminders.Where(r => r.contribution_id == contributionId));
        db.Locations.RemoveRange(db.Locations.Where(l => l.contribution_id == contributionId));
        db.VoiceNotes.RemoveRange(db.VoiceNotes.Where(v => v.contribution_id == contributionId));
        await db.SaveChangesAsync();

        // 2. then delete the contribution itself
        db.Contributions.Remove(c);
        await db.SaveChangesAsync();
        await NotifyMembers(noteId);
    }

    // --- EXCLUSIONS (Hide a contribution from a specific member) ---
    public async Task ExcludeMember(int userId, int noteId, int contributionId, int excludedUserId)
    {
        var note = await GetGroupNote(userId, noteId);
        var c = await db.Contributions.FirstOrDefaultAsync(x => x.id == contributionId && x.note_id == noteId)
            ?? throw new AppException(404, "Contribution not found");

        if (c.user_id != userId && note.owner_id != userId)
            throw new AppException(403, "Not allowed");

        var exists = await db.NoteExclusions.AnyAsync(e => e.contribution_id == contributionId && e.user_id == excludedUserId);
        if (!exists)
        {
            db.NoteExclusions.Add(new NoteExclusions
            {
                note_id = noteId,
                contribution_id = contributionId,
                user_id = excludedUserId,
                created_by = userId
            });
            await db.SaveChangesAsync();
        }
    }
    
    
    // Owner gives the group to another member (owner must do this before leaving)
    public async Task TransferOwnership(int userId, int noteId, int newOwnerId)
    {
        var note = await GetGroupNote(userId, noteId);
        if (note.owner_id != userId) throw new AppException(403, "Only the owner can transfer ownership");
        if (newOwnerId == userId) throw new AppException(400, "You are already the owner");
        var isMember = await db.NoteMembers.AnyAsync(m => m.note_id == noteId && m.user_id == newOwnerId);
        if (!isMember) throw new AppException(400, "The new owner must be a member of this group");
        note.owner_id = newOwnerId;
        note.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // A member leaves. The owner must transfer ownership first. Their contributions stay.
    public async Task LeaveGroup(int userId, int noteId)
    {
        var note = await GetGroupNote(userId, noteId);
        if (note.owner_id == userId)
            throw new AppException(400, "You are the owner. Transfer ownership to another member first.");
        var m = await db.NoteMembers.FirstOrDefaultAsync(x => x.note_id == noteId && x.user_id == userId)
            ?? throw new AppException(404, "You are not a member");
        db.NoteMembers.Remove(m);
        await db.SaveChangesAsync();
    }

    private async Task NotifyMembers(int noteId)
    {
        var ids = await db.NoteMembers.Where(m => m.note_id == noteId).Select(m => m.user_id).ToArrayAsync();
        await Realtime.Notify(ids);
    }

    
}