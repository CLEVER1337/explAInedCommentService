public interface ICommentService
{
    Task<IEnumerable<Comment>> GetByArticleAsync(string articleId);

    Task<IEnumerable<Comment>> GetByAuthorAsync(string authorId, int limit, int offset);

    Task<int> CountByAuthorAsync(string authorId);

    Task<Comment?> GetByIdAsync(int id);
    Task<int> CreateAsync(Comment comment);
    Task UpdateAsync(Comment comment);
    Task SoftDeleteAsync(Comment comment);
}
