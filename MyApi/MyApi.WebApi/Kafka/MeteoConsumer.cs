using Confluent.Kafka;

namespace MyApi.WebApi.Kafka;

public class MeteoConsumer : IDisposable, IMeteoConsumer
{
    private readonly IConsumer<string, string> _kafkaConsumer;

    public MeteoConsumer(IConsumer<string, string> kafkaConsumer)
    {
        _kafkaConsumer = kafkaConsumer ?? throw new ArgumentNullException(nameof(kafkaConsumer));
    }

    public void Dispose()
    {
        _kafkaConsumer?.Close();
        _kafkaConsumer?.Dispose();
    }

    public Task<string> ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = _kafkaConsumer?.Consume(cancellationToken);
            if (result == null || result.Message == null)
            {
                Console.WriteLine("Aucun message consommé.");
                return null;
            }

            Console.WriteLine($"Message Kafka reçu : {result.Message.Value}");
            return Task.FromResult(result.Message.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public void Subscribe(string topic)
    {
        _kafkaConsumer.Subscribe(topic);
    }
}
