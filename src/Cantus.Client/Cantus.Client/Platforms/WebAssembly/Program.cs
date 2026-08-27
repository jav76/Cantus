using Uno.UI.Hosting;

namespace Cantus.Client;

public class Program
{
    public static async Task Main(string[] args)
    {
        App.InitializeLogging();

        await UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build()
            .RunAsync();
    }
}
