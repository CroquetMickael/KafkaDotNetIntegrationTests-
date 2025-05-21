# Module 3: Test du service Kafka avec TestContainers

## Qu'allons-nous faire?

Comme vu dans le module 2, nous n'avons en réalité pas tester notre application, ajustons cela en passant par un testContainer Kafka qui permettra de produce un vrai message.

## Instructions

### Modification du code applicatif

Nous allons devoir modifier le code applicatif pour pouvoir injecter un `IMeteoHandler`, le but étant d'effectuer un mock de cette interface dans notre test plus tard.

Nous allons aussi devoir gérer les exceptions lié à l'arrêt d'un service via l'exception `OperationCanceledException`

Il faudra modifier le backgroundService pour accepter notre nouvelle interface via de l'injection de dépendance.

#### IMeteoHandler

```c#
namespace MyApi.WebApi.Kafka;

public interface IMeteoHandler
{
    public Task<bool> ExecuteAsync(string message);
}
```

#### MeteoHandler

```diff
+namespace MyApi.WebApi.Kafka;

+public class MeteoHandler: IMeteoHandler
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
```

### MeteoConsumer

Modifions notre `Consumer` pour gérer l'arrêt de l'application de manière propre.

```diff
catch (OperationCanceledException)
{
-    return null
+    throw;
}
```

### MeteoConsumerBackgroundService

```diff
+   using Microsoft.Extensions.DependencyInjection;
    using MyApi.WebApi.Kafka;

public class MeteoConsumerBackgroundService : BackgroundService
{
    private readonly IMeteoConsumer _kafkaConsumer;
-   private readonly IServiceProvider _provider;
+   private readonly IMeteoHandler _meteoHandler;
+   private readonly IServiceScopeFactory _providerScopeFactory;

- public MeteoConsumerBackgroundService(IMeteoConsumer kafkaConsumer, IServiceProvider provider)
+ public MeteoConsumerBackgroundService(IMeteoConsumer kafkaConsumer, IMeteoHandler meteoHandler, IServiceScopeFactory serviceScopeFactory)
    {
        _kafkaConsumer = kafkaConsumer;
+        _meteoHandler = meteoHandler;
+        _providerScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _kafkaConsumer.Subscribe("meteo");

+        using var scope = _providerScopeFactory.CreateScope();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumeResult = await _kafkaConsumer.ConsumeAsync(stoppingToken);
-               var handlerMeteo = _provider.GetRequiredService<MeteoHandler>();
-               await handlerMeteo.ExecuteAsync(consumeResult);
+               await _meteoHandler.ExecuteAsync(consumeResult);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service
        }
        catch (Exception ex)
        {
            throw;
        }
    }
}
```

#### Program.cs

Pour finir nous devons ajuster un element de cycle de vie de notre application.

```diff
-builder.Services.AddTransient<MeteoHandler>();
+builder.Services.AddTransient<IMeteoHandler, MeteoHandler>();
```

Bien notre code applicatif est prêt, modifions notre test pour accepter ces changements !

### Modification du code de test

Pour utiliser Test Container & Kafka Test Container, il est obligatoire d'installer `   Testcontainers` & `Testcontainers.Kafka`. Néanmoins pour des soucis de simplicité, les deux références à ces packages Nugets sont déjà présente dans `MyApi.WebApi.Tests.csproj`

Nous allons commencer par modifier le hook de démarrage de notre test `InitWebApplicationFactory.cs`

### InitWebApplicationFactory

```diff
await InitializeRespawnAsync();

+        var kafkaContainer = new KafkaBuilder()
+         .WithImage("confluentinc/cp-kafka:latest")
+            .WithEnvironment("KAFKA_BROKER_ID", "1")
+            .WithEnvironment("KAFKA_ZOOKEEPER_CONNECT", "localhost:2181")
+            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "PLAINTEXT://localhost:9092")
+            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
+            .WithPortBinding(9092, 9092)
+            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9092))
+            .Build();

+        await kafkaContainer.StartAsync();

+        scenarioContext.TryAdd("kafkaContainer", kafkaContainer);

+        var bootstrapServers = kafkaContainer.GetBootstrapAddress();
+       scenarioContext["KafkaBootstrapServers"] = bootstrapServers;

+        await CreateKafkaTopicAsync(bootstrapServers, "meteo");

var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    ReplaceLogging(services);
                    ReplaceDatabase(services, objectContainer);
-                   ReplaceKafka(services, scenarioContext);
+                   ReplaceKafka(services, bootstrapServers, scenarioContext);
                });
            });
```

