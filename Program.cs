using System.Text;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var dbProvider = configuration["Database:Provider"] ?? "Postgres";

    if (string.Equals(dbProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase(
            configuration["Database:InMemoryName"] ?? "explAIned-comments-tests");
    }
    else
    {
        options.UseNpgsql(configuration["ConnectionStrings:PostgreSQL"]);
    }
});

builder.Services.AddOptions();
builder.Services.AddSingleton<MemoryDistributedCache>();
builder.Services.AddSingleton<RedisCache>();
builder.Services.Configure<RedisCacheOptions>(options =>
{
    options.Configuration = builder.Configuration["ConnectionStrings:Redis"];
    options.InstanceName = "explAIned_";
});

builder.Services.AddSingleton<IDistributedCache>(sp =>
{
    var cacheProvider = sp.GetRequiredService<IConfiguration>()["Cache:Provider"] ?? "Redis";

    return string.Equals(cacheProvider, "Memory", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<MemoryDistributedCache>()
        : sp.GetRequiredService<RedisCache>();
});

builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

builder.Services.AddAuthorization();

builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));

builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"],
        Acks = Acks.All,
        EnableIdempotence = true,
    };
    return new ProducerBuilder<string, string>(producerConfig).Build();
});

builder.Services.AddSingleton<IUserEventProducer, UserEventProducer>();

builder.Services.AddScoped<CacheService>();
builder.Services.AddScoped<ICommentService, CommentService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseHttpMetrics();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapMetrics();

app.Run();

public partial class Program;
