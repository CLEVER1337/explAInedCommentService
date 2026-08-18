using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class CommentWebApplicationFactory : WebApplicationFactory<Program>
{
    public string InMemoryDbName { get; } = $"explAIned-comments-tests-{Guid.NewGuid()}";

    public RecordingUserEventProducer UserEvents { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "InMemory",
                ["Database:InMemoryName"] = InMemoryDbName,
                ["Cache:Provider"] = "Memory",
                ["Jwt:Key"] = "JwtSecretPlaceHolder_explAIned32",
                ["Jwt:Issuer"] = "http://localhost:5125/",
                ["Jwt:Audience"] = "http://localhost:5125/",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IUserEventProducer>(UserEvents);
        });
    }

    public ApplicationDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }
}
