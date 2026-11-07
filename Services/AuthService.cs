using Microsoft.EntityFrameworkCore;
using SmartNotes.Data;
using SmartNotes.Data.Entities;

namespace SmartNotes.Services;

public class AuthService(AppDbContext db)
{
    public async Task<Users> Register(string fullName, string email, string password)
    {
        if (await db.Users.AnyAsync(u => u.email == email))
            throw new AppException(400, "Email already exists");

        var user = new Users { full_name = fullName, email = email, password = password };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
    
    
    public async Task<Users> Login(string email, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.email == email && u.password == password);
        if (user == null)
            throw new AppException(401, "Wrong email or password");
        return user;
    }
    
}