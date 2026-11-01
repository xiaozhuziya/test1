using IndustrialHmi.Core.Models;
using IndustrialHmi.Core.Services;
using Xunit;
namespace IndustrialHmi.Tests;

public sealed class ManualClock : TimeProvider {
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
public sealed class TestPlc : IPlcClient {
    public Dictionary<Tag, object> Data { get; } = Enum.GetValues<Tag>().ToDictionary(t => t, t => TagCatalog.ValueType(t) == typeof(double) ? (object)0d : TagCatalog.ValueType(t) == typeof(int) ? 0 : false);
    public List<(Tag Tag, object Value)> Writes { get; } = [];
    public bool FailRead { get; set; }
    public Tag? FailWrite { get; set; }
    public bool FailWriteOnlyWhenTrue { get; set; }
    public bool Connected { get; private set; }
    public TestPlc() {
        foreach(var t in new[] {Tag.EStopOk, Tag.DoorClosed, Tag.AirOk, Tag.ServoReady, Tag.ManualMode}) Data[t] = true;
        Data[Tag.MachineState] = 10;
    }
    public Task ConnectAsync(CancellationToken ct) { Connected = true; return Task.CompletedTask; }
    public Task<IReadOnlyDictionary<Tag, object>> ReadAsync(IReadOnlyList<Tag> tags, CancellationToken ct) {
        if(FailRead) throw new IOException("Read timed out");
        return Task.FromResult<IReadOnlyDictionary<Tag, object>>(tags.Where(Data.ContainsKey).ToDictionary(t => t,t => Data[t]));
    }
    public Task WriteAsync(Tag t, object value, CancellationToken ct) {
        Writes.Add((t,value));
        if(FailWrite == t && (!FailWriteOnlyWhenTrue || value is true)) throw new InvalidOperationException("Write denied");
        Data[t] = value; return Task.CompletedTask;
    }
    public Task DisconnectAsync(CancellationToken ct) { Connected = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public class ControllerTests {
    [Fact] public async Task ConnectClearsEveryLatchedCommandBeforeEnablingControls() {
        var plc=new TestPlc(); foreach(var t in TagCatalog.CommandOutputs) plc.Data[t]=true;
        await using var c=new MachineController(plc); await c.ConnectAsync();
        Assert.True(c.Connected); Assert.NotNull(c.Snapshot);
        Assert.All(TagCatalog.CommandOutputs,t => Assert.Equal(false,plc.Data[t]));
    }
    [Fact] public async Task JogSetsSpeedAndMutuallyExclusiveDirectionThenReleases() {
        var plc=new TestPlc(); await using var c=new MachineController(plc); await c.ConnectAsync();
        await c.JogAsync(true,12.5); Assert.Equal(true,plc.Data[Tag.JogPositive]); Assert.Equal(false,plc.Data[Tag.JogNegative]); Assert.Equal(12.5,plc.Data[Tag.JogSpeed]);
        await c.JogAsync(false,5); Assert.Equal(false,plc.Data[Tag.JogPositive]); Assert.Equal(true,plc.Data[Tag.JogNegative]);
        await c.StopManualAsync(); Assert.Equal(false,plc.Data[Tag.JogNegative]);
    }
    [Theory] [InlineData(Tag.DoorClosed,false)] [InlineData(Tag.EStopOk,false)] [InlineData(Tag.AirOk,false)] [InlineData(Tag.ServoAlarm,true)] [InlineData(Tag.ManualMode,false)] [InlineData(Tag.ServoReady,false)]
    public async Task EveryManualInterlockPreventsJog(Tag input,bool value) {
        var plc=new TestPlc();plc.Data[input]=value;await using var c=new MachineController(plc); await c.ConnectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.JogAsync(true,10)); Assert.DoesNotContain(plc.Writes,w=>w.Tag==Tag.JogPositive && w.Value is true);
    }
    [Theory] [InlineData(0)] [InlineData(-2)] [InlineData(51)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public async Task InvalidJogSpeedNeverWritesMotion(double speed) {
        var plc=new TestPlc();await using var c=new MachineController(plc); await c.ConnectAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>c.JogAsync(true,speed)); Assert.True(c.Connected);
    }
    [Fact] public async Task OpenDoorDuringJogStopsBothDirections() {
        var plc=new TestPlc(); await using var c=new MachineController(plc); await c.ConnectAsync();await c.JogAsync(true,10);
        plc.Data[Tag.DoorClosed]=false;await c.PollAsync();Assert.Equal(false,plc.Data[Tag.JogPositive]);Assert.False(c.Snapshot!.ManualAllowed);
    }
    [Fact] public async Task JogAutomaticallyStopsAtFiveSeconds() {
        var clock=new ManualClock();var plc=new TestPlc();await using var c=new MachineController(plc,clock);await c.ConnectAsync();await c.JogAsync(false,10);
        clock.Now+=TimeSpan.FromSeconds(5);await c.PollAsync();Assert.Equal(false,plc.Data[Tag.JogNegative]);
    }
    [Fact] public async Task StaleSnapshotPreventsCommands() {
        var clock=new ManualClock();var plc=new TestPlc();await using var c=new MachineController(plc,clock);await c.ConnectAsync();clock.Now+=TimeSpan.FromSeconds(3);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.JogAsync(true,10));
    }
    [Fact] public async Task ReadFailureClearsSnapshotStopsMotionAndDisconnects() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();await c.JogAsync(true,10);plc.FailRead=true;
        await Assert.ThrowsAsync<IOException>(()=>c.PollAsync());Assert.False(c.Connected);Assert.Null(c.Snapshot);Assert.Equal(false,plc.Data[Tag.JogPositive]);
    }
    [Fact] public async Task InvalidOperationDuringWriteAlsoFailsClosed() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();plc.FailWrite=Tag.JogPositive;plc.FailWriteOnlyWhenTrue=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.JogAsync(true,10));Assert.False(c.Connected);Assert.Null(c.Snapshot);Assert.Equal(false,plc.Data[Tag.JogPositive]);
    }
    [Fact] public async Task StopAttemptsOtherOutputsEvenWhenOneWriteFails() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();await c.JogAsync(false,10);plc.FailWrite=Tag.Start;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.StopManualAsync());Assert.Equal(false,plc.Data[Tag.JogNegative]);Assert.False(c.Connected);
    }
    [Theory] [InlineData(Tag.ServoPosition)] [InlineData(Tag.MachineState)] [InlineData(Tag.EStopOk)]
    public async Task WrongNodeTypeRejectsConnection(Tag tag) {
        var plc=new TestPlc();plc.Data[tag]="wrong type";await using var c=new MachineController(plc);
        await Assert.ThrowsAsync<InvalidDataException>(()=>c.ConnectAsync());Assert.False(c.Connected);Assert.Null(c.Snapshot);
    }
    [Fact] public async Task MissingNodeRejectsConnection() {
        var plc=new TestPlc();plc.Data.Remove(Tag.ServoReady);await using var c=new MachineController(plc);await Assert.ThrowsAsync<InvalidDataException>(()=>c.ConnectAsync());
    }
    [Fact] public async Task UnknownStateMapsToFaultAndDisablesManual() {
        var plc=new TestPlc();plc.Data[Tag.MachineState]=999;await using var c=new MachineController(plc);await c.ConnectAsync();Assert.Equal(MachineState.Fault,c.Snapshot!.State);Assert.False(c.Snapshot.ManualAllowed);
    }
    [Fact] public async Task StartRequiresAutomaticModeAndStateComesOnlyFromPlc() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>c.PulseAsync(Tag.Start));
        plc.Data[Tag.ManualMode]=false;await c.PollAsync();await c.PulseAsync(Tag.Start);
        Assert.Contains(plc.Writes,w=>w.Tag==Tag.Start && w.Value is true);Assert.Equal(false,plc.Data[Tag.Start]);Assert.Equal(MachineState.Ready,c.Snapshot!.State);
    }
    [Fact] public async Task CancelledCylinderPulseResetsOutputAndDisconnects() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();using var cts=new CancellationTokenSource();
        var action=c.PulseAsync(Tag.CylinderExtend,cts.Token);cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>action);Assert.Equal(false,plc.Data[Tag.CylinderExtend]);Assert.False(c.Connected);
    }
    [Fact] public async Task ConcurrentReleaseCannotBeOvertakenByPressedJog() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();
        var press=c.JogAsync(true,10);var release=c.StopManualAsync();await Task.WhenAll(press,release);Assert.Equal(false,plc.Data[Tag.JogPositive]);
    }
    [Fact] public async Task HeartbeatChangesEachPoll() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();await c.PollAsync();var first=plc.Data[Tag.Heartbeat];await c.PollAsync();Assert.NotEqual(first,plc.Data[Tag.Heartbeat]);
    }
    [Fact] public async Task DisconnectStopsAndAllowsCleanReconnect() {
        var plc=new TestPlc();await using var c=new MachineController(plc);await c.ConnectAsync();await c.JogAsync(true,10);await c.DisconnectAsync();Assert.Equal(false,plc.Data[Tag.JogPositive]);Assert.Null(c.Snapshot);await c.ConnectAsync();Assert.True(c.Connected);
    }
}
