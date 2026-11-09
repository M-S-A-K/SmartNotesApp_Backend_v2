using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartNotes.Data;
using SmartNotes.Data.Entities;

namespace SmartNotes.Services;

public class SocialService(AppDbContext db)
{
    // --- PROFILE ---
    public async Task<object> GetProfile(int userId)
    {
        var u = await db.Users.FindAsync(userId) ?? throw new AppException(404, "User not found");
        return new { u.id, u.full_name, u.email, u.phone, u.created_at };
    }

    public async Task<object> UpdateProfile(int userId, string fullName, string email, string? phone)
    {
        var u = await db.Users.FindAsync(userId) ?? throw new AppException(404, "User not found");
        if (string.IsNullOrWhiteSpace(fullName)) throw new AppException(400, "Name is required");

        u.full_name = fullName.Trim();
        u.email = email.Trim();
        u.phone = phone;
        await db.SaveChangesAsync();
        return new { u.id, u.full_name, u.email, u.phone };
    }

    public async Task ChangePassword(int userId, string currentPassword, string newPassword)
    {
        var u = await db.Users.FindAsync(userId) ?? throw new AppException(404, "User not found");
        if (u.password != currentPassword) throw new AppException(400, "Current password is wrong");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new AppException(400, "New password too short");

        u.password = newPassword;
        await db.SaveChangesAsync();
    }

    // --- FRIENDS ---
    public async Task<object> GetFriends(int userId) =>
        await (from f in db.Friendships
               join u in db.Users on f.friend_id equals u.id
               where f.user_id == userId
               orderby u.full_name
               select new { u.id, u.full_name, u.email, u.phone, f.created_at }).ToListAsync();

    public async Task<object> AddFriend(int userId, string emailOrPhone)
    {
        if (string.IsNullOrWhiteSpace(emailOrPhone)) throw new AppException(400, "Email or phone is required");
        var key = emailOrPhone.Trim();

        var friend = await db.Users.FirstOrDefaultAsync(u => u.email == key || u.phone == key)
            ?? throw new AppException(404, "User not found");

        if (friend.id == userId) throw new AppException(400, "Cannot add yourself");

        var now = DateTime.UtcNow;
        // Two-way friendship so both users see each other in Chat
        if (!await db.Friendships.AnyAsync(f => f.user_id == userId && f.friend_id == friend.id))
            db.Friendships.Add(new Friendships { user_id = userId, friend_id = friend.id, created_at = now });

        if (!await db.Friendships.AnyAsync(f => f.user_id == friend.id && f.friend_id == userId))
            db.Friendships.Add(new Friendships { user_id = friend.id, friend_id = userId, created_at = now });

        await db.SaveChangesAsync();
        return new { friend.id, friend.full_name, friend.email, friend.phone };
    }

    public async Task RemoveFriend(int userId, int friendId)
    {
        var links = await db.Friendships
            .Where(f => (f.user_id == userId && f.friend_id == friendId) ||
                        (f.user_id == friendId && f.friend_id == userId))
            .ToListAsync();

        db.Friendships.RemoveRange(links);
        await db.SaveChangesAsync();
    }

    // --- MESSAGES / CHAT & NOTE SHARING ---
    public async Task<object> GetThread(int userId, int friendId) =>
        await db.Messages
            .Where(m => (m.sender_id == userId && m.receiver_id == friendId) ||
                        (m.sender_id == friendId && m.receiver_id == userId))
            .OrderBy(m => m.sent_at)
            .Select(m => new
            {
                m.id,
                m.sender_id,
                m.receiver_id,
                m.message_text,
                m.shared_note_id,
                m.shared_title,
                m.shared_content,
                m.is_read,
                m.sent_at
            })
            .ToListAsync();

