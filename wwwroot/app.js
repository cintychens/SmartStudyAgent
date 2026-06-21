let sessionId = localStorage.getItem("smartStudy.currentSessionId") ?? `web-${Date.now()}`;
localStorage.setItem("smartStudy.currentSessionId", sessionId);

const materialsList = document.querySelector("#materialsList");
const uploadForm = document.querySelector("#uploadForm");
const uploadTitle = document.querySelector("#uploadTitle");
const materialFile = document.querySelector("#materialFile");
const refreshMaterials = document.querySelector("#refreshMaterials");
const messages = document.querySelector("#messages");
const chatForm = document.querySelector("#chatForm");
const chatInput = document.querySelector("#chatInput");
const steps = document.querySelector("#steps");
const clearChat = document.querySelector("#clearChat");
const sessionSelect = document.querySelector("#sessionSelect");
const newSession = document.querySelector("#newSession");
const connectionDot = document.querySelector("#connectionDot");
const connectionText = document.querySelector("#connectionText");
const materialCount = document.querySelector("#materialCount");
const messageCount = document.querySelector("#messageCount");
const toolCount = document.querySelector("#toolCount");
const agentCount = document.querySelector("#agentCount");
const memoryStatus = document.querySelector("#memoryStatus");
const moduleItems = document.querySelectorAll(".module-item");
const workspace = document.querySelector(".workspace");
const toggleMaterialPicker = document.querySelector("#toggleMaterialPicker");
const selectedMaterials = document.querySelector("#selectedMaterials");
const materialPicker = document.querySelector("#materialPicker");
const materialPickerList = document.querySelector("#materialPickerList");
const closeMaterialPicker = document.querySelector("#closeMaterialPicker");
const cancelMaterialPicker = document.querySelector("#cancelMaterialPicker");
const saveMaterialPicker = document.querySelector("#saveMaterialPicker");
const selectAllMaterials = document.querySelector("#selectAllMaterials");
const clearSelectedMaterials = document.querySelector("#clearSelectedMaterials");
const agentDecision = document.querySelector("#agentDecision");
const toolHistory = document.querySelector("#toolHistory");
const learningHistory = document.querySelector("#learningHistory");
const clearLearningHistory = document.querySelector("#clearLearningHistory");
const knowledgeTags = document.querySelector("#knowledgeTags");

let messageTotal = 0;
let cachedMaterials = [];
const selectedMaterialIds = new Set();
const pendingMaterialIds = new Set();
const AGENT_NAMES = ["CoordinatorAgent", "MaterialAgent", "PracticeAgent", "PlanningAgent", "InsightAgent"];
const TOOL_NAMES = ["search_materials", "summarize_material", "generate_quiz", "create_study_plan", "list_materials", "extract_learning_points"];
const TOOL_HISTORY_KEY = "smartStudy.toolHistory";
const LEARNING_HISTORY_KEY = "smartStudy.learningHistory";
const SELECTED_MATERIALS_KEY = "smartStudy.selectedMaterialIds";

for (const id of readStoredList(SELECTED_MATERIALS_KEY)) {
    selectedMaterialIds.add(id);
}

if (toolCount) {
    toolCount.textContent = `${TOOL_NAMES.length} 个`;
}

if (agentCount) {
    agentCount.textContent = `${AGENT_NAMES.length} 个`;
}

if (memoryStatus) {
    memoryStatus.textContent = "ON";
}

function addMessage(role, content) {
    removeWelcomeCard();

    const row = document.createElement("div");
    row.className = `message-row ${role}`;

    const avatar = document.createElement("div");
    avatar.className = "avatar";
    avatar.textContent = role === "user" ? "你" : "S";

    const bubble = document.createElement("div");
    bubble.className = "message";
    bubble.textContent = role === "assistant" ? formatAssistantText(content) : content;

    if (role === "user") {
        row.append(bubble, avatar);
    } else {
        row.append(avatar, bubble);
    }

    messages.appendChild(row);
    messages.scrollTop = messages.scrollHeight;
    messageTotal++;
    updateMessageCount();

    return bubble;
}

function updateMessageBubble(bubble, content) {
    bubble.textContent = formatAssistantText(content);
    messages.scrollTop = messages.scrollHeight;
}

