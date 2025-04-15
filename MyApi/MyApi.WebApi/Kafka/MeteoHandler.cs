using Confluent.Kafka;

namespace MyApi.WebApi.Kafka;

public class MeteoHandler
{
    public Task<bool> ExecuteAsync(string message)
    {
        // je reçois un message
        if (message == null)
        {
            return Task.FromResult(false);
        }
        Console.WriteLine($"Message reçu : {message}");
        return Task.FromResult(true);
    }
}
