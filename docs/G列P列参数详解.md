# EonTest 测试工步表 G / P 列参数详解（JEMSts UDS 函数库）

> 适用：`DoIP_UDS_Step` / `DoIP_UDS_ExcelStep` / `DoIP_UDS_ExcelRunAll` / `CAN_UDS_Step` / `CAN_UDS_ExcelStep` / `CAN_UDS_ExcelRunAll`
> 同一张测试工步表，EonTest 与 JEMSts 都能跑（键契约完全一致）。

---

## 一、两列的分工（总览）

| 列 | 列名（表头） | 放什么 |
|---|---|---|
| **G 列** | 配置参数1 | **UDS 收发配置**：地址 / 报文 / 匹配 / 会话 / 时序 / 重试 / 超时 / 保活 / 虚拟模式 |
| **P 列** | 解码配置 | **解码规则 + 期望值 / 校验键**：怎么从响应里取测量值、每个测量值的期望/上下限/字符串校验 |

> 约定：**G 列只放"怎么发"**；**P 列放"怎么取 + 期望多少"**（跟着测量项走，一个 Step 可有多个测量项）。
> G 列的 `expectedValue`/`expectedMin` 等校验键仍兼容（单测量项、无 P 列时可用），但多测量项请放 P 列。

### 解码校验优先级

当 P 列（DecodeConfig）包含解码项校验时，优先使用 P 列规则进行值判定和 `TestType` 选择；只有 P 列没有校验规则时，才使用 G 列的 `expectedPrefix` / `expectedResponse` 作为报文匹配类型。一个工步只返回一个主要 `TestType`，不会生成 `1|5` 这类组合码。

字符串长度校验支持：

| 参数 | 含义 | 示例 |
|---|---|---|
| `expectedLength` | 字符串长度必须严格等于指定值 | `expectedLength=16` |
| `expectedMinLength` | 字符串长度下限 | `expectedMinLength=8` |
| `expectedMaxLength` | 字符串长度上限 | `expectedMaxLength=32` |

`expectedLength` 与 min/max 可以同时配置，所有条件都必须满足。长度按解码后的字符串字符数计算；`length`/`valueLength` 则是从响应中截取的字节数，两者不是同一个概念。

示例：

```json
[{"name":"PCBA SN","offset":3,"length":16,"type":"string","encoding":"ascii","trim":false,"expectedLength":16}]
```

格式约定：
- G 列 = `key=value` 逗号分隔（支持 `{pad:N}` `{retry:N}` 内联标注、正则里的逗号/花括号不会误拆）。
- P 列 = ① 单测量项直接 `key=value`；② 多测量项 JSON 数组；③ 引用 Profile 文件。
- 每个 `key=value` 之间用英文逗号 `,` 分隔；多条指令用竖线 `|` 分隔。

---

## 二、G 列（配置参数1）参数详解

### 2.0 CAN/CAN-FD/ISO-TP 示例

CAN UDS 使用 `CAN_UDS_*` 入口。先用 `CAN_UDS_Init` 加载并注册 CAN provider，再用 `CAN_UDS_Step` 执行 UDS over ISO-TP；`CAN_UDS_ExcelStep` 和 `CAN_UDS_ExcelRunAll` 可直接读取同一张 `StepTable`。CAN 配置仍沿用 G/P 两列格式，但传输选择和 CAN 参数写在 G 列：

```text
CAN_UDS_Init("Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,0", "", "HRU_CAN")
CAN_UDS_Step(
  "transport=can,canChannel=0,canFd=false,canNominalBitrate=500000,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 8C,expectedPrefix=62 F1 8C",
  "name=PCBA SN,valueOffset=3,valueType=string,encoding=ascii,trim=true",
  "HRU_CAN"
)
CAN_UDS_Release()
```

CAN-FD 示例：

```text
CAN_UDS_Step(
  "transport=can,canChannel=0,canFd=true,canBitRateSwitch=true,canNominalBitrate=500000,canDataBitrate=2000000,canFdStandard=0,canFdMode=0,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=10 03,expectedPrefix=50 03",
  "",
  "HRU_CAN"
)
```

在 Excel 的 `StepTable` 中，CAN 步骤的 `Action` 填 `send_uds`，`Resource` 可填 `HRU_CAN`，G 列填上述配置串，P 列填解码配置。例如：

```text
RequestConfig: transport=can,canDeviceKey=HRU_CAN,canChannel=0,canFd=false,canNominalBitrate=500000,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 8C,expectedPrefix=62 F1 8C
DecodeConfig:  name=PCBA SN,valueOffset=3,valueType=string,encoding=ascii,trim=true
```

常用 CAN 参数：

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `transport` | DoIP | 选择 CAN 传输时填写 `can` | `transport=can` |
| `canDeviceKey` | 当前 provider | `CAN_UDS_Init` 注册的设备键 | `canDeviceKey=HRU_CAN` |
| `canChannel` | 0 | CAN 通道索引 | `canChannel=0` |
| `canFd` | false | 是否使用 CAN-FD | `canFd=true` |
| `canBitRateSwitch` | 与 `canFd` 相同 | CAN-FD 是否启用 BRS | `canBitRateSwitch=true` |
| `canNominalBitrate` | 500000 | 仲裁/名义速率，单位 bit/s | `canNominalBitrate=500000` |
| `canDataBitrate` | 2000000 | CAN-FD 数据段速率 | `canDataBitrate=2000000` |
| `canFdStandard` | 0 | provider 使用的 CAN-FD 标准模式 | `canFdStandard=0` |
| `canFdMode` | 0 | provider 使用的 CAN-FD 设备模式 | `canFdMode=0` |
| `canRequestId` | 0x7E0 | ISO-TP 请求 CAN ID | `canRequestId=0x7E0` |
| `canResponseId` | 0x7E8 | ISO-TP 响应 CAN ID | `canResponseId=0x7E8` |
| `canFrameLength` | 8 | CAN UDS 固定帧数据长度；0 为不固定 | `canFrameLength=8` |
| `canPaddingByte` | CC | 固定帧的填充字节 | `canPaddingByte=CC` |
| `canCustomBitrateConfig` | — | 厂商自定义时序字符串 | `canCustomBitrateConfig=...` |

CAN UDS 的 `udsPayload` 只填写有效 UDS 数据，例如 `udsPayload=10 01`。默认会发送 `02 10 01 CC CC CC CC CC`；不要写成 `udsPayload=10 01 CC CC CC CC CC`，否则 PCI 长度会把 `CC` 误计入 UDS 数据。

`CAN_UDS_Init` 的参数依次为：provider DLL、provider 类型、设备地址、设备序列号、设备键。ZLG 示例中的 `41,0` 只是设备地址示例，必须按实际 ZLG 设备类型和索引调整。CAN-FD 设备不能使用仅支持经典 CAN 的设备类型。`CAN_UDS_Release` 应在测试结束时调用以关闭通道和 provider。

#### 2.0.1 从硬件到第一条 CAN UDS

CAN 步骤开始前，先确认 `Jabil.Uds.Can.dll`、ZLG provider DLL 及其依赖 DLL 已部署到 JEMSts 可加载的位置，并确认 ZLG 驱动能识别设备。初始化不是 G 列配置的一部分，而是脚本生命周期操作：

```text
($ok,$provider,$message,$address,$serial,$key) = CAN_UDS_Init(
  "Jabil.Uds.Can.Zlg.dll",
  "ZLGAPI.ZlgCanDeviceProvider",
  "41,0",
  "",
  "HRU_CAN"
)
if($ok != true)
{
  UpdateStatus("CAN init failed: " + $message)
}
```

参数顺序是 provider DLL、完整 provider 类型名、设备地址、设备序列号、设备键。`41,0` 只是 ZLG 设备地址示例，具体设备类型和索引必须以现场硬件配置为准；有多个设备时应为每个设备使用不同的设备键。初始化成功后，G 列用 `canDeviceKey=HRU_CAN` 选择这个 provider，`canChannel=0` 再选择设备上的通道 0。

不要忽略初始化返回值。`CAN_UDS_Init` 即使打开设备失败，也会把失败信息写入返回值和状态栏，然后让脚本继续运行；此时后续步骤必然报 `CAN provider is not registered for device key: ...`。因此应先判断 `ReturnValue0`，失败时不要执行 `CAN_UDS_Step`。例如 AIUM 脚本应使用：

```text
($canInitOk,$provider,$message,$address,$serial,$key) = CAN_UDS_Init("D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,0", "", "AIUM_CAN")
if($canInitOk != true)
{
  UpdateStatus("CAN init failed: " + $message)
  CAN_UDS_Release()
  End
}
```

