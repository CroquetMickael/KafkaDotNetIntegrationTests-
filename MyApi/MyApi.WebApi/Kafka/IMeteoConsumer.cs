namespace MyApi.WebApi.Kafka;

public interface IMeteoConsumer
{
    void Subscribe(string topic);
    Task<string> ConsumeAsync(CancellationToken cancellationToken);
}