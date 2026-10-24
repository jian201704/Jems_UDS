# JEMSts_UDS_Parser

把 **EonTest 测试工步表的 G 列（配置参数1）与 P 列（解码配置）的 UDS 相关能力**移植到 JEMSts（C#）的函数库，并提供 DoIP 与 CAN/CAN-FD/ISO-TP 入口。

- **G 列**：UDS 收发报文配置（`udsPayload` / `udsSequence` / `expectedPrefix` / `expectedResponse` / 地址 / 会话 / 重试 / 保活……）
- **P 列**：返回报文解码配置（`valueOffset` / `valueLength` / `valueType` / `endian` / `formula` 或 `decodeProfile=xxx.json` 多测量项）
- **CAN**：通过 `CAN_UDS_*` 入口调用已注册的 CAN provider；ZLG 实现由 `Jabil.Uds.Can.Zlg.dll` 提供。

> 配置格式与 EonTest 完全一致（不做任何改写），因此**同一张测试工步表，EonTest 和 JEMSts 都能跑**。

---

## 目录结构

```
JEMSts_UDS_Parser/
├── lib/                          # 从 JEMSts 8.55 拷贝的编译引用 DLL
│   ├── Jabil_Test_Base_Function.dll
│   └── ScriptVariableSpaceInterface.dll
├── ExcelDecodeCore/              # 共享引擎 DLL（协议无关，其它硬件协议库也复用）
│   ├── ExcelDecodeCore.csproj
│   ├── MiniXlsx.cs               #   无依赖 XLSX 读取（可读被 Excel 占用的文件）
│   ├── ExcelStepReader.cs        #   测试工步表/设备通道映射表解析（按表头识别列）
│   ├── ConfigPairParser.cs       #   G 列 key=value 解析
│   ├── DecodeModel.cs            #   解码模型（DecodeSpec/DecodedMeasurement）
│   ├── ResponseDecoder.cs        #   响应解码引擎（移植 EonTest ResponseDecoder）
│   ├── FormulaEvaluator.cs       #   安全公式求值（x + - * / % abs/min/max/round…）
│   ├── HexUtils.cs               #   十六进制解析/格式化
│   └── MiniJson.cs               #   无依赖 JSON 解析
├── UdsCore/                      # UDS/DoIP 引擎 DLL（纯 C#，无 JEMSts 依赖，可独立测试）
│   ├── UdsStepConfig.cs          #   步骤配置模型（对齐 DoipMasterPlugin）
│   ├── DoipConstants.cs          #   ISO 13400 常量
│   ├── DoipClient.cs             #   DoIP TCP 客户端（路由激活/诊断/ACK/NACK/0x78）
│   ├── DoipSessionPool.cs        #   会话池（跨步骤复用 TCP 连接）
│   ├── UdsRequestExecutor.cs     #   步骤执行器（移植 DoipMasterPlugin::executeStep）
│   └── ExcelStepExtensions.cs    #   UDS 侧 Excel 扩展（IsUdsStep/ToStepConfig/设备表回填）
├── UdsFunctionLibrary/           # net48 JEMSts 函数库（Test 子类，供脚本反射调用）
│   ├── DoIP_UDS_Step.cs          #   配置驱动单步（G 列 + P 列）
│   ├── DoIP_UDS_ExcelStep.cs     #   Excel 直读单步（按工步号，返回 E/J/K/L/M + 判定）
│   ├── DoIP_UDS_ExcelRunAll.cs   #   Excel 一键跑完所有 UDS 步骤
│   ├── DoIP_UDS_Init.cs          #   手动建会话
│   ├── DoIP_UDS_Request.cs       #   手动发一条请求（对齐 ZLG ZUDS_Request 返回约定）
│   ├── DoIP_UDS_Release.cs       #   关闭会话
│   ├── DoIP_UDS_Set_Tester_Present.cs
│   └── DoIP_UDS_Decode.cs        #   独立解码
├── UdsConsoleHarness/            # 控制台自测（不依赖 JEMSts）
├── tools/LoadProbe/              # 探针：模拟 JEMSts 函数库发现机制，验证多 DLL 可解析
├── scripts/
│   ├── demo_cell.jts             #   JEMSts 示例脚本
│   └── DecodeProfiles/DoIP_DID_F130.json
├── docs/
│   ├── 架构与使用说明.md
│   ├── G列P列参数详解.md       #   G/P 两列全部参数 + 详细用法示例
│   └── EonTest_CSharp_翻译对照表.md
└── JEMSts_UDS_Parser.slnx
```

