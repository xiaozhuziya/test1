using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IndustrialHmi.Core.ViewModels;
namespace IndustrialHmi.App;
public partial class MainWindow : Window {
    private readonly MainViewModel vm;
    private readonly DispatcherTimer timer;
    private bool ticking, closing, shutdownComplete;
    public MainWindow(MainViewModel viewModel) {
        vm = viewModel; InitializeComponent(); DataContext = vm;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(vm.Settings.PollIntervalMs) };
        timer.Tick += async (_, _) => { if (ticking || closing) return; ticking = true; try { await vm.TickAsync(); } finally { ticking = false; } };
        Loaded += async (_, _) => { timer.Start(); if (vm.IsSimulation) await vm.ToggleConnectionCommand.ExecuteAsync(null); };
        Deactivated += async (_, _) => { if (!closing && vm.IsConnected) await vm.StopManualAsync(); };
        Closing += OnClosing;
    }
    private async void OnPageChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is MainViewModel model && model.IsConnected) await model.StopManualAsync(); }
    private async void OnClosing(object? sender, CancelEventArgs e) {
        if (shutdownComplete) return;
        e.Cancel = true; if (closing) return; closing = true; timer.Stop(); IsEnabled = false;
        // Let any in-flight dispatcher tick finish before disposing its controller.
        while (ticking || vm.IsBusy) await Task.Delay(25);
        try { await vm.ShutdownAsync(); }
        catch (Exception error) { MessageBox.Show(error.Message + "\n请确认 PLC 看门狗已停止运动。", "退出时通讯异常", MessageBoxButton.OK, MessageBoxImage.Warning); }
        shutdownComplete = true; Close();
    }
}