ZLG 原生库的目录也必须符合当前适配器的搜索规则。64 位 JEMSts 应至少存在：

```text
D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\zlgcan\\zlgcan_x64\\zlgcan.dll
D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\zlgcan\\zlgcan_x64\\ZNetConfig.dll
D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\zlgcan\\zlgcan_x64\\zuds.dll
```

如果初始化返回类似 `ZLG CAN device open failed`，优先检查 `41,0` 是否与实际设备类型/索引一致、设备是否被其他工具占用以及驱动位数是否匹配。如果返回 `provider DLL not found`、类型找不到或接口不匹配，检查 provider DLL 路径、类型名 `ZLGAPI.ZlgCanDeviceProvider` 和 `Jabil.Uds.Can.dll` 是否来自同一套构建。只有初始化返回 `true` 后，`AIUM_CAN` 才会进入 provider 注册表。

#### 2.0.2 经典 CAN、CAN-FD 和 ISO-TP 的配置

经典 CAN 最小配置：

```text
transport=can,canDeviceKey=HRU_CAN,canChannel=0,canFd=false,canNominalBitrate=500000,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 8C,expectedPrefix=62 F1 8C
```

CAN-FD 配置：

```text
transport=can,canDeviceKey=HRU_CAN,canChannel=0,canFd=true,canBitRateSwitch=true,canNominalBitrate=500000,canDataBitrate=2000000,canFdStandard=0,canFdMode=0,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 8C,expectedPrefix=62 F1 8C
```

字段含义如下：

| 字段 | 使用方式 |
|---|---|
| `transport=can` | 选择 CAN UDS；不能省略，否则会按默认传输解析 |
| `canDeviceKey` | 对应 `CAN_UDS_Init` 的设备键 |
| `canChannel` | provider 内的通道索引，从 0 开始 |
| `canFd` | `false` 为经典 CAN，`true` 为 CAN-FD |
| `canBitRateSwitch` | CAN-FD 的 BRS 开关；按硬件和总线配置决定 |
| `canNominalBitrate` | 仲裁段速率，例如 `500000` |
| `canDataBitrate` | CAN-FD 数据段速率，例如 `2000000` |
| `canFdStandard` / `canFdMode` | 传给 provider 的 CAN-FD 模式值，使用已验证的硬件值 |
| `canRequestId` / `canResponseId` | 发送和接收方向的 CAN ID，例如 `0x7E0` / `0x7E8` |
| `canCustomBitrateConfig` | 厂商自定义时序字符串；只有 provider 支持时才填写 |
| `udsPayload` | 只写 UDS 字节，不写 ISO-TP PCI 头 |
| `expectedPrefix` | 对重组后的 UDS 响应做前缀匹配，不包含 ISO-TP PCI 头 |

ISO-TP 由 CAN transport 自动处理：短报文使用单帧，长请求自动执行首帧、等待流控、连续帧发送，长响应自动接收并重组。比如 `22 F1 8C` 是 UDS payload，不应手工写成带 `03` 的 CAN 数据；P 列的 `valueOffset=3` 也是相对于重组后的 `62 F1 8C ...`，不需要为 ISO-TP 头增加偏移。

多请求步骤可以用 `udsSequence`，响应条件按 `|` 与请求一一对应：

```text
transport=can,canDeviceKey=HRU_CAN,canChannel=0,canFd=false,canRequestId=0x7E0,canResponseId=0x7E8,udsSequence=10 03|22 F1 8C,expectedPrefix=50 03|62 F1 8C,timeoutMs=5000,minRequestIntervalMs=20
```

CAN 不需要 DoIP 的 `host`、`port`、路由激活和 TCP 保活；这些键对 CAN transport 没有作用。`timeoutMs`、`minRequestIntervalMs`、`sequenceRetryCount` 等通用 UDS 时序键仍可使用。

#### 2.0.3 只初始化 CAN，不发送 UDS

`CAN_UDS_Step` 支持 `initializeOnly=true`（别名 `initOnly=true`）。该模式只根据 G 列打开并启动 CAN channel，不要求 `udsPayload` 或 `udsSequence`，也不会发送任何 UDS 报文：

```text
CAN_UDS_Step(
  "transport=can,canDeviceKey=AIUM_CAN,canChannel=0,canFd=true,canBitRateSwitch=true,canNominalBitrate=500000,canDataBitrate=2000000,canFdStandard=0,canFdMode=0,initializeOnly=true",
  "",
  "AIUM_CAN"
)
```

初始化成功后，`ReturnValue0=true`，`ReturnValue1` 为 `CAN channel initialized`，`ReturnValue2` 为通道号。初始化失败时 `ReturnValue0=false`，具体原因在 `ReturnValue2` 和状态栏中。此模式适合把“设备/provider 初始化”和“通道初始化”拆成两个 Cell：前一个 Cell 调用 `CAN_UDS_Init`，后一个 Cell 调用上面的 `CAN_UDS_Step`。

只初始化不等于只加载 provider：`CAN_UDS_Init` 负责打开物理设备并注册 `canDeviceKey`，`initializeOnly=true` 负责打开并启动该设备上的 CAN channel。两步都成功后，才可以使用 `CAN_Send`、`CAN_Receive` 或不带 `initializeOnly` 的 `CAN_UDS_Step`。

如果需要普通 CAN 收发，也可以使用功能更直接的 `CAN_OpenChannel`；它与上述初始化模式使用同一套 channel 资源，不能对同一个 `deviceKey + channel` 重复配置不同速率。测试结束时使用 `CAN_CloseChannel(channel, deviceKey)`；如果整个进程没有其他 Cell 使用 CAN，再调用 `CAN_UDS_Release()`。

#### 2.0.4 多 Cell 访问 CAN 资源

CAN provider 注册表是 JEMSts 进程内共享的，但本库把已打开的通道按 `deviceKey:canChannel` 建立独立资源。例如两个 Cell 可以分别使用两个设备：

```text
; Cell A：设备 A，通道 0
CAN_UDS_Init("D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,0", "", "CELL_A_CAN")
CAN_OpenChannel(0, 1, 1, 500, 2000, 0, 0, "CELL_A_CAN")

; Cell B：设备 B，通道 0；deviceKey 不同，因此不会复用 Cell A 的通道
CAN_UDS_Init("D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,1", "", "CELL_B_CAN")
CAN_OpenChannel(0, 1, 1, 500, 2000, 0, 0, "CELL_B_CAN")
```

同一个 provider 的不同通道也可以被不同 Cell 使用，只要通道号不同：

```text
CAN_OpenChannel(0, 0, 0, 500, 2000, 0, 0, "CELL_A_CAN")
CAN_OpenChannel(1, 0, 0, 500, 2000, 0, 0, "CELL_B_CAN")
```

多 Cell 时必须遵守以下规则：

- 每个物理设备使用唯一 `deviceKey`，每个 `CAN_UDS_Step` 的第三个参数传入该设备键；G 列不需要重复写 `canDeviceKey`。
- 普通 CAN 的 `CAN_Send`、`CAN_Receive`、`CAN_CloseChannel` 最后一个设备参数必须传相同的 `deviceKey`；不要依赖“当前 provider”。
- `CAN_UDS_Step` 第三个参数就是设备键，必须与 `CAN_UDS_Init` 注册时的设备键一致。
- 同一个 `deviceKey` 和通道号不能配置成两套速率或 CAN/CAN-FD 模式；需要另一套配置时使用另一个通道或先关闭旧通道。
- `CAN_UDS_Release()` 会关闭所有 CAN 通道、清空所有 provider，是进程级清理操作。Cell 结束时如果还有其他 Cell 在使用 CAN，不要调用它；只调用 `CAN_CloseChannel(channel, deviceKey)`，并由统一的最终清理 Cell 调用 `CAN_UDS_Release()`。
- `CAN_SelectDevice` 会改变全局当前 provider，只适合兼容旧的无 `deviceKey` 调用；多 Cell 场景优先使用显式设备键的入口。

多 Cell 普通 CAN 发送示例：

```text
; 初始化和开通道只需对 CELL_A_CAN 做一次
CAN_Send(0, 0x709, "10 01", 0, 0, "CELL_A_CAN")
($hasFrame,$count,$frames) = CAN_Receive(0, 500, 0, "CELL_A_CAN")
UpdateStatus("CELL_A_CAN RX=" + $frames)
CAN_CloseChannel(0, "CELL_A_CAN")
```

