using Microsoft.Extensions.DependencyInjection;
using MyApi.WebApi.Kafka;

public class MeteoConsumerBackgroundService : BackgroundService
{
    private readonly IMeteoConsumer _kafkaConsumer;
    private readonly IMeteoHandler _meteoHandler;
    private readonly IServiceScopeFactory _providerScopeFactory;

    public MeteoConsumerBackgroundService(IMeteoConsumer kafkaConsumer, IMeteoHandler meteoHandler, IServiceScopeFactory serviceScopeFactory)
    {
        _kafkaConsumer = kafkaConsumer;
        _meteoHandler = meteoHandler;
        _providerScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _kafkaConsumer.Subscribe("meteo");

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