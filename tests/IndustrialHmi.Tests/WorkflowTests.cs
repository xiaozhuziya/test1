using IndustrialHmi.Core.Models;
using IndustrialHmi.Core.Services;
using IndustrialHmi.Core.ViewModels;
using Xunit;
namespace IndustrialHmi.Tests;
public class WorkflowTests {
    [Fact] public async Task SimulationExercisesJogCylinderAndAutomaticStateWorkflow() {
        var sim=new SimulatedPlcClient();await using var c=new MachineController(sim);await c.ConnectAsync();
        await c.JogAsync(true,10);await Task.Delay(120);await c.PollAsync();Assert.True(c.Snapshot!.Position>0);Assert.Equal(10,c.Snapshot.Velocity);await c.StopManualAsync();await c.PollAsync();Assert.Equal(0,c.Snapshot.Velocity);
        await c.PulseAsync(Tag.CylinderExtend);await c.PollAsync();Assert.True(c.Snapshot.Bit(Tag.CylinderWork));Assert.False(c.Snapshot.Bit(Tag.CylinderHome));
        await c.PulseAsync(Tag.CylinderRetract);await c.PollAsync();Assert.True(c.Snapshot.Bit(Tag.CylinderHome));
        sim.SetInput(Tag.ManualMode,false);await c.PollAsync();await c.PulseAsync(Tag.Start);await c.PollAsync();Assert.Equal(MachineState.Running,c.Snapshot.State);
        await c.PulseAsync(Tag.Pause);await c.PollAsync();Assert.Equal(MachineState.Paused,c.Snapshot.State);
        await c.PulseAsync(Tag.Start);await c.PollAsync();Assert.Equal(MachineState.Running,c.Snapshot.State);
    }
    [Fact] public async Task EmergencyRequiresReleasedInputAndExplicitReset() {
        var sim=new SimulatedPlcClient();await using var c=new MachineController(sim);await c.ConnectAsync();sim.SetInput(Tag.EStopOk,false);await c.PollAsync();Assert.Equal(MachineState.Emergency,c.Snapshot!.State);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.PulseAsync(Tag.Reset));sim.SetInput(Tag.EStopOk,true);await c.PollAsync();Assert.Equal(MachineState.Emergency,c.Snapshot.State);
        await c.PulseAsync(Tag.Reset);await c.PollAsync();Assert.Equal(MachineState.Ready,c.Snapshot.State);
    }
    [Fact] public async Task ViewModelConnectsNavigatesReportsAlarmsAndInvalidatesOfflineInputs() {
        var sim=new SimulatedPlcClient();var vm=new MainViewModel(new MachineController(sim),new HmiSettings(),sim);
        await vm.ToggleConnectionCommand.ExecuteAsync(null);Assert.True(vm.IsConnected);Assert.True(vm.ManualAllowed);Assert.All(vm.Inputs,p=>Assert.True(p.IsValid));
        vm.SelectedPage="手动调试";Assert.Equal("手动调试",vm.SelectedPage);await vm.ExtendCommand.ExecuteAsync(null);Assert.True(vm.CylinderWork);
        sim.SetInput(Tag.DoorClosed,false);await vm.TickAsync();Assert.False(vm.ManualAllowed);Assert.True(vm.AlarmCount>0);Assert.Contains(vm.Events,e=>e.Level=="ALARM");
        await vm.ToggleConnectionCommand.ExecuteAsync(null);Assert.False(vm.IsConnected);Assert.All(vm.Inputs,p=>{Assert.False(p.IsValid);Assert.Equal("未知",p.Display);});await vm.ShutdownAsync();
    }
    [Fact] public async Task ViewModelDoesNotExposeRawCommandExceptionsToUi() {
        var sim=new SimulatedPlcClient();var vm=new MainViewModel(new MachineController(sim),new HmiSettings(),sim);await vm.StartCommand.ExecuteAsync(null);Assert.Contains(vm.Events,e=>e.Level=="ERROR");Assert.False(vm.IsConnected);await vm.ShutdownAsync();
    }
    [Fact] public void ConfigurationRequiresCompleteMappingAndBoundedPolling() {
        var s=new HmiSettings();s.Validate();s.PollIntervalMs=1;Assert.Throws<ArgumentException>(s.Validate);s.PollIntervalMs=200;s.Nodes.Remove("Start");Assert.Throws<ArgumentException>(s.Validate);
    }
    [Fact] public void EventLogIsBounded() {
        var sim=new SimulatedPlcClient();var vm=new MainViewModel(new MachineController(sim),new HmiSettings(),sim);for(var i=0;i<300;i++)vm.Log("INFO",i.ToString());Assert.Equal(200,vm.Events.Count);Assert.Equal("299",vm.Events[0].Message);
    }
}
