using Microsoft.AspNetCore.Mvc;
using SmartNotes.Services;

namespace SmartNotes.Controllers;

[ApiController]
[Route("api/notes")]
public class VoiceController(VoiceService voice, IWebHostEnvironment env) : ApiController
{
    [HttpPost("{noteId:int}/voice")]
    public async Task<IActionResult> Upload(
        int noteId,
        IFormFile? file,
        [FromForm] string? displayName,
        [FromForm] int? durationSeconds,
        [FromForm] string? transcript,
        [FromForm] string? language)
    {
        return Ok(await voice.UploadVoice(UserId, noteId, file, displayName, durationSeconds, transcript, language));
    }

    [HttpDelete("{noteId:int}/voice/{voiceId:int}")]
    public async Task<IActionResult> Delete(int noteId, int voiceId)
    {
        await voice.DeleteVoice(UserId, noteId, voiceId);
        return Ok(new { voiceId });
    }

    // Streams the audio recording back to any phone/device over HTTP
    [HttpGet("voice/file/{fileName}")]
    public IActionResult GetFile(string fileName)
    {
        var path = Path.Combine(env.ContentRootPath, "uploads", "voice", Path.GetFileName(fileName));
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "audio/wav", enableRangeProcessing: true);
    }
}