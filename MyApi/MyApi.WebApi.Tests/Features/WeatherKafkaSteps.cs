namespace MyApi.WebApi.Tests.Features;

using TechTalk.SpecFlow;
using Moq;
using System.Threading.Tasks;
using MyApi.WebApi.Kafka;

[Binding]
internal class WeatherKafkaSteps
{
    private readonly ScenarioContext _scenarioContext;

    public WeatherKafkaSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"The kafka topic meteo with a message ""(.*)""")]
    public void GivenTheKafkaTopicMeteoWithAMessage(string p1)
    {
        var mock = _scenarioContext.Get<Mock<IMeteoConsumer>>("kafkaConsumer");
        mock
         .Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
         .ReturnsAsync(p1);
    }

    [When("the MeteoConsumerService starts")]
    public async Task WhenTheMeteoConsumerServiceStarts()
    {
        var mock = _scenarioContext.Get<Mock<IMeteoConsumer>>("kafkaConsumer");
        var consumerResult = mock.Object.ConsumeAsync(It.IsAny<CancellationToken>());
        _scenarioContext.TryAdd("result", consumerResult);
    }

    [Then(@"The message ""(.*)"" should be consumed")]
    public async void ThenTheMessageShouldBeConsumed(string p0)
    {
        var result = _scenarioContext["result"];
        var handler = new MeteoHandler();

        var handlerResult = await handler.ExecuteAsync(p0);
        Assert.True(handlerResult);
    }

}
