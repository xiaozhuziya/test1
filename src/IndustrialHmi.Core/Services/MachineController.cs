using IndustrialHmi.Core.Models;
namespace IndustrialHmi.Core.Services;

// All reads/writes share a gate: a late start cannot overtake an earlier stop.
public sealed class MachineController(IPlcClient client, TimeProvider? clock = null) : IAsyncDisposable {
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private bool heartbeat;
    private DateTimeOffset jogStarted;
    private bool jogging;
    public bool Connected { get; private set; }
    public PlcSnapshot? Snapshot { get; private set; }
    private async Task StopOutputsAsync(CancellationToken ct) {
        // Attempt every output, even if a previous write failed.
        Exception? failure = null;
        foreach (var t in TagCatalog.CommandOutputs) {
            try { await client.WriteAsync(t, false, ct); } catch (Exception e) { failure ??= e; }
        }
        jogging = false;
        if (failure is not null) throw new InvalidOperationException("撤销指令失败，必须由 PLC 看门狗停止运动。", failure);
    }
    public async Task ConnectAsync(CancellationToken ct = default) {
        await gate.WaitAsync(ct);
        try {
            if (Connected) return;
            await client.ConnectAsync(ct);
            await StopOutputsAsync(ct);
            Snapshot = await ReadSnapshotAsync(ct);
            Connected = true;
        } catch { Connected = false; Snapshot = null; await client.DisconnectAsync(CancellationToken.None); throw; }
        finally { gate.Release(); }
    }
    private async Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken ct) {
        var values = await client.ReadAsync(TagCatalog.Inputs, ct);
        foreach (var t in TagCatalog.Inputs) {
            if (!values.TryGetValue(t, out var value) || value is null || value.GetType() != TagCatalog.ValueType(t))
                throw new InvalidDataException($"节点 {t} 类型错误或缺失；请检查 PLC 映射和数据类型。");
            if (value is double d && !double.IsFinite(d)) throw new InvalidDataException($"节点 {t} 数值无效。");
        }
        return new(values, time.GetUtcNow());
    }
    public async Task PollAsync(CancellationToken ct = default) {
        await gate.WaitAsync(ct);
        try {
            EnsureConnected();
            Snapshot = await ReadSnapshotAsync(ct);
            if (jogging && (!Snapshot.ManualAllowed || time.GetUtcNow() - jogStarted >= TimeSpan.FromSeconds(5))) await StopOutputsAsync(ct);
            heartbeat = !heartbeat;
            await client.WriteAsync(Tag.Heartbeat, heartbeat, ct);
        } catch { await FailClosedAsync(); throw; }
        finally { gate.Release(); }
    }
    private void EnsureConnected() { if (!Connected) throw new InvalidOperationException("PLC 未连接。"); }
    private void EnsureFresh() {
        EnsureConnected();
        if (Snapshot is null || time.GetUtcNow() - Snapshot.Timestamp > TimeSpan.FromSeconds(2)) throw new InvalidOperationException("PLC 数据已过期，禁止操作。");
    }
    private async Task FailClosedAsync() {
        Connected = false; Snapshot = null;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await StopOutputsAsync(timeout.Token); } catch { /* PLC watchdog is the final authority. */ }
        try { await client.DisconnectAsync(CancellationToken.None); } catch { }
    }
    public async Task JogAsync(bool positive, double speed, CancellationToken ct = default) {
        await gate.WaitAsync(ct);
        try {
            EnsureFresh();
            if (!Snapshot!.ManualAllowed) throw new InvalidOperationException("点动条件不满足：需要手动、就绪和全部安全联锁。");
            if (!double.IsFinite(speed) || speed is < 0.1 or > 50) throw new ArgumentOutOfRangeException(nameof(speed), "点动速度范围为 0.1–50 mm/s。");
            try {
                await StopOutputsAsync(ct);
                await client.WriteAsync(Tag.JogSpeed, speed, ct);
                await client.WriteAsync(positive ? Tag.JogPositive : Tag.JogNegative, true, ct);
                jogStarted = time.GetUtcNow(); jogging = true;
            } catch { await FailClosedAsync(); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task StopManualAsync(CancellationToken ct = default) {
        await gate.WaitAsync(ct);
        try { if (Connected) await StopOutputsAsync(ct); }
        catch { await FailClosedAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task PulseAsync(Tag command, CancellationToken ct = default) {
        await gate.WaitAsync(ct);
        try {
            EnsureFresh();
            var s = Snapshot!;
            var permitted = command switch {
                Tag.Start => s.Safe && s.Bit(Tag.ServoReady) && !s.Bit(Tag.ManualMode) && s.State is MachineState.Ready or MachineState.Paused,
                Tag.Pause => s.State == MachineState.Running,
                Tag.Reset => s.Safe && s.State is MachineState.Fault or MachineState.Emergency or MachineState.Idle,
                Tag.CylinderExtend or Tag.CylinderRetract => s.ManualAllowed,
                _ => false
            };
            if (!permitted) throw new InvalidOperationException("当前状态或联锁不允许该操作。");
            try {
                if (command is Tag.CylinderExtend or Tag.CylinderRetract) await StopOutputsAsync(ct);
                try { await client.WriteAsync(command, true, ct); await Task.Delay(100, ct); }
                finally { await client.WriteAsync(command, false, CancellationToken.None); }
            } catch { await FailClosedAsync(); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task DisconnectAsync(CancellationToken ct = default) {
        await gate.WaitAsync(ct);
        try {
            try { if (Connected) await StopOutputsAsync(ct); }
            finally { Connected = false; Snapshot = null; await client.DisconnectAsync(ct); }
        } finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); await client.DisposeAsync(); /* Keep the gate valid for late view-release callbacks during shutdown. */ }
}
