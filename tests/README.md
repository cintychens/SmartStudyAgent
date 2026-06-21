# SmartStudyAgent Tests

这里放项目的基础自测脚本，方便答辩或提交前验证主要接口是否还能正常工作。

## 冒烟测试

先启动项目：

```powershell
$env:DOTNET_CLI_HOME="D:\SmartStudyAgent\.dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --launch-profile http
```

然后另开一个 PowerShell 窗口运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke-test.ps1
```

脚本会检查：

- 后端信息接口 `/api/info`
- 课程资料接口 `/api/materials`
- Agent 问答接口 `/api/agent/chat`
