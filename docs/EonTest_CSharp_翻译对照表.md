# EonTest C++ → C# 翻译对照表

> 移植依据：EonTest 源码 + 统一通信响应解码与测量配置架构实施方案.md。
> 本表列出每个能力在两侧的落点，便于评审与回归。

## 1. DoIP 协议层

| 能力 | EonTest (C++) | JEMSts_UDS_Parser (C#) |
|---|---|---|
| DoIP 常量（版本/载荷类型/NACK 码） | `Infrastructure/include/eon/infra/DoipProtocolLayer.h`（`Doip` 命名空间） | `UdsCore/DoipConstants.cs` |
| 组帧（header/路由激活/诊断消息） | `DoipProtocolLayer.cpp::buildDoipHeader/buildRoutingActivationRequest/buildDiagnosticMessage` | `UdsCore/DoipClient.cs`（`BuildDoipHeader`/`BuildRoutingActivationRequest`/`BuildDiagnosticMessage`） |
| 读帧（先读 8 字节头再读载荷） | `DoipProtocolLayer.cpp::receiveTcpFrame` | `UdsCore/DoipClient.cs::ReceiveTcpFrame/ReadExact` |
| 路由激活 + 响应码校验 | `DoipProtocolLayer.cpp::activateRouting` | `UdsCore/DoipClient.cs::ActivateRouting` |
| 诊断消息 + ACK/NACK + 0x78 延长超时 | `DoipProtocolLayer.cpp::sendDiagnosticMessage` | `UdsCore/DoipClient.cs::SendDiagnosticMessage` |
| 保活（3E 80 只发不读 / 3E 00 读丢） | `DoipProtocolLayer.cpp::sendKeepAliveOnly` | `UdsCore/DoipClient.cs::SendKeepAliveOnly` |
| 发送前清残留帧 | `DoipProtocolLayer.cpp`（`flushReceiveBuffer`） | `UdsCore/DoipClient.cs::FlushReceiveBuffer` |

## 2. 步骤执行（DoipMasterPlugin）

| 能力 | EonTest | C# |
|---|---|---|
| 配置键读取（host/port/address/…) | `DoipMasterPlugin.cpp::executeStep` 起始段 | `UdsProtocol/UdsStepConfig.cs` |
| 命令列表（udsSequence 管道 / udsPayload） | `DoipMasterPlugin.cpp`（commands 构建） | `UdsEngine/UdsTransactionExecutor.cs::BuildCommands` |
| 内联标注 `{pad:N}{retry:N}` | `DoipMasterPlugin.cpp`（brace 解析） | `UdsEngine/UdsTransactionExecutor.cs::ParseInlineAnnotations` |
| 序列重试 + 逐命令重试 | `DoipMasterPlugin.cpp`（seqRetry/cmdRetry） | `UdsCore/UdsRequestExecutor.cs::ExecuteStep`（DoIP session 专属） |
| NACK 0x08 重连 + 回放会话控制 | `DoipMasterPlugin.cpp`（`_doip.recoveryActive`） | `UdsCore/UdsRequestExecutor.cs::RecoverSession` |
| expectedPrefix/expectedResponse 匹配 | `DoipMasterPlugin.cpp::matchesExpectedResponse` | `UdsEngine/UdsTransactionExecutor.cs::MatchesExpectedResponse` |
| 31 01 失败先发 31 02 停止 | `DoipMasterPlugin.cpp` | `UdsCore/UdsRequestExecutor.cs`（DoIP session 专属） |
| dataFrom 选响应 | `DoipMasterPlugin.cpp` | `UdsEngine/UdsTransactionExecutor.cs` |
| 会话池（session/perStep） | `DoipMasterPlugin.cpp`（`sessions_` map） | `UdsCore/DoipSessionPool.cs` |
| 空闲保活 | `DoipMasterPlugin.cpp::sendIdleKeepAlive` | `UdsCore/DoipSessionPool.cs::SendIdleKeepAlive` |

## 3. 解码

| 能力 | EonTest | C# |
|---|---|---|
| DecodeSpec 模型 | `Infrastructure/include/eon/infra/ResponseDecoder.h` | `ExcelDecodeCore/DecodeModel.cs` |
| 解码引擎（类型/字节序/位域/字符串/公式） | `Infrastructure/src/ResponseDecoder.cpp::decode` | `ExcelDecodeCore/ResponseDecoder.cs::Decode` |
| 多测量项 | `ResponseDecoder.cpp::decodeMany` | `ExcelDecodeCore/ResponseDecoder.cs::DecodeMany` |
| 安全公式 | `ResponseDecoder.cpp::FormulaParser` | `ExcelDecodeCore/FormulaEvaluator.cs` |
| extractRegex + postType | `ResponseDecoder.cpp::decode` | `ExcelDecodeCore/ResponseDecoder.cs::Decode` |

## 4. 配置与 Excel

| 能力 | EonTest | C# |
|---|---|---|
| G 列 key=value 解析 | `Infrastructure/src/XlsxParser.cpp::parseConfigPairs` | `ExcelDecodeCore/ConfigPairParser.cs` |
| P 列（JSON 数组/对象/内联） | `Infrastructure/src/XlsxParser.cpp` + `DoipMasterPlugin.cpp::loadDecodeProfile` | `UdsProtocol/UdsStepConfig.cs::FromColumns` + `UdsEngine/UdsTransactionExecutor.cs` |
| XLSX 读取 | `Infrastructure/src/XlsxParser.cpp`（QXlsx） | `ExcelDecodeCore/MiniXlsx.cs`（无依赖） |
| 测试工步表列识别 | `XlsxParser.cpp`（固定列 + 表头探测） | `ExcelDecodeCore/ExcelStepReader.cs`（纯表头识别） |

## 5. 已知差异（有意为之）

- **float32/64 字节序**：`BitConverter` 按本机小端解释；大端线上字节先反转。EonTest C++ 原版 `if(little) reverse` 对大端 float 是反向的（潜在 bug），此处按正确语义实现。
- **虚拟模式解码**：`virtualMode=true` 时 EonTest 直接返回不解码；本库在虚拟模式也执行解码，便于无硬件验证解码规则。

## 6. 程序集分层结果

本次 C# 重构将原先混在 DoIP 项目中的通用 UDS 事务执行逻辑独立为 `Jabil.Uds.Engine.dll`。最终依赖方向是：

```text
Jabil.Uds.Shared -> Jabil.Uds.Protocol -> Jabil.Uds.Engine
										   ├─ Jabil.Uds.DoIp
										   ├─ Jabil.Uds.Can
										   └─ Jabil.Uds.Lin（未来）
```

`UdsEngine` 通过 `IUdsTransport` 接收具体传输，因此不引用 DoIP TCP、ZLG CAN 或任何厂商 native DLL。`Jabil.Uds.DoIp.dll` 继续提供旧 `UdsRequestExecutor` 名称，以兼容 JEMS/JabilTest 脚本；其通用 transport overload 转发到 Engine，session overload 保留 DoIP 特有的连接复用、路由激活、保活、NACK 0x08 恢复和 `31 02` 清理策略。

CAN/CAN-FD/ISO-TP 由 `Jabil.Uds.Can.dll` 提供，ZLG 实现位于 `Jabil.Uds.Can.Zlg.dll`。未来 LIN 应沿同一方向引用 Engine，而不能引用 DoIP。
