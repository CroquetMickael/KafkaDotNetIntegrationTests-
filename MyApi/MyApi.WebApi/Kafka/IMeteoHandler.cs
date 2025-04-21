namespace MyApi.WebApi.Kafka;

public interface IMeteoHandler
{
    public Task<bool> ExecuteAsync(string message);
}
