using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartNotes.Data;
using SmartNotes.Data.Entities;
using Whisper.net;

namespace SmartNotes.Services;

public class VoiceService(AppDbContext db, IWebHostEnvironment env)
{
    // Queue lock so multiple users transcribing at the exact same time never crash RAM
    private static readonly SemaphoreSlim WhisperLock = new(1, 1);
    private static WhisperFactory? _factory;

    private async Task<Notes> GetAccessible(int userId, int noteId)
    {
        var note = await db.Notes.FindAsync(noteId) ?? throw new AppException(404, "Note not found");
        var isMember = await db.NoteMembers.AnyAsync(m => m.note_id == noteId && m.user_id == userId);
        if (note.owner_id != userId && !isMember) throw new AppException(404, "Note not found");
        return note;
    }

    // 1. Transcribe Audio with Offline Whisper (English + Urdu + Mix)
    private async Task<string> TranscribeWithWhisper(string audioFilePath, string? languageHint)
    {
        await WhisperLock.WaitAsync();
        try
        {
            var modelDir = Path.Combine(env.ContentRootPath, "models");
            Directory.CreateDirectory(modelDir);
            var modelPath = Path.Combine(modelDir, "ggml-small-ur-en.bin");

            if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < 400_000_000)
                throw new AppException(500, "Whisper model missing or not downloaded. Run: git lfs pull");

            // Convert .m4a / .mp3 to 16kHz Mono .wav so Whisper.net can read it
            var wavPath = audioFilePath + ".16k.wav";
            if (OperatingSystem.IsMacOS())
                await Process.Start("afconvert", ["-f", "WAVE", "-d", "LEI16@16000", "-c", "1", audioFilePath, wavPath])!.WaitForExitAsync();
            else
                await Process.Start("ffmpeg", ["-y", "-i", audioFilePath, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wavPath])!.WaitForExitAsync();

            var fileToRead = File.Exists(wavPath) ? wavPath : audioFilePath;

            _factory ??= WhisperFactory.FromPath(modelPath);
            var builder = _factory.CreateBuilder()
                .WithLanguage(string.IsNullOrWhiteSpace(languageHint) ? "auto" : languageHint)
                .WithPrompt("English, Urdu, and Roman Urdu mix note: reminder, location, exclude.");

            await using var processor = builder.Build();
            await using var fileStream = File.OpenRead(fileToRead);

            var sb = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(fileStream))
                sb.Append(segment.Text).Append(' ');

