(* IEC 61131-3 reference template. Split the two sections into a GVL named
   GVL_HMI and a PROGRAM POU. Adapt to the actual Inovance model/toolchain.
   No physical IO or vendor axis block is assumed; all hardware mapping is TODO.
   This file has NOT been compiled in an Inovance PLC toolchain. *)

(* ===== GVL_HMI: publish these variables through the OPC UA symbol server ===== *)
VAR_GLOBAL
    (* PLC-owned feedback: populate from validated hardware/axis logic. *)
    EStopOk, DoorClosed, AirOk, PartPresent : BOOL;
    CylinderHome, CylinderWork, ServoReady, ServoAlarm, ManualMode : BOOL;
    ValveExtend, ValveRetract, ServoEnabled, RunLamp, AlarmLamp : BOOL;
    ServoPosition, ServoVelocity : LREAL;
    MachineState : DINT := 0;
    CycleCount : DINT := 0;
    (* HMI-owned request variables; do not map directly onto physical outputs. *)
    Start, Pause, Reset, CylinderExtend, CylinderRetract : BOOL;
    JogPositive, JogNegative, Heartbeat : BOOL;
    JogSpeed : LREAL := 10.0;
END_VAR

(* ===== PROGRAM PRG_HmiContract ===== *)
PROGRAM PRG_HmiContract
VAR
    startEdge, pauseEdge, resetEdge, extendEdge, retractEdge : R_TRIG;
    completeEdge : R_TRIG;
    watchdog, cylinderTimeout : TON;
    previousHeartbeat, heartbeatEdge, linkAlive, linkSeen, safe, manualPermit : BOOL;
    cylinderRequest : INT := 0; (* +1 extend; -1 retract; 0 idle *)
    cylinderFault : BOOL;
    (* Connect these to real motion/sequence logic in your PLC project. *)
    RequestJogPositive, RequestJogNegative, RequestAxisStop : BOOL;
    LimitedJogSpeed : LREAL;
    PositiveLimitOk, NegativeLimitOk : BOOL; (* default FALSE; map actual limits *)
    AutoCycleCompleted : BOOL; (* default FALSE; real sequence completion *)
END_VAR

(* TODO: populate GVL_HMI feedback from physical inputs, safety controller,
   servo actual values and local mode selector before executing this logic. *)
heartbeatEdge := GVL_HMI.Heartbeat <> previousHeartbeat;
previousHeartbeat := GVL_HMI.Heartbeat;
IF heartbeatEdge THEN linkSeen := TRUE; END_IF;
watchdog(IN := NOT heartbeatEdge, PT := T#2s);
linkAlive := linkSeen AND NOT watchdog.Q;
safe := GVL_HMI.EStopOk AND GVL_HMI.DoorClosed AND GVL_HMI.AirOk
        AND NOT GVL_HMI.ServoAlarm;

startEdge(CLK := GVL_HMI.Start);
pauseEdge(CLK := GVL_HMI.Pause);
resetEdge(CLK := GVL_HMI.Reset);
extendEdge(CLK := GVL_HMI.CylinderExtend);
retractEdge(CLK := GVL_HMI.CylinderRetract);
completeEdge(CLK := AutoCycleCompleted);

(* A latched cylinder fault needs a safe, explicit reset; do not auto-restart. *)
IF resetEdge.Q AND safe AND linkAlive
   AND NOT (GVL_HMI.CylinderHome AND GVL_HMI.CylinderWork) THEN cylinderFault := FALSE; END_IF;

(* Safety / communication faults take priority and remain latched. *)
IF NOT GVL_HMI.EStopOk THEN
    GVL_HMI.MachineState := 50;
ELSIF NOT safe OR NOT linkAlive OR cylinderFault THEN
    GVL_HMI.MachineState := 40;
ELSE
    CASE GVL_HMI.MachineState OF
        0, 40, 50:
            IF resetEdge.Q AND GVL_HMI.ServoReady THEN
                GVL_HMI.MachineState := 10;
            END_IF;
        10, 30:
            IF startEdge.Q AND NOT GVL_HMI.ManualMode AND GVL_HMI.ServoReady THEN
                GVL_HMI.MachineState := 20;
            END_IF;
        20:
            IF pauseEdge.Q OR GVL_HMI.ManualMode OR NOT GVL_HMI.ServoReady THEN
                GVL_HMI.MachineState := 30;
            END_IF;
            IF completeEdge.Q THEN GVL_HMI.CycleCount := GVL_HMI.CycleCount + 1; END_IF;
    ELSE
        GVL_HMI.MachineState := 40;
    END_CASE;
END_IF;

manualPermit := safe AND linkAlive AND GVL_HMI.ManualMode
                AND GVL_HMI.ServoReady AND (GVL_HMI.MachineState = 10);
LimitedJogSpeed := LIMIT(0.1, GVL_HMI.JogSpeed, 50.0);
RequestJogPositive := manualPermit AND GVL_HMI.JogPositive
                      AND NOT GVL_HMI.JogNegative AND PositiveLimitOk;
RequestJogNegative := manualPermit AND GVL_HMI.JogNegative
                      AND NOT GVL_HMI.JogPositive AND NegativeLimitOk;
RequestAxisStop := NOT safe OR NOT linkAlive OR NOT GVL_HMI.ServoReady
                   OR ((GVL_HMI.MachineState <> 20) AND NOT RequestJogPositive AND NOT RequestJogNegative);
(* TODO: feed requests into the actual Inovance motion library. Implement
   MC_Stop/MC_Halt (as supported), arbitration, homing, software limits,
   drive enable/STO and fault acknowledgement in the real axis program.
   Stop requests override jog and automatic motion. *)

(* Example pulse-to-action latch; actual valve safe state depends on hardware. *)
IF NOT manualPermit THEN cylinderRequest := 0;
ELSIF extendEdge.Q AND NOT retractEdge.Q THEN cylinderRequest := 1;
ELSIF retractEdge.Q AND NOT extendEdge.Q THEN cylinderRequest := -1;
END_IF;
IF (cylinderRequest = 1 AND GVL_HMI.CylinderWork)
   OR (cylinderRequest = -1 AND GVL_HMI.CylinderHome) THEN cylinderRequest := 0; END_IF;
cylinderTimeout(IN := cylinderRequest <> 0, PT := T#3s);
IF cylinderTimeout.Q OR (GVL_HMI.CylinderHome AND GVL_HMI.CylinderWork) THEN
    cylinderFault := TRUE; cylinderRequest := 0;
END_IF;
GVL_HMI.ValveExtend := manualPermit AND NOT cylinderFault AND (cylinderRequest = 1);
GVL_HMI.ValveRetract := manualPermit AND NOT cylinderFault AND (cylinderRequest = -1);
GVL_HMI.RunLamp := GVL_HMI.MachineState = 20;
GVL_HMI.AlarmLamp := (GVL_HMI.MachineState = 40) OR (GVL_HMI.MachineState = 50);
(* TODO: map valve requests to reviewed hardware logic and publish actual
   output feedback. Never infer a de-energized valve is mechanically safe.
   Populate ServoEnabled from actual axis feedback, not from the HMI. *)
END_PROGRAM
