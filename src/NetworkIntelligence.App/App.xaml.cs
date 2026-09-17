using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using NetworkIntelligence.Application;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Infrastructure;
namespace NetworkIntelligence.App;
public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? window;
    private ServiceProvider? services;
    public static string DataDirectory { get; private set; } = "";
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            if (Directory.Exists(DataDirectory)) File.AppendAllText(Path.Combine(DataDirectory, "startup-error.log"), e.Exception + Environment.NewLine);
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--data-dir");
        DataDirectory = index >= 0 && index + 1 < arguments.Length
            ? Path.GetFullPath(arguments[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkIntelligence");
        Directory.CreateDirectory(DataDirectory);
        var collection = new ServiceCollection();
        collection.AddLogging(builder => builder.AddProvider(new LocalLoggerProvider(DataDirectory)));
        collection.AddOptions<AppSettings>();
        collection.AddSingleton<INetworkCollector, NetworkCollector>();
        collection.AddSingleton<IHistoryStore>(new SqliteHistoryStore(Path.Combine(DataDirectory, "network-intelligence.db")));
        collection.AddSingleton<IApplicationTrafficClient, MonitoringServiceClient>();
        collection.AddSingleton<MonitoringService>();
        collection.AddSingleton<AnomalyDetectionService>();
        collection.AddSingleton<Diagnostics>();
        collection.AddSingleton<TransferTest>();
        collection.AddSingleton<MainWindow>();
        try
        {
            services = collection.BuildServiceProvider();
            window = services.GetRequiredService<MainWindow>();
            window.Activate();
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(DataDirectory, "startup-error.log"), ex + Environment.NewLine);
            throw;
        }
    }
}
