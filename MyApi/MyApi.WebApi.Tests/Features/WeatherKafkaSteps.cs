namespace MyApi.WebApi.Tests.Features;

using TechTalk.SpecFlow;
using Moq;
using System.Threading.Tasks;
using MyApi.WebApi.Kafka;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using MyApi.WebApi.Tests.Hooks;
using Microsoft.Extensions.DependencyInjection;

[Binding]
internal class WeatherKafkaSteps
{
    private readonly ScenarioContext _scenarioContext;

    public WeatherKafkaSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"The kafka topic meteo with a message ""(.*)""")]
    public async Task GivenTheKafkaTopicMeteoWithAMessage(string message)
    {
        _scenarioContext.TryAdd("kafkaMessage", message);
    }

    [When("the MeteoConsumerService starts")]
    public async Task WhenTheMeteoConsumerServiceStarts()
    {
        var application = _scenarioContext.Get<WebApplicationFactory<Program>>(InitWebApplicationFactory.ApplicationKey);
       var topic =  _scenarioContext["KafkaTopic"].ToString();
        var serviceProvider = application.Services;

        var kafkaConsumer = serviceProvider.GetRequiredService<IMeteoConsumer>();
        var meteoHandler = serviceProvider.GetRequiredService<IMeteoHandler>();
        var serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var backgroundService = new MeteoConsumerBackgroundService(kafkaConsumer, meteoHandler, serviceScopeFactory, topic);

        // Démarrer le service manuellement avec un CancellationToken
        var cancellationTokenSource = new CancellationTokenSource();
        _scenarioContext.TryAdd("CancellationTokenSource", cancellationTokenSource);

        var task = Task.Run(() => backgroundService.StartAsync(cancellationTokenSource.Token));
        _scenarioContext.TryAdd("BackgroundServiceTask", task);
        // Récupérer le bootstrap server depuis le ScenarioContext
        await Task.Delay(15000);
    }

    [Then(@"The message ""(.*)"" should be consumed")]
    public async void ThenTheMessageShouldBeConsumed(string p0)
    {
        if (_scenarioContext.TryGetValue("CancellationTokenSource", out var tokenSource) && tokenSource is CancellationTokenSource cancellationTokenSource)
        {
            cancellationTokenSource.Cancel();

            if (_scenarioContext.TryGetValue("BackgroundServiceTask", out var task) && task is Task backgroundServiceTask)
            {
                    await backgroundServiceTask; // Attendre que le service s'arrête proprement
            }
        }

        var mockHandler = _scenarioContext.Get<Mock<IMeteoHandler>>("meteoHandler");
        var message = _scenarioContext["kafkaMessage"].ToString() ?? "";

        mockHandler.Verify(h => h.ExecuteAsync(It.IsAny<string>()), Times.AtLeastOnce);
    }

}