这里的返回值为：`CAN_Send` 的 `ReturnValue0/1/2` 分别是成功标志、发送帧数、发送详情；`CAN_Receive` 的 `ReturnValue0/1/2` 分别是是否收到、帧数、以 `|` 分隔的帧文本。接收文本格式类似 `789:50 01`。`CAN_Receive` 不做 ISO-TP 重组，也不按 UDS 正响应判断，它返回底层 CAN 帧；需要 UDS 语义时才使用 `CAN_UDS_Step`。

#### 2.0.5 普通 CAN 收发（不使用 UDS）

普通 CAN 适合发送周期控制帧、读取原始报文、验证 CAN-FD 链路或调用非 UDS 协议。它的流程是：初始化 provider -> 打开通道 -> 发送/接收原始帧 -> 关闭通道。`CAN_Send` 的参数依次为：通道号、CAN ID、数据 HEX、扩展帧标志、远程帧标志、设备键；`CAN_Receive` 的参数依次为：通道号、超时 ms、接收模式、设备键。

经典 CAN 示例：

```text
CAN_UDS_Init("D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "4,0", "", "RAW_CAN")
CAN_OpenChannel(0, 0, 0, 500, 2000, 0, 0, "RAW_CAN")
CAN_Send(0, 0x321, "01 02 03 04 05 06 07 08", 0, 0, "RAW_CAN")
($received,$number,$frames) = CAN_Receive(0, 1000, 1, "RAW_CAN")
UpdateStatus("RAW_CAN received=" + $number + ": " + $frames)
CAN_CloseChannel(0, "RAW_CAN")
CAN_CloseDevice("RAW_CAN")
```

CAN-FD 示例：

```text
CAN_UDS_Init("D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,0", "", "RAW_FD")
CAN_OpenChannel(0, 1, 1, 500, 2000, 0, 0, "RAW_FD")
CAN_Send(0, 0x321, "00 11 22 33 44 55 66 77 88 99 AA BB CC DD EE FF", 0, 0, "RAW_FD")
($received,$number,$frames) = CAN_Receive(0, 1000, 2, "RAW_FD")
CAN_CloseChannel(0, "RAW_FD")
CAN_CloseDevice("RAW_FD")
```

`CAN_OpenChannel` 的参数依次为：通道号、CAN-FD 标志、BRS 标志、名义速率、数据速率、CAN-FD 标准值、CAN-FD 模式值、设备键。速率传 `500` 时会按 kbit/s 转成 `500000`，传 `500000` 则按 bit/s 使用。`CAN_Receive` 的接收模式为 `0=默认`、`1=经典 CAN`、`2=CAN-FD`、`3=两者`；具体过滤行为由 provider 实现。

普通 CAN 不会自动添加或解析 ISO-TP PCI，也不会自动应答。若报文长度超过经典 CAN 的 8 字节或需要 UDS 分帧，请改用 `CAN_UDS_Step`，不要用 `CAN_Send` 手工模拟 ISO-TP，除非测试目的就是验证原始 PCI 帧。

#### 2.0.6 设备信息、过滤和自动发送

对于 `CAN_UDS_Init` 注册的设备，推荐使用带 `deviceKey` 的入口。它们不会依赖全局当前设备，适合多设备或多 Cell 流程：

```text
CAN_UDS_Init("D:\\Software\\JEMSts_8.65.00\\Modules\\UserLibraries\\Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,0", "", "AIUM_CAN")

; 普通 11 位 CAN：只接收 0x700 ~ 0x7FF
CAN_SetFilter("AIUM_CAN", 0, "0", "0x700", "0x7FF")
CAN_EnableFilter("AIUM_CAN", 0)

; 需要恢复全部接收时：
CAN_ClearFilter("AIUM_CAN", 0)
```

`CAN_SetFilter` 参数依次为：设备键、通道号、过滤模式、起始 ID、结束 ID。过滤模式 `0` 表示标准帧；标准 11 位 CAN 的 ID 最大为 `0x7FF`，因此不能把 `0xFFF` 当作标准帧范围的结束值。`CAN_EnableFilter` 将配置应用到通道，`CAN_ClearFilter` 清除通道过滤器。实际可用的路径和过滤语义由 provider/厂商驱动决定。

需要设置其它 ZLG provider 属性时，使用通用入口，把厂商属性路径原样传入：

```text
CAN_SetDeviceValue("AIUM_CAN", "0/baud_rate_custom", "500000")
CAN_SetDeviceValue("AIUM_CAN", "0/initenal_resistance", "1")
```

通用入口参数依次为：设备键、provider 属性路径、属性值。扩展新的便捷函数时，保持同样的结构：先按 `deviceKey` 获取 provider，再调用 `ICanDeviceDiagnostics.SetValue(path, value)`；如果操作需要多个属性，则封装成一个专用入口并返回统一的成功标志和错误消息。

旧的 ZCAN 兼容入口仍可用于设备级操作，但它们通常使用 provider 当前句柄，适合单设备或初始化后立即操作的流程：

```text
($info) = ZCAN_GetDeviceInfo()
ZCAN_SetValue_Str(0, "canfd.channel.0.nom_baudrate", "500000")
```

自动发送需要传入兼容层的 `device_handle` 和通道参数，典型顺序如下：

```text
; 参数位置：device_handle、channel_index、frame_index、can_type、CANID、FrameType、CANdata、Interval
ZCAN_AddSingleAutoSendFrame($deviceHandle, 0, 0, 0, 0x500, 0, "AA 55", 100)
ZCAN_AutoSend_Enable($deviceHandle, 0)
; ... 测试期间自动发送 ...
ZCAN_AutoSend_Disable($deviceHandle, 0)
ZCAN_CancelSingleAutoSendFrame($deviceHandle, 0, 0, 0, 0x500, 0, "AA 55", 100)
```

自动发送能力依赖 provider；若 provider 不支持，会返回明确的 `automatic transmission` 错误。兼容层的 `$deviceHandle` 应使用 `CAN_UDS_Init` 或 `ZCAN_OpenDevice` 返回的句柄，不要把 `deviceKey` 字符串当作句柄传入。

#### 2.0.7 普通 CAN 与 UDS 的选择

| 需求 | 入口 |
|---|---|
| 发送一条原始 CAN/CAN-FD 帧 | `CAN_Send` |
| 接收原始 CAN/CAN-FD 帧 | `CAN_Receive` |
| 打开或关闭物理通道 | `CAN_OpenChannel` / `CAN_CloseChannel` |
| UDS 单帧或多帧、ISO-TP 流控和响应匹配 | `CAN_UDS_Step` |
| 从 Excel 的 G/P 列执行 UDS | `CAN_UDS_ExcelStep` / `CAN_UDS_ExcelRunAll` |
| 周期发送固定 CAN 帧 | `ZCAN_AddSingleAutoSendFrame` / `ZCAN_AutoSend_Enable` |

不要把 `CAN_Send(0, 0x709, "10 01", ...)` 当作 UDS 执行：它只发送一个原始 CAN 帧，不等待 `0x789`，也不检查 `50 01`。需要请求/响应、ISO-TP 或 UDS 负响应处理时使用 `CAN_UDS_Step`。

#### 2.0.3 三种执行入口

**脚本直接执行一个 CAN 步骤：**

```text
CAN_UDS_Step(
  "transport=can,canDeviceKey=HRU_CAN,canChannel=0,canFd=false,canNominalBitrate=500000,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 8C,expectedPrefix=62 F1 8C",
  "name=PCBA SN,valueOffset=3,valueType=string,encoding=ascii,trim=true",
  "HRU_CAN"
)
```

第 1、2、3 个参数分别是 G 列、P 列和会话键；还可以追加下限、上限、单位覆盖参数。脚本应先调用 `CAN_UDS_Init`，步骤完成后调用 `CAN_UDS_Release`。

**按工步号读取 Excel：**

```text
CAN_UDS_ExcelStep("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表", "5")
```

该入口读取指定行的 Action、Resource、G 列、P 列、测试项、上下限和单位。Excel 行的 G 列必须包含 `transport=can` 和正确的 `canDeviceKey`；禁用行会记录为 `SKIP`，不会发送 CAN 报文。

**整张表执行：**

```text
($passed,$failed,$total) = CAN_UDS_ExcelRunAll("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表", 0)
CAN_UDS_Release()
```

第三个参数为最大执行数，`0` 表示执行全部可运行 UDS 行。返回值依次为通过数、失败数、实际执行数；`ReturnValue3` 是整表 `PASS/FAIL`，`ReturnValue4` 保存首个失败原因。