> **快速上手**：G/P 列全部参数及用法示例见 [`docs/G列P列参数详解.md`](docs/G列P列参数详解.md)。

---

## 部署到 JEMSts

1. `dotnet build -c Release`（或运行 `build.ps1`，将所有项目 DLL 收集到 `artifacts`）；
2. 把 **`artifacts` 里的 7 个项目 DLL** 全部拷贝到
   `D:\Projects\ZEEKR\Software\JEMSts_8.55.00x64\Modules\UserLibraries\`：
    - `Jabil.Uds.Shared.dll`（共享引擎：Excel/解码/配置）
    - `Jabil.Uds.Protocol.dll`（协议配置与 Excel 步骤模型）
    - `Jabil.Uds.DoIp.dll`（UDS/DoIP 引擎）
    - `Jabil.Uds.Engine.dll`（传输无关 UDS 引擎）
    - `Jabil.Uds.Can.dll` 和 `Jabil.Uds.Can.Zlg.dll`（CAN 引擎及 ZLG provider）
    - `Jabil.Uds.FunctionLibrary.dll`（JEMSts 函数库）
3. 重启 JEMSts，脚本里即可调用 `DoIP_UDS_Step(...)` 等函数；
4. 解码 Profile 文件（如 `DoIP_DID_F130.json`）放在 Excel 同目录的 `DecodeProfiles\` 下，或 Excel 同目录。

使用 CAN 时，额外将 `Jabil.Uds.Can.dll`、实际 provider DLL（例如 `Jabil.Uds.Can.Zlg.dll`）及厂商驱动依赖部署到 JEMSts 可加载的位置。先调用 `CAN_UDS_Init` 打开并注册设备，再执行 CAN 步骤；测试结束调用 `CAN_UDS_Release`。CAN 初始化参数、经典 CAN/CAN-FD、ISO-TP 多帧和排错流程见 [`docs/G列P列参数详解.md`](docs/G列P列参数详解.md) 的 CAN 章节。

> **为什么 3 个 DLL 能行**：`JEMSts.exe.config` 的 `<probing privatePath>` 已包含
> `Modules\UserLibraries`，所以 JEMSts 发现（临时 AppDomain 按名加载）和运行（`Assembly.LoadFile`）
> 都能解析 ExcelDecodeCore/UdsCore 依赖。已用 `tools/LoadProbe` 按 JEMSts 的发现机制实测通过
> （`PROBE OK: types=10 testSubclasses=9 concrete=8`）。
> 无需签名、无需校验登记。

---

## 脚本用法

```text
; ---- 用法 1：配置驱动（G 列原文 + P 列解码配置）----
DoIP_UDS_Step("host=172.16.0.8,port=13400,sourceAddress=0x102D,targetAddress=0x1008,activationType=0x00,udsPayload=10 01,expectedPrefix=50 01,connectionMode=session,minRequestIntervalMs=20", "", "DoIP_ECU")

; 统一返回：($result,$testItem,$req,$resp,$value,$unit,$limits)
($result,$testItem,$req,$resp,$value,$unit,$limits) = DoIP_UDS_Step(
    "udsPayload=22 F1 05,expectedPrefix=62 F1 05,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4",
    "", "DoIP_ECU")

; ---- 用法 2：Excel 直读（按工步号，返回同样 7 个）----
($xresult,$xtestItem,$xreq,$xresp,$xvalue,$xunit,$xlimits) = DoIP_UDS_ExcelStep("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表", "5")

; ---- 用法 3：Excel 一键全跑 ----
($pass,$fail,$total) = DoIP_UDS_ExcelRunAll("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表")

; ---- 手动会话控制（对齐 ZLG ZUDS 习惯）----
($handle,$ok) = DoIP_UDS_Init("172.16.0.8", 13400, "0x102D", "0x1008", "0x00", "DoIP_ECU")
DoIP_UDS_Set_Tester_Present("DoIP_ECU", 2000, "3E 80", 1)
($respData,$ustatus,$utype,$uds_str) = DoIP_UDS_Request("DoIP_ECU", "22 F1 05", "62 F1 05", "", 5000)
DoIP_UDS_Release("DoIP_ECU")

