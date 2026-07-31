# LLJ Device Link

这是针对 `LilyChan Roshutsu`（Unity 2022.3.50f1 / IL2CPP）的 BepInEx 6 游戏联动 Mod。最终可分发目录在 `mod_out`；其中全部内容可直接覆盖到游戏根目录。

## 支持设备

- 役次元电击器一代：名称 `YYC-DJ`，服务 `FF30`，写入 `FF31`，通知 `FF32`
- 役次元电击器二代：名称 `YYC-DJ-V2`，服务/特性同上
- 郊狼 3.0 电击器：名称 `47L121000`，服务 `180C`，写入 `150A`，通知 `150B`
- 役次元飞机杯/跳蛋：服务 `FF40`，写入 `FF41`，通知 `FF42`

设备可同时连接。游戏启动时不会扫描；点击“连接设备”后执行一次 10 秒扫描并连接新发现设备。电击器会按设备型号选择各自协议与波形；飞机杯/跳蛋会查询实际 A/B/C 马达数量，并以实时速率协议输出。

## 构建

需要 Windows、.NET SDK 和已经由 BepInEx 首次启动生成的 `Game/BepInEx/interop`。

```powershell
$env:APPDATA=(Resolve-Path '.\.appdata').Path
$env:DOTNET_CLI_HOME=(Resolve-Path '.\.appdata').Path
$env:NUGET_PACKAGES=(Resolve-Path '.\.appdata').Path + '\packages'
dotnet restore .\src\LLJDeviceLink\LLJDeviceLink.csproj --configfile .\NuGet.Config
dotnet build .\src\LLJDeviceLink\LLJDeviceLink.csproj -c Release --no-restore
```

协议封包自检：

```powershell
dotnet run --project .\tests\ProtocolSelfTest\ProtocolSelfTest.csproj
```

## 源码结构

- `BleManager.cs` / `DeviceSession.cs`：Windows BLE 扫描、多连接、GATT 通信与输出调度
- `ProtocolPackets.cs`：四类设备协议封包
- `GameEventMonitor.cs`：基于游戏真实 IL2CPP 类型的事件/状态采集
- `EventRouter.cs`：每事件、每设备请求路由及多来源聚合
- `ModBehaviour.cs`：不依赖 IL2CPP `GUILayout` 的低遮挡状态条、连接页、事件配置页和事件诊断；F8 打开时隔离游戏鼠标输入
- `InputBlockPatches.cs`：阻止 F8 窗口点击穿透到相机、交互、道具切换及游戏 EventSystem
- `WaveformLibrary.cs`：启动时递归加载游戏根目录 `waveforms` 下的 JSON

更完整的玩家说明见 `mod_out/LLJ设备联动使用说明.md`。