#### 2.0.4 响应、解码和排错

推荐只在 `expectedPrefix` 中写稳定的 UDS 前缀，把动态数据交给 P 列解码：

```text
G: transport=can,canDeviceKey=HRU_CAN,canChannel=0,canFd=false,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 05,expectedPrefix=62 F1 05
P: name=SoftwareVersion,valueOffset=3,valueType=string,encoding=ascii,trim=true
```

收到 `7F 22 13` 等负响应时，不会匹配 `62 F1 05`，步骤会失败并保留实际响应。结果变量使用下划线命名，例如：

```text
($response) = Script_GetVariable("Result_step_5_Response")
($stage) = Script_GetVariable("Result_step_5_FailureStage")
($error) = Script_GetVariable("Result_step_5_Error")
```

常见失败按以下顺序检查：`CONFIG` 看 G/P 格式和设备键；`CONNECT` 看驱动、设备地址和通道占用；`TIMEOUT` 看 CAN ID、CAN/CAN-FD 模式、名义/数据速率和多帧超时；`RESPONSE_MISMATCH` 先读取实际响应再调整前缀；`DECODE`/`LIMIT` 检查 P 列偏移、类型、编码、公式和范围。

### 2.1 地址与会话

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `host` / `ip` | —（必填，或 Argument3 / Sheet2） | DoIP 网关 IP | `host=172.16.0.8` |
| `port` | 13400 | DoIP 端口 | `port=13400` |
| `sourceAddress` | 0x102D | 测试仪地址（`0x` 前缀十六进制，否则十进制） | `sourceAddress=0x102D` 或 `sourceAddress=4141` |
| `targetAddress` | 0x1008 | ECU 地址 | `targetAddress=0x1008` |
| `activationType` | 0x00 | 路由激活类型 | `activationType=0x00` |
| `connectionMode` | session | `session`=跨步骤复用同一个 TCP（推荐）；`perStep`=每步重连 | `connectionMode=session` |
| `closeConnection` | false | 该步骤发完是否立即断开 TCP | `closeConnection=true` |
| `idleTimeoutMs` | 60000 | 会话空闲多少 ms 自动断开（防占用） | `idleTimeoutMs=30000` |

### 2.2 报文与响应匹配

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `udsPayload` | — | 单条 UDS 指令（HEX，空格分隔）；支持 `{pad:N}`（不足补位）、`{retry:N}`（单指令重试） | `udsPayload=22 F1 05`、`udsPayload=10 01` |
| `udsSequence` | — | 多条指令，`\|` 分隔，顺序执行 | `udsSequence=10 03\|22 F1 05\|22 F1 30` |
| `expectedPrefix` | — | 每条指令的响应**前缀**匹配（`\|` 与指令一一对应；不填则该项不校验） | `expectedPrefix=62 F1 05`、`expectedPrefix=50 03\|62 F1 05` |
| `expectedResponse` | — | 每条指令的响应**精确**匹配（`\|` 一一对应） | `expectedResponse=62 F1 87 53 57 5F 56 45 52 5F 31 2E 30` |
| `dataFrom` | -1 | 取哪条响应做解码/返回：`N`=第 N 条；未指定时默认最后一条 | `dataFrom=1`（取第二条） |
| `canMinFrameLength`（兼容 `canFrameLength`） | 0 | CAN-FD 最小帧长度；0=自动选择，配置为 8 可让小帧统一发 8 字节，大帧自动选择更大的合法长度；支持 0-8、12、16、20、24、32、48、64 | `canMinFrameLength=8` |
| `canPaddingByte` | CC | CAN/CAN-FD 帧填充字节 | `canPaddingByte=00` |
| `minRequestIntervalMs` | 20 | 相邻请求最小间隔（ECU 处理节流，0x08 教训：≥20） | `minRequestIntervalMs=20` |

### 2.3 时序与重试

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `timeoutMs` / `timeout` | 5000 | 首次等待单条请求响应的时间；CAN/DoIP 收到 `7F <SID> 78` 后会在同一请求中继续等待最终响应 | `timeoutMs=3000` |
| `responsePendingTimeoutMs` | 0（自动） | 收到 `7F <SID> 78` 后等待最终响应的专用时长，单位 ms；大于 0 时精确使用该值，0/不填时使用 `max(timeoutMs,min(120000,timeoutMs*4))` | `responsePendingTimeoutMs=60000` |
| `udsInitialDelayMs` | 0 | 第一条请求发出前的延时 | `udsInitialDelayMs=50` |
| `doipNackRetryCount` | 2 | 收到 DoIP NACK 0x08（并发冲突）时重试次数（0-5） | `doipNackRetryCount=2` |
| `doipNackRetryDelayMs` | 100 | NACK 0x08 重试间隔（50-2000） | `doipNackRetryDelayMs=100` |
| `routingActivationRetryCount` | 2 | 路由激活响应码 `0x03` 后重新建 TCP 并重试的次数（0-5）；其它响应码不重试 | `routingActivationRetryCount=2` |
| `routingActivationRetryDelayMs` | 1000 | `0x03` 路由激活重试间隔，单位 ms（0-60000） | `routingActivationRetryDelayMs=1500` |
| `sequenceRetryCount` | 1 | 整条序列从第一条命令开始执行的总次数（1-20）；与单条命令 `{retry:N}` 独立 | `sequenceRetryCount=3` |
| `sequenceRetryDelayMs` | 500 | 整序列重试间隔 | `sequenceRetryDelayMs=1000` |
| `decodeRetryCount`（简写 `decodeRetry`） | 0 | CAN 解码值或范围校验失败后的整条序列额外重试次数（0-20） | `decodeRetryCount=10` |
| `decodeRetryDelayMs`（简写 `decodeRetryDelay`） | 500 | CAN 解码重试间隔，单位 ms | `decodeRetryDelayMs=500` |
| `responsePendingRetryCount` | 3 | 同一请求在 `0x78` 扩展等待也超时后，追加的原请求重试次数（0-20）；重试期间不发送 `31 02`，不关闭 session | `responsePendingRetryCount=3` |

`0x78` 的完整时序是：

```text
TX 14 FF FF FF
RX 7F 14 78
在同一 CAN/DoIP 请求中继续等待，不重发请求
RX 54                         -> 正常返回 54
扩展等待超时                  -> 重试 TX 14 FF FF FF
```

`responsePendingTimeoutMs` 只控制收到 `0x78` 之后的等待阶段，不会缩短或替代 `timeoutMs` 的首次等待。收到 `0x78` 不会重发原请求或发送 `31 02`；DoIP 下也不会因此断开 TCP 或重新路由激活。

> NACK 0x08 自动处理：检测到 0x08 会重建 TCP/DoIP 会话并**回放** `10 xx` 会话控制，再重发。
> `31 01` 启动例程失败且还有重试机会时，会先发 `31 02` 停止例程再重试。

### 2.4 保活（TesterPresent）

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `udsKeepAliveEnabled` | false | 是否在会话空闲时发保活 | `udsKeepAliveEnabled=true` |
| `udsKeepAliveIntervalMs` | 2000 | 保活周期 ms（≥250） | `udsKeepAliveIntervalMs=500` |
| `udsKeepAlivePayload` | 3E 80 | 保活载荷，**支持 `\|` 分隔多条**：每条要么是 UDS 报文（封装成 DoIP DiagnosticMessage 发），要么是**原始 DoIP 帧**（版本对 `02 FD`/`03 FC` 开头、≥8 字节，自动识别后原样发 TCP）。一次周期内按顺序发完全部 | `udsKeepAlivePayload=3E 80\|02 FD 00 08 00 00 00 02 10 2D` |
| `tcpKeepAlive` | true | TCP 层 keepalive | `tcpKeepAlive=true` |
| `localBindAddress` | — | TCP 本机源 IP；多网卡时填写目标网关对应网卡配置的本机 IP | `localBindAddress=172.16.0.100` |
| `localBindPort` | 0（系统分配） | TCP 本机源端口；0 表示由系统分配，固定端口须确保未被占用 | `localBindPort=50000` |

多网卡连接时，`host`/`port` 是远端网关，`localBindAddress`/`localBindPort` 是本机 TCP 源端点。`localBindAddress` 必须是配置在对应网卡上的本机 IP。不同本地绑定会使用独立 DoIP 会话；省略本地端口或设为 `0` 时由系统分配临时端口，通常建议使用 `0`，仅在网络策略要求固定源端口时再指定固定值。

