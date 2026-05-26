using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

public class CommentEndpointsTests : IClassFixture<CommentWebApplicationFactory>
{
    private const string JwtKey = "JwtSecretPlaceHolder_explAIned32";
    private const string Issuer = "http://localhost:5125/";
    private const string Audience = "http://localhost:5125/";

    private const string AuthorId = "author-1";
    private const string StrangerId = "stranger-2";
    private const string ArticleId = "article-abc";

    private readonly CommentWebApplicationFactory _factory;

    public CommentEndpointsTests(CommentWebApplicationFactory factory)
    {
        _factory = factory;
        ResetState();
    }

    private void ResetState()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Comments.RemoveRange(db.Comments);
        db.SaveChanges();

        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        cache.Remove($"comments:article:{ArticleId}");
        cache.Remove("comments:article:other");
    }

    private Comment Seed(Comment comment)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        if (comment.CreatedAt == default) comment.CreatedAt = now;
        if (comment.UpdatedAt == default) comment.UpdatedAt = now;
        db.Comments.Add(comment);
        db.SaveChanges();
        return comment;
    }

    private Comment? Reload(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Comments.AsNoTracking().FirstOrDefault(c => c.Id == id);
    }

    private HttpClient CreateClient(string? userId = null)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAccessToken(userId));
        }
        return client;
    }

    private static string CreateAccessToken(string userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Email, $"{userId}@test"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, "access"),
            },
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record CreatedResponse(
        [property: JsonPropertyName("id")] int Id);

    [Fact]
    public async Task ListForArticle_Empty_Returns200WithEmptyArray()
    {
        var client = CreateClient();

        var resp = await client.GetAsync($"/comments/article/{ArticleId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<Comment>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task ListForArticle_ReturnsSeededAndExcludesDeleted()
    {
        Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "first" });
        Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "deleted", DeletedAt = DateTime.UtcNow });
        Seed(new Comment { ArticleId = "other", AuthorId = AuthorId, Content = "other-article" });

        var client = CreateClient();
        var resp = await client.GetAsync($"/comments/article/{ArticleId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<Comment>>();
        Assert.NotNull(body);
        Assert.Single(body!);
        Assert.Equal("first", body![0].Content);
    }

    [Fact]
    public async Task GetById_Found_Returns200()
    {
        var seeded = Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "hi" });
        var client = CreateClient();

        var resp = await client.GetAsync($"/comments/{seeded.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var client = CreateClient();

        var resp = await client.GetAsync("/comments/999999");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_SoftDeleted_Returns404()
    {
        var seeded = Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "x", DeletedAt = DateTime.UtcNow });
        var client = CreateClient();

        var resp = await client.GetAsync($"/comments/{seeded.Id}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var resp = await client.PostAsJsonAsync("/comments", new CreateCommentDto(ArticleId, "hi"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithGarbageToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "garbage");

        var resp = await client.PostAsJsonAsync("/comments", new CreateCommentDto(ArticleId, "hi"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidToken_Returns201_AndPersistsWithAuthor()
    {
        var client = CreateClient(AuthorId);

        var resp = await client.PostAsJsonAsync("/comments", new CreateCommentDto(ArticleId, "first comment"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(body);
        var stored = Reload(body!.Id);
        Assert.NotNull(stored);
        Assert.Equal(AuthorId, stored!.AuthorId);
        Assert.Equal("first comment", stored.Content);
        Assert.Equal(ArticleId, stored.ArticleId);
    }

    [Fact]
    public async Task Create_EmptyContent_Returns400()
    {
        var client = CreateClient(AuthorId);

        var resp = await client.PostAsJsonAsync("/comments", new CreateCommentDto(ArticleId, "   "));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_AsAuthor_Returns204AndPersistsChange()
    {
        var seeded = Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "old" });
        var client = CreateClient(AuthorId);

        var resp = await client.PutAsJsonAsync($"/comments/{seeded.Id}", new UpdateCommentDto("new"));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var stored = Reload(seeded.Id);
        Assert.Equal("new", stored!.Content);
    }

    [Fact]
    public async Task Update_AsStranger_Returns403()
    {
        var seeded = Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "old" });
        var client = CreateClient(StrangerId);

        var resp = await client.PutAsJsonAsync($"/comments/{seeded.Id}", new UpdateCommentDto("new"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var client = CreateClient(AuthorId);

        var resp = await client.PutAsJsonAsync("/comments/999999", new UpdateCommentDto("new"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var resp = await client.PutAsJsonAsync("/comments/1", new UpdateCommentDto("new"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_AsAuthor_Returns204_AndSubsequentReadsReturn404()
    {
        var seeded = Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "bye" });
        var client = CreateClient(AuthorId);

        var del = await client.DeleteAsync($"/comments/{seeded.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await client.GetAsync($"/comments/{seeded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var list = await client.GetAsync($"/comments/article/{ArticleId}");
        var body = await list.Content.ReadFromJsonAsync<List<Comment>>();
        Assert.Empty(body!);
    }

    [Fact]
    public async Task Delete_AsStranger_Returns403()
    {
        var seeded = Seed(new Comment { ArticleId = ArticleId, AuthorId = AuthorId, Content = "x" });
        var client = CreateClient(StrangerId);

        var resp = await client.DeleteAsync($"/comments/{seeded.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var client = CreateClient(AuthorId);

        var resp = await client.DeleteAsync("/comments/999999");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var resp = await client.DeleteAsync("/comments/1");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
