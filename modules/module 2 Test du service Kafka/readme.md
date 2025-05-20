# Module 1: création du projet de test

## Qu'allons-nous faire?

Nous allons testé notre connexion à notre service Kafka.

## Instructions

### Création du Gherkin

Dans un premier temps, créer un fichier nommé `WeatherKafka.feature` avec le contenu suivant :

```gherkin
Feature: Kafka Consumer Integration
  As a developer
  I want to test the integration of MeteoConsumerService
  So that I can verify it consumes messages from Kafka

  Scenario: Consume a message from Kafka
    Given The kafka topic meteo with a message "Test Message"
    When the MeteoConsumerService starts
    Then The message "Test Message" should be consumed
```

Dans un second temps, nous allons créer l'implémentation de ces steps dans le fichier `WeatherKafkaSteps.cs` au même niveau que le fichier `WeatherKafka.feature`

```c#
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
```

Bien qu'on ai définit le Gherkin & les steps associé via notre code en C#, il nous faut modifier le hook de démarrage de nos tests.

### InitWebApplicationFactory.cs

Pour modifier le hook de démarrage, il faut modifier le fichier `InitWebApplicationFactory.cs`

Nous allons ajouter une nouvelle fonction privée nommé `ReplaceKafka`

```c#
private static void ReplaceKafka(IServiceCollection services, ScenarioContext scenarioContext)
{
    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MeteoConsumerBackgroundService));
    if (descriptor != null)
    {
        services.Remove(descriptor);
    }

    var mockKafkaConsumer = new Mock<IMeteoConsumer>();
    scenarioContext.TryAdd("kafkaConsumer", mockKafkaConsumer);

    services.AddTransient<IMeteoConsumer, MeteoConsumer>();
    services.AddTransient<MeteoHandler>();
}
```

Et nous allons l'intégrer dans la fonction `ConfigureTestServices`

```diff
var application = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder =>
    {
        builder.ConfigureTestServices(services =>
        {
            ReplaceLogging(services);
            ReplaceDatabase(services, objectContainer);
+            ReplaceKafka(services, scenarioContext);
        });
    });
```

Si vous lancez vos test, vous devriez voir un nouveau test lié à Kafka et il sera normalement en succès.

### Mais on test vraiment ?

Non en réalité, ce test ne fait rien de réel, mais c'est voulu, en démontrant qu'il était très compliqué de tester Kafka en utilisant simplement MOQ.

Pour tester de manière pertinente, il faut démarrer le background service et envoyé un message dans le consommateur.

Pour résumer :

Ce code ne teste rien d’utile : il ne fait que manipuler des mocks locaux et appeler des méthodes manuellement, sans jamais traverser le pipeline réel de l’application.

Pour un vrai test d’intégration

- Démarrez l’application avec tous ses services.
- Publiez un message sur Kafka.
- Laissez le service consommer ce message.
- Vérifiez que le handler a bien été appelé.

Nous allons voir dans le prochain module comment intégrer un test container qui permettra de consommer un vrai message Kafka dans notre test et ainsi gommé le manque d'efficacité présent ici.

```
git clone https://github.com/CroquetMickael/KafkaDotNetIntegrationTests.git --branch feature/module2
```

[suivant >](../Module%202%20Ajout%20des%20tests%20du%20service%20externe/readme.md)
