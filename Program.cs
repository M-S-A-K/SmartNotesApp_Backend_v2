using Microsoft.EntityFrameworkCore;
using SmartNotes;
using SmartNotes.Data;
using SmartNotes.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();


builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("SmartNotesDb"))
        .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<NotesService>();
builder.Services.AddScoped<GroupNotesService>();
builder.Services.AddScoped<SocialService>();
builder.Services.AddScoped<VoiceService>();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (AppException e)
    {
        ctx.Response.StatusCode = e.StatusCode;
        await ctx.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});
app.UseWebSockets();
app.Map("/ws", Realtime.Handle);
app.MapControllers();
app.Run();