using Microsoft.AspNetCore.Mvc;
using SmartNotes.Services;

namespace SmartNotes.Controllers;

public record RegisterRequest(string FullName, string Email, string Password);
public record LoginRequest(string Email, string Password);

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest r)
    {
        var user = await auth.Register(r.FullName, r.Email, r.Password);
        return Ok(new { userId = user.id, fullName = user.full_name, email = user.email });
    }
    
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest r)
    {
        var user = await auth.Login(r.Email, r.Password);
        return Ok(new { userId = user.id, fullName = user.full_name, email = user.email });
    }
    
}

