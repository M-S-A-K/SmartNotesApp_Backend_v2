using Microsoft.AspNetCore.Mvc;
using SmartNotes.Data;

namespace SmartNotes.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", users = db.Users.Count() });
}