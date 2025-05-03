using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using MyApi.WebApi;
using MyApi.WebApi.Kafka;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.MapType<DateOnly>(() => new OpenApiSchema
{
    Type = "string",
    Format = "date"
}));

// Configuration Kafka
builder.Services.AddSingleton(provider =>
{
    var config = new ConsumerConfig
    {
        BootstrapServers = "localhost:9093",
        GroupId = "1",
        AutoOffsetReset = AutoOffsetReset.Earliest
    };
    return new ConsumerBuilder<string, string>(config).Build();
});

builder.Services.AddTransient<IMeteoConsumer, MeteoConsumer>();
builder.Services.AddTransient<IMeteoHandler, MeteoHandler>();
builder.Services.AddHostedService(provider =>
{
    var kafkaConsumer = provider.GetRequiredService<IMeteoConsumer>();
    var serviceScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
    var meteoHandler = provider.GetRequiredService<IMeteoHandler>();
    return new MeteoConsumerBackgroundService(kafkaConsumer, meteoHandler, serviceScopeFactory,"meteo");
});

var connectionString = builder.Configuration.GetSection("ConnectionStrings")["WeatherContext"];

builder.Services.AddDbContext<WeatherContext>(options =>
    options.UseSqlServer(connectionString, providerOptions =>
    {
        providerOptions.EnableRetryOnFailure();
    }));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
