# 反思报告草稿

## 1. Agent 内部工作原理

本项目中的 Agent 由 LLM、Agent Loop、Memory 和 Tools 组成。LLM 负责语言理解和回答生成；Agent Loop 负责控制执行流程；Memory 保存会话上下文；Tools 负责执行具体任务，例如资料检索、总结、出题和学习计划生成。

## 2. ReAct 模式说明

ReAct 是 Reasoning and Acting 的缩写。本项目中每次用户提问后，Agent 会先产生 Thought，判断用户意图；然后执行 Action，调用一个合适的工具；工具返回 Observation；最后 Agent 综合记忆和工具结果生成回答。

## 3. 核心循环代码说明

核心代码在 `Agents/StudyAgent.cs` 的 `RunAsync` 方法中：

1. 生成或读取 `sessionId`。
2. 将用户消息写入 `ConversationMemory`。
3. 根据用户问题调用 `PlanNextAction` 选择工具。
4. 通过 `StudyToolRegistry.ExecuteAsync` 执行工具。
5. 将工具结果保存为 `AgentStep`。
6. 调用 `BuildFinalAnswerAsync` 生成最终回答。
7. 将回答写入记忆并返回给前端。

## 4. AI 工具使用说明

本项目允许使用 AI 辅助开发，但答辩时必须能解释代码。建议在最终报告中如实记录：

- 哪些后端文件由 AI 辅助生成；
- 自己如何修改配置、运行和测试；
- 自己是否理解 Agent Loop、Tool Calling、Memory 和 LLM 调用。

## 5. 后续改进方向

- 增加前端页面；
- 支持 PDF 上传和解析；
- 引入向量数据库实现 RAG；
- 为核心服务编写单元测试；
- 增加多 Agent 协作。
