using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("comments")]
public class CommentController : Controller
{
    private readonly ICommentService _commentService;

    public CommentController(ICommentService commentService)
    {
        _commentService = commentService;
    }

    [HttpGet]
    [Route("article/{articleId}")]
    public async Task<IResult> ListForArticle([FromRoute] string articleId)
    {
        var comments = await _commentService.GetByArticleAsync(articleId);
        return Results.Ok(comments);
    }

    [HttpGet]
    [Route("author/{authorId}")]
    public async Task<IResult> ListForAuthor(
        [FromRoute] string authorId, [FromQuery] int? limit, [FromQuery] int? offset)
    {
        var effectiveLimit = Math.Clamp(limit ?? 20, 1, 100);
        var effectiveOffset = Math.Max(offset ?? 0, 0);

        var comments = await _commentService.GetByAuthorAsync(authorId, effectiveLimit, effectiveOffset);

        Response.Headers["X-Total-Count"] = (await _commentService.CountByAuthorAsync(authorId)).ToString();

        return Results.Ok(comments);
    }

    [HttpGet]
    [Route("{id:int}")]
    public async Task<IResult> GetById([FromRoute] int id)
    {
        var comment = await _commentService.GetByIdAsync(id);
        return comment is null ? Results.NotFound() : Results.Ok(comment);
    }

    [HttpPost]
    [Authorize]
    public async Task<IResult> Create([FromBody] CreateCommentDto dto)
    {
        var authorId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (authorId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(dto.ArticleId))
        {
            return Results.BadRequest("ArticleId is required");
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return Results.BadRequest("Content is required");
        }

        if (dto.Content.Length > 4000)
        {
            return Results.BadRequest("Content is too long (max 4000 characters)");
        }

        var comment = new Comment
        {
            ArticleId = dto.ArticleId,
            Content = dto.Content,
            AuthorId = authorId,
        };

        try
        {
            var id = await _commentService.CreateAsync(comment);
            return Results.Created($"/comments/{id}", new { Id = id });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    [HttpPut]
    [Route("{id:int}")]
    [Authorize]
    public async Task<IResult> Update([FromRoute] int id, [FromBody] UpdateCommentDto dto)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return Results.BadRequest("Content is required");
        }

        if (dto.Content.Length > 4000)
        {
            return Results.BadRequest("Content is too long (max 4000 characters)");
        }

        var existing = await _commentService.GetByIdAsync(id);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.AuthorId != userId)
        {
            return Results.Forbid();
        }

        existing.Content = dto.Content;
        existing.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _commentService.UpdateAsync(existing);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    [HttpDelete]
    [Route("{id:int}")]
    [Authorize]
    public async Task<IResult> Delete([FromRoute] int id)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var existing = await _commentService.GetByIdAsync(id);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.AuthorId != userId)
        {
            return Results.Forbid();
        }

        try
        {
            await _commentService.SoftDeleteAsync(existing);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