Modifions la fonction `ReplaceKafka`

```diff
 private static void ReplaceKafka(IServiceCollection services, string bootstrapServers, ScenarioContext scenarioContext)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MeteoConsumerBackgroundService));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }

+        // Configuration Kafka
+        services.AddSingleton(provider =>
+        {
+            var config = new ConsumerConfig
+            {
+                BootstrapServers = bootstrapServers,
+                GroupId = "test-group",
+                AutoOffsetReset = AutoOffsetReset.Earliest
+            };
+            return new ConsumerBuilder<string, string>(config).Build();
+        });
+
-        var mockKafkaConsumer = new Mock<IMeteoConsumer>();
-        scenarioContext.TryAdd("kafkaConsumer", mockKafkaConsumer);
-        services.AddTransient<IMeteoConsumer, MeteoConsumer>();
-        services.AddTransient<MeteoHandler>();
+        var meteoHandler = new Mock<IMeteoHandler>();
+        scenarioContext.TryAdd("meteoHandler", meteoHandler);
+        services.AddSingleton<IMeteoConsumer, MeteoConsumer>();
+        services.AddSingleton(meteoHandler.Object);
    }
```

Et créer la fonction `CreateKafkaTopicAsync`

```c#
private static async Task CreateKafkaTopicAsync(string bootstrapServers, string topicName)
    {
        var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
        using var adminClient = new AdminClientBuilder(config).Build();

        try
        {
            await adminClient.CreateTopicsAsync(new[]
            {
            new TopicSpecification
            {
                Name = topicName,
                NumPartitions = 1, // Nombre de partitions
                ReplicationFactor = 1 // Facteur de réplication
            }
        });
            Console.WriteLine($"Topic '{topicName}' créé avec succès.");
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            Console.WriteLine($"Le topic '{topicName}' existe déjà.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de la création du topic '{topicName}': {ex.Message}");
            throw;
        }
```

### Modification des steps

Nous devons modifier le code de nos steps pour intégrer les modifications apporté dans nos classes et notre hook de démarrage.

#### WeatherKafkaSteps.cs

#### Given

```diff
using Moq;
using System.Threading.Tasks;
using MyApi.WebApi.Kafka;
+using Confluent.Kafka;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.Extensions.Hosting;
+using MyApi.WebApi.Tests.Hooks;
+using Microsoft.Extensions.DependencyInjection;

 [Given(@"The kafka topic meteo with a message ""(.*)""")]
    public async Task GivenTheKafkaTopicMeteoWithAMessage(string message)
    {
-var mock = _scenarioContext.Get<Mock<IMeteoConsumer>>("kafkaConsumer");
-        mock
-         .Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
-         .ReturnsAsync(p1)
+        _scenarioContext.TryAdd("kafkaMessage", message);
    }
```

#### When