    public async Task<object> SendMessage(
        int userId, int friendId, string? text, int? sharedNoteId,
        bool includeText, bool includeLocation, bool includeReminder, bool includeVoice)
    {
        string? sharedTitle = null;
        string? sharedContent = null;
        
        if (!await db.Friendships.AnyAsync(f => f.user_id == userId && f.friend_id == friendId))
            throw new AppException(403, "You can only message your friends");

        if (sharedNoteId != null)
        {
            // Rule: Cannot share ONLY location or reminder — must include Text or Voice
            if (!includeText && !includeVoice)
                throw new AppException(400, "Must select at least Text or Voice note to share");

            var note = await db.Notes.FindAsync(sharedNoteId.Value)
                ?? throw new AppException(404, "Shared note not found");

            var isMember = await db.NoteMembers.AnyAsync(m => m.note_id == note.id && m.user_id == userId);
            if (note.owner_id != userId && !isMember)
                throw new AppException(403, "You cannot share this note");

            var location = includeLocation
                ? await db.Locations.Where(l => l.note_id == note.id)
                    .Select(l => new { l.place_name, l.sub_location, l.latitude, l.longitude })
                    .FirstOrDefaultAsync()
                : null;

            var reminder = includeReminder
                ? await db.Reminders.Where(r => r.note_id == note.id)
                    .Select(r => new { r.title, r.remind_at })
                    .FirstOrDefaultAsync()
                : null;

            var voices = includeVoice
                ? await db.VoiceNotes.Where(v => v.note_id == note.id)
                    .Select(v => new { v.id, v.display_name, v.file_path, v.duration_seconds, v.transcript })
                    .ToListAsync()
                : null;

            if (includeVoice && !includeText && (voices == null || voices.Count == 0))
                throw new AppException(400, "This note has no voice notes to share");

            sharedTitle = note.title;
            sharedContent = JsonSerializer.Serialize(new
            {
                text = includeText ? note.content : null,
                location,
                reminder,
                voices
            });
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            throw new AppException(400, "Message text or shared note is required");
        }

        var msg = new Messages
        {
            sender_id = userId,
            receiver_id = friendId,
            message_text = text?.Trim(),
            shared_note_id = sharedNoteId,
            shared_title = sharedTitle,
            shared_content = sharedContent,
            is_read = false,
            sent_at = DateTime.UtcNow
        };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();
        await Realtime.Notify(friendId, userId);

        return new
        {
            msg.id, msg.sender_id, msg.receiver_id, msg.message_text,
            msg.shared_note_id, msg.shared_title, msg.shared_content, msg.sent_at
        };
    }

    
    public async Task MarkRead(int userId, int messageId)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.id == messageId && x.receiver_id == userId)
                ?? throw new AppException(404, "Message not found");
        m.is_read = true;
        await db.SaveChangesAsync();
    }

    // Call this when the user opens a chat
    public async Task<int> MarkAllRead(int userId, int friendId)
    {
        var unread = await db.Messages
            .Where(m => m.sender_id == friendId && m.receiver_id == userId && !m.is_read).ToListAsync();
        foreach (var m in unread) m.is_read = true;
        await db.SaveChangesAsync();
        await Realtime.Notify(friendId);
        return unread.Count;
    }
    
    // --- GLOBAL SEARCH (Notes + Group Contributions + Messages) ---
    public async Task<object> Search(int userId, string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return new { notes = Array.Empty<object>(), messages = Array.Empty<object>() };
        q = q.Trim();

        var notes = await db.Notes
            .Where(n => (n.owner_id == userId || db.NoteMembers.Any(m => m.note_id == n.id && m.user_id == userId)) &&
                        (n.title.Contains(q) || n.content.Contains(q)))
            .Select(n => new { n.id, n.title, n.note_type, n.updated_at })
            .ToListAsync();

        var messages = await db.Messages
            .Where(m => (m.sender_id == userId || m.receiver_id == userId) &&
                        (m.message_text != null && m.message_text.Contains(q) ||
                         m.shared_title != null && m.shared_title.Contains(q)))
            .Select(m => new { m.id, m.sender_id, m.receiver_id, m.message_text, m.shared_title, m.sent_at })
            .ToListAsync();

        return new { notes, messages };
    }
    
    
    
}