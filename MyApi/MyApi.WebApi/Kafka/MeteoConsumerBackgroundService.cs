using Microsoft.Extensions.DependencyInjection;
using MyApi.WebApi.Kafka;

public class MeteoConsumerBackgroundService : BackgroundService
{
    private readonly IMeteoConsumer _kafkaConsumer;
    private readonly IMeteoHandler _meteoHandler;
    private readonly IServiceScopeFactory _providerScopeFactory;
    private readonly string _topic;

    public MeteoConsumerBackgroundService(IMeteoConsumer kafkaConsumer, IMeteoHandler meteoHandler, IServiceScopeFactory serviceScopeFactory, string topic)
    {
        _kafkaConsumer = kafkaConsumer;
        _meteoHandler = meteoHandler;
        _providerScopeFactory = serviceScopeFactory;
        _topic = topic;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _kafkaConsumer.Subscribe(_topic);

        using var scope = _providerScopeFactory.CreateScope();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumeResult = await _kafkaConsumer.ConsumeAsync(stoppingToken);
                await _meteoHandler.ExecuteAsync(consumeResult);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur dans le service : {ex.Message}");
            throw;  // Relancer l'exception si elle n'est pas attendue
        }
    }
}