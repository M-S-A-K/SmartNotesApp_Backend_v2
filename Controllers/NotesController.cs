using Microsoft.AspNetCore.Mvc;
using SmartNotes.Services;

namespace SmartNotes.Controllers;

public record NoteRequest(string Title, string? Content, string NoteType);
public record TagRequest(string Name);
public record LockRequest(string Password);
public record ReminderRequest(string Title, DateTime RemindAt);
public record LocationRequest(string PlaceName, string? SubLocation, decimal? Latitude, decimal? Longitude);

[ApiController]
[Route("api/notes")]
public class NotesController(NotesService notes) : ApiController
{
    [HttpPost]
    public async Task<IActionResult> Create(NoteRequest r)
    {
        var n = await notes.Create(UserId, r.Title, r.Content, r.NoteType);
        return Ok(new { n.id, n.title, n.note_type });
    }

    [HttpGet]
    public async Task<IActionResult> List(string? tag, string? type) =>
        Ok(await notes.List(UserId, tag, type));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await notes.Get(UserId, id));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, NoteRequest r)
    {
        await notes.Update(UserId, id, r.Title, r.Content);
        return Ok(new { id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await notes.Delete(UserId, id);
        return Ok(new { id });
    }

    [HttpGet("tags")]
    public async Task<IActionResult> UserTags() => Ok(await notes.GetUserTags(UserId));

    [HttpPost("{id:int}/tags")]
    public async Task<IActionResult> AddTag(int id, TagRequest r) =>
        Ok(await notes.AddTagToNote(UserId, id, r.Name));

    [HttpDelete("{id:int}/tags/{tagId:int}")]
    public async Task<IActionResult> RemoveTag(int id, int tagId)
    {
        await notes.RemoveTagFromNote(UserId, id, tagId);
        return Ok(new { id, tagId });
    }

    [HttpPost("{id:int}/lock")]
    public async Task<IActionResult> Lock(int id, LockRequest r)
    {
        await notes.Lock(UserId, id, r.Password);
        return Ok(new { id, is_locked = true });
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, LockRequest r)
    {
        await notes.Unlock(UserId, id, r.Password);
        return Ok(new { id, is_locked = false });
    }

    [HttpPost("{id:int}/verify-lock")]
    public async Task<IActionResult> VerifyLock(int id, LockRequest r)
    {
        await notes.VerifyLock(UserId, id, r.Password);
        return Ok(new { ok = true });
    }
    
    [HttpPost("{id:int}/reminders")]
    public async Task<IActionResult> AddReminder(int id, ReminderRequest r) =>
        Ok(await notes.AddReminder(UserId, id, r.Title, r.RemindAt));

    [HttpPut("{id:int}/reminders/{reminderId:int}/toggle")]
    public async Task<IActionResult> ToggleReminder(int id, int reminderId)
    {
        await notes.ToggleReminder(UserId, id, reminderId);
        return Ok(new { reminderId });
    }

    [HttpDelete("{id:int}/reminders/{reminderId:int}")]
    public async Task<IActionResult> DeleteReminder(int id, int reminderId)
    {
        await notes.DeleteReminder(UserId, id, reminderId);
        return Ok(new { reminderId });
    }


    [HttpPost("{id:int}/locations")]
    public async Task<IActionResult> AddLocation(int id, LocationRequest r) =>
        Ok(await notes.AddLocation(UserId, id, r.PlaceName, r.SubLocation, r.Latitude, r.Longitude));

    [HttpDelete("{id:int}/locations/{locationId:int}")]
    public async Task<IActionResult> DeleteLocation(int id, int locationId)
    {
        await notes.DeleteLocation(UserId, id, locationId);
        return Ok(new { locationId });
    }

    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar() => Ok(await notes.GetCalendar(UserId));
}