; ---- CAN/CAN-FD UDS 示例 ----
; 先注册 CAN provider，再执行 CAN_UDS_Step；provider DLL/type 以实际部署版本为准。
CAN_UDS_Init("Jabil.Uds.Can.Zlg.dll", "ZLGAPI.ZlgCanDeviceProvider", "41,0", "", "HRU_CAN")
CAN_SetFilter("HRU_CAN", 0, "0", "0x700", "0x7FF")
CAN_EnableFilter("HRU_CAN", 0)
CAN_UDS_Step("transport=can,canChannel=0,canFd=false,canNominalBitrate=500000,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=22 F1 8C,expectedPrefix=62 F1 8C", "name=PCBA SN,valueOffset=3,valueType=string,encoding=ascii,trim=true", "HRU_CAN")
CAN_UDS_Release()

; CAN-FD 只需切换 canFd、速率和设备类型；ISO-TP 仍由 CAN_UDS_Step 负责封装。
CAN_UDS_Step("transport=can,canChannel=0,canFd=true,canBitRateSwitch=true,canNominalBitrate=500000,canDataBitrate=2000000,canFdStandard=0,canFdMode=0,canRequestId=0x7E0,canResponseId=0x7E8,udsPayload=10 03,expectedPrefix=50 03", "", "HRU_CAN")

; CAN UDS 默认按固定 8 字节发送，末尾填充 CC；udsPayload 只写 UDS 有效数据，不要把填充字节写进 payload。
```

完整示例见 `scripts/demo_cell.jts`。

### 日期解码

在 G 列内嵌解码配置或 P 列测量项中设置 `valueType=date`。日期输入编码支持 `bcd` 和 `ascii`；`dateFormat` 使用 .NET 日期格式字符串描述输入，默认是 BCD 编码的 `yyyyMMdd`。日期解码严格检查真实日历日期，并将结果规范化为 `yyyy-MM-dd`，因此 `expectedValue` 使用规范化后的日期比较。格式必须包含四位年份 `yyyy`，不接受有世纪歧义的两位年份。

```text
; UDS 响应 62 F1 8B 20 26 10 08，日期从偏移 3 开始，共 4 字节
udsPayload=22 F1 8B,expectedPrefix=62 F1 8B,valueOffset=3,valueLength=4,valueType=date,dateEncoding=bcd,dateFormat=yyyyMMdd,expectedValue=2026-10-08

; P 列 ASCII 日期示例，输入字节为 08/10/2026
name=BuildDate,valueOffset=3,valueLength=10,valueType=date,dateEncoding=ascii,dateFormat=dd/MM/yyyy

