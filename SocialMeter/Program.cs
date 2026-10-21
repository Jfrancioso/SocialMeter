using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Movies;
using SocialMeter.Infrastructure.Tmdb;
using SocialMeter.Options;
using SocialMeter.Services;
using SocialMeter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<TmdbOptions>()
    .Bind(builder.Configuration.GetSection(TmdbOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Tmdb:ApiKey is required.")
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be a valid absolute URL.")
    .ValidateOnStart();

builder.Services.AddScoped<IMovieService, MovieService>();

builder.Services.AddHttpClient<TmdbClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;

    // Ensure it ends with a trailing slash so relative URLs combine correctly.
    var baseUrl = opts.BaseUrl.EndsWith("/") ? opts.BaseUrl : opts.BaseUrl + "/";
    http.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<PracticeHttpService>();
builder.Services.AddDbContext<SocialMeterDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ADD THIS - CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("AllowVueFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
