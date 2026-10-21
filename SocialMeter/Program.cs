using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Ratings;
using SocialMeter.Application.Social;
using SocialMeter.Application.Titles;
using Anthropic;
using SocialMeter.Infrastructure.Anthropic;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Infrastructure.Links;
using SocialMeter.Infrastructure.Omdb;
using SocialMeter.Infrastructure.Reddit;
using SocialMeter.Infrastructure.Tmdb;
using SocialMeter.Infrastructure.X;
using SocialMeter.Options;

var builder = WebApplication.CreateBuilder(args);

// ---- Options ----
builder.Services
    .AddOptions<TmdbOptions>()
    .Bind(builder.Configuration.GetSection(TmdbOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Tmdb:ApiKey is required.")
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be a valid absolute URL.")
    .ValidateOnStart();

builder.Services.Configure<RedditOptions>(builder.Configuration.GetSection(RedditOptions.SectionName));
builder.Services.Configure<XOptions>(builder.Configuration.GetSection(XOptions.SectionName));
builder.Services.Configure<OmdbOptions>(builder.Configuration.GetSection(OmdbOptions.SectionName));
builder.Services.Configure<SocialMeterOptions>(builder.Configuration.GetSection(SocialMeterOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));

// ---- External clients ----
builder.Services.AddHttpClient<TmdbClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
});
builder.Services.AddHttpClient<OmdbClient>();
builder.Services.AddHttpClient<RatingLinkResolver>();
builder.Services.AddHttpClient<RedditSource>();
builder.Services.AddHttpClient<XSource>();

// Social sources are resolved as a set; add a platform by registering another ISocialSource.
builder.Services.AddScoped<ISocialSource>(sp => sp.GetRequiredService<RedditSource>());
builder.Services.AddScoped<ISocialSource>(sp => sp.GetRequiredService<XSource>());

// ---- Application services ----
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITitleService, TitleService>();
builder.Services.AddScoped<IRatingsService, RatingsService>();
builder.Services.AddScoped<ISocialMeterService, SocialMeterService>();
builder.Services.AddSingleton<ISentimentAnalyzer, LexiconSentimentAnalyzer>();
builder.Services.AddSingleton<TrustPolicy>();

// Post assessment: Claude reads posts in context (is it about this title? how does the author feel about *it*?)
// when an API key is configured; otherwise the word lexicon scores tone on its own.
builder.Services.AddSingleton<LexiconPostAssessorFallback>();
builder.Services.AddSingleton(sp => new AnthropicClient { ApiKey = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey });
builder.Services.AddSingleton<IPostAssessor>(sp =>
    sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.IsConfigured
        ? ActivatorUtilities.CreateInstance<ClaudePostAssessor>(sp)
        : sp.GetRequiredService<LexiconPostAssessorFallback>().Inner);
builder.Services.AddScoped<DeletionSyncService>();
builder.Services.AddHostedService<DeletionSyncWorker>();

builder.Services.AddDbContext<SocialMeterDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---- Web ----
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Enums go over the wire as "movie" / "tv" / "positive" rather than numbers.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

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

// Apply pending migrations so a fresh clone gets its tables and seed posts on first run.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SocialMeterDbContext>();
    db.Database.Migrate();
}

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