; P 列 JSON：区间只用于内部 PASS/FAIL，不会作为 ReturnValue6 数值上下限返回
[{"name":"获取生产日期","offset":3,"length":-1,"type":"date","dateEncoding":"bcd","dateFormat":"yyyyMMdd","expectedMinDate":"2026-01-01","expectedMaxDate":"2027-05-01"}]
```

日期会检查格式和真实日历合法性；也可以用 `expectedMinDate` / `expectedMaxDate` 在内部检查含边界的日期区间，边界必须使用 ISO 格式 `yyyy-MM-dd`。日期区间不写入数值上下限返回值 `ReturnValue6`，日期校验类型码为 `8`。如需比较某一个确定日期，可用 `expectedValue=yyyy-MM-dd` 做精确相等校验。

---

## 函数清单

| 函数 | 说明 |
|---|---|
| `DoIP_UDS_Step(G列配置, P列解码配置, ECU键, [host], [port])` | 配置驱动 UDS 单步（主入口） |
| `DoIP_UDS_ExcelStep(excel, sheet, 工步号, [ECU键])` | 按工步号直读共享 Excel 执行 |
| `DoIP_UDS_ExcelRunAll(excel, sheet, [maxSteps], [ECU键])` | 一键跑完整张表的 UDS 步骤 |
| `DoIP_UDS_Init(host, port, src, dst, act, ECU键)` | 手动建会话（TCP + 路由激活） |
| `DoIP_UDS_Request(ECU键, 请求HEX, [前缀], [完整], [超时])` | 手动发一条请求 |
| `DoIP_UDS_Set_Tester_Present(ECU键, 间隔ms, 载荷, 开关)` | 配置 3E 保活 |
| `DoIP_UDS_Release(ECU键)` | 关闭会话 |
| `DoIP_UDS_Decode(响应HEX, P列解码配置, [名称])` | 独立解码（无需会话） |
| `CAN_UDS_Init(providerDll, providerType, deviceAddress, serialNumber, deviceKey)` | 加载 provider、打开 CAN 设备并注册设备键 |
| `CAN_SetDeviceValue(deviceKey, path, value)` | 按设备键调用 provider 的通用设备配置接口；例如 `0/filter_mode` |
| `CAN_SetFilter(deviceKey, channel, mode, startId, endId)` | 按设备键设置通道 ID 范围过滤器 |
| `CAN_EnableFilter(deviceKey, channel)` | 应用指定通道的过滤器 |
| `CAN_ClearFilter(deviceKey, channel)` | 清除指定通道的过滤器 |
| `CAN_UDS_Step(G列配置, P列解码配置, CAN设备键, [下限], [上限], [单位])` | 执行一个 CAN/CAN-FD UDS over ISO-TP 步骤；第三个参数直接选择 `CAN_UDS_Init` 注册的设备 |
| `CAN_UDS_Step("transport=can,...,initializeOnly=true", "", CAN键)` | 只打开并启动 CAN channel，不发送 UDS |
| `CAN_UDS_ExcelStep(excel, sheet, 工步号)` | 按工步号读取 Excel 并执行 CAN 步骤 |
| `CAN_UDS_ExcelRunAll(excel, sheet, [maxSteps])` | 执行 Excel 中全部可运行 CAN UDS 步骤 |
| `CAN_UDS_Release()` | 关闭所有 CAN 通道并释放 provider |
| `CAN_OpenChannel(channel, canFd, brs, nominal, data, fdStandard, fdMode, deviceKey)` | 打开并启动指定 CAN/CAN-FD 通道 |
| `CAN_Send(channel, canId, data, extended, remote, deviceKey)` | 发送一帧原始 CAN/CAN-FD 报文，不做 UDS/ISO-TP 处理 |
| `CAN_Receive(channel, timeoutMs, receiveMode, deviceKey)` | 接收原始 CAN/CAN-FD 报文 |
| `CAN_CloseChannel(channel, deviceKey)` | 关闭指定设备键下的通道 |
| `CAN_SelectDevice(deviceKey)` | 选择兼容旧入口使用的当前 provider；多 Cell 优先显式传 deviceKey |
| `CAN_CloseDevice(deviceKey)` | 关闭并注销指定设备 |
| `ZCAN_AddSingleAutoSendFrame` / `ZCAN_AutoSend_Enable` | 配置并启用 provider 支持的周期自动发送 |

### DoIP 保活与失败恢复

- `tcpKeepAlive=true` 会启用操作系统的 TCP KeepAlive，但使用系统默认探测时序；它不替代 UDS TesterPresent。
- 路由激活后，空闲监视与请求接收都会处理 DoIP Alive Check (`0x0007`) 并回复带测试仪逻辑地址的 `0x0008`。空闲旧 ACK/响应按完整帧消费，未收齐的 TCP 半帧保留，不再直接清空原始字节。
- 一次接收中的控制帧、帧头、负载与分段读取共用同一截止时间；Alive Check 不会重置请求等待时间。
- 收到 UDS `0x78` 后按 `responsePendingTimeoutMs` 等待最终响应。等待超时将关闭旧连接，保留 `RESPONSE_PENDING_TIMEOUT` 和最后的 `7F <SID> 78`，终止当前步骤的命令及序列重试，不自动重放可能已执行的请求。`responsePendingRetryCount` 不会覆盖这一断链安全策略；下一次显式请求可重新建连和激活路由，但不保证 ECU 诊断会话或安全解锁状态已恢复。
- 现有 NACK `0x08` 恢复流程成功后会重启已配置的周期 TesterPresent；恢复失败则关闭未完成的连接。UDS 保活仍由 `udsKeepAliveEnabled`、`udsKeepAliveIntervalMs` 和 `udsKeepAlivePayload` 控制。
- 本地回归场景位于 `UdsConsoleHarness/DoipLifecycleTests.cs`，覆盖 ACK 队首阻塞、半帧跨请求、控制帧超时、pending 超时不重放与 NACK 恢复后的真实 TesterPresent 收发。

### `TestType` / `ReturnValue7` 类型码

DoIP 和 CAN 共用同一套类型码，函数返回的 `ReturnValue7` 表示当前测试项的主判定类型：

| 类型码 | 含义 | 配置来源 |
|---:|---|---|
| `0` | 无判定 | 没有报文匹配、值校验或范围条件 |
| `1` | 报文匹配 | `expectedResponse`、`expectedPrefix`、`expectedSuffix` 或字节匹配 |
| `2` | 完全相等 | `expectedValue` |
| `3` | 包含/字符串匹配 | `expectedContains`、`expectedStartsWith`、`expectedEndsWith` |
| `4` | 不包含 | `expectedNotContains` |
| `5` | 长度 | `expectedLength`、`expectedMinLength`、`expectedMaxLength` |
| `6` | 正则 | `expectedRegex` |
| `7` | 数值范围 | `expectedMin`/`expectedMax`，或 Excel J/K 下限/上限 |
| `8` | DTC 校验 | DTC 黑名单或 DTC 状态位校验 |
| `9` | 日期校验 | `valueType=date`；严格匹配 `dateFormat`、验证真实日历日期及可选 `expectedMinDate`/`expectedMaxDate` 区间 |

有多个校验条件时仍按 AND 规则判定，但只返回一个主类型码。值校验优先于报文匹配；DTC 校验返回 `8`，日期解码及其可选日期区间校验返回 `9`，仅当配置数值范围（`expectedMin`/`expectedMax`、Excel J/K 或函数下限/上限参数）时才返回 `7`。没有值校验、DTC 校验或日期校验时，即使有测量值，也返回报文匹配类型 `1`。

多测量项时，`ReturnValue0` 按测量项返回小写布尔文本并用 `|` 对齐，例如 `true|false|true`；单测量项仍直接返回布尔值。脚本应先逐项检查多项结果，再汇总为整体布尔值。各测量项的独立结果也保存在 `Result_step_<step>_<index>_Passed` / `_Status` 中，测量值、单位和上下限则分别通过 `ReturnValue4`、`ReturnValue5`、`ReturnValue6` 用 `|` 对齐返回。

**`DoIP_UDS_ExcelRunAll` 返回值：**

| 返回 | 含义 |
|---|---|
| `ReturnValue0` | 通过数 |
| `ReturnValue1` | 失败数 |
| `ReturnValue2` | 运行总数 |
| `ReturnValue3` | 整表判定：PASS / FAIL |
| `ReturnValue4` | 首个失败错误信息 |

**`DoIP_UDS_Step` / `DoIP_UDS_ExcelStep` 返回值约定（精简固定 7 个，配置驱动与 Excel 直读一致）：**

| 返回 | 含义 |
|---|---|
| `ReturnValue0` | **结果（布尔）**：`true`=PASS，`false`=FAIL/ERROR（UDS 收发 + 值校验综合） |
| `ReturnValue1` | **名称**：单测量项 = Excel E 列测试项名（无则解码测量名/G 列 `testItem`）；多测量项 = 各测量项名 `\|` 分隔（与 4/5/6 按位置对齐） |
| `ReturnValue2` | **UDS 请求** HEX（多命令 `\|` 分隔） |
| `ReturnValue3` | **UDS 响应** HEX（`dataFrom` 选中；mismatch 时保留实际收到的响应） |
| `ReturnValue4` | **测试值**（解码工程值；多测量项 `\|` 分隔） |
| `ReturnValue5` | **单位**（多测量项 `\|` 分隔） |
| `ReturnValue6` | **上下限** `下限~上限`（多测量项 `\|` 分隔；per-item 校验键优先，否则 J/K 或 G 列 `expectedMin/Max`） |

> **多测量项示例**：某步骤解出 转速=250、状态=1 两个测量项 →
> `ReturnValue1 = "转速|状态"`，`ReturnValue4 = "250|1"`，`ReturnValue5 = "rpm|"`，`ReturnValue6 = "0~8000|"`（4/5/6 用 `\|` 分隔、位置一一对应，1 是各自的名称）。
> **无解码**的步骤（如会话控制 `10 01`）→ `ReturnValue4/5/6` 为空，响应 HEX 在 `ReturnValue3`，这是"没有测量值"，不是结构缺失。
> **失败原因**（如 mismatch 期望/实际、超时等）通过状态栏打印（`UpdateStatusTextFromUpdateStatus`），不进返回值。
> 不再区分配置驱动/Excel 直读：所有字段固定位置，Excel 直读时测试项名称/上下限自动来自 E/J/K 列。

**失败处理策略（ZLG 风格，重要）：**
本库所有函数**都不会**因步骤失败而终止 JEMSts 脚本——真实成败状态通过 `$result`（`ReturnValue0`）暴露，由脚本自行判断（继续 / 停机 / 记录）。这与现有 `ZUDS_Request` 的用法一致：

```text
($result,$testItem,$req,$resp,$value,$unit,$limits) = DoIP_UDS_Step("...", "...", "DoIP_ECU")
if($result == true)   ; 布尔：true=PASS，false=FAIL/ERROR
{
    UpdateStatus("OK: " + $testItem + " = " + $value + " " + $unit + " [" + $limits + "]")
}
else
{
    UpdateStatus("FAILED: " + $testItem)   ; 原因在状态栏；不会终止，可继续处理
}
```

**状态打印通道：**
- **关键信息**（步骤判定、错误、关键测量值、ExcelRunAll 汇总）→ `UpdateStatusTextFromUpdateStatus`（与 JEMSts 自己 `FailTest` 相同的通道，除 verbosity=0 外都记录，保证重要信息不丢失）；
- **详细报告**（多行 request/response/解码明细）→ `UpdateStatusText`（verbose 通道）。

**DoIP 网关地址（host）解析优先级**（`DoIP_UDS_ExcelStep` / `DoIP_UDS_ExcelRunAll`）：
1. **工步 G 列（配置参数1）的 `host=` / `ip=`** —— Sheet1 自包含，推荐方式（EonTest 同样认这个键，两边通用）；
2. **设备通道映射表（Sheet2）** —— 若共享 Excel 恰好有该表（如 EonTest 的 `real-device-acceptance.xlsx`），按设备名自动回填 `host`/`port`/`targetAddress`；
3. **默认 host 参数** —— `DoIP_UDS_ExcelStep(excel, sheet, 工步号, ECU键, 默认host)` 或 `DoIP_UDS_ExcelRunAll(excel, sheet, maxSteps, ECU键, 默认host)`，每站一个网关 IP 时用。

> 只共享 Sheet1、没有 Sheet2 时：在**第一条 DoIP 步骤的 G 列**写上 `host=<网关IP>` 即可；
> 若三处都没有 host，会快速报"host is empty..."明确错误（而不是 TCP 超时）。

---

## G 列（配置参数1）支持的键（与 EonTest `DoipMasterPlugin` 一致）

| 键 | 说明 | 默认 |
|---|---|---|
| `host` / `ip` | DoIP 网关地址 | —（必填，或 Argument3） |
| `port` | DoIP 端口 | 13400 |
| `sourceAddress` / `targetAddress` / `activationType` | DoIP 地址（`0x` 前缀十六进制，否则十进制） | 0x102D / 0x1008 / 0x00 |
| `udsPayload` | 单条 UDS 指令，支持 `{pad:N}` `{retry:N}` | — |
| `udsSequence` | 多条指令，`\|` 分隔 | — |
| `expectedPrefix` / `expectedResponse` | 每条指令的返回匹配（`\|` 与指令一一对应） | — |
| `dataFrom` | -1=取最后一条，N=取第 N 条 | -1 |
| `connectionMode` | `session`（复用连接）/ `perStep` | session |
| `minRequestIntervalMs` | 相邻请求最小间隔（0x08 教训：≥20ms） | 20 |
| `doipNackRetryCount` / `doipNackRetryDelayMs` | NACK 0x08 重试 | 2 / 100 |
| `sequenceRetryCount` / `sequenceRetryDelayMs` | 整序列从第一条命令开始执行的总次数/间隔 | 1 / 500 |
| `decodeRetryCount` / `decodeRetryDelayMs` | 解码值校验失败后的整序列重试（CAN） | 0 / 500 |
| `canMinFrameLength` | CAN-FD 最小帧长度；小帧按此长度发送，大帧自动选择更大的合法长度 | 0（自动） |
| `removeSpaces` | ASCII 字符串解码后删除所有 `0x20` 空格；只影响最终值，不改变 RawValue | false |
| `timeoutMs` / `timeout` | 请求超时 | 5000 |
| `closeConnection` | 发完是否断开 | false |
| `udsKeepAliveEnabled` / `udsKeepAliveIntervalMs` / `udsKeepAlivePayload` | 3E 保活 | off / 2000 / `3E 80` |
| `tcpKeepAlive` / `localBindAddress` | TCP 细节 | true / — |
| `virtualMode` / `simResponse` | 虚拟模式（无硬件调试） | off / `50 01` |
| `testItem` / `lowerLimit` / `upperLimit` / `unit` | 步骤元信息 | — |
| `expectedValue` | 唯一正确值（字符串/状态/数值，无上下限时用；解码后相等才 PASS） | — |

---

## P 列（解码配置）：解码 + 期望值 / 校验都写在这里

> **P 列既放解码规则，也放期望值/校验键**——都跟着测量项走，一个 Step 可多个测量项、每个各有自己的期望值。G 列只放 UDS 收发配置（`udsPayload` / `expectedPrefix` / host 等）。

```text
; 1) 单测量项：直接 key=value（解码 + 期望值/校验混在一起写）
valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4,unit=rpm,expectedMin=0,expectedMax=8000

