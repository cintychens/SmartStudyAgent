param(
    [string]$BaseUrl = "http://localhost:5153"
)

$ErrorActionPreference = "Stop"

function Invoke-JsonRequest {
    param(
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null
    )

    $headers = @{ "Content-Type" = "application/json" }

    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
    }

    $json = $Body | ConvertTo-Json -Depth 8
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $json
}

Write-Host "SmartStudyAgent smoke test"
Write-Host "BaseUrl: $BaseUrl"

$info = Invoke-JsonRequest -Method "GET" -Uri "$BaseUrl/api/info"
if ($info.name -ne "SmartStudyAgent") {
    throw "api/info did not return SmartStudyAgent."
}
Write-Host "[OK] Backend info endpoint"

$materials = Invoke-JsonRequest -Method "GET" -Uri "$BaseUrl/api/materials"
if ($null -eq $materials) {
    throw "api/materials returned null."
}
Write-Host "[OK] Materials endpoint"

$sessionId = "smoke-" + [Guid]::NewGuid().ToString("N")
$chat = Invoke-JsonRequest -Method "POST" -Uri "$BaseUrl/api/agent/chat" -Body @{
    sessionId = $sessionId
    message = "请用一句话介绍 SmartStudyAgent 的功能"
    maxSteps = 3
}

if ([string]::IsNullOrWhiteSpace($chat.answer)) {
    throw "api/agent/chat returned an empty answer."
}

if ($chat.sessionId -ne $sessionId) {
    throw "api/agent/chat returned unexpected sessionId."
}

Write-Host "[OK] Agent chat endpoint"
Write-Host "All smoke tests passed."
