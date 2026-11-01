namespace IndustrialHmi.Core.Models;

public enum MachineState { Offline = -1, Idle = 0, Ready = 10, Running = 20, Paused = 30, Fault = 40, Emergency = 50 }
public enum Tag {
    EStopOk, DoorClosed, AirOk, PartPresent, CylinderHome, CylinderWork, ServoReady, ServoAlarm,
    ManualMode, ValveExtend, ValveRetract, ServoEnabled, RunLamp, AlarmLamp, ServoPosition, ServoVelocity, MachineState, CycleCount,
    Start, Pause, Reset, CylinderExtend, CylinderRetract, JogPositive, JogNegative, JogSpeed, Heartbeat
}
public static class TagCatalog {
    public static readonly Tag[] Inputs = [Tag.EStopOk, Tag.DoorClosed, Tag.AirOk, Tag.PartPresent,
        Tag.CylinderHome, Tag.CylinderWork, Tag.ServoReady, Tag.ServoAlarm, Tag.ManualMode,
        Tag.ServoPosition, Tag.ServoVelocity, Tag.MachineState, Tag.CycleCount,
        Tag.ValveExtend, Tag.ValveRetract, Tag.ServoEnabled, Tag.RunLamp, Tag.AlarmLamp];
    public static readonly Tag[] ManualOutputs = [Tag.JogPositive, Tag.JogNegative, Tag.CylinderExtend, Tag.CylinderRetract];
    public static readonly Tag[] CommandOutputs = [Tag.JogPositive, Tag.JogNegative, Tag.CylinderExtend, Tag.CylinderRetract, Tag.Start, Tag.Pause, Tag.Reset];
    public static Type ValueType(Tag t) => t switch {
        Tag.ServoPosition or Tag.ServoVelocity or Tag.JogSpeed => typeof(double),
        Tag.MachineState or Tag.CycleCount => typeof(int), _ => typeof(bool)
    };
}
public sealed record PlcSnapshot(IReadOnlyDictionary<Tag, object> Values, DateTimeOffset Timestamp) {
    public bool Bit(Tag tag) => (bool)Values[tag];
    public double Position => (double)Values[Tag.ServoPosition];
    public double Velocity => (double)Values[Tag.ServoVelocity];
    public int Count => (int)Values[Tag.CycleCount];
    public MachineState State => Enum.IsDefined(typeof(MachineState), (int)Values[Tag.MachineState])
        ? (MachineState)(int)Values[Tag.MachineState] : MachineState.Fault;
    public bool Safe => Bit(Tag.EStopOk) && Bit(Tag.DoorClosed) && Bit(Tag.AirOk) && !Bit(Tag.ServoAlarm);
    public bool ManualAllowed => Safe && Bit(Tag.ManualMode) && Bit(Tag.ServoReady) && State == MachineState.Ready;
}
public sealed class HmiSettings {
    public bool Simulation { get; set; } = true;
    public string Endpoint { get; set; } = "opc.tcp://192.168.1.10:4840";
    public int PollIntervalMs { get; set; } = 200;
    public Dictionary<string, string> Nodes { get; set; } = Enum.GetValues<Tag>().ToDictionary(t => t.ToString(), t => $"ns=4;s=|var|Inovance.Application.GVL_HMI.{t}");
    public void Validate() {
        if (PollIntervalMs is < 100 or > 1000) throw new ArgumentException("轮询周期必须在 100–1000 ms 内。");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "opc.tcp") throw new ArgumentException("请输入有效的 opc.tcp 地址。");
        foreach (var t in Enum.GetValues<Tag>())
            if (!Nodes.TryGetValue(t.ToString(), out var node) || string.IsNullOrWhiteSpace(node)) throw new ArgumentException($"缺少节点映射：{t}");
    }
}