```text
# 网卡 1：本机 IP 172.16.0.100
host=172.16.0.8,port=13400,localBindAddress=172.16.0.100,localBindPort=50000

# 网卡 2：目标网关仍是 172.16.0.8，改用本机 IP 192.168.10.100
host=172.16.0.8,port=13400,localBindAddress=192.168.10.100,localBindPort=50000
```

两条配置的远端地址相同，但本地绑定不同，因此分别从指定网卡建立连接。固定端口时，本机源端点（本地 IP + 本地端口）必须可用；若不需要固定源端口，可将 `localBindPort` 设为 `0` 或省略。

> **多帧保活示例**（先发心跳 `3E 80`，立马再发原始 DoIP 帧 `02 FD ...`，周期 500ms）：
> ```text
> udsKeepAliveEnabled=true,udsKeepAliveIntervalMs=500,udsKeepAlivePayload=3E 80|02 FD 00 08 00 00 00 02 10 2D
> ```
> 判定规则：以 `02 FD`/`03 FC`（ISO 13400 版本/反版本对）开头且 ≥8 字节 → 当作完整 DoIP 帧**原样发送**；否则当作 UDS 报文封装成 DiagnosticMessage（0x8001）发送。

### 2.5 虚拟模式（无硬件调试）

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `virtualMode` | false | 不连 TCP，直接返回 `simResponse`，用于验证配置/解码/校验 | `virtualMode=true` |
| `simResponse` | 50 01 | 虚拟返回的响应 HEX | `simResponse=62 F1 05 0B B8` |

### 2.6 步骤元信息

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `testItem` | UDS Step | 测试项名称（Excel 直读时 E 列优先） | `testItem=Read DID F105` |
| `lowerLimit` / `upperLimit` / `unit` | — | 上下限 / 单位（与 Excel J/K/L 等价；仅当无校验键时参与 J/K 判定） | `lowerLimit=0,upperLimit=8000,unit=rpm` |

### 2.7 结果变量（跨步骤复用）

| 参数 | 默认 | 说明 | 例子 |
|---|---|---|---|
| `saveAs` | — | 将本步骤解码结果保存到指定变量名；只需填写变量名，不需要填写类型 | `saveAs=VehicleVoltage` |

`saveAs` 保存的是解码结果的实际类型：字符串、整数、浮点数、布尔值等由 P 列解码结果决定，不通过 G 列重复声明类型。

保存单测量项时：

```text
G 列：udsPayload=22 F1 05,expectedPrefix=62 F1 05,saveAs=VehicleVoltage
P 列：name=电压,valueOffset=3,valueLength=2,valueType=uint16,formula=x/100,unit=V
```

执行后可使用：

| 变量 | 含义 |
|---|---|
| `VehicleVoltage` | 解码后的第一个测量值，保留实际类型，例如 `Double` |
| `VehicleVoltage.Value` | 第一个测量值 |
| `VehicleVoltage.Response` | 本步骤实际响应报文 HEX |
| `VehicleVoltage.Passed` | 本步骤是否通过，布尔值 |
| `VehicleVoltage.Status` | `PASS` / `FAIL` |

没有 P 列解码项时，`saveAs` 保存实际响应 HEX 字符串。

一行包含多个测量项时，按测量项索引保存：

```text
saveAs=Measurements
```

结果变量示例：

```text
Measurements       // 第 0 个测量项的值
Measurements.0     // 第 0 个测量项的值
Measurements.1     // 第 1 个测量项的值
Measurements.2     // 第 2 个测量项的值
```

每个测量项还提供：

```text
Measurements.1.Value
Measurements.1.Name
Measurements.1.Unit
Measurements.1.RawValue
Measurements.1.RawHex
Measurements.1.Passed
```

标准步骤路径也可使用，例如第 3 步的第二个测量项：

```text
Result_step_3_1
Result_step_3_1_Value
Result_step_3_1_RawHex
Result_step_3_Response
```

这些结果变量独立于 `ReturnValue0..7`，后续步骤的 `ClearReturns()` 不会清除它们。

### 2.8 标准步骤状态变量（`Result_step_*`）

JEMSts 每次执行 Excel 工步后，会把结果写入持久变量空间。通过 `Script_GetVariable` 读取时，参数必须传完整变量名，例如：

```text
($value) = Script_GetVariable("Result_step_5_FailureStage")
```

其中 `5` 是 Excel 工步号，不是从 0 开始的数组索引。步骤级字段如下：

| 变量 | 类型/示例 | 含义 |
|---|---|---|
| `Result_step_<step>_Passed` | Boolean：`true` / `false` | 综合 UDS 收发、响应匹配、解码和值校验后的最终结果 |
| `Result_step_<step>_Status` | String：`PASS` / `FAIL` | `Passed` 的文本形式 |
| `Result_step_<step>_Request` | String | 实际发送的 UDS 请求 HEX；多条命令用 `\|` 分隔 |
| `Result_step_<step>_Response` | String | 实际收到的响应 HEX；响应 mismatch 或失败时仍尽量保留实际报文 |
| `Result_step_<step>_Value` | Object/String | 第一个测量项的解码值；无解码时为空或响应值 |
| `Result_step_<step>_Name` | String | 第一个测量项名称 |
| `Result_step_<step>_Unit` | String | 对外显示的单位或判定条件文本 |
| `Result_step_<step>_RawUnit` | String | 解码项原始单位 |
| `Result_step_<step>_Values` | String | 所有测量值，按测量项顺序以 `\|` 拼接 |
| `Result_step_<step>_Units` | String | 所有测量项单位，按顺序以 `\|` 拼接 |
| `Result_step_<step>_Limits` | String | 所有测量项范围，格式如 `0~8000`，多项用 `\|` 拼接 |
| `Result_step_<step>_Error` | String | 失败或执行错误信息；成功时为空 |
| `Result_step_<step>_Trace` | String | CAN/DoIP 收发 trace，包含 TX/RX/超时等诊断信息 |
| `Result_step_<step>_FailureStage` | String | 失败阶段，见下表；通过时为 `PASS` |
| `Result_step_<step>_expectedContext` | String | 当前步骤实际使用的 expected/match/limit 条件摘要 |

`FailureStage` 的取值：

| 值 | 含义 |
|---|---|
| `PASS` | 步骤通过 |
| `CONFIG` | 配置解析失败或必需配置缺失 |
| `CONNECT` | TCP 连接建立失败 |
| `ROUTING` | DoIP 路由激活或路由相关操作失败 |
| `SEND` | 发送、socket、连接关闭或 DoIP NACK 等传输阶段失败 |
| `TIMEOUT` | 等待响应超时；包括 `0x78` 扩展等待超时 |
| `RESPONSE_MISMATCH` | 收到响应，但与 `expectedPrefix/expectedResponse/expectedSuffix` 等报文条件不匹配 |
| `DECODE` | 响应解码失败 |
| `LIMIT` | 解码成功，但值校验、长度校验或上下限校验失败 |
| `EXECUTION` | 其它未分类执行失败 |

### 2.9 测量项结果变量与 `TestType`

当步骤包含解码测量项时，每个测量项按索引保存。索引从 `0` 开始：

```text
Result_step_5_0
Result_step_5_0_Value
Result_step_5_0_Name
Result_step_5_0_Unit
Result_step_5_0_RawValue
Result_step_5_0_RawHex
Result_step_5_0_Passed
Result_step_5_0_TestType
Result_step_5_0_Criteria
```

| 变量 | 含义 |
|---|---|
| `Result_step_<step>_<index>` | 该测量项的解码值本身 |
| `.Value` | 工程值，经过 `formula` 后的值 |
| `.Name` | 测量项名称 |
| `.Unit` | 该测量项的判定条件摘要 |
| `.RawValue` | 未经过公式换算的原始解码值 |
| `.RawHex` | 该测量项对应的原始字节 HEX，可用于后续 UDS 报文拼接 |
| `.Passed` | 该测量项是否通过所有校验 |
| `.TestType` | 该测量项主校验类型码，见下表 |
| `.Criteria` | 该测量项完整校验条件摘要 |

`TestType` 类型码：