            if (File.Exists(wavPath)) File.Delete(wavPath);
            return sb.ToString().Trim();
        }
        finally
        {
            WhisperLock.Release();
        }
    }

    // 2. Upload Voice Note + Transcribe + Run Commands
    public async Task<object> UploadVoice(
        int userId, int noteId, IFormFile? file,
        string? displayName, int? durationSeconds, string? manualTranscript, string? language)
    {
        var note = await GetAccessible(userId, noteId);
        var now = DateTime.UtcNow;

        var dir = Path.Combine(env.ContentRootPath, "uploads", "voice");
        Directory.CreateDirectory(dir);

        // Unique GUID filename so concurrent users never overwrite each other
        var ext = file != null ? Path.GetExtension(file.FileName) : ".wav";
        if (string.IsNullOrEmpty(ext)) ext = ".wav";
        var fileName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(dir, fileName);

        if (file != null && file.Length > 0)
        {
            await using var stream = File.Create(fullPath);
            await file.CopyToAsync(stream);
        }

        // Run Whisper STT on the uploaded file (or use manualTranscript if testing without audio)
        var transcript = !string.IsNullOrWhiteSpace(manualTranscript)
            ? manualTranscript.Trim()
            : (file != null && file.Length > 0 ? await TranscribeWithWhisper(fullPath, language) : "");

        // If Group Note, create a Contribution row first
        int? contributionId = null;
        if (note.note_type == "group")
        {
            var c = new Contributions
            {
                note_id = noteId,
                user_id = userId,
                type = "voice",
                text = string.IsNullOrWhiteSpace(transcript) ? "Voice note" : transcript,
                created_at = now
            };
            db.Contributions.Add(c);
            await db.SaveChangesAsync();
            contributionId = c.id;
        }

        var count = await db.VoiceNotes.CountAsync(v => v.note_id == noteId);
        var v = new VoiceNotes
        {
            note_id = noteId,
            contribution_id = contributionId,
            created_by = userId,
            display_name = string.IsNullOrWhiteSpace(displayName) ? $"Audio {count + 1}" : displayName.Trim(),
            file_path = $"/api/notes/voice/file/{fileName}",
            duration_seconds = durationSeconds ?? 5,
            transcript = transcript,
            language = language ?? "auto",
            created_at = now
        };
        db.VoiceNotes.Add(v);
        note.updated_at = now;
        await db.SaveChangesAsync();

        // Check CommandKeywords for Reminder, Location, and Group Exclusion
        var commandsTriggered = new List<object>();
        if (!string.IsNullOrWhiteSpace(transcript))
            commandsTriggered = await RunVoiceCommands(userId, noteId, contributionId, transcript);

        return new
        {
            v.id, v.note_id, v.contribution_id, v.display_name,
            v.file_path, v.duration_seconds, v.transcript, v.language,
            v.created_at, commandsTriggered
        };
    }

    // 3. Execute Matched Commands from CommandKeywords table
    private async Task<List<object>> RunVoiceCommands(int userId, int noteId, int? contributionId, string transcript)
    {
        var results = new List<object>();
        var lower = transcript.ToLowerInvariant().Replace('’', '\'');

        // Pick ONE command: the longest keyword found ("remind me" beats "remind")
        var keywords = await db.CommandKeywords.ToListAsync();
        var k = keywords.Where(x => lower.Contains(x.keyword.ToLowerInvariant()))
                        .OrderByDescending(x => x.keyword.Length)
                        .FirstOrDefault();
        if (k == null) return results;

        var idx = lower.IndexOf(k.keyword.ToLowerInvariant(), StringComparison.Ordinal);
        var after = transcript[(idx + k.keyword.Length)..].Trim(' ', ':', '-', ',', '.', '،', '۔');

        if (k.action.Contains("reminder"))
        {
            var (title, whenUtc) = ParseReminder(after);
            var r = await db.Reminders.FirstOrDefaultAsync(x => x.note_id == noteId && x.contribution_id == contributionId);
            if (r == null)
            {
                r = new Reminders { note_id = noteId, contribution_id = contributionId, created_by = userId, created_at = DateTime.UtcNow };
                db.Reminders.Add(r);
            }
            r.title = title; r.remind_at = whenUtc; r.is_done = false;
            await db.SaveChangesAsync();
            results.Add(new { action = "reminder", title, remindAt = whenUtc });
        }
        else if (k.action.Contains("location"))
        {
            var place = Regex.Replace(after, @"^(is|at|in|to)\s+", "", RegexOptions.IgnoreCase).Trim();
            var loc = await db.Locations.FirstOrDefaultAsync(x => x.note_id == noteId && x.contribution_id == contributionId);
            if (loc == null)
            {
                loc = new Locations { note_id = noteId, contribution_id = contributionId, created_by = userId, created_at = DateTime.UtcNow };
                db.Locations.Add(loc);
            }
            loc.place_name = place;
            await db.SaveChangesAsync();
            results.Add(new { action = "location", place });
        }
        else if (k.action.Contains("exclude"))
        {
            if (contributionId == null)
            {
                results.Add(new { action = "exclude", error = "Exclusion works only in group notes" });
                return results;
            }
            var members = await (from m in db.NoteMembers
                join u in db.Users on m.user_id equals u.id
                where m.note_id == noteId && u.id != userId
                select u).ToListAsync();
            var spoken = after.ToLowerInvariant();
            var member = members.FirstOrDefault(u => spoken.Contains(u.full_name.ToLowerInvariant())
                                                     || spoken.Contains(u.full_name.Split(' ')[0].ToLowerInvariant()));
            if (member == null)
            {
                results.Add(new { action = "exclude", error = "No group member name found after the keyword" });
                return results;
            }
            if (!await db.NoteExclusions.AnyAsync(e => e.contribution_id == contributionId.Value && e.user_id == member.id))
            {
                db.NoteExclusions.Add(new NoteExclusions
                {
                    note_id = noteId, contribution_id = contributionId.Value,
                    user_id = member.id, created_by = userId
                });
                await db.SaveChangesAsync();
            }
            results.Add(new { action = "exclude", excludedUser = member.full_name });
        }
        return results;
    }

    public async Task DeleteVoice(int userId, int noteId, int voiceId)
    {
        var note = await GetAccessible(userId, noteId);
        var v = await db.VoiceNotes.FirstOrDefaultAsync(x => x.id == voiceId && x.note_id == noteId)
            ?? throw new AppException(404, "Voice note not found");

        if (note.owner_id != userId && v.created_by != userId)
            throw new AppException(403, "Not allowed");

        db.VoiceNotes.Remove(v);
        await db.SaveChangesAsync();
    }
    
    
    
    
    // Pakistan is UTC+5. We store UTC, so we convert spoken local time to UTC.
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(5);

    // "to call Ali tomorrow at 3 pm" -> (title: "call Ali", time in UTC)
    private static (string title, DateTime whenUtc) ParseReminder(string text)
    {
        var localNow = DateTime.UtcNow + LocalOffset;
        var date = localNow.Date.AddDays(1);      // default day: tomorrow
        int hour = 9, minute = 0;                 // default time: 9:00 AM

        // 1. Which day? (English + Urdu words)
        foreach (var (word, days) in new[] { ("day after tomorrow", 2), ("parso", 2), ("پرسوں", 2),
                                             ("tomorrow", 1), ("kal", 1), ("کل", 1),
                                             ("today", 0), ("aaj", 0), ("آج", 0) })
            if (RemoveWord(ref text, word)) { date = localNow.Date.AddDays(days); break; }

        foreach (var d in Enum.GetValues<DayOfWeek>())            // "Thursday" = the next Thursday
            if (RemoveWord(ref text, d.ToString()))
            {
                var diff = ((int)d - (int)localNow.DayOfWeek + 7) % 7;
                date = localNow.Date.AddDays(diff == 0 ? 7 : diff);
            }

        // 2. What time? "3 pm", "10:30 am", "9 بجے", "9 baje"
        var m = Regex.Match(text, @"(\d{1,2})(?::(\d{2}))?\s*(a\.?m\.?|p\.?m\.?|baje|بجے)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var h = int.Parse(m.Groups[1].Value);
            var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            var suffix = m.Groups[3].Value.ToLower();
            if (suffix.StartsWith("p") && h < 12) h += 12;
            if (suffix.StartsWith("a") && h == 12) h = 0;
            if (h <= 23 && min <= 59) { hour = h; minute = min; }
            text = text.Remove(m.Index, m.Length);
        }

        // 3. Clean the title
        foreach (var filler in new[] { "please", "laga do", "lagado", "lagao", "kar do" })
            RemoveWord(ref text, filler);
        text = Regex.Replace(text, @"^\s*(to|for|ko|that|at|on)\s+", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '.', '،', '۔', ':', '-');
        if (text == "") text = "Reminder";

        return (text, date.AddHours(hour).AddMinutes(minute) - LocalOffset);
    }

    private static bool RemoveWord(ref string text, string word)
    {
        var pattern = $@"(?<!\w){Regex.Escape(word)}(?!\w)";
        if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return false;
        text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);
        return true;
    }
}