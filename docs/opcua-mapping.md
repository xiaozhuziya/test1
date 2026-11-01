# OPC UA / PLC 节点契约

示例命名空间仅用于说明。用 UaExpert 确认实际 NodeId、访问权限与数据类型；命名空间索引也可能随 PLC 项目变化。应用严格校验类型，不隐式把 WORD、REAL、UINT 或字符串转换成期望值。

| 标签 | PLC 类型 / UA 类型 | 方向 | 约定 |
|---|---|---|---|
| EStopOk | BOOL / Boolean | PLC→HMI | 急停安全回路反馈正常 |
| DoorClosed | BOOL / Boolean | PLC→HMI | 门安全回路反馈关闭 |
| AirOk | BOOL / Boolean | PLC→HMI | 气压允许 |
| PartPresent | BOOL / Boolean | PLC→HMI | 工件到位 |
| CylinderHome | BOOL / Boolean | PLC→HMI | 气缸原位反馈 |
| CylinderWork | BOOL / Boolean | PLC→HMI | 气缸到位反馈 |
| ServoReady | BOOL / Boolean | PLC→HMI | 轴使能/就绪反馈 |
| ServoAlarm | BOOL / Boolean | PLC→HMI | 驱动报警 |
| ManualMode | BOOL / Boolean | PLC→HMI | 手动模式；真机由现场选择开关/PLC 管理 |
| ValveExtend | BOOL / Boolean | PLC→HMI | 伸出阀实际输出反馈 |
| ValveRetract | BOOL / Boolean | PLC→HMI | 缩回阀实际输出反馈 |
| ServoEnabled | BOOL / Boolean | PLC→HMI | 轴使能输出/反馈 |
| RunLamp | BOOL / Boolean | PLC→HMI | 运行指示灯输出 |
| AlarmLamp | BOOL / Boolean | PLC→HMI | 报警指示灯输出 |
| ServoPosition | LREAL / Double | PLC→HMI | mm，有符号实际位置 |
| ServoVelocity | LREAL / Double | PLC→HMI | mm/s，有符号实际速度 |
| MachineState | DINT / Int32 | PLC→HMI | 0 待机、10 就绪、20 自动运行、30 暂停、40 故障、50 急停 |
| CycleCount | DINT / Int32 | PLC→HMI | 累计完成件数 |
| Start | BOOL / Boolean | HMI→PLC | 启动/继续请求，100 ms，PLC 上升沿处理 |
| Pause | BOOL / Boolean | HMI→PLC | 自动暂停请求，100 ms，非急停 |
| Reset | BOOL / Boolean | HMI→PLC | 故障/待机复位请求，100 ms |
| CylinderExtend | BOOL / Boolean | HMI→PLC | 伸出请求，100 ms，PLC 动作保持 |
| CylinderRetract | BOOL / Boolean | HMI→PLC | 缩回请求，100 ms，PLC 动作保持 |
| JogPositive | BOOL / Boolean | HMI→PLC | 正向按住有效，松开 False |
| JogNegative | BOOL / Boolean | HMI→PLC | 负向按住有效，与正向互斥 |
| JogSpeed | LREAL / Double | HMI→PLC | 0.1–50 mm/s；PLC 再次限幅，按下时更新 |
| Heartbeat | BOOL / Boolean | HMI→PLC | 每轮采样翻转；PLC 检测边沿而非固定真值 |

读标签只给 HMI 读权限；写标签按角色赋权。不要将原始物理 Q 点直接映射为这些请求。PLC 必须独立验证当前模式、反馈、限位、命令互斥、心跳和安全条件。HMI 首次连接会将 7 个命令位清零，故必须使用独立 HMI 请求变量，不能与其他客户端共享有保持意义的输出位。

采样周期默认 200 ms；允许 100–1000 ms。PLC 看门狗超时建议 2 s，本例只在有效读取后翻转心跳。读取失败不再喂狗，客户端会尝试清零命令并关闭会话。视现场风险评估设置更短的超时及独立安全系统。

`plc-contract.st` 是 IEC 61131-3 接口与顺序逻辑模板，不假定具体汇川运动库/IO 通道。需拆分为工程中的 GVL 与 PROGRAM，并将硬件反馈、轴实际值、限位、点动/停止及气阀输出接入已验证的实际设备逻辑。它不是可直接下载到未知型号的完整 PLC 工程。