| 码 | 类型 | 触发条件 |
|---:|---|---|
| `0` | 无判定 | 没有报文匹配、值校验或范围条件 |
| `1` | 报文匹配 | `expectedResponse`、`expectedPrefix`、`expectedSuffix` 或 `expectedByteOffset/expectedByteValue` |
| `2` | 完全相等 | `expectedValue` |
| `3` | 字符串匹配 | `expectedContains`、`expectedStartsWith` 或 `expectedEndsWith` |
| `4` | 不包含 | `expectedNotContains` |
| `5` | 长度 | `expectedLength`、`expectedMinLength` 或 `expectedMaxLength` |
| `6` | 正则 | `expectedRegex` |
| `7` | 数值范围 | P/G 列的 `expectedMin` 或 `expectedMax`，或者 Excel J/K 下限/上限；手动调用时也包括 `CAN_UDS_Step` / `DoIP_UDS_Step` 的下限、上限参数 |
| `8` | DTC 校验 | DTC 黑名单或 DTC 状态位校验 |
| `9` | 日期校验 | `valueType=date`；严格匹配 `dateFormat`、验证真实日历日期及可选 `expectedMinDate` / `expectedMaxDate` 区间 |

一个测量项配置多个校验键时，所有条件仍按 AND 规则判定，但 `TestType` 只返回一个主类型码，不拼接成 `1|5`。值校验优先于报文匹配：P 列、G 列或 J/K 上下限存在值校验时，返回对应的 `2` 至 `7`；DTC 黑名单或状态位校验返回 `8`；日期解码及可选日期区间校验返回 `9`；其他没有值校验时才返回 G 列报文匹配码 `1`。步骤函数的 `ReturnValue7` 使用相同规则；多测量项时可以返回按顺序排列的类型码字符串。

DoIP 和 CAN 共用这套类型定义，因为两者最终都通过 `StepRunner.Publish` 发布返回值。因此，CAN 返回 `12.232` 且 J/K 有数值上下限时，DoIP 也会返回 `TestType=7`；如果只有 `expectedPrefix` 而没有值校验，则返回 `TestType=1`，即使 P 列已经成功解码出测量值。

示例：

```text
P 列：name=软件版本,valueOffset=3,valueType=string,expectedLength=16
读取：Result_step_5_0_TestType
结果：5

Excel J/K：下限=10，上限=15；P 列解码得到 12.232
读取：函数 ReturnValue7
结果：7
```

`Result_step_<step>_TestType` 不是当前标准步骤级字段；需要查询类型时应读取具体测量项的 `_TestType`，或读取函数的 `ReturnValue7`。

---

## 三、P 列（解码配置）参数详解

> P 列 = **解码 + 期望值/校验**，三种写法：
> ① 单测量项：`key=value` 直接写
> ② 多测量项：JSON 数组，每个对象一个测量项
> ③ Profile 文件：`{"decodeProfile":"xxx.json"}`（文件里每个 measurement 同样可带校验键）

### 3.1 解码字段

| 参数 | 别名 | 默认 | 说明 |
|---|---|---|---|
| `name` | `measurementName` | — | 测量项名称（返回值 RV1、报告里显示） |
| `valueOffset` | `offset` | 0 | 从响应第几个字节开始取 |
| `valueLength` | `length` | -1 | 取几个字节；`-1`=从 offset 到结尾 |
| `valueType` | `type` | hex | 类型：`hex` / `uint8..64` / `int8..64` / `float32` / `float64` / `string` / `bits` / `date`（简写 `uint`/`int`/`float`） |
| `dateEncoding` | — | bcd | 日期字节表示：`bcd` 或 `ascii`；ASCII 日期使用 `encoding` 指定字符编码 |
| `dateFormat` | — | yyyyMMdd | 输入日期格式；严格解析并验证真实日期，格式必须包含四位年份 `yyyy`；输出统一为 `yyyy-MM-dd` |
| `endian` | — | big | `big`=大端 / `little`=小端 |
| `encoding` | — | ascii | 字符串编码：`ascii` / `utf8` / `utf16` |
| `bitOffset` / `bitLength` | — | 0 / 0 | `bits` 类型：取第几位、几位 |
| `formula` | — | — | 安全公式：`x + - * / % ( ) abs min max round floor ceil`（`x`=原始值） |
| `unit` | `measuredUnit` | — | 单位 |
| `extractRegex` | — | — | 解码后再用正则提取（取第 1 个捕获组） |
| `postType` | — | — | 提取结果转类型：`int` / `double` |
| `trim` | — | true | 字符串解码后去首尾空白 |
| `padding` | — | — | 预留（字节补位） |
| `hexSeparator` | — | 空格 | `type=hex` 的字节分隔符；设为 `""` 输出紧凑 HEX，例如 `00010102` |
| `forbiddenDtcFile` | — | — | DTC 禁止清单路径；支持文本或 `.xlsx`，响应中出现清单任一 DTC 即 FAIL |
| `forbiddenDtcSheet` | — | — | `.xlsx` 清单的工作表名称，例如 `DCT检查清单` |
| `forbiddenDtcColumn` | — | C | `.xlsx` 清单的列名，例如 `C` |

### 3.2 期望值 / 校验键（跟着测量项走，多键 AND）

| 参数 | 说明 | 例子值 |
|---|---|---|
| `expectedValue` | 相等：字符串精确；数值可带 `0x` 前缀按数值比较 | `expectedValue=SW_VER_1.0`、`expectedValue=0x00` |
| `expectedContains` | 包含子串 | `expectedContains=VER` |
| `expectedNotContains` | 不包含子串 | `expectedNotContains=DEV` |
| `expectedStartsWith` | 以...开头 | `expectedStartsWith=SW` |
| `expectedEndsWith` | 以...结尾 | `expectedEndsWith=1.0` |
| `expectedRegex` | 正则匹配（部分匹配；含 `{1,5}`/逗号也正确解析） | `expectedRegex=[A-Z]{1,5}_[0-9]{1,3}` |
| `removeSpaces` | ASCII 字符串解码后删除所有 `0x20` 空格；只影响最终值，`RawValue` 保留原文 | `removeSpaces=true` |
| `expectedMinLength` / `expectedMaxLength` | 字符串长度下限 / 上限 | `expectedMinLength=6` |
| `expectedMin` / `expectedMax` | 数值下限 / 上限（需能转数值，否则 FAIL） | `expectedMin=0,expectedMax=8000` |
| `expectedMinDate` / `expectedMaxDate` | 日期下限 / 上限（含边界；ISO `yyyy-MM-dd`；只对规范化日期字符串校验） | `expectedMinDate=2026-01-01,expectedMaxDate=2027-05-01` |
| `expectedCaseSensitive` | 大小写敏感（影响 相等/包含/前缀/后缀/正则），默认 true | `expectedCaseSensitive=false` |

> 同一测量项多个校验键 = **AND**：全部满足才 PASS，任一不满足即 FAIL。
> 一个 Step 多个测量项：每个测量项写各自的解码 + 校验键，全过才 PASS。

`forbiddenDtcFile` 文本示例：

#### DTC 状态位检查（可选）

只配置 `forbiddenDtcFile` 时，响应命中名单中的 DTC 即 FAIL。若同时配置 `dtcStatusMask` 或 `dtcStatusBits`，则状态位只对名单中的 DTC 检查：名单内 DTC 的状态命中掩码才 FAIL；名单外 DTC 不因状态字节触发失败。未配置名单时，状态掩码仍独立检查响应中的所有 DTC。

推荐使用明确的掩码：

```text
dtcStatusMask=0x09
```

也支持多选位配置。按当前项目的 DTC 文档约定，`bit1` 对应掩码 `0x01`，`bit3` 对应掩码 `0x08`；如果要表达示例中的 `0x09`（`0x01 | 0x08`），应配置为：

```text
dtcStatusBits=bit1|bit3
```

也可以写成 `dtcStatusBits=current|history`。未选中的其它状态位不会参与判断。为避免不同文档的位编号习惯产生歧义，产线配置推荐使用 `dtcStatusMask=0x09`。

```text
# DTC 禁止清单，每行一个，也可使用 0x 前缀
0xEF1017
0x900013
0x9A1113
```

P 列使用 JSON，避免 Windows 路径中的逗号或特殊字符影响键值解析：

```json
[{"name":"DTC检查","offset":0,"valueLength":-1,"type":"hex","hexSeparator":"","forbiddenDtcFile":"D:\\Tests\\DTC禁止清单.txt"}]
```

直接读取 Excel 清单：

```json
[{"name":"DTC检查","offset":0,"valueLength":-1,"type":"hex","hexSeparator":"","forbiddenDtcFile":"D:\\Projects\\XPeng\\XPeng_Robot\\AIU & HRU\\Test spec\\机器人R02项目_HRU_生产测试用例_V1.1_20260514.xlsx","forbiddenDtcSheet":"DCT检查清单","forbiddenDtcColumn":"C"}]
```

