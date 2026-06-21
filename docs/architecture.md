# 架构设计文档

## 1. 项目定位

SmartStudyAgent 是一个基于 .NET 8 的智能学习助手 Agent 系统。它面向学习场景，支持课程资料管理、资料检索、内容总结、练习题生成和学习计划制定。

## 2. 核心组件

```text
User
  |
  v
ASP.NET Core Web API
  |
  v
StudyAgent
  |
  +--> ConversationMemory
  |
  +--> StudyToolRegistry
  |      +--> search_materials
  |      +--> summarize_material
  |      +--> generate_quiz
  |      +--> create_study_plan
  |
  +--> ILlmService
         +--> Mock / OpenAI-compatible API
```

## 3. Agent Loop

```text
1. 接收用户问题
2. 写入短期记忆
3. Thought：分析用户意图
4. Action：选择并调用工具
5. Observation：读取工具返回结果
6. 调用 LLM 生成最终回答
7. 将回答写入记忆
```

当前实现采用单步 ReAct，适合课程项目演示。后续可以扩展为多步规划。

## 4. 工具设计

| 工具 | 作用 |
| --- | --- |
| `search_materials` | 检索本地课程资料并返回片段 |
| `summarize_material` | 对资料进行总结 |
| `generate_quiz` | 根据资料生成练习题 |
| `create_study_plan` | 根据学习目标制定学习计划 |

## 5. 记忆设计

当前实现短期对话记忆，按 `sessionId` 保存用户和助手消息。这样用户连续追问时，Agent 可以拿到最近的上下文。

## 6. LLM 设计

`ILlmService` 抽象了模型调用。默认 `Mock` 模式保证没有 API Key 也能演示后端流程。配置真实 API 后，会通过 OpenAI-compatible Chat Completions 接口调用模型。
