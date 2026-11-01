using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IndustrialHmi.Core.Models;
using IndustrialHmi.Core.Services;

namespace IndustrialHmi.Core.ViewModels;
public partial class IoPointViewModel(string address, string name, Tag tag) : ObservableObject {
    public string Address { get; } = address;
    public string Name { get; } = name;
    public Tag Tag { get; } = tag;
    [ObservableProperty] private bool isOn;
    [ObservableProperty] private bool isValid;
    public string Display => !IsValid ? "未知" : IsOn ? "ON" : "OFF";
    partial void OnIsOnChanged(bool value) => OnPropertyChanged(nameof(Display));
    partial void OnIsValidChanged(bool value) => OnPropertyChanged(nameof(Display));
}
public sealed record EventEntry(string Time, string Level, string Message);
public partial class MainViewModel : ObservableObject {
    private readonly MachineController controller;
    private readonly SimulatedPlcClient? simulator;
    private readonly Dictionary<string, bool> activeAlarms = new();
    public HmiSettings Settings { get; }
    public bool IsSimulation => Settings.Simulation;
    public string ModeLabel => IsSimulation ? "SIMULATION · 模拟运行" : "LIVE · OPC UA";
    public string EndpointLabel => IsSimulation ? "本地数字仿真 / 无需 PLC" : Settings.Endpoint;
    public ObservableCollection<IoPointViewModel> Inputs { get; } = [
        new("DI 00", "急停回路正常", Tag.EStopOk), new("DI 01", "安全门关闭", Tag.DoorClosed),
        new("DI 02", "气压正常", Tag.AirOk), new("DI 03", "工件到位", Tag.PartPresent),
        new("DI 04", "气缸原位", Tag.CylinderHome), new("DI 05", "气缸到位", Tag.CylinderWork),
        new("DI 06", "伺服就绪", Tag.ServoReady), new("DI 07", "伺服报警", Tag.ServoAlarm), new("DI 08", "手动模式", Tag.ManualMode) ];
    public ObservableCollection<IoPointViewModel> Outputs { get; } = [
        new("DO 00", "伸出电磁阀", Tag.ValveExtend), new("DO 01", "缩回电磁阀", Tag.ValveRetract),
        new("DO 02", "伺服使能反馈", Tag.ServoEnabled), new("DO 03", "运行指示灯", Tag.RunLamp), new("DO 04", "报警指示灯", Tag.AlarmLamp) ];
    public ObservableCollection<double> PositionSamples { get; } = [];
    public ObservableCollection<EventEntry> Events { get; } = [];
    public string[] Pages { get; } = ["总览", "IO 监控", "手动调试", "报警与事件", "连接设置"];
    [ObservableProperty] private string selectedPage = "总览";
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "等待连接";
    [ObservableProperty] private string stateLabel = "离线";
    [ObservableProperty] private MachineState state = MachineState.Offline;
    [ObservableProperty] private double position;
    [ObservableProperty] private double velocity;
    [ObservableProperty] private double jogSpeed = 10;
    [ObservableProperty] private int cycleCount;
    [ObservableProperty] private bool cylinderHome;
    [ObservableProperty] private bool cylinderWork;
    [ObservableProperty] private bool manualAllowed;
    [ObservableProperty] private bool canStart;
    [ObservableProperty] private bool canPause;
    [ObservableProperty] private bool canReset;
    [ObservableProperty] private string lastUpdated = "--:--:--";
    [ObservableProperty] private string operationMode = "未知";
    [ObservableProperty] private int alarmCount;
    public MainViewModel(MachineController controller, HmiSettings settings, SimulatedPlcClient? simulator = null) {
        this.controller = controller; Settings = settings; this.simulator = simulator;
        Log("INFO", "工作站已启动。真实设备请先核对节点、证书与 PLC 联锁。");
    }
    public void Log(string level, string message) {
        Events.Insert(0, new(DateTime.Now.ToString("HH:mm:ss"), level, message));
        while (Events.Count > 200) Events.RemoveAt(Events.Count - 1);
    }
    [RelayCommand]
    private async Task ToggleConnectionAsync() {
        if (IsBusy) return;
        IsBusy = true;
        try {
            if (controller.Connected) { await controller.DisconnectAsync(); MarkOffline(); StatusMessage = "连接已断开"; Log("INFO", StatusMessage); }
            else { StatusMessage = "正在建立连接…"; await controller.ConnectAsync(); ApplySnapshot(); StatusMessage = "通讯正常"; Log("INFO", $"已连接：{EndpointLabel}"); }
        } catch (Exception e) { MarkOffline(); Report(e); }
        finally { IsBusy = false; }
    }
    public async Task TickAsync() {
        if (!controller.Connected || IsBusy) return;
        try { await controller.PollAsync(); ApplySnapshot(); }
        catch (Exception e) { MarkOffline(); Report(e); }
    }
    private void ApplySnapshot() {
        var s = controller.Snapshot; if (s is null) { MarkOffline(); return; }
        IsConnected = true; State = s.State;
        StateLabel = State switch { MachineState.Idle => "待机", MachineState.Ready => "就绪", MachineState.Running => "运行中", MachineState.Paused => "已暂停", MachineState.Fault => "故障", MachineState.Emergency => "急停", _ => "离线" };
        Position = s.Position; PositionSamples.Add(Position); while (PositionSamples.Count > 120) PositionSamples.RemoveAt(0); Velocity = s.Velocity; CycleCount = s.Count;
        CylinderHome = s.Bit(Tag.CylinderHome); CylinderWork = s.Bit(Tag.CylinderWork);
        OperationMode = s.Bit(Tag.ManualMode) ? "手动" : "自动";
        ManualAllowed = s.ManualAllowed;
        CanStart = s.Safe && s.Bit(Tag.ServoReady) && !s.Bit(Tag.ManualMode) && s.State is MachineState.Ready or MachineState.Paused;
        CanPause = s.State == MachineState.Running;
        CanReset = s.Safe && s.State is MachineState.Idle or MachineState.Fault or MachineState.Emergency;
        LastUpdated = s.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
        foreach (var point in Inputs.Concat(Outputs)) { point.IsOn = s.Bit(point.Tag); point.IsValid = true; }
        var alarms = new Dictionary<string, bool> { ["急停回路断开"] = !s.Bit(Tag.EStopOk), ["安全门打开"] = !s.Bit(Tag.DoorClosed), ["供气压力不足"] = !s.Bit(Tag.AirOk), ["伺服驱动器报警"] = s.Bit(Tag.ServoAlarm), ["设备故障 / 未知状态码"] = s.State == MachineState.Fault };
        foreach (var (name, active) in alarms) {
            var previous = activeAlarms.GetValueOrDefault(name);
            if (active != previous) Log(active ? "ALARM" : "INFO", active ? name : $"已恢复：{name}");
            activeAlarms[name] = active;
        }
        AlarmCount = alarms.Count(x => x.Value);
        StatusMessage = AlarmCount > 0 ? "联锁异常，请处理报警" : "通讯正常 · 数据有效";
    }
    private void MarkOffline() {
        PositionSamples.Clear(); IsConnected = false; State = MachineState.Offline; StateLabel = "离线"; OperationMode = "未知";
        ManualAllowed = CanStart = CanPause = CanReset = false;
        CylinderHome = CylinderWork = false; LastUpdated = "--:--:--";
        foreach (var p in Inputs.Concat(Outputs)) { p.IsValid = false; p.IsOn = false; }
    }
    private void Report(Exception e) { StatusMessage = e.Message; Log("ERROR", e.Message); }
    private async Task ExecuteAsync(Func<Task> action, string message) {
        try { await action(); Log("INFO", message); if (controller.Connected) { await controller.PollAsync(); ApplySnapshot(); } }
        catch (Exception e) { if (!controller.Connected) MarkOffline(); Report(e); }
    }
    [RelayCommand] private Task StartAsync() => ExecuteAsync(() => controller.PulseAsync(Tag.Start), "已发送启动请求，状态由 PLC 确认");
    [RelayCommand] private Task PauseAsync() => ExecuteAsync(() => controller.PulseAsync(Tag.Pause), "已发送暂停请求");
    [RelayCommand] private Task ResetAsync() => ExecuteAsync(() => controller.PulseAsync(Tag.Reset), "已发送复位请求");
    [RelayCommand] private Task ExtendAsync() => ExecuteAsync(() => controller.PulseAsync(Tag.CylinderExtend), "气缸伸出请求");
    [RelayCommand] private Task RetractAsync() => ExecuteAsync(() => controller.PulseAsync(Tag.CylinderRetract), "气缸缩回请求");
    // Invoked by the hold-button view adapter; never fire-and-forget async writes.
    public Task BeginJogAsync(bool positive) => ExecuteAsync(() => controller.JogAsync(positive, JogSpeed), positive ? "正向点动" : "反向点动");
    public Task StopManualAsync() => ExecuteAsync(() => controller.StopManualAsync(), "手动指令已撤销");
    [RelayCommand] private void ToggleSimulationMode() { if (simulator is null || IsBusy) return; simulator.SetInput(Tag.ManualMode, OperationMode != "手动"); }
    [RelayCommand] private void ToggleSimulationDoor() { if (simulator is null || IsBusy) return; simulator.SetInput(Tag.DoorClosed, !Inputs.First(x => x.Tag == Tag.DoorClosed).IsOn); }
    [RelayCommand] private void ToggleSimulationEStop() { if (simulator is null || IsBusy) return; simulator.SetInput(Tag.EStopOk, !Inputs.First(x => x.Tag == Tag.EStopOk).IsOn); }
    public async Task ShutdownAsync() { await controller.DisposeAsync(); }
}