Excel 和文本清单均支持 `0xEF1017` 或 `EF1017` 格式。文件不存在或无法读取时不判定为命中；部署测试时应确保清单文件存在。生产配置建议使用绝对路径。

---

## 四、详细例子

### 例 1：建会话 + 10 01（地址参数只写第一步，后续复用）

**G 列：**
```text
host=172.16.0.8,port=13400,sourceAddress=0x102D,targetAddress=0x1008,activationType=0x00,udsPayload=10 01,expectedPrefix=50 01,connectionMode=session,minRequestIntervalMs=20,doipNackRetryCount=2,udsKeepAliveEnabled=true,udsKeepAliveIntervalMs=2000
```

### 例 2：读 DID，单测量项（解码 + 期望值都放 P 列）

**G 列：** `udsPayload=22 F1 05,expectedPrefix=62 F1 05`
**P 列：**
```text
name=转速,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4,unit=rpm,expectedMin=0,expectedMax=8000
```
含义：取响应第 3-4 字节（大端 uint16），`x/4` 得工程值，须在 [0, 8000]。

### 例 3：多测量项（P 列 JSON 数组，每项各自解码 + 期望值）

**G 列：** `udsPayload=22 F1 30,expectedPrefix=62 F1 30`
**P 列：**
```json
[
  {"name":"转速","offset":3,"length":2,"type":"uint16","endian":"big","formula":"x/4","unit":"rpm","expectedMin":0,"expectedMax":8000},
  {"name":"电压","offset":5,"length":2,"type":"uint16","endian":"big","formula":"x/100","unit":"V","expectedMin":9,"expectedMax":15},
  {"name":"状态","offset":7,"length":1,"type":"uint8","expectedValue":"0x01"}
]
```
> 每项全过才 PASS；返回值：RV1=`转速|电压|状态`，RV4=`250|14|1`，RV5=`rpm|V|`，RV6=`0~8000|9~15|`。

如需保存该行结果，在 G 列追加：

```text
saveAs=Measurements
```

之后可分别读取 `Measurements.0`、`Measurements.1`、`Measurements.2`。
如果要把某个测量项用于后续 UDS 报文，使用该项的原始 HEX：

```text
${Measurements.1.RawHex}
```

不要直接使用 `${Measurements.1}` 拼接报文，因为该变量可能是 `Double`、`Integer` 或其他非 HEX 类型。

### 例 4：读软件版本（字符串 + 期望值）

**G 列：** `udsPayload=22 F1 87,expectedPrefix=62 F1 87`
**P 列：**
```text
name=版本,valueOffset=3,valueType=string,encoding=ascii,trim=true,expectedValue=SW_VER_1.0
```

### 例 5：字符串综合校验（包含 + 正则 + 长度）

**G 列：** `udsPayload=22 F1 87,expectedPrefix=62 F1 87`
**P 列：**
```text
name=版本,valueOffset=3,valueType=string,encoding=ascii,trim=true,expectedContains=VER,expectedRegex=[A-Z]{1,5}_[0-9]{1,3},expectedMinLength=6,expectedMaxLength=20
```
加 `expectedCaseSensitive=false` 可大小写不敏感。

### 例 6：状态码（数值相等，0x 前缀）

**G 列：** `udsPayload=31 01 02 01,expectedPrefix=71 01 02 01`
**P 列：** `name=状态,valueOffset=3,valueLength=1,valueType=uint8,expectedValue=0x00`

### 例 7：序列命令（udsSequence，多条 + 各自匹配）

**G 列：**
```text
udsSequence=10 03|22 F1 05|22 F1 30,expectedResponse=50 03|62 F1 05|62 F1 30,connectionMode=session,minRequestIntervalMs=20
```

### 例 8：31 01 启动例程（失败自动 31 02 停止重试）

**G 列：**
```text
udsPayload=31 01 02 01,expectedPrefix=71 01 02 01,sequenceRetryCount=3,sequenceRetryDelayMs=1000
```

### 例 9：虚拟模式（无硬件先验证配置/解码）

**G 列：** `virtualMode=true,simResponse=62 F1 05 0B B8,udsPayload=22 F1 05`
**P 列：** `name=转速,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4,unit=rpm`
> `0B B8`=3000 → 3000/4=750 rpm。虚拟模式也执行解码和值校验。

### 例 10：完整 Excel 一行（真实测试工步表）

| A 工步号 | B 设备分类 | C 设备名称 | E 测试项 | F 动作 | G 配置参数1 | J 下限 | K 上限 | L 单位 | ... | P 解码配置 |
|---|---|---|---|---|---|---|---|---|---|---|
| 5 | DoIP | DoIP_ECU | Check MCU Monitor [22 A5 F5] | send_uds | `host=172.16.0.8,udsPayload=22 A5 F5,expectedPrefix=62 A5 F5,connectionMode=session,minRequestIntervalMs=20` | 0 | 8000 | rpm | | `name=转速,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4,unit=rpm` |

> host 也可不写 G 列：Sheet2 设备通道映射表会自动按设备名回填 host/port/targetAddress；或 `DoIP_UDS_ExcelStep` 传默认 host 参数兜底。

---

## 五、返回值约定（`DoIP_UDS_Step` / `DoIP_UDS_ExcelStep`）

| 返回 | 含义 |
|---|---|
| `ReturnValue0` | 结果（布尔）：`true`=PASS，`false`=FAIL/ERROR（UDS 收发 + 值校验综合） |
| `ReturnValue1` | 名称：单测量项=Excel E 列测试项名；多测量项=各测量项名 `\|` 分隔 |
| `ReturnValue2` | UDS 请求 HEX（多命令 `\|` 分隔） |
| `ReturnValue3` | UDS 响应 HEX（mismatch 时保留实际收到的响应） |
| `ReturnValue4` | 测试值（多测量项 `\|` 分隔） |
| `ReturnValue5` | 单位（多测量项 `\|` 分隔） |
| `ReturnValue6` | 上下限 `下限~上限`（多测量项 `\|` 分隔） |
| `ReturnValue7` | 测试类型码：`0` 无判定，`1` 报文匹配（仅无值判定时返回；exact/prefix/suffix/byte），`2` 完全相等，`3` 字符串匹配（contains/starts/ends），`4` 不包含，`5` 长度，`6` 正则，`7` 数值范围，`8` DTC 校验，`9` 日期校验 |

多测量项时，`ReturnValue0` 按测量项返回小写布尔文本，例如 `true|false|true`；单测量项仍直接返回布尔值。脚本应先逐项检查多项结果，再汇总为整体布尔值。各项独立结果也写入 `Result_step_<step>_<index>_Passed` 和 `Result_step_<step>_<index>_Status`；`ReturnValue4/5/6` 的值、单位、上下限使用 `|` 按测量项顺序对齐。

RV7 的 `1` 只表示原始报文匹配，不表示字符串相等。字符串或解码后状态值的完全相等使用 `2`；例如 `valueLength=1,valueType=uint8,expectedValue=0x01` 是状态 byte 的 `2`。报文匹配字段为 `expectedResponse`、`expectedPrefix`、`expectedSuffix`，或 `expectedByteOffset` + `expectedByteValue`。报文匹配字段与值校验可以同时存在，但 RV7 只返回一个测试类型：有值/长度/范围等判定时返回值校验类型，例如 `len>=16;len<=16` 返回 `5`；没有值校验时才返回报文匹配类型 `1`。同一测量项配置多个值校验键时，按类型优先级返回一个类型码，不再拼接 `|`。DTC 黑名单仍属于内部值校验，不单独占用 RV7 类型码。

`DoIP_UDS_Step` 原有 3 个参数：`Argument0=配置参数1`、`Argument1=解码配置`、`Argument2=SessionKey`。现在追加三个可选参数：`Argument3=LowerLimit`、`Argument4=UpperLimit`、`Argument5=Unit`。非空时它们覆盖 G 列配置中的 `lowerLimit`、`upperLimit`、`unit`，用于接口直接传入判定范围和单位；原有三参数调用不受影响。例如：

```text
DoIP_UDS_Step(
  "udsPayload=22 F1 05,expectedPrefix=62 F1 05,valueOffset=3,valueLength=2,valueType=uint16",
  "",
  "DoIP_ECU",
  "0",
  "5",
  "V"
)
```

返回值中，`ReturnValue5` 为 `V`，`ReturnValue6` 为 `0~5`。如果是无解码报文匹配工步，未传 `Argument7` 时 `ReturnValue5` 仍返回选中的 expected 报文条件。

