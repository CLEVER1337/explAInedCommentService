public interface ICommentService
{
    Task<IEnumerable<Comment>> GetByArticleAsync(string articleId);
    Task<Comment?> GetByIdAsync(int id);
    Task<int> CreateAsync(Comment comment);
    Task UpdateAsync(Comment comment);
    Task SoftDeleteAsync(Comment comment);
}
