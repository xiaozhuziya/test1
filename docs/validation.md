# 验证记录

验证日期：2026-11-01。环境：Debian Linux x64，.NET SDK 8.0.427（global.json 允许 8.0.4xx 补丁升级）。

## 当前环境实际完成

| 项目 | 结果 |
|---|---|
| 整个解决方案 Release 构建（含 WPF / Windows 冒烟程序） | 通过，0 警告、0 错误 |
| xUnit 核心、模拟流程及 OPC UA 集成测试 | 36 通过，0 失败，0 跳过 |
| 真正 OPC UA 二进制加密会话 | 本地测试服务器，Basic256Sha256 / SignAndEncrypt 连接通过 |
| 服务器证书未信任 | 确认返回 BadCertificateUntrusted，未自动接受 |
| 双向证书信任后的访问 | Boolean / Int32 / Double 读取、Boolean / Double 写入通过 |
| OPC UA 无效节点、错误写入及映射类型不匹配 | 确认拒绝，无静默转换 |
| 依赖安全检查 | 当前 NuGet 数据源未报告已知漏洞，含传递依赖 |
| Windows x64 自包含发布 | 生成成功，附带 .NET / WPF 运行时 |

控制逻辑测试覆盖：连接时撤销遗留请求、正反互斥和速度下发、全部手动联锁、速度范围/NaN/Infinity、运行中开门、5 秒点动超时、过期快照、读失败、写失败（包含 InvalidOperationException）、撤销部分失败仍尝试其他输出、节点缺失/类型错误、未知状态、自动模式启动、取消气缸脉冲后的清零、按下/松开顺序、心跳翻转、断开/重连。模拟业务覆盖点动位置反馈、气缸伸缩、自动启动/暂停/继续、急停恢复和显式复位、ViewModel 命令/日志/离线数据无效化。

复现命令（仓库根目录）：

```text
dotnet build IndustrialHmi.sln -c Release
dotnet test tests/IndustrialHmi.Tests -c Release --no-build --logger trx --results-directory artifacts/test-results
dotnet list IndustrialHmi.sln package --vulnerable --include-transitive
dotnet publish src/IndustrialHmi.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

本地测试结果文件：`artifacts/test-results/core-tests.trx`。测试使用临时证书和随机可用本地端口，结束后清理证书目录，不包含生产凭据。

## 尚未执行 / 不能据此宣称通过

- **Windows WPF 窗口运行与视觉验收**：当前是 Linux，无法加载 WindowsDesktop 运行时。已编写 `IndustrialHmi.WpfSmoke`，用于真实窗口遍历五页、检查绑定错误、保存截图、验证按住/松开、气缸反馈和门联锁；已编译，但未在当前环境运行。
- **Windows CI**：`.github/workflows/build.yml` 已配置 Windows 界面冒烟和 Linux 核心测试；未推送/触发远程流水线，不能把配置存在等同于 CI 通过。
- **真实汇川 PLC / 伺服 / 气缸**：没有提供型号、固件、设备网络、真实 NodeId 或硬件。OPC UA 本地协议测试不能证明实际汇川设备的兼容性、授权和动作方向。
- **PLC ST 模板**：没有汇川工程/编译器，`docs/plc-contract.st` 未经 PLC 工具编译；需要映射硬件、运动库与安全逻辑，并按现场验收文件实测。

因此，本记录证明的是跨平台业务逻辑、模拟业务、标准 OPC UA 协议连接和 Windows 交叉编译/打包；不是对现场设备“完全无问题”的承诺。
