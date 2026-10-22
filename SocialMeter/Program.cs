using Microsoft.EntityFrameworkCore;
using SocialMeter.Application;
using SocialMeter.Infrastructure;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Web;

// Composition root. Each layer registers itself; see Application/, Infrastructure/ and Web/ DependencyInjection.cs.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddApplication(builder.Configuration)
    .AddWeb(builder.Configuration);

var app = builder.Build();

// Apply pending migrations so a fresh clone gets its tables on first run. With more than one instance this moves to CI.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<SocialMeterDbContext>().Database.Migrate();
}

app.UseWeb();
app.Run();
