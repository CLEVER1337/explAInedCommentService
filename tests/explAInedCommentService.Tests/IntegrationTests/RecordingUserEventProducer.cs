public sealed class RecordingUserEventProducer : IUserEventProducer
{
    public sealed record Emission(
        string EventType, string UserId, string ArticleId, IDictionary<string, object?>? Metadata);

    private readonly List<Emission> _emissions = [];

    public IReadOnlyList<Emission> Emissions
    {
        get { lock (_emissions) return _emissions.ToList(); }
    }

    public Task EmitAsync(
        string eventType,
        string userId,
        string articleId,
        IDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        lock (_emissions)
        {
            _emissions.Add(new Emission(eventType, userId, articleId, metadata));
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        lock (_emissions) _emissions.Clear();
    }
}
