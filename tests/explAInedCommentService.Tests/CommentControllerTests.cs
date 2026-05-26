using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class CommentControllerTests
{
    private const string AuthorId = "author-123";
    private const string OtherUserId = "someone-else";
    private const string ArticleId = "article-1";

    private static CommentController Build(ICommentService service, string? sub = AuthorId)
    {
        var controller = new CommentController(service);

        var claims = sub is null ? Array.Empty<Claim>() : [new Claim(JwtRegisteredClaimNames.Sub, sub)];
        var identity = sub is null ? new ClaimsIdentity() : new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static Comment SampleComment(string? authorId = AuthorId, int id = 1) => new()
    {
        Id = id,
        ArticleId = ArticleId,
        AuthorId = authorId ?? AuthorId,
        Content = "hello",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static int StatusOf(IResult result) => result switch
    {
        ForbidHttpResult => StatusCodes.Status403Forbidden,
        IStatusCodeHttpResult s => s.StatusCode ?? 200,
        _ => throw new InvalidOperationException($"Result {result.GetType().Name} has no status code"),
    };

    [Fact]
    public async Task ListForArticle_ReturnsOk()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByArticleAsync(ArticleId)).ReturnsAsync(new[] { SampleComment() });
        var controller = Build(svc.Object);

        var result = await controller.ListForArticle(ArticleId);

        Assert.IsAssignableFrom<Ok<IEnumerable<Comment>>>(result);
    }

    [Fact]
    public async Task GetById_NotFound_WhenNull()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((Comment?)null);
        var controller = Build(svc.Object);

        var result = await controller.GetById(99);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task GetById_Ok_WhenFound()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(SampleComment());
        var controller = Build(svc.Object);

        var result = await controller.GetById(1);

        Assert.IsAssignableFrom<Ok<Comment>>(result);
    }

    [Fact]
    public async Task Create_Unauthorized_WhenNoSub()
    {
        var svc = new Mock<ICommentService>();
        var controller = Build(svc.Object, sub: null);

        var result = await controller.Create(new CreateCommentDto(ArticleId, "hi"));

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
        svc.Verify(s => s.CreateAsync(It.IsAny<Comment>()), Times.Never);
    }

    [Fact]
    public async Task Create_BadRequest_WhenArticleIdEmpty()
    {
        var svc = new Mock<ICommentService>();
        var controller = Build(svc.Object);

        var result = await controller.Create(new CreateCommentDto("  ", "hi"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        svc.Verify(s => s.CreateAsync(It.IsAny<Comment>()), Times.Never);
    }

    [Fact]
    public async Task Create_BadRequest_WhenContentEmpty()
    {
        var svc = new Mock<ICommentService>();
        var controller = Build(svc.Object);

        var result = await controller.Create(new CreateCommentDto(ArticleId, ""));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Create_BadRequest_WhenContentTooLong()
    {
        var svc = new Mock<ICommentService>();
        var controller = Build(svc.Object);

        var result = await controller.Create(new CreateCommentDto(ArticleId, new string('x', 4001)));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Create_Created_OnSuccess_AndAuthorIdFromSub()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.CreateAsync(It.IsAny<Comment>())).ReturnsAsync(42);
        var controller = Build(svc.Object);

        var result = await controller.Create(new CreateCommentDto(ArticleId, "hello"));

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        svc.Verify(s => s.CreateAsync(It.Is<Comment>(c =>
            c.ArticleId == ArticleId &&
            c.Content == "hello" &&
            c.AuthorId == AuthorId)), Times.Once);
    }

    [Fact]
    public async Task Create_Problem_WhenServiceThrows()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.CreateAsync(It.IsAny<Comment>())).ThrowsAsync(new Exception("boom"));
        var controller = Build(svc.Object);

        var result = await controller.Create(new CreateCommentDto(ArticleId, "hi"));

        Assert.Equal(StatusCodes.Status500InternalServerError, StatusOf(result));
    }

    [Fact]
    public async Task Update_Unauthorized_WhenNoSub()
    {
        var svc = new Mock<ICommentService>();
        var controller = Build(svc.Object, sub: null);

        var result = await controller.Update(1, new UpdateCommentDto("x"));

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task Update_NotFound_WhenMissing()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync((Comment?)null);
        var controller = Build(svc.Object);

        var result = await controller.Update(1, new UpdateCommentDto("x"));

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Update_Forbidden_WhenAuthorMismatch()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(SampleComment(OtherUserId));
        var controller = Build(svc.Object);

        var result = await controller.Update(1, new UpdateCommentDto("x"));

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        svc.Verify(s => s.UpdateAsync(It.IsAny<Comment>()), Times.Never);
    }

    [Fact]
    public async Task Update_NoContent_OnSuccess_AndContentUpdated()
    {
        var existing = SampleComment();
        var before = existing.UpdatedAt;
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(existing);
        var controller = Build(svc.Object);

        var result = await controller.Update(1, new UpdateCommentDto("edited"));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        svc.Verify(s => s.UpdateAsync(It.Is<Comment>(c =>
            c.Content == "edited" &&
            c.UpdatedAt >= before)), Times.Once);
    }

    [Fact]
    public async Task Delete_Unauthorized_WhenNoSub()
    {
        var svc = new Mock<ICommentService>();
        var controller = Build(svc.Object, sub: null);

        var result = await controller.Delete(1);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task Delete_NotFound_WhenMissing()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync((Comment?)null);
        var controller = Build(svc.Object);

        var result = await controller.Delete(1);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Delete_Forbidden_WhenAuthorMismatch()
    {
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(SampleComment(OtherUserId));
        var controller = Build(svc.Object);

        var result = await controller.Delete(1);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        svc.Verify(s => s.SoftDeleteAsync(It.IsAny<Comment>()), Times.Never);
    }

    [Fact]
    public async Task Delete_NoContent_OnSuccess()
    {
        var existing = SampleComment();
        var svc = new Mock<ICommentService>();
        svc.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(existing);
        var controller = Build(svc.Object);

        var result = await controller.Delete(1);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        svc.Verify(s => s.SoftDeleteAsync(existing), Times.Once);
    }
}