function formatAssistantText(content) {
    return String(content ?? "")
        .replace(/\r\n/g, "\n")
        .replace(/([^\n])(\s*(?:#{2,6}\s+))/g, "$1\n\n$2")
        .replace(/([^\n])(\s*\d+[.、]\s+)/g, "$1\n\n$2")
        .replace(/([^\n])(\s*[一二三四五六七八九十]+[、.]\s+)/g, "$1\n\n$2")
        .replace(/([^\n])(\s*(?:答案|解析|任务|输出|复习检查点|难点|复习建议)[:：])/g, "$1\n$2")
        .replace(/\n{3,}/g, "\n\n")
        .trimStart();
}

function renderWelcomeCard() {
    messages.innerHTML = `
        <section class="welcome-card">
            <div class="welcome-icon">S</div>
            <h3>你好，我是 SmartStudyAgent</h3>
            <p>上传课程资料后，我可以围绕资料完成学习支持任务。</p>
            <div class="prompt-grid">
                <button type="button" data-prompt="请总结刚上传的课件内容">总结知识点</button>
                <button type="button" data-prompt="这个文档的重点和难点是什么">回答问题</button>
                <button type="button" data-prompt="根据这份资料生成 5 道练习题">生成练习题</button>
                <button type="button" data-prompt="根据这份资料制定三天学习计划">制定学习计划</button>
                <button type="button" data-prompt="提取这份资料的关键词和复习提纲">学习提纲</button>
                <button type="button" data-prompt="列出当前已有资料">资料列表</button>
            </div>
        </section>
    `;
}

function removeWelcomeCard() {
    const welcome = messages.querySelector(".welcome-card");
    if (welcome) {
        welcome.remove();
    }
}

function updateMessageCount() {
    messageCount.textContent = `${messageTotal} 条`;
}

function getStoredSessions() {
    const raw = localStorage.getItem("smartStudy.sessions");
    const sessions = raw ? JSON.parse(raw) : [];
    if (!sessions.some((session) => session.id === sessionId)) {
        sessions.unshift({ id: sessionId, name: `会话 ${sessions.length + 1}` });
        localStorage.setItem("smartStudy.sessions", JSON.stringify(sessions));
    }

    return sessions;
}

function renderSessionSelect() {
    const sessions = getStoredSessions();
    sessionSelect.innerHTML = "";
    for (const session of sessions) {
        const option = document.createElement("option");
        option.value = session.id;
        option.textContent = session.name;
        option.selected = session.id === sessionId;
        sessionSelect.appendChild(option);
    }
}

function setConnectionState(isConnected) {
    connectionDot.classList.toggle("offline", !isConnected);
    connectionText.textContent = isConnected ? "后端已连接" : "后端未连接";
}

function setBusy(isBusy) {
    chatInput.disabled = isBusy;
    chatForm.querySelector("button").disabled = isBusy;
}

async function loadMaterials() {
    materialsList.innerHTML = `<div class="empty-state">正在加载资料...</div>`;

    try {
        const response = await fetch("/api/materials");
        const data = await response.json();
        const materials = Array.isArray(data) ? data : data.value ?? [];
        cachedMaterials = materials;
        renderMaterialPicker();
        setConnectionState(true);
        materialCount.textContent = `${materials.length} 份`;

        if (materials.length === 0) {
            renderKnowledgePanel([]);
            materialsList.innerHTML = `<div class="empty-state">还没有课程资料，请先上传文件。</div>`;
            return;
        }

        materialsList.innerHTML = "";
        for (const material of materials) {
            const item = document.createElement("article");
            item.className = "material-item";
            item.innerHTML = `
                <div class="material-actions">
                    <button class="preview-material" data-id="${escapeHtml(material.id)}" type="button" title="预览资料">⌕</button>
                    <button class="rename-material" data-id="${escapeHtml(material.id)}" data-title="${escapeHtml(material.title)}" type="button" title="重命名">✎</button>
                    <button class="delete-material" data-id="${escapeHtml(material.id)}" type="button" title="删除资料">×</button>
                </div>
                <div class="file-type">${escapeHtml(material.fileType ?? getMaterialType(material))}</div>
                <strong>${escapeHtml(material.title)}</strong>
                <span>${material.characterCount} 字符 · ${formatSize(material.fileSize)} · ${formatDate(material.createdAt)}</span>
            `;
            materialsList.appendChild(item);
        }
        refreshKnowledgePanel(materials);
    } catch (error) {
        setConnectionState(false);
        materialsList.innerHTML = `<div class="empty-state">资料加载失败：${escapeHtml(error.message)}</div>`;
    }
}

function renderSteps(agentSteps) {
    if (!agentSteps || agentSteps.length === 0) {
        steps.className = "timeline empty-state";
        steps.textContent = "这次回答没有调用工具。";
        return;
    }

    steps.className = "timeline";
    steps.innerHTML = "";

    for (const rawStep of agentSteps) {
        const step = normalizeAgentStep(rawStep);
        const item = document.createElement("details");
        item.className = "timeline-item";
        item.open = true;
        item.innerHTML = `
            <summary>
                <span class="timeline-dot"></span>
                <div>
                    <strong>${getTimelineTitle(step.action)}</strong>
                    <small>步骤 ${step.step}</small>
                </div>
            </summary>
            <div class="timeline-body">
                <p><b>执行 Agent：</b>${escapeHtml(step.agent || "CoordinatorAgent")}</p>
                <p><b>执行说明：</b>${escapeHtml(step.thought)}</p>
                <p><b>工具名称：</b>${escapeHtml(step.action)}</p>
                <p><b>返回摘要：</b>${escapeHtml(summarizeObservation(step.action, step.observation))}</p>
                <details class="trace-detail">
                    <summary>查看工具返回详情</summary>
                    <pre>${escapeHtml(formatAssistantText(shorten(step.observation, 1200)))}</pre>
                </details>
            </div>
        `;
        steps.appendChild(item);
    }

    const done = document.createElement("div");
    done.className = "timeline-finished";
    done.textContent = "完成回答";
    steps.appendChild(done);
}

function normalizeAgentStep(step) {
    return {
        step: step.step ?? step.Step ?? "",
        thought: step.thought ?? step.Thought ?? "",
        action: step.action ?? step.Action ?? "",
        observation: step.observation ?? step.Observation ?? "",
        agent: step.agent ?? step.Agent ?? "CoordinatorAgent"
    };
}

function summarizeObservation(action, value) {
    const text = String(value ?? "")
        .replace(/\s+/g, " ")
        .trim();

    if (!text) {
        return "工具已执行，没有返回额外文本。";
    }

    const toolName = String(action ?? "");
    if (toolName === "generate_quiz") {
        return "练习题已生成，完整题目、答案和解析已显示在左侧回答区。";
    }

    if (toolName === "summarize_material") {
        return "资料总结已完成，完整总结内容已显示在左侧回答区。";
    }

    if (toolName === "search_materials") {
        return "课程资料检索已完成，已根据相关片段组织回答。";
    }

    if (toolName === "create_study_plan") {
        return "学习计划已生成，完整计划已显示在左侧回答区。";
    }

    if (toolName === "extract_learning_points") {
        return "学习重点、难点和复习建议已整理完成。";
    }

    if (toolName === "list_materials") {
        return "课程资料列表已读取完成。";
    }

    return "工具执行完成，完整结果已显示在左侧回答区。";
}

function renderExecutionPanels(agentSteps, userQuestion) {
    if (!Array.isArray(agentSteps) || agentSteps.length === 0) {
        renderAgentDecision(null, userQuestion);
        return;
    }

    const normalizedSteps = agentSteps.map(normalizeAgentStep);
    const firstStep = normalizedSteps[0];
    renderAgentDecision(firstStep, userQuestion);

    for (const step of normalizedSteps) {
        addToolHistory({
            time: new Date().toISOString(),
            agent: step.agent || "CoordinatorAgent",
            tool: step.action || "unknown",
            status: step.observation ? "Success" : "Fail"
        });
    }

    addLearningHistory({
        time: new Date().toISOString(),
        question: userQuestion,
        agent: firstStep.agent || "CoordinatorAgent",
        tool: firstStep.action || "unknown",
        type: getTaskType(firstStep.action)
    });
}

function renderAgentDecision(step, userQuestion) {
    if (!agentDecision) {
        return;
    }

    if (!step) {
        agentDecision.className = "decision-card empty-state";
        agentDecision.textContent = "本次回答没有检测到工具调用。";
        return;
    }

    agentDecision.className = "decision-card";
    agentDecision.innerHTML = `
        <dl>
            <dt>检测到用户意图：</dt>
            <dd>${escapeHtml(getTaskType(step.action))}</dd>
            <dt>选择 Agent：</dt>
            <dd>${escapeHtml(step.agent || "CoordinatorAgent")}</dd>
            <dt>调用工具：</dt>
            <dd>${escapeHtml(step.action || "unknown")}</dd>
            <dt>完成任务：</dt>
            <dd>${escapeHtml(getCompletionText(step.action))}</dd>
        </dl>
    `;
}

function getTaskType(toolName) {
    switch (toolName) {
        case "summarize_material":
            return "总结资料";
        case "search_materials":
            return "资料问答";
        case "generate_quiz":
            return "生成练习题";
        case "create_study_plan":
            return "制定学习计划";
        case "list_materials":
            return "查看资料列表";
        case "extract_learning_points":
            return "提取知识点";
        default:
            return "学习辅助";
    }
}

function getCompletionText(toolName) {
    switch (toolName) {
        case "summarize_material":
            return "返回总结结果";
        case "search_materials":
            return "返回基于资料的回答";
        case "generate_quiz":
            return "返回练习题、答案和解析";
        case "create_study_plan":
            return "返回学习计划";
        case "list_materials":
            return "返回资料列表";
        case "extract_learning_points":
            return "返回关键词、重点和复习建议";
        default:
            return "完成当前学习任务";
    }
}

function addToolHistory(entry) {
    const items = readStoredList(TOOL_HISTORY_KEY);
    items.unshift(entry);
    localStorage.setItem(TOOL_HISTORY_KEY, JSON.stringify(items.slice(0, 20)));
    renderToolHistory();
}

function renderToolHistory() {
    if (!toolHistory) {
        return;
    }

    const items = readStoredList(TOOL_HISTORY_KEY).slice(0, 20);
    if (items.length === 0) {
        toolHistory.className = "history-list empty-state";
        toolHistory.textContent = "暂无工具调用记录。";
        return;
    }

    toolHistory.className = "history-list";
    toolHistory.innerHTML = items.map((item) => `
        <article class="history-item">
            <time>${escapeHtml(formatTimeOnly(item.time))}</time>
            <strong>${escapeHtml(item.agent)}</strong>
            <span>${escapeHtml(item.tool)}</span>
            <em class="${item.status === "Success" ? "success" : "fail"}">${escapeHtml(item.status)}</em>
        </article>
    `).join("");
}

function addLearningHistory(entry) {
    const items = readStoredList(LEARNING_HISTORY_KEY);
    items.unshift(entry);
    localStorage.setItem(LEARNING_HISTORY_KEY, JSON.stringify(items.slice(0, 50)));
    renderLearningHistory();
}

function renderLearningHistory() {
    if (!learningHistory) {
        return;
    }

    const items = readStoredList(LEARNING_HISTORY_KEY).slice(0, 50);
    if (items.length === 0) {
        learningHistory.className = "history-list empty-state";
        learningHistory.textContent = "暂无学习历史。";
        return;
    }

    learningHistory.className = "history-list";
    learningHistory.innerHTML = items.map((item) => `
        <article class="history-item learning-item">
            <time>${escapeHtml(formatDateOnly(item.time))}</time>
            <p><b>问题：</b>${escapeHtml(shorten(item.question, 72))}</p>
            <p><b>Agent：</b>${escapeHtml(item.agent)}</p>
            <p><b>Tool：</b>${escapeHtml(item.tool)}</p>
            <p><b>类型：</b>${escapeHtml(item.type)}</p>
        </article>
    `).join("");
}

function readStoredList(key) {
    const raw = localStorage.getItem(key);
    if (!raw) {
        return [];
    }

    try {
        const value = JSON.parse(raw);
        return Array.isArray(value) ? value : [];
    } catch {
        return [];
    }
}

function formatTimeOnly(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? "--:--"
        : date.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" });
}

function formatDateOnly(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? ""
        : date.toLocaleDateString("zh-CN");
}

async function refreshKnowledgePanel(materials) {
    if (!knowledgeTags) {
        return;
    }

    if (!Array.isArray(materials) || materials.length === 0) {
        knowledgeTags.className = "knowledge-tags empty-state";
        knowledgeTags.textContent = "上传资料后会自动提取关键词、核心概念和重要术语。";
        return;
    }

    knowledgeTags.className = "knowledge-tags empty-state";
    knowledgeTags.textContent = "正在分析资料知识点...";

    try {
        const previews = await Promise.all(
            materials.slice(0, 8).map(async (material) => {
                try {
                    const response = await fetch(`/api/materials/${material.id}`);
                    const data = await response.json();
                    return `${material.title}\n${data.preview ?? ""}`;
                } catch {
                    return material.title ?? "";
                }
            }));

        renderKnowledgePanel(extractKnowledgeTerms(previews.join("\n")));
    } catch {
        renderKnowledgePanel(extractKnowledgeTerms(materials.map((material) => material.title).join("\n")));
    }
}

function renderKnowledgePanel(terms) {
    if (!knowledgeTags) {
        return;
    }

    if (!Array.isArray(terms) || terms.length === 0) {
        knowledgeTags.className = "knowledge-tags empty-state";
        knowledgeTags.textContent = "暂未提取到知识点。";
        return;
    }

    knowledgeTags.className = "knowledge-tags";
    knowledgeTags.innerHTML = terms
        .slice(0, 18)
        .map((term) => `<button type="button" class="knowledge-tag" data-term="${escapeHtml(term)}">${escapeHtml(term)}</button>`)
        .join("");
}

function extractKnowledgeTerms(text) {
    const source = String(text ?? "");
    const knownTerms = [
        "Agent Loop",
        "Tool Calling",
        "Memory",
        "ReAct",
        "Semantic Kernel",
        "Streaming",
        "OCR",
        "Multi-Agent",
        "RAG",
        "Embedding",
        "Vector Store",
        "Entity Framework Core",
        "EF Core",
        "Concurrency",
        "Repository Pattern",
        "Specification Pattern",
        "Color linez",
        "C++",
        "while",
        "do-while",
        "for",
        "break",
        "continue"
    ];

    const found = knownTerms.filter((term) => source.toLowerCase().includes(term.toLowerCase()));
    const chineseTerms = Array.from(source.matchAll(/[\u4e00-\u9fa5A-Za-z0-9.+#-]{2,24}/g))
        .map((match) => match[0])
        .filter((term) => !/^\d+$/.test(term))
        .filter((term) => !["这个", "资料", "文档", "内容", "要求", "使用", "进行", "实现", "可以", "需要"].includes(term));

    const frequency = new Map();
    for (const term of chineseTerms) {
        frequency.set(term, (frequency.get(term) ?? 0) + 1);
    }

    const frequentTerms = Array.from(frequency.entries())
        .sort((a, b) => b[1] - a[1])
        .map(([term]) => term)
        .slice(0, 12);

    return Array.from(new Set([...found, ...frequentTerms])).slice(0, 18);
}

function renderMaterialPicker() {
    if (!materialPickerList || !selectedMaterials) {
        return;
    }

    const validIds = new Set(cachedMaterials.map((material) => material.id));
    for (const id of Array.from(selectedMaterialIds)) {
        if (!validIds.has(id)) {
            selectedMaterialIds.delete(id);
        }
    }
    persistSelectedMaterials();

    if (cachedMaterials.length === 0) {
        materialPickerList.innerHTML = `<div class="material-picker-empty">还没有已上传资料，请先到资料上传模块上传。</div>`;
        selectedMaterials.textContent = "未选择资料，默认检索全部已上传资料";
        return;
    }

    materialPickerList.innerHTML = cachedMaterials.map((material) => `
        <label class="material-choice">
            <input type="checkbox" value="${escapeHtml(material.id)}" ${pendingMaterialIds.has(material.id) ? "checked" : ""}>
            <span>
                <strong>${escapeHtml(material.title)}</strong>
                <small>${escapeHtml(material.fileType ?? getMaterialType(material))} · ${material.characterCount} 字符</small>
            </span>
        </label>
    `).join("");

    renderSelectedMaterials();
}

function renderSelectedMaterials() {
    if (!selectedMaterials) {
        return;
    }

    const selected = cachedMaterials.filter((material) => selectedMaterialIds.has(material.id));
    if (selected.length === 0) {
        selectedMaterials.textContent = "未选择资料，默认检索全部已上传资料";
        return;
    }

    selectedMaterials.innerHTML = selected
        .map((material) => `<span class="selected-chip">${escapeHtml(material.title)}</span>`)
        .join("");
}

function persistSelectedMaterials() {
    localStorage.setItem(SELECTED_MATERIALS_KEY, JSON.stringify(Array.from(selectedMaterialIds)));
}

function openMaterialPicker() {
    if (!materialPicker) {
        return;
    }

    pendingMaterialIds.clear();
    for (const id of selectedMaterialIds) {
        pendingMaterialIds.add(id);
    }

    renderMaterialPicker();
    materialPicker.hidden = false;
    materialPickerList?.querySelector("input[type='checkbox']")?.focus();
}

function closeMaterialPickerDialog() {
    if (materialPicker) {
        materialPicker.hidden = true;
    }
}

function saveMaterialSelection() {
    selectedMaterialIds.clear();
    for (const id of pendingMaterialIds) {
        selectedMaterialIds.add(id);
    }

    persistSelectedMaterials();
    renderSelectedMaterials();
    closeMaterialPickerDialog();
}

uploadForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    const file = materialFile.files[0];
    if (!file) {
        return;
    }

    const button = uploadForm.querySelector("button");
    button.disabled = true;
    button.textContent = "正在读取...";

    try {
        const formData = new FormData();
        formData.append("file", file);
        formData.append("title", uploadTitle.value.trim());

        const uploadResult = await uploadMaterial(formData, (percent) => {
            button.textContent = `上传中 ${percent}%`;
        });

        button.textContent = "正在解析文件...";
        const text = uploadResult.text;
        const data = tryParseJson(text);
        const response = uploadResult;
        if (!uploadResult.ok) {
            throw new Error(data?.error ?? text ?? `上传失败，状态码 ${response.status}`);
        }

        uploadTitle.value = "";
        materialFile.value = "";
        await loadMaterials();
        addMessage("assistant", `已上传并读取《${data.title}》，共 ${data.characterCount} 个字符。现在可以围绕这份资料提问。`);
    } catch (error) {
        addMessage("assistant", `上传失败：${error.message}`);
    } finally {
        button.disabled = false;
        button.textContent = "上传并读取";
    }
});

chatForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await sendMessage();
});

chatInput.addEventListener("keydown", async (event) => {
    if (event.key === "Enter" && !event.shiftKey) {
        event.preventDefault();
        await sendMessage();
    }
});

chatInput.addEventListener("input", () => {
    chatInput.style.height = "auto";
    chatInput.style.height = `${Math.min(chatInput.scrollHeight, 140)}px`;
});

async function sendMessage() {
    const message = chatInput.value.trim();
    if (!message || chatInput.disabled) {
        return;
    }

    addMessage("user", message);
    chatInput.value = "";
    chatInput.style.height = "auto";
    setBusy(true);
    steps.className = "timeline empty-state";
    steps.textContent = "Agent 正在思考并准备流式回答...";

    try {
        const streamed = await streamAgentAnswer(message);
        if (!streamed) {
            await sendMessageWithNormalApi(message);
        }
    } catch (error) {
        addToolHistory({
            time: new Date().toISOString(),
            agent: "CoordinatorAgent",
            tool: "agent_chat",
            status: "Fail"
        });
        addMessage("assistant", `请求失败：${error.message}`);
        steps.className = "timeline empty-state";
        steps.textContent = "请求失败，请确认后端仍在运行。";
    } finally {
        setBusy(false);
        chatInput.focus();
    }
}

async function sendMessageWithNormalApi(message) {
    steps.className = "timeline empty-state";
    steps.textContent = "Agent 正在思考并调用工具...";

    const response = await fetch("/api/agent/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            message,
            sessionId,
            materialIds: Array.from(selectedMaterialIds)
        })
    });

    const text = await response.text();
    const data = tryParseJson(text);
    if (!response.ok) {
        throw new Error(data?.error ?? text ?? "Agent 请求失败");
    }

    addMessage("assistant", data.answer);
    renderSteps(data.steps);
    renderExecutionPanels(data.steps, message);
}

