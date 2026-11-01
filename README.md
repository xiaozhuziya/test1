# NEXUS · 汇川 PLC / OPC UA 工业上位机

基于 **WPF + .NET 8 + CommunityToolkit.Mvvm** 的装配单元案例。深色现代工业风格，默认使用本地模拟 PLC，打开即可体验业务。真实连接使用 OPC Foundation .NET 客户端，禁止自动信任未知证书或降级到明文端点。

## 下载到本地

- [源码 ZIP](downloads/IndustrialHmi-source.zip)：解压后用 Visual Studio 2022 打开 `IndustrialHmi.sln`。
- [Windows x64 运行包](downloads/IndustrialHmi-win-x64.zip)：约 69 MB，包含 .NET / WPF 运行时，完整解压后运行 `IndustrialHmi.App.exe`。

在 GitHub 文件页面点击 **Download raw file / 下载原始文件**（下载图标），不要尝试在线预览 ZIP。也可以使用页面右上角 **Code → Download ZIP** 下载整个仓库（含运行包）。验证范围和未验证事项见 [测试记录](docs/validation.md)。

## 快速运行（Windows 10/11 x64）

安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 后，在仓库根目录执行：

```powershell
dotnet restore IndustrialHmi.sln
dotnet build IndustrialHmi.sln -c Release
dotnet run --project src/IndustrialHmi.App -c Release
```

启动默认自动连接**模拟 PLC**；真机模式不会自动连接。按左侧菜单切换五个页面：

- **总览**：设备状态、产量、报警数、伺服位置/速度、联锁、PLC 状态机和最近 120 次采样的位置趋势。
- **IO 监控**：9 路数字输入与 5 路只读输出反馈，显示 ON / OFF / 未知，不提供绕过联锁的输出强制。
- **手动调试**：伺服正反点动（鼠标或空格/Enter 按住），气缸伸缩脉冲请求；带模拟模式/安全门/急停切换。
- **报警与事件**：活动报警及最近 200 条会话日志，记录故障出现、恢复、操作和通讯错误。
- **连接设置**：显示实际配置和接入步骤；编辑 `appsettings.json` 后重启生效。

建议体验：手动页点动 → 松开验证速度归零 → 伸/缩气缸 → 打开安全门观察禁用与报警 → 关闭门并到总览复位 → 手动页切换自动 → 总览启动、暂停、继续 → 手动页触发急停 → 恢复输入并复位。

## 接入汇川 PLC

1. 确认具体型号/固件/授权确实支持 OPC UA Server，在汇川开发环境启用服务和符号导出。不同系列的地址空间不同，本项目不假定所有汇川 PLC 都支持相同接口。
2. 按 [节点契约](docs/opcua-mapping.md) 建立 `GVL_HMI`，实现 [PLC 参考逻辑](docs/plc-contract.st) 和现场安全逻辑。
3. 用 UaExpert 浏览真实 NodeId，替换 `src/IndustrialHmi.App/appsettings.json` 中的 `Nodes`。示例 `ns=4;s=|var|Inovance.Application.GVL_HMI.*` **是占位映射，不保证对应实际 PLC**。
4. 设置 `Simulation=false`、实际 `Endpoint`，完成客户端/服务器证书双向信任。需要账户时在运行用户环境中配置 `PLC_OPCUA_USER`、`PLC_OPCUA_PASSWORD`；不要写入仓库或日志。
5. 重启，点击连接，按 [现场验收步骤](docs/commissioning.md) 验证。应用只接受 `Basic256Sha256 + SignAndEncrypt`。

证书默认位于 `%LOCALAPPDATA%\IndustrialHmi\pki`。将 `own\certs` 中的**公开证书**导入 PLC 信任列表；首次未知服务器连接会被拒绝，核对 PLC 证书指纹、有效期和主机名后，将对应公开证书放到 `trusted\certs`，再连接。不要共享 `own\private`，不要设置自动接受证书。服务端证书需要覆盖实际连接主机名/IP；被拒绝的证书位于 `rejected`。证书路径与 UaExpert 的信任库相互独立。

## 设计与安全边界

```text
WPF Views + HoldJogButton（仅处理按下/松开与失焦）
    ↓ binding / commands
MainViewModel（MVVM Toolkit、画面状态与事件）
    ↓
MachineController（串行通讯、联锁、数据有效性、指令撤销）
    ↓ IPlcClient
SimulatedPlcClient  /  OpcUaPlcClient
```

- 所有读写通过同一异步门串行执行，检查每个返回质量码和数据类型；整批有效才更新 UI。
- OPC UA 会话/节点问题会禁用控制并将输入标为未知，不把缓存输入当作有效数据；位置/产量保留上次读数。
- 点动先撤销互斥指令，再写速度和方向；松开、失焦、切页、窗口失活/关闭均尝试撤销；单次最长 5 秒。失联后不自动重连或恢复运动，操作员需重新连接。
- 气缸由 100 ms 请求脉冲触发，动作保持、到位和超时由 PLC 负责。发出请求不等于动作已经完成。
- 自动启动需要自动模式、就绪/暂停状态和联锁正常。界面状态**只由 PLC 反馈更新**。
- HMI 心跳每次完整轮询翻转。PLC 必须独立检测 **2 秒无边沿**并撤销运动许可；PLC 扫描周期应显著短于 100 ms，需锁存命令上升沿。OPC UA 写入失败时，上位机不能保证停止指令送达。
- UI 不是实时控制器。暂停按钮、软件超时和网络协议不替代安全 PLC、实体急停、安全门、驱动 STO、正反硬限位与机械防护。本例中的位置信息单位约定为 mm / mm·s⁻¹，必须匹配现场轴参数。

## 编译、测试和发布

```powershell
# 核心/模拟/真实 OPC UA 协议集成测试，可在 Windows 或 Linux 运行
dotnet test tests/IndustrialHmi.Tests -c Release

# Windows：创建实际 WPF 窗口，遍历 5 页并捕获绑定错误，测试按住/松开和联锁
dotnet run --project tests/IndustrialHmi.WpfSmoke -c Release
# 生成 artifacts/screenshots/*.png；必须在 Windows 上运行

# 自包含 Windows 发布，不需要目标机安装 .NET
dotnet publish src/IndustrialHmi.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
# 在 Windows 上执行 artifacts/win-x64/IndustrialHmi.App.exe
```

Linux 只能交叉编译 WPF，不能运行 WPF 窗口。Windows UI 冒烟程序已提供并纳入 CI，但当前 Linux 开发环境的实际执行结果不包括它，也不包括现场 PLC/伺服动作。验证记录见 [docs/validation.md](docs/validation.md)。

本项目使用轮询，非历史数据库/完整 SCADA；事件和趋势仅保留在内存。未实现用户权限、配方、趋势落盘和多轴生产逻辑，这些可在现有服务层基础上扩展。应用配置采用显式重启生效，避免运行中切换到另一台设备。
