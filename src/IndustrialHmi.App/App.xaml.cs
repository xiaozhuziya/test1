using System.IO;
using System.Text.Json;
using System.Windows;
using IndustrialHmi.Core.Models;
using IndustrialHmi.Core.Services;
using IndustrialHmi.Core.ViewModels;
using IndustrialHmi.OpcUa;
namespace IndustrialHmi.App;
public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        try {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var settings = JsonSerializer.Deserialize<HmiSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("配置为空。");
            settings.Validate();
            var sim = settings.Simulation ? new SimulatedPlcClient() : null;
            IPlcClient client = sim is not null ? sim : new OpcUaPlcClient(settings);
            var vm = new MainViewModel(new MachineController(client), settings, sim);
            MainWindow = new MainWindow(vm); MainWindow.Show();
        } catch (Exception error) { MessageBox.Show(error.Message, "启动失败 · 请检查 appsettings.json", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
}