async function streamAgentAnswer(message) {
    if (!window.ReadableStream || !window.TextDecoder) {
        return false;
    }

    const response = await fetch("/api/agent/chat/stream", {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Accept": "text/event-stream"
        },
        body: JSON.stringify({
            message,
            sessionId,
            materialIds: Array.from(selectedMaterialIds)
        })
    });

    if (!response.ok || !response.body) {
        return false;
    }

    const bubble = addMessage("assistant", "正在生成回答...");
    bubble.classList.add("streaming");
    steps.className = "timeline empty-state";
    steps.textContent = "Agent 正在流式输出回答...";

    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8");
    let buffer = "";
    let answer = "";
    let hasRenderedSteps = false;

    while (true) {
        const { value, done } = await reader.read();
        if (done) {
            break;
        }

        buffer += decoder.decode(value, { stream: true });
        const parts = buffer.split("\n\n");
        buffer = parts.pop() ?? "";

        for (const part of parts) {
            const event = parseSseEvent(part);
            if (!event) {
                continue;
            }

            if (event.event === "done" || event.data === "[DONE]") {
                bubble.classList.remove("streaming");
                if (!hasRenderedSteps) {
                    steps.className = "timeline empty-state";
                    steps.textContent = "流式回答已完成。";
                }
                return true;
            }

            if (event.event === "steps") {
                const agentSteps = tryParseJson(event.data);
                if (Array.isArray(agentSteps)) {
                    renderSteps(agentSteps);
                    renderExecutionPanels(agentSteps, message);
                    hasRenderedSteps = true;
                }
                continue;
            }

            const chunk = event.event === "answer"
                ? tryParseJson(event.data) ?? event.data
                : event.data;
            answer += chunk;
            updateMessageBubble(bubble, answer);
        }
    }

    if (buffer.trim()) {
        const event = parseSseEvent(buffer);
        if (event && event.data !== "[DONE]") {
            const chunk = event.event === "answer"
                ? tryParseJson(event.data) ?? event.data
                : event.data;
            answer += chunk;
            updateMessageBubble(bubble, answer);
        }
    }

    bubble.classList.remove("streaming");
    if (!hasRenderedSteps) {
        steps.className = "timeline empty-state";
        steps.textContent = "流式回答已完成。";
    }
    return true;
}

