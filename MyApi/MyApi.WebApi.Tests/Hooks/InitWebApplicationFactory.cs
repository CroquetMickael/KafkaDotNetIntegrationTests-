using BoDi;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using Microcks.Testcontainers;
using Microcks.Testcontainers.Connection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MyApi.WebApi.Kafka;
using Respawn;
using TechTalk.SpecFlow;
using Testcontainers.Kafka;

namespace MyApi.WebApi.Tests.Hooks;

[Binding]
internal class InitWebApplicationFactory
{
    internal const string HttpClientKey = nameof(HttpClientKey);
    internal const string ApplicationKey = nameof(ApplicationKey);

    [BeforeScenario]
    public async Task BeforeScenario(ScenarioContext scenarioContext, IObjectContainer objectContainer)
    {
        await InitializeRespawnAsync();
        await ReplaceKafkaMicrocks(scenarioContext);


        var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    ReplaceLogging(services);
                    ReplaceDatabase(services, objectContainer);
                    ReplaceKafkaTestContainer(services, scenarioContext);
                });
            });

        var client = application.CreateClient();

        scenarioContext.TryAdd(HttpClientKey, client);
        scenarioContext.TryAdd(ApplicationKey, application);
    }

    [AfterScenario]
    public void AfterScenario(ScenarioContext scenarioContext)
    {
        if (scenarioContext.TryGetValue(HttpClientKey, out var client) && client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (scenarioContext.TryGetValue(ApplicationKey, out var application) && application is IDisposable disposableApplication)
        {
            disposableApplication.Dispose();
        }
    }

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

    private static void ReplaceKafkaTestContainer(IServiceCollection services, ScenarioContext scenarioContext)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory != null);
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }

        var bootstrapServers = scenarioContext["KafkaBootstrapServers"].ToString();

        // Configuration Kafka
        services.AddSingleton(provider =>
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = "test-group",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };
            return new ConsumerBuilder<string, string>(config).Build();
        });

        //var mockKafkaConsumer = new Mock<IMeteoConsumer>();
        var meteoHandler = new Mock<IMeteoHandler>();
        //scenarioContext.TryAdd("kafkaConsumer", mockKafkaConsumer);
        scenarioContext.TryAdd("meteoHandler", meteoHandler);
        services.AddSingleton<IMeteoConsumer, MeteoConsumer>();
        services.AddSingleton(meteoHandler.Object);
    }

    private async static Task ReplaceKafkaMicrocks(ScenarioContext scenarioContext)
    {
        var network = new NetworkBuilder().Build();

        await network.CreateAsync();
        var listener = "test-kafka:19092";

        var kafkaContainer = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:latest")
                .WithNetwork(network)
                .WithListener(listener)
                .WithNetworkAliases("test-kafka")
                .Build();

        await kafkaContainer.StartAsync();
        await Task.Delay(5000);

        if (!File.Exists("Mocks\\meteoKafkaAsyncApi.yml"))
        {
            throw new FileNotFoundException("L'artefact Microcks 'meteoKafkaAsyncApi.yml' est introuvable.");
        }

        MicrocksContainerEnsemble ensemble = new MicrocksContainerEnsemble(network, "quay.io/microcks/microcks-uber:1.11.0")
            .WithMainArtifacts("Mocks\\meteoKafkaAsyncApi.yml")
            .WithKafkaConnection(new KafkaConnection(listener));

        await ensemble.StartAsync();
        var topic = ensemble.AsyncMinionContainer.GetKafkaMockTopic("Meteo Kafka API", "1.0.0", "meteo");

        // get bootstrap server
        var bootstrapServers = kafkaContainer.GetBootstrapAddress();

        scenarioContext.TryAdd("KafkaBootstrapServers", bootstrapServers);
        scenarioContext.TryAdd("KafkaTopic", topic);
    }

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
    }

    private static void ReplaceLogging(IServiceCollection services)
    {
        services.RemoveAll(typeof(ILogger<>));
        services.RemoveAll<ILogger>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private void ReplaceDatabase(IServiceCollection services, IObjectContainer objectContainer)
    {
        services.RemoveAll<DbContextOptions<WeatherContext>>();
        services.RemoveAll<WeatherContext>();

        services.AddDbContext<WeatherContext>(options =>
            options.UseSqlServer(DatabaseHook.MsSqlContainer.GetConnectionString(), providerOptions =>
            {
                providerOptions.EnableRetryOnFailure();
            }));

        var database = new WeatherContext(new DbContextOptionsBuilder<WeatherContext>()
            .UseSqlServer(DatabaseHook.MsSqlContainer.GetConnectionString())
            .Options);

        objectContainer.RegisterInstanceAs(database);
    }

    private async Task InitializeRespawnAsync()
    {
        var respawner = await Respawner.CreateAsync(
            DatabaseHook.MsSqlContainer.GetConnectionString(),
            new()
            {
                DbAdapter = DbAdapter.SqlServer
            });

        await respawner.ResetAsync(DatabaseHook.MsSqlContainer.GetConnectionString());
    }
}
