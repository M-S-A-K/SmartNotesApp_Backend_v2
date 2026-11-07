using Microsoft.AspNetCore.Mvc;
using SmartNotes.Services;

namespace SmartNotes.Controllers;

public record UpdateProfileRequest(string FullName, string Email, string? Phone);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record AddFriendRequest(string EmailOrPhone);
public record SendMessageRequest(
    string? Text,
    int? SharedNoteId,
    bool IncludeText = true,
    bool IncludeLocation = false,
    bool IncludeReminder = false,
    bool IncludeVoice = false
);

[ApiController]
[Route("api")]
public class SocialController(SocialService social) : ApiController
{
    // --- PROFILE ---
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile() => Ok(await social.GetProfile(UserId));

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest r) =>
        Ok(await social.UpdateProfile(UserId, r.FullName, r.Email, r.Phone));

    [HttpPost("profile/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest r)
    {
        await social.ChangePassword(UserId, r.CurrentPassword, r.NewPassword);
        return Ok(new { ok = true });
    }

    // --- FRIENDS ---
    [HttpGet("friends")]
    public async Task<IActionResult> GetFriends() => Ok(await social.GetFriends(UserId));

    [HttpPost("friends")]
    public async Task<IActionResult> AddFriend(AddFriendRequest r) =>
        Ok(await social.AddFriend(UserId, r.EmailOrPhone));

    [HttpDelete("friends/{friendId:int}")]
    public async Task<IActionResult> RemoveFriend(int friendId)
    {
        await social.RemoveFriend(UserId, friendId);
        return Ok(new { friendId });
    }

    // --- CHAT / MESSAGES ---
    [HttpGet("messages/{friendId:int}")]
    public async Task<IActionResult> GetThread(int friendId) =>
        Ok(await social.GetThread(UserId, friendId));

    // 2. Replace SendMessage inside SocialController class:
    [HttpPost("messages/{friendId:int}")]
    public async Task<IActionResult> SendMessage(int friendId, SendMessageRequest r) =>
        Ok(await social.SendMessage(
            UserId, friendId, r.Text, r.SharedNoteId,
            r.IncludeText, r.IncludeLocation, r.IncludeReminder, r.IncludeVoice));

    // --- GLOBAL SEARCH ---
    [HttpGet("search")]
    public async Task<IActionResult> Search(string q) =>
        Ok(await social.Search(UserId, q));
}