refreshMaterials.addEventListener("click", loadMaterials);

toggleMaterialPicker?.addEventListener("click", () => {
    openMaterialPicker();
});

materialPickerList?.addEventListener("change", (event) => {
    const input = event.target.closest("input[type='checkbox']");
    if (!input) {
        return;
    }

    if (input.checked) {
        pendingMaterialIds.add(input.value);
    } else {
        pendingMaterialIds.delete(input.value);
    }
});

selectAllMaterials?.addEventListener("click", () => {
    pendingMaterialIds.clear();
    for (const material of cachedMaterials) {
        pendingMaterialIds.add(material.id);
    }
    renderMaterialPicker();
});

clearSelectedMaterials?.addEventListener("click", () => {
    pendingMaterialIds.clear();
    renderMaterialPicker();
});

saveMaterialPicker?.addEventListener("click", saveMaterialSelection);
closeMaterialPicker?.addEventListener("click", closeMaterialPickerDialog);
cancelMaterialPicker?.addEventListener("click", closeMaterialPickerDialog);

materialPicker?.addEventListener("click", (event) => {
    if (event.target === materialPicker) {
        closeMaterialPickerDialog();
    }
});

knowledgeTags?.addEventListener("click", (event) => {
    const tag = event.target.closest(".knowledge-tag");
    if (!tag) {
        return;
    }

    chatInput.value = `请解释「${tag.dataset.term}」这个知识点`;
    workspace.classList.remove("upload-mode");
    workspace.classList.add("chat-mode");
    moduleItems.forEach((item) => item.classList.toggle("active", item.dataset.target === "chatModule"));
    chatInput.focus();
});

