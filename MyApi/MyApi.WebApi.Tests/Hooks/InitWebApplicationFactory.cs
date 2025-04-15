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
using Confluent.Kafka;
using Moq;
using MyApi.WebApi.Kafka;
using Microsoft.Extensions.Hosting;

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

        var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    ReplaceLogging(services);
                    ReplaceDatabase(services, objectContainer);
                    ReplaceKafka(services, scenarioContext);
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
