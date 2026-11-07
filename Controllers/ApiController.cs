using Microsoft.AspNetCore.Mvc;
namespace SmartNotes.Controllers;

public class ApiController : ControllerBase
{
    protected int UserId =>
        int.TryParse(Request.Headers["X-User-Id"], out var id)
            ? id : throw new AppException(401, "X-User-Id header is missing");
}