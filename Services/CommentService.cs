using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public class CommentService : ICommentService
{
    private const int CacheTtlSeconds = 60;

    private readonly ApplicationDbContext _db;
    private readonly CacheService _cache;
    private readonly IUserEventProducer _userEventProducer;
    private readonly ILogger<CommentService> _logger;

    public CommentService(
        ApplicationDbContext db,
        CacheService cache,
        IUserEventProducer userEventProducer,
        ILogger<CommentService> logger)
    {
        _db = db;
        _cache = cache;
        _userEventProducer = userEventProducer;
        _logger = logger;
    }

    public async Task<IEnumerable<Comment>> GetByArticleAsync(string articleId)
    {
        var key = CacheKey(articleId);

        try
        {
            var cached = await _cache.GetValue(key);
            if (cached is not null)
            {
                var deserialized = JsonSerializer.Deserialize<List<Comment>>(cached);
                if (deserialized is not null) return deserialized;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for {Key}", key);
        }

        var list = await _db.Comments
            .Where(c => c.ArticleId == articleId && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        try
        {
            await _cache.SetValue(key, JsonSerializer.Serialize(list), TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }

        return list;
    }

    public async Task<IEnumerable<Comment>> GetByAuthorAsync(string authorId, int limit, int offset)
    {
        return await _db.Comments
            .Where(c => c.AuthorId == authorId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<int> CountByAuthorAsync(string authorId)
    {
        return _db.Comments
            .Where(c => c.AuthorId == authorId && c.DeletedAt == null)
            .CountAsync();
    }

    public Task<Comment?> GetByIdAsync(int id)
    {
        return _db.Comments.FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
    }

    public async Task<int> CreateAsync(Comment comment)
    {
        var now = DateTime.UtcNow;
        comment.CreatedAt = now;
        comment.UpdatedAt = now;
        comment.DeletedAt = null;

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();

        await InvalidateArticleCache(comment.ArticleId);

        await _userEventProducer.EmitAsync(
            "ArticleCommented",
            comment.AuthorId,
            comment.ArticleId,
            new Dictionary<string, object?> { ["commentId"] = comment.Id });

        return comment.Id;
    }

    public async Task UpdateAsync(Comment comment)
    {
        _db.Comments.Update(comment);
        await _db.SaveChangesAsync();
        await InvalidateArticleCache(comment.ArticleId);
    }

    public async Task SoftDeleteAsync(Comment comment)
    {
        comment.DeletedAt = DateTime.UtcNow;
        _db.Comments.Update(comment);
        await _db.SaveChangesAsync();
        await InvalidateArticleCache(comment.ArticleId);
    }

    private async Task InvalidateArticleCache(string articleId)
    {
        try
        {
            await _cache.RemoveValue(CacheKey(articleId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for article {ArticleId}", articleId);
        }
    }

    private static string CacheKey(string articleId) => $"comments:article:{articleId}";
}
