using IndustrialHmi.Core.Models;
namespace IndustrialHmi.Core.Services;
public sealed class SimulatedPlcClient : IPlcClient {
    private readonly Dictionary<Tag, object> data = Enum.GetValues<Tag>().ToDictionary(t => t, t => TagCatalog.ValueType(t) == typeof(double) ? (object)0d : TagCatalog.ValueType(t) == typeof(int) ? 0 : false);
    private bool connected;
    private DateTimeOffset lastTick = DateTimeOffset.UtcNow;
    private double cycleSeconds;
    public SimulatedPlcClient() {
        foreach (var t in new[] { Tag.EStopOk, Tag.DoorClosed, Tag.AirOk, Tag.CylinderHome, Tag.ServoReady, Tag.ManualMode }) data[t] = true;
        data[Tag.MachineState] = (int)MachineState.Ready; data[Tag.JogSpeed] = 10d;
    }
    public Task ConnectAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); connected = true; lastTick = DateTimeOffset.UtcNow; return Task.CompletedTask; }
    private bool Bit(Tag t) => (bool)data[t];
    public Task<IReadOnlyDictionary<Tag, object>> ReadAsync(IReadOnlyList<Tag> tags, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); Check();
        var now = DateTimeOffset.UtcNow;
        var dt = Math.Clamp((now - lastTick).TotalSeconds, 0, 0.5); lastTick = now;
        var safe = Bit(Tag.EStopOk) && Bit(Tag.DoorClosed) && Bit(Tag.AirOk) && !Bit(Tag.ServoAlarm);
        if (!Bit(Tag.EStopOk)) data[Tag.MachineState] = (int)MachineState.Emergency;
        else if (!safe) data[Tag.MachineState] = (int)MachineState.Fault;
        var manual = safe && Bit(Tag.ManualMode) && Bit(Tag.ServoReady) && (int)data[Tag.MachineState] == 10;
        var velocity = manual ? (Bit(Tag.JogPositive) ? 1 : Bit(Tag.JogNegative) ? -1 : 0) * (double)data[Tag.JogSpeed] : 0;
        data[Tag.ServoVelocity] = velocity; data[Tag.ServoPosition] = (double)data[Tag.ServoPosition] + velocity * dt;
        if ((int)data[Tag.MachineState] == 20) {
            cycleSeconds += dt;
            data[Tag.PartPresent] = cycleSeconds % 3 < 1.5;
            if (cycleSeconds >= 3) { data[Tag.CycleCount] = (int)data[Tag.CycleCount] + 1; cycleSeconds = 0; }
        }
        data[Tag.ValveExtend] = safe && Bit(Tag.CylinderExtend);
        data[Tag.ValveRetract] = safe && Bit(Tag.CylinderRetract);
        data[Tag.ServoEnabled] = safe && Bit(Tag.ServoReady);
        data[Tag.RunLamp] = (int)data[Tag.MachineState] == 20;
        data[Tag.AlarmLamp] = !safe || (int)data[Tag.MachineState] is 40 or 50;
        return Task.FromResult<IReadOnlyDictionary<Tag, object>>(tags.ToDictionary(t => t, t => data[t]));
    }
    public Task WriteAsync(Tag tag, object value, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); Check();
        if (value.GetType() != TagCatalog.ValueType(tag)) throw new InvalidDataException("模拟节点类型错误。");
        data[tag] = value;
        if (value is true) switch (tag) {
            case Tag.Start: data[Tag.MachineState] = 20; break;
            case Tag.Pause: data[Tag.MachineState] = 30; break;
            case Tag.Reset: data[Tag.MachineState] = 10; break;
            case Tag.CylinderExtend: data[Tag.CylinderWork] = true; data[Tag.CylinderHome] = false; break;
            case Tag.CylinderRetract: data[Tag.CylinderWork] = false; data[Tag.CylinderHome] = true; break;
        }
        return Task.CompletedTask;
    }
    // Simulation-only fault injection. Never exported by the real OPC UA client.
    public void SetInput(Tag tag, object value) {
        if (tag is not (Tag.ManualMode or Tag.DoorClosed or Tag.EStopOk or Tag.AirOk or Tag.ServoAlarm)) throw new ArgumentException("非模拟输入。");
        if (value is not bool) throw new ArgumentException("输入需要 Boolean。");
        data[tag] = value;
    }
    private void Check() { if (!connected) throw new IOException("模拟 PLC 已断开。"); }
    public Task DisconnectAsync(CancellationToken ct) { connected = false; foreach (var t in TagCatalog.CommandOutputs) data[t] = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
