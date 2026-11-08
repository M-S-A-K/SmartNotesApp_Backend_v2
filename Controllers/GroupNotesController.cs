using Microsoft.AspNetCore.Mvc;
using SmartNotes.Services;

namespace SmartNotes.Controllers;

public record AddMemberRequest(int UserId);
public record ContributionRequest(string Type, string Text, string? SubLocation, decimal? Latitude, decimal? Longitude, DateTime? RemindAt);
public record EditContributionRequest(string Text);
public record ExcludeRequest(int ExcludedUserId);
public record TransferRequest(int NewOwnerId);

[ApiController]
[Route("api/notes/{noteId:int}")]
public class GroupNotesController(GroupNotesService group) : ApiController
{
    [HttpGet("members")]
    public async Task<IActionResult> GetMembers(int noteId) =>
        Ok(await group.GetMembers(UserId, noteId));

    [HttpPost("members")]
    public async Task<IActionResult> AddMember(int noteId, AddMemberRequest r) =>
        Ok(await group.AddMember(UserId, noteId, r.UserId));

    [HttpDelete("members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember(int noteId, int memberId)
    {
        await group.RemoveMember(UserId, noteId, memberId);
        return Ok(new { memberId });
    }

    [HttpGet("contributions")]
    public async Task<IActionResult> GetContributions(int noteId) =>
        Ok(await group.GetContributions(UserId, noteId));


    [HttpPost("contributions")]
    public async Task<IActionResult> AddContribution(int noteId, ContributionRequest r) =>
        Ok(await group.AddContribution(UserId, noteId, r.Type, r.Text, r.SubLocation, r.RemindAt, r.Latitude, r.Longitude));

    [HttpPut("contributions/{contributionId:int}")]
    public async Task<IActionResult> UpdateContribution(int noteId, int contributionId, EditContributionRequest r)
    {
        await group.UpdateContribution(UserId, noteId, contributionId, r.Text);
        return Ok(new { contributionId });
    }

    [HttpDelete("contributions/{contributionId:int}")]
    public async Task<IActionResult> DeleteContribution(int noteId, int contributionId)
    {
        await group.DeleteContribution(UserId, noteId, contributionId);
        return Ok(new { contributionId });
    }

    [HttpPost("contributions/{contributionId:int}/exclude")]
    public async Task<IActionResult> Exclude(int noteId, int contributionId, ExcludeRequest r)
    {
        await group.ExcludeMember(UserId, noteId, contributionId, r.ExcludedUserId);
        return Ok(new { contributionId, r.ExcludedUserId });
    }
    
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(int noteId, TransferRequest r)
    {
        await group.TransferOwnership(UserId, noteId, r.NewOwnerId);
        return Ok(new { noteId, newOwnerId = r.NewOwnerId });
    }

    [HttpDelete("members/me")]
    public async Task<IActionResult> Leave(int noteId)
    {
        await group.LeaveGroup(UserId, noteId);
        return Ok(new { noteId });
    }
}