using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartNotes.Data;
using SmartNotes.Data.Entities;
using Whisper.net;
using Whisper.net.Ggml;

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

            if (!File.Exists(modelPath))
            {
                await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Small);
                await using var fileWriter = File.OpenWrite(modelPath);
                await modelStream.CopyToAsync(fileWriter);
            }

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
        var keywords = await db.CommandKeywords.ToListAsync();
        var lower = transcript.ToLowerInvariant();

        foreach (var k in keywords)
        {
            var kw = k.keyword.ToLowerInvariant();
            var idx = lower.IndexOf(kw, StringComparison.Ordinal);
            if (idx < 0) continue;

            var valueAfter = transcript[(idx + k.keyword.Length)..].Trim(' ', ':', '-', ',', '.');
            if (string.IsNullOrWhiteSpace(valueAfter)) valueAfter = transcript.Trim();

            // A) ADD LOCATION COMMAND
            if (k.action.Contains("location", StringComparison.OrdinalIgnoreCase))
            {
                var loc = await db.Locations.FirstOrDefaultAsync(x => x.note_id == noteId && x.contribution_id == contributionId);
                if (loc == null)
                {
                    loc = new Locations { note_id = noteId, contribution_id = contributionId, created_by = userId, created_at = DateTime.UtcNow };
                    db.Locations.Add(loc);
                }
                loc.place_name = valueAfter;
                await db.SaveChangesAsync();
                results.Add(new { action = "location", keyword = k.keyword, value = valueAfter });
            }
            // B) ADD REMINDER COMMAND
            else if (k.action.Contains("reminder", StringComparison.OrdinalIgnoreCase))
            {
                var r = await db.Reminders.FirstOrDefaultAsync(x => x.note_id == noteId && x.contribution_id == contributionId);
                if (r == null)
                {
                    r = new Reminders { note_id = noteId, contribution_id = contributionId, created_by = userId, created_at = DateTime.UtcNow };
                    db.Reminders.Add(r);
                }
                r.title = valueAfter;
                r.remind_at = DateTime.UtcNow.AddDays(1);
                r.is_done = false;
                await db.SaveChangesAsync();
                results.Add(new { action = "reminder", keyword = k.keyword, value = valueAfter });
            }
            // C) EXCLUDE USER IN GROUP NOTE COMMAND
            else if (k.action.Contains("exclude", StringComparison.OrdinalIgnoreCase) && contributionId != null)
            {
                // Find any group member whose name is spoken in the transcript
                var members = await (from m in db.NoteMembers
                                     join u in db.Users on m.user_id equals u.id
                                     where m.note_id == noteId && u.id != userId
                                     select u).ToListAsync();

                foreach (var member in members)
                {
                    var firstName = member.full_name.Split(' ')[0].ToLowerInvariant();
                    if (lower.Contains(member.full_name.ToLowerInvariant()) || lower.Contains(firstName))
                    {
                        var exists = await db.NoteExclusions.AnyAsync(e => e.contribution_id == contributionId.Value && e.user_id == member.id);
                        if (!exists)
                        {
                            db.NoteExclusions.Add(new NoteExclusions
                            {
                                note_id = noteId,
                                contribution_id = contributionId.Value,
                                user_id = member.id,
                                created_by = userId
                            });
                            await db.SaveChangesAsync();
                        }
                        results.Add(new { action = "exclude", excludedUser = member.full_name });
                    }
                }
            }
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
}