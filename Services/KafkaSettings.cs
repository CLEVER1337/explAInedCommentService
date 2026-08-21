public class KafkaSettings
{
    public string BootstrapServers { get; set; } = default!;
    public string UserEventsTopic { get; set; } = "user.events";
}