materialsList.addEventListener("click", async (event) => {
    const previewButton = event.target.closest(".preview-material");
    const renameButton = event.target.closest(".rename-material");
    const deleteButton = event.target.closest(".delete-material");

    if (previewButton) {
        await previewMaterialFileDialog(previewButton.dataset.id);
        return;
    }

    if (renameButton) {
        await renameMaterial(renameButton.dataset.id, renameButton.dataset.title);
        return;
    }

    if (!deleteButton) {
        return;
    }

    deleteButton.disabled = true;
    try {
        const response = await fetch(`/api/materials/${deleteButton.dataset.id}`, { method: "DELETE" });
        if (!response.ok) {
            throw new Error("删除失败");
        }

        await loadMaterials();
        addMessage("assistant", "已删除这份课程资料。");
    } catch (error) {
        addMessage("assistant", `删除失败：${error.message}`);
        deleteButton.disabled = false;
    }
});

async function previewMaterial(id) {
    showModal("资料预览", `
        <div class="preview-loading">正在读取资料内容...</div>
    `);

    try {
        const response = await fetch(`/api/materials/${id}`);
        const text = await response.text();
        const data = tryParseJson(text);
        if (!response.ok) {
            throw new Error(data?.error ?? text ?? "预览失败");
        }

        showModal(data?.title ?? "资料预览", `
            <div class="preview-meta">${data.fileType} · ${data.characterCount} 字符 · ${formatSize(data.fileSize)}</div>
            <pre>${escapeHtml(data.preview)}</pre>
        `);
    } catch (error) {
        addMessage("assistant", `预览失败：${error.message}`);
    }
}

