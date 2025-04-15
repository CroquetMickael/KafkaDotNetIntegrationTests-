using MyApi.WebApi.Kafka;

public class MeteoConsumerBackgroundService : BackgroundService
{
    private readonly IMeteoConsumer _kafkaConsumer;
    private readonly IServiceProvider _provider;

    public MeteoConsumerBackgroundService(IMeteoConsumer kafkaConsumer, IServiceProvider provider)
    {
        _kafkaConsumer = kafkaConsumer;
        _provider = provider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _kafkaConsumer.Subscribe("meteo");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumeResult = await _kafkaConsumer.ConsumeAsync(stoppingToken);
                var handlerMeteo = _provider.GetRequiredService<MeteoHandler>();
                await handlerMeteo.ExecuteAsync(consumeResult);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service
        }
    }
}