; 2) 多测量项：JSON 数组（每个测量项对象里写自己的解码 + 期望值/校验键）
[{"name":"转速","offset":3,"length":2,"type":"uint16","endian":"big","formula":"x/4","unit":"rpm","expectedMin":0,"expectedMax":8000},
 {"name":"状态","offset":7,"length":1,"type":"uint8","expectedValue":"0x01"}]

; 3) 引用解码 Profile 文件（Profile 里每个 measurement 同样可带 expectedXxx）
{"decodeProfile":"DoIP_DID_F130.json"}
```

支持的 `valueType`：`hex` `uint8..64` `int8..64` `float32` `float64` `string` `bits` `date`（及简写 `uint`/`int`/`float`）。
解码字段：`endian`（big/little）、`encoding`（ascii/utf8/utf16）、日期的 `dateEncoding`（bcd/ascii）和 `dateFormat`、`bitOffset`/`bitLength`、`extractRegex`+`postType`、`formula`（安全公式：`x + - * / % ( ) abs min max round floor ceil`）、`trim`。
校验键（可写在每项，多键 AND）：`expectedValue` / `expectedContains` / `expectedNotContains` / `expectedStartsWith` / `expectedEndsWith` / `expectedRegex` / `expectedMinLength` / `expectedMaxLength` / `expectedMin` / `expectedMax` / `expectedCaseSensitive`（详见下节）。

---

## 期望值 / 校验：放 P 列（跟着测量项走，推荐）

> **推荐：解码规则和期望值/校验键都写在 P 列**——一个 Step 可有多个测量项，每个测量项有自己的解码 + 期望值/上下限/校验。

**① 原始报文匹配**（EonTest 原生，写 G 列）：`expectedResponse=<正确响应HEX>`（精确）或 `expectedPrefix=<响应前缀HEX>`（前缀）

例：`udsPayload=22 F1 87,expectedResponse=62 F1 87 53 57 5F 56 45 52 5F 31 2E 30`（ASCII "SW_VER_1.0"）；状态码：`udsPayload=31 01 02 01,expectedPrefix=71 01 02 01`

**② 解码后的值匹配（期望值放 P 列）**：

| P 列写法 | 说明 |
|---|---|
| `valueType=string,encoding=ascii,trim=true,expectedValue=SW_VER_1.0` | 解码字符串 + 期望相等 |
| `valueOffset=3,valueLength=1,valueType=uint8,expectedValue=0x00` | 解码状态码 + 期望相等（`0x` 数值也认） |
| `valueType=string,encoding=ascii,trim=true,expectedContains=VER,expectedRegex=[A-Z]{1,5}_[0-9]{1,3},expectedMinLength=6` | 解码 + 综合校验（多键 AND） |

**③ 多测量项**：P 列 JSON 数组，每个测量项对象里写自己的解码 + 期望值/校验键（见下节）。

**P 列校验键清单**（单测量项 inline / 多测量项 JSON 每项均可写，全部 AND）：

| 键 | 说明 | 默认 |
|---|---|---|
| `expectedValue` | 相等（数值可带 `0x` 前缀；字符串精确） | — |
| `expectedContains` | 包含子串 | — |
| `expectedNotContains` | 不包含子串 | — |
| `expectedStartsWith` | 以...开头 | — |
| `expectedEndsWith` | 以...结尾 | — |
| `expectedRegex` | 正则匹配（`[0-9]{1,5}` 含逗号/花括号也能正确解析） | — |
| `expectedMinLength` / `expectedMaxLength` | 字符串长度下限/上限 | — |
| `expectedMin` / `expectedMax` | 数值下限/上限（需能转数值，否则 FAIL） | — |
| `expectedCaseSensitive` | 大小写敏感（影响 相等/包含/前缀/后缀/正则） | true |

> 多个键 = **AND**：全部满足才 PASS，任一不满足即 FAIL。
> 多测量项时每个测量项各用各的校验键；没配 P 列解码时校验键作用于原始响应 HEX 文本。

**G 列校验键（兼容，仅作用于第一个解码值）**：`expectedValue` / `expectedMin` / `expectedMax` / `expectedContains` 等仍可写在 G 列（配置参数1），单测量项、不想用 P 列时可用；只要 G 列写了任一校验键，Excel J/K 列就被忽略（二选一，不混用）。多测量项请用 P 列 per-measurement 写法。

**G 列 `expectedMin/expectedMax` 与 Excel J/K 列（二选一）**：

| 写法 | 作用对象 | 说明 |
|---|---|---|
| G 列 `expectedMin=0,expectedMax=8000` | 第一个解码值 | 优先：G 列写了任一校验键则 J/K 不参与；配置驱动 `DoIP_UDS_Step` 无 Excel 行时也能用 |
| Excel J/K 列 | 第一个解码值 | 仅当 G 列没有任何校验键时回退生效；纯数值区间 |

---

## 多测量项：P 列每个测量项内嵌校验键（per-measurement）

> 适用：一个步骤里解出多个值，**每个值各自判断**（J/K 只有一对，做不了）。

P 列 JSON 数组里，**每个测量项对象写自己的解码 + 期望值/校验键**，所有测量项 **AND**：全部通过才 PASS，任一不通过即 FAIL（该项没写校验键就跳过，保持"没写不判"）。

```json
[
  {"name":"转速","offset":3,"length":2,"type":"uint16","endian":"big","formula":"x/4","unit":"rpm","expectedMin":0,"expectedMax":8000},
  {"name":"电压","offset":5,"length":2,"type":"uint16","endian":"big","formula":"x/100","unit":"V","expectedMin":9,"expectedMax":15},
  {"name":"状态","offset":7,"length":1,"type":"uint8","expectedValue":"0x01"}
]
```

- **每测量项可带自己的期望值/校验**（`expectedValue` / `expectedMin` / `expectedMax` / `expectedContains` / `expectedRegex` 等），全部 AND：全过才 PASS，任一不通过即 FAIL（该项没写校验键就跳过）。
- 判定返回：`ReturnValue0`（布尔 true=PASS / false=FAIL）、`ReturnValue1`（各测量项名 `\|` 分隔）、`ReturnValue4/5/6`（值/单位/上下限 `\|` 分隔、按位置对齐）。
- 可与 G 列校验键同时用（AND）：G 列作用于第一个值，P 列 per-item 作用于各自测量项。
- EonTest 侧由 `ResponseDecoder` 在解码阶段逐项校验（`resultItems[i].passed`），`ValueAnalyzer` 读取后合并成最终 verdict——同一张表两端行为一致。

**代码怎么判断**：
- **① 原始报文匹配**：`UdsRequestExecutor` 对响应字节做精确/前缀比对（`expectedResponse`/`expectedPrefix`），不匹配 → `ReturnValue0=false`、状态栏打印 `UDS response mismatch`。
- **② P 列期望值/校验**：`ResponseDecoder` 解码后对每个测量项做值校验（相等/包含/正则/长度/区间），任一失败 → `ReturnValue0=false`；`ReturnValue1` 是测量项名列表，`ReturnValue4/5/6` 是值/单位/上下限。

---

## 自测（无需 JEMSts / 无需硬件）

```powershell
dotnet run --project UdsConsoleHarness\UdsConsoleHarness.csproj
```

覆盖：hex / G 列解析 / 公式 / 解码（含 EonTest 文档示例 62 F1 05 0B B8 → 750 rpm）/ Profile 文件 / 真实 EonTest Excel 读取 / 虚拟 DoIP 步骤。
自测里会直接读 `E:\SourceCode\EonTest\Workflows\real-device-acceptance.xlsx` 验证 Excel 直读（找不到则跳过该项）。

---

## 说明

- **float32/64 字节序**：`BitConverter` 按本机小端解释，大端线上字节先反转（EonTest C++ 原版对大端 float 存在潜在错误，此处按数学正确语义实现）。
- 硬件的 DoIP 联调需要真实网关/ECU；`virtualMode=true` 可先在无硬件环境验证配置与解码规则.