async function previewMaterialDialog(id) {
    showModal("资料预览", `
        <div class="preview-loading">正在读取资料内容...</div>
    `);

    try {
        const response = await fetch(`/api/materials/${id}`);
        const text = await response.text();
        const data = tryParseJson(text);
        if (!response.ok) {
            throw new Error(data?.error ?? text ?? "预览失败");
        }

        showModal(data?.title ?? "资料预览", `
            <div class="preview-meta">
                ${escapeHtml(data?.fileType ?? "TXT")} · ${data?.characterCount ?? 0} 字符 · ${formatSize(data?.fileSize)}
            </div>
            <pre>${escapeHtml(data?.preview || "这份资料暂时没有可预览的文本内容。")}</pre>
        `);
    } catch (error) {
        showModal("预览失败", `
            <div class="preview-error">
                <strong>没有成功读取这份资料。</strong>
                <p>${escapeHtml(getErrorMessage(error))}</p>
                <p>可以先刷新资料列表，或重新上传一份包含可复制文字的 PDF / PPTX / TXT / MD 文件。</p>
            </div>
        `);
    }
}

async function previewMaterialFileDialog(id) {
    showModal("资料预览", `
        <div class="preview-loading">正在读取原始文件...</div>
    `);

    try {
        const metaResponse = await fetch(`/api/materials/${id}`);
        const metaText = await metaResponse.text();
        const data = tryParseJson(metaText);
        if (!metaResponse.ok) {
            throw new Error(data?.error ?? metaText ?? "预览失败");
        }

        const fileResponse = await fetch(`/api/materials/${id}/file`);
        if (!fileResponse.ok) {
            showExtractedTextPreview(data, "这份资料是旧版本上传的，没有保存原始文件。重新上传后可以预览文件本身样式。");
            return;
        }

        const blob = await fileResponse.blob();
        const fileUrl = URL.createObjectURL(blob);
        const fileType = String(data?.fileType ?? "").toUpperCase();
        const title = data?.title ?? "资料预览";
        const meta = `${escapeHtml(fileType || "FILE")} · ${data?.characterCount ?? 0} 字符 · ${formatSize(data?.fileSize)}`;

        if (fileType === "PDF") {
            showModal(title, `
                <div class="preview-meta">${meta}</div>
                <iframe class="file-preview-frame" src="${fileUrl}" title="PDF 原文件预览"></iframe>
            `);
            return;
        }

        if (fileType === "TXT" || fileType === "MD") {
            const originalText = await blob.text();
            showModal(title, `
                <div class="preview-meta">${meta}</div>
                <pre>${escapeHtml(originalText || data?.preview || "这份资料暂时没有可预览的文本内容。")}</pre>
            `);
            return;
        }

        if (fileType === "PPTX") {
            showModal(title, `
                <div class="preview-meta">${meta}</div>
                <div class="preview-error">
                    <strong>PPTX 已保存为原始文件。</strong>
                    <p>浏览器不能直接像 PowerPoint 一样渲染本地 PPTX。你可以下载后用 PowerPoint 或 WPS 打开查看原样式。</p>
                    <p><a class="preview-download" href="${fileUrl}" download="${escapeHtml(title)}.pptx">下载 / 打开 PPTX 原文件</a></p>
                </div>
            `);
            return;
        }

        showModal(title, `
            <div class="preview-meta">${meta}</div>
            <div class="preview-error">
                <strong>这个文件类型暂不支持浏览器内嵌预览。</strong>
                <p><a class="preview-download" href="${fileUrl}" download="${escapeHtml(title)}">下载原文件</a></p>
            </div>
        `);
    } catch (error) {
        showModal("预览失败", `
            <div class="preview-error">
                <strong>没有成功读取这份资料。</strong>
                <p>${escapeHtml(getErrorMessage(error))}</p>
                <p>请刷新资料列表，或重新上传一份 PDF / PPTX / TXT / MD 文件。</p>
            </div>
        `);
    }
}