脚本示例：
```text
($result,$testItem,$req,$resp,$value,$unit,$limits) = DoIP_UDS_ExcelStep("D:\\Tests\\real-device-acceptance.xlsx","测试工步表","5")
if($result == true)
{
    UpdateStatus("OK: " + $testItem + " = " + $value + " " + $unit + " [" + $limits + "]")
}
else
{
    UpdateStatus("FAILED: " + $testItem)   ; 失败原因在状态栏/DebugView（UpdateStatusTextFromUpdateStatus）
}
```

---

## 六、结果变量引用与 UDS 报文拼接

结果变量可以通过 `${变量名}` 在后续 Excel 工步配置中引用。例如：

```text
G 列：udsPayload=22 F1 05,expectedPrefix=62 F1 05,saveAs=Measurements
P 列：name=数据,valueOffset=3,valueLength=2,valueType=hex
```

如果后续步骤只是文本参数或条件判断，可以引用解码值：

```text
expectedValue=${Measurements.0}
```

如果后续步骤要构造 UDS HEX 报文，必须引用字符串形式的原始报文字段：

```text
udsPayload=2E F1 91 ${Measurements.1.RawHex}
```

或者引用整个响应：

```text
udsPayload=2E F1 91 ${Measurements.Response}
```

### 6.1 脚本变量、`saveAs` 变量和 Excel 占位符

在 JEMSts 中，脚本变量和 Excel 占位符是同一个变量空间的两种写法：

```text
脚本中：$serialNum = "30 31"
Excel G 列：udsPayload=2E F1 8C ${serialNum}
```

执行 Excel 工步前，`${serialNum}` 会展开为 `30 31`，实际发送：

```text
2E F1 8C 30 31
```

前一步使用 `saveAs` 保存的变量也可以在后续步骤使用：

```text
前一步 G 列：udsPayload=22 F1 87,saveAs=SerialNumber
前一步 P 列：name=序列号,valueOffset=3,valueType=string
后一步 G 列：udsPayload=2E F1 8C ${SerialNumber.RawHex}
后一步 P 列：name=回读序列号,valueOffset=3,valueType=string,expectedValue=${SerialNumber}
```

`saveAs=SerialNumber` 保存后，脚本可以用以下方式读取：

```text
($serialText) = Script_GetVariable("SerialNumber")
($serialHex) = Script_GetVariable("SerialNumber.RawHex")
```

可用字段：

| 写法 | 含义 | 适用场景 |
|---|---|---|
| `${SerialNumber}` | 第一个测量项的解码值 | P 列 `expectedValue`、文本条件 |
| `${SerialNumber.Value}` | 解码后的工程值 | 数值或字符串比较 |
| `${SerialNumber.RawValue}` | 公式换算前的原始值 | 诊断和比较原始解码值 |
| `${SerialNumber.RawHex}` | 测量项原始字节 HEX | 拼接 UDS 写入报文，优先使用 |
| `${SerialNumber.Response}` | 本步骤完整响应 HEX | 需要引用完整响应时使用 |
| `${SerialNumber.Passed}` | 本步骤是否通过 | 条件或状态判断 |
| `${SerialNumber.Status}` | `PASS` / `FAIL` | 文本状态判断 |

如果扫码值本身已经是 HEX，例如：

```text
$serialNum = "30 31"
```

可以直接写：

```text
udsPayload=2E F1 8C ${serialNum}
```

如果扫码得到的是普通产品序列号，例如 `SN20260819`，它不是 HEX，不能直接放进 `udsPayload`。应先由扫码输入层转换成 ECU 要求的字节 HEX，例如 ASCII 编码：

```text
SN20260819 -> 53 4E 32 30 32 36 30 38 31 39
```

然后使用：

```text
udsPayload=2E F1 8C ${serialNumHex}
```

### 6.2 EonTest 中外部扫码后写入产品

EonTest 没有 JTS 的 `$serialNum` 脚本语法。它使用 `WorkflowContext::data` 作为跨工步变量空间，DoIP 插件当前支持在 `udsPayload` / `udsSequence` 中展开字符串占位符：

```text
udsPayload=2E F1 8C ${serialNumHex}
```

要让外部扫码值进入 EonTest，需要在启动 workflow 前或扫码输入插件中写入：

```cpp
context.data["serialNumHex"] = "30 31";
```

然后 DoIP 工步配置为：

```json
{
  "stepId": "step.write.serial",
  "pluginId": "doip.master",
  "initialData": {
    "udsPayload": "2E F1 8C ${serialNumHex}",
    "expectedPrefix": "6E F1 8C",
    "connectionMode": "session",
    "testItem": "Write serial number"
  }
}
```

实际流程是：

```text
扫码器/上位机读取字符串
    -> 转成 ECU 所需 HEX（必要时 ASCII 编码）
    -> 写入 WorkflowContext::data["serialNumHex"]
    -> 执行 doip.master 工步
    -> 展开 ${serialNumHex}
    -> 发送 2E F1 8C <扫码值>
```

如果是运行前就知道的固定值，也可以放在 workflow 的根级 `initialData`：

```json
{
  "initialData": {
    "serialNumHex": "30 31"
  }
}
```

但这只是预置值，不是扫码输入。当前 EonTest 的 `message` 工步只有确定/取消确认，没有文本输入能力；若要在 Studio 内直接扫码，需要增加一个扫码/文本输入工步，或者由外部扫码程序在 workflow 启动前把值注入 `WorkflowContext::data`。不要把普通字符串直接写入 `serialNumHex`，否则 HEX 解析会失败。

### 报文引用规则

- `${变量名}` 只有在变量本身是 HEX 字符串时，才可以直接出现在 `udsPayload` / `udsSequence` 中。
- 数值变量（如 `Double`、`Integer`）不能直接拼接到 HEX 报文中。
- 数值对应的原始字节必须使用 `.RawHex`，例如 `${Measurements.1.RawHex}`。
- 如果协议要求把工程值转换成报文字节，必须在 P 列解码/协议配置中明确缩放、字节长度和大小端，不能由变量替换自动猜测。
- JEMSts 支持脚本 `$变量名` 与 Excel `${变量名}` 共享变量空间；EonTest 不支持 `$变量名`，使用 `WorkflowContext::data`，当前 DoIP 主要在 `udsPayload` / `udsSequence` 中展开 `${变量名}`。
- 在 JEMSts 的 `DoIP_UDS_Step` 和 `DoIP_UDS_ExcelStep` 中，G/P 配置会在解析前展开 `${变量名}`；变量从当前脚本变量空间读取。例如 Excel G 列可写 `udsPayload=2E F1 8C ${serialNum}`。变量值必须是可解析的 HEX 字符串，例如 `30 31`。

---

## 七、常见问题

- **J/K 列什么时候生效**：仅当 G 列没有任何校验键、且 P 列对应测量项没写 `expectedMin/Max` 时，用 J/K 做数值上下限判定（作用于第一个值）。
- **期望值放 G 还是 P**：单测量项两者都行（G 列兼容）；多测量项必须放 P 列（每项各自）。
- **P 列写正则含逗号**：`[A-Z]{1,5}_[0-9]{1,3}` 里的 `{}`/`,` 不会被误拆，直接写。
- **失败看不到响应**：`ReturnValue3` 始终给实际收到的响应（mismatch 也保留），便于对照 `expected vs actual`。
- **`{pad:N}` / `{retry:N}`**：`udsPayload=10 01 {pad:4}`（补位到 4 字节）、`udsPayload=22 F1 05 {retry:2}`（该指令失败重试 2 次）。
- **保存结果变量**：G 列只写 `saveAs=变量名`，不要再写 `saveType` 或 `saveField`；结果变量类型直接沿用解码结果类型。
- **多测量项保存**：使用 `saveAs=Measurements` 后，按 `Measurements.0`、`Measurements.1`、`Measurements.2` 读取各项。
- **数值如何放入 UDS 报文**：不要把 `Double` / `Integer` 直接放进 `udsPayload`；应使用 `${变量名.RawHex}` 或 `${变量名.Response}`。
- **变量不存在或类型错误**：EonTest 在 HEX 报文中引用不存在变量或非字符串变量时会报错，并提示使用 `.RawHex` / `.Response`；JEMSts 对未找到变量保留原始 `${...}` 文本，需检查变量名和引用字段。
- **EonTest 扫码写入**：扫码值必须先进入 `WorkflowContext::data`，键名与 `${...}` 内的名称完全一致；如果是 ASCII 产品码，先转换为字节 HEX，再作为 `serialNumHex` 使用。
