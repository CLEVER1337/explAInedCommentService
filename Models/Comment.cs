public class Comment
{
    public int Id { get; set; }
    public string AuthorId { get; set; } = default!;
    public string ArticleId { get; set; } = default!;
    public string Content { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