function showExtractedTextPreview(data, notice) {
    showModal(data?.title ?? "资料预览", `
        <div class="preview-meta">
            ${escapeHtml(data?.fileType ?? "TXT")} · ${data?.characterCount ?? 0} 字符 · ${formatSize(data?.fileSize)}
        </div>
        <div class="preview-notice">${escapeHtml(notice)}</div>
        <pre>${escapeHtml(data?.preview || "这份资料暂时没有可预览的文本内容。")}</pre>
    `);
}

async function renameMaterial(id, currentTitle) {
    const title = window.prompt("请输入新的资料名称：", currentTitle ?? "");
    if (!title || !title.trim()) {
        return;
    }

    try {
        const response = await fetch(`/api/materials/${id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ title: title.trim() })
        });
        const text = await response.text();
        const data = tryParseJson(text);
        if (!response.ok) {
            throw new Error(data?.error ?? text ?? "重命名失败");
        }

        await loadMaterials();
        addMessage("assistant", `已将资料重命名为《${data.title}》。`);
    } catch (error) {
        addMessage("assistant", `重命名失败：${error.message}`);
    }
}

clearChat.addEventListener("click", async () => {
    await fetch(`/api/memory/${sessionId}`, { method: "DELETE" });
    messages.innerHTML = "";
    messageTotal = 0;
    updateMessageCount();
    renderSteps([]);
    renderAgentDecision(null, "");
    renderWelcomeCard();
});

clearLearningHistory?.addEventListener("click", () => {
    localStorage.removeItem(LEARNING_HISTORY_KEY);
    renderLearningHistory();
});

newSession.addEventListener("click", () => {
    const sessions = getStoredSessions();
    sessionId = `web-${Date.now()}`;
    sessions.unshift({ id: sessionId, name: `会话 ${sessions.length + 1}` });
    localStorage.setItem("smartStudy.sessions", JSON.stringify(sessions));
    localStorage.setItem("smartStudy.currentSessionId", sessionId);
    messageTotal = 0;
    updateMessageCount();
    renderSessionSelect();
    renderSteps([]);
    renderAgentDecision(null, "");
    renderWelcomeCard();
    chatInput.focus();
});

sessionSelect.addEventListener("change", async () => {
    sessionId = sessionSelect.value;
    localStorage.setItem("smartStudy.currentSessionId", sessionId);
    messages.innerHTML = "";
    renderWelcomeCard();
    renderSteps([]);
    renderAgentDecision(null, "");

    try {
        const response = await fetch(`/api/memory/${sessionId}`);
        const history = await response.json();
        messages.innerHTML = "";
        messageTotal = 0;
        for (const item of history) {
            addMessage(item.role === "user" ? "user" : "assistant", item.content);
        }

        if (history.length === 0) {
            renderWelcomeCard();
        }
    } catch {
        renderWelcomeCard();
    }
});

messages.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-prompt]");
    if (!button) {
        return;
    }

    chatInput.value = button.dataset.prompt;
    await sendMessage();
});

moduleItems.forEach((item) => {
    item.addEventListener("click", () => {
        moduleItems.forEach((button) => button.classList.remove("active"));
        item.classList.add("active");

        if (item.dataset.target === "chatModule") {
            workspace.classList.remove("upload-mode");
            workspace.classList.add("chat-mode");
            chatInput.focus();
        } else {
            workspace.classList.remove("chat-mode");
            workspace.classList.add("upload-mode");
            uploadTitle.focus();
        }
    });
});

function getTimelineTitle(action) {
    const names = {
        search_materials: "检索课程资料",
        summarize_material: "总结资料",
        generate_quiz: "生成练习题",
        create_study_plan: "制定学习计划",
        list_materials: "查看资料列表",
        extract_learning_points: "提取学习重点"
    };
    return names[action] ?? "调用 Agent 工具";
}

function getMaterialType(material) {
    const name = `${material.fileName ?? ""} ${material.title ?? ""}`.toLowerCase();
    if (name.includes("pptx")) return "PPTX";
    if (name.includes("txt")) return "TXT";
    if (name.includes("md")) return "MD";
    return "PDF";
}

function showModal(title, html) {
    const old = document.querySelector(".modal-backdrop");
    old?.remove();

    const modal = document.createElement("div");
    modal.className = "modal-backdrop";
    modal.innerHTML = `
        <section class="modal-card">
            <button class="modal-close" type="button">×</button>
            <h3>${escapeHtml(title)}</h3>
            <div class="modal-content">${html}</div>
        </section>
    `;
    document.body.appendChild(modal);
    modal.querySelector(".modal-close").addEventListener("click", () => modal.remove());
    modal.addEventListener("click", (event) => {
        if (event.target === modal) {
            modal.remove();
        }
    });
}

function shorten(value, maxLength) {
    value = String(value ?? "");
    return value.length <= maxLength ? value : `${value.slice(0, maxLength)}...`;
}

function uploadMaterial(formData, onProgress) {
    return new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open("POST", "/api/materials/upload");
        xhr.timeout = 180000;

        xhr.upload.onprogress = (event) => {
            if (!event.lengthComputable) {
                return;
            }

            const percent = Math.max(1, Math.min(99, Math.round((event.loaded / event.total) * 100)));
            onProgress?.(percent);
        };

        xhr.onload = () => {
            resolve({
                ok: xhr.status >= 200 && xhr.status < 300,
                status: xhr.status,
                text: xhr.responseText
            });
        };

        xhr.onerror = () => reject(new Error("上传连接失败，请确认后端仍在运行。"));
        xhr.ontimeout = () => reject(new Error("上传或解析时间过长，请尝试换一个较小文件，或先拆分 PDF/PPTX。"));
        xhr.send(formData);
    });
}

function tryParseJson(value) {
    try {
        return value ? JSON.parse(value) : null;
    } catch {
        return null;
    }
}

function parseSseEvent(raw) {
    const lines = raw
        .split(/\r?\n/)
        .map((line) => line.trimEnd())
        .filter(Boolean);

    if (lines.length === 0) {
        return null;
    }

    let event = "message";
    const data = [];

    for (const line of lines) {
        if (line.startsWith("event:")) {
            event = line.slice("event:".length).trim();
            continue;
        }

        if (line.startsWith("data:")) {
            data.push(line.slice("data:".length).trimStart());
        }
    }

    return {
        event,
        data: data.join("\n")
    };
}

function getErrorMessage(error) {
    if (!error) {
        return "未知错误。";
    }

    if (typeof error === "string") {
        return error || "未知错误。";
    }

    return error.message || "未知错误。";
}

function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
}

function formatDate(value) {
    if (!value) {
        return "";
    }

    return new Intl.DateTimeFormat("zh-CN", {
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
    }).format(new Date(value));
}

function formatSize(value) {
    if (!value || value < 1024) {
        return `${value ?? 0} B`;
    }

    if (value < 1024 * 1024) {
        return `${(value / 1024).toFixed(1)} KB`;
    }

    return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

renderWelcomeCard();
updateMessageCount();
renderSessionSelect();
renderToolHistory();
renderLearningHistory();
loadMaterials();