```diff
    [When("the MeteoConsumerService starts")]
    public async Task WhenTheMeteoConsumerServiceStarts()
    {
-        var mock = _scenarioContext.Get<Mock<IMeteoConsumer>>("kafkaConsumer");
-        var consumerResult = mock.Object.ConsumeAsync(It.IsAny<CancellationToken>());
-        _scenarioContext.TryAdd("result", consumerResult);

+        var application = _scenarioContext.Get<WebApplicationFactory<Program>>(InitWebApplicationFactory.ApplicationKey);
+        var serviceProvider = application.Services;

+        var kafkaConsumer = serviceProvider.GetRequiredService<IMeteoConsumer>();
+        var meteoHandler = serviceProvider.GetRequiredService<IMeteoHandler>();
+        var serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

+        var backgroundService = new MeteoConsumerBackgroundService(kafkaConsumer, meteoHandler, serviceScopeFactory);

+        var cancellationTokenSource = new CancellationTokenSource();
+        _scenarioContext.TryAdd("CancellationTokenSource", cancellationTokenSource);

+        var task = Task.Run(() => backgroundService.StartAsync(cancellationTokenSource.Token));
+        _scenarioContext.TryAdd("BackgroundServiceTask", task);
+        var bootstrapServers = _scenarioContext["KafkaBootstrapServers"].ToString();

+        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };
+        var producer = new ProducerBuilder<Null, string>(producerConfig).Build();
+        var message = _scenarioContext["kafkaMessage"].ToString();
+        var deliveryResult = await producer.ProduceAsync("meteo", new Message<Null, string> { Value = message });
+        await Task.Delay(5000);

+        Console.WriteLine($"Message produit : {deliveryResult.Value} sur le topic {deliveryResult.TopicPartition.Topic}");
    }
```

La nouvelle version de la méthode `WhenTheMeteoConsumerServiceStarts` remplace l'utilisation d'un mock de `IMeteoConsumer` par une instance réelle, permettant de tester le service dans un environnement plus réaliste. Elle crée et démarre un service en arrière-plan (`MeteoConsumerBackgroundService`) avec un `CancellationTokenSource` pour gérer l'annulation, intégrant ainsi la consommation de messages.

De plus, elle configure un producteur Kafka pour envoyer un message au topic "meteo", tandis que l'ancienne version ne gérait pas la production de messages.

#### Then

```diff
[Then(@"The message ""(.*)"" should be consumed")]
    public async void ThenTheMessageShouldBeConsumed(string p0)
    {
-        var result = _scenarioContext["result"];
-        var handler = new MeteoHandler();
+        if (_scenarioContext.TryGetValue("CancellationTokenSource", out var tokenSource) && tokenSource is CancellationTokenSource cancellationTokenSource)
+        {
+            cancellationTokenSource.Cancel();

+            if (_scenarioContext.TryGetValue("BackgroundServiceTask", out var task) && task is Task backgroundServiceTask)
+            {
+                    await backgroundServiceTask; // Attendre que le service s'arrête proprement
+            }
+        }

+        var mockHandler = _scenarioContext.Get<Mock<IMeteoHandler>>("meteoHandler");
+        var message = _scenarioContext["kafkaMessage"].ToString() ?? "";

-        var handlerResult = await handler.ExecuteAsync(p0);
-        Assert.True(handlerResult);
+        mockHandler.Verify(h => h.ExecuteAsync(message), Times.AtLeastOnce);
    }
```

La nouvelle version de la méthode `ThenTheMessageShouldBeConsumed` améliore la gestion des annulations en introduisant un `CancellationTokenSource` pour arrêter proprement un service en arrière-plan, et attend l'achèvement de ce service avant de continuer.

Au lieu d'utiliser directement une instance de `MeteoHandler`, elle s'appuie sur un mock de `IMeteoHandler` pour vérifier que la méthode `ExecuteAsync` est appelée avec le message approprié, assurant ainsi un test plus fiable et indépendant des implémentations concrètes.

Enfin, le message est récupéré à partir du contexte, garantissant que le bon contenu est vérifié.

Vous pouvez relancé le test, celui-ci sera en succès, ici comme vous l'avez vu, nous utilisons une vrai file kafka avec test container, et nous démarrons réellement notre background service.

Nous sommes donc passé d'un semblant de test a un test d'intégration complet, seul la partie `Handler` n'est pas testé mais cela dépendra de votre code applicatif, ici comme précisé plus tôt dans ce DOJO, j'ai tenté de gardé un fonctionnement très simple.

```
git clone https://github.com/CroquetMickael/KafkaDotNetIntegrationTests.git --branch feature/module3
```

[suivant >](../Module%202%20Ajout%20des%20tests%20du%20service%20externe/readme.md)
