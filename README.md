# SmartStudyAgent

基于 .NET 8 的智能学习助手 Agent 系统。项目面向课程期末作业要求，包含 LLM 集成、Agent Loop、工具调用、记忆机制、课程资料上传和 Web 页面交互。

## 当前功能

- LLM 服务：默认 `Mock` 模式，可切换 OpenAI-compatible Chat Completions API。
- Agent Loop：展示 `Thought -> Action -> Observation -> Final Answer` 执行轨迹。
- Tools：已实现 4 个自定义工具。
  - `search_materials`：检索课程资料。
  - `summarize_material`：总结资料。
  - `generate_quiz`：生成练习题。
  - `create_study_plan`：制定学习计划。
- Memory：按 `sessionId` 保存对话上下文。
- 文件上传：支持 `.pdf`、`.pptx`、`.txt`、`.md`。
- Web 页面：支持上传资料、聊天、查看 ReAct 执行步骤。

## 运行项目

```powershell
$env:DOTNET_CLI_HOME="D:\SmartStudyAgent\.dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --launch-profile http
```

打开：

```text
http://localhost:5153
```

如果提示文件被占用，说明旧后端还在运行。先在旧终端按 `Ctrl + C`，或者执行：

```powershell
Stop-Process -Id 进程号
```

## 配置 VectorEngine / OpenAI-Compatible API

不要把 API Key 写进代码。建议使用环境变量：

```powershell
$env:Llm__Provider="OpenAI"
$env:Llm__BaseUrl="https://api.vectorengine.ai/v1"
$env:Llm__Model="gpt-4o-mini"
$env:Llm__ApiKey="你的 API Key"
dotnet run --launch-profile http
```

系统会自动调用：

```text
https://api.vectorengine.ai/v1/chat/completions
```

也可以直接配置完整 Endpoint：

```powershell
$env:Llm__Provider="OpenAI"
$env:Llm__Endpoint="https://api.vectorengine.ai/v1/chat/completions"
$env:Llm__Model="gpt-4o-mini"
$env:Llm__ApiKey="你的 API Key"
dotnet run --launch-profile http
```

## API 示例

查看资料：

```powershell
Invoke-RestMethod http://localhost:5153/api/materials
```

和 Agent 对话：

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5153/api/agent/chat `
  -ContentType "application/json" `
  -Body '{"message":"请总结刚上传的课件内容","sessionId":"demo"}'
```

查看接口说明：

```text
http://localhost:5153/api/info
```

## 答辩说明重点

- 说明系统为什么是 Agent，而不是普通聊天机器人。
- 解释 `StudyAgent.RunAsync` 中的 Agent Loop。
- 说明工具如何注册和调用。
- 说明 Memory 如何保存对话上下文。
- 说明上传资料后如何由工具检索、总结、出题和制定学习计划。
- 如实说明 AI 辅助开发范围，以及自己做过哪些修改和理解。
