using Microsoft.AspNetCore.Mvc.Testing;
using TechTalk.SpecFlow;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BoDi;
using Respawn;
using Moq;
using MyApi.WebApi.Kafka;
using Microsoft.Extensions.Hosting;
using Testcontainers.Kafka;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using Confluent.Kafka.Admin;

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

        var kafkaContainer = new KafkaBuilder()
         .WithImage("confluentinc/cp-kafka:latest")
            .WithEnvironment("KAFKA_BROKER_ID", "1")
            .WithEnvironment("KAFKA_ZOOKEEPER_CONNECT", "localhost:2181")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "PLAINTEXT://localhost:9092")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithPortBinding(9092, 9092)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9092))
            .Build();

        await kafkaContainer.StartAsync();

        scenarioContext.TryAdd("kafkaContainer", kafkaContainer);

        var bootstrapServers = kafkaContainer.GetBootstrapAddress();
        scenarioContext["KafkaBootstrapServers"] = bootstrapServers;

        await CreateKafkaTopicAsync(bootstrapServers, "meteo");

        var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    ReplaceLogging(services);
                    ReplaceDatabase(services, objectContainer);
                    ReplaceKafka(services, bootstrapServers, scenarioContext);
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

    private static void ReplaceKafka(IServiceCollection services, string bootstrapServers, ScenarioContext scenarioContext)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MeteoConsumerBackgroundService));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }

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
