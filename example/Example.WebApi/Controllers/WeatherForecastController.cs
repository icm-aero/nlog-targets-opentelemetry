using Microsoft.AspNetCore.Mvc;
using NLog;
using System.Diagnostics;
using OpenTelemetry;

namespace Example.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly string[] Summaries = new[]
        {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly Random _random;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
            _random = new Random();
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            Activity.Current?.AddTag("myTag", "SomeValue");
            var trace = Activity.Current?.Context.TraceId;
            var activityBaggage = Baggage.Current.GetBaggage()
                .Where(baggageItem => baggageItem.Value != null);
            Logger.Info("Handling Weather");

            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = _random.Next(-20, 55),
                Summary = Summaries[_random.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
}