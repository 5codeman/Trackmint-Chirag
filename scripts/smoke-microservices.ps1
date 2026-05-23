param(
    [string]$GatewayRootUrl = "http://localhost:8090",
    [string]$ApiBaseUrl = "http://localhost:8090/api"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

Write-Step "Checking gateway health"
$health = Invoke-RestMethod -Uri "$GatewayRootUrl/health" -Method Get
Write-Host "Gateway status: $($health.status)"

Write-Step "Checking service routing through gateway"
$authInfo = Invoke-RestMethod -Uri "$ApiBaseUrl/auth/service-info" -Method Get
$financeInfo = Invoke-RestMethod -Uri "$ApiBaseUrl/finance/service-info" -Method Get
$insightsInfo = Invoke-RestMethod -Uri "$ApiBaseUrl/insights/service-info" -Method Get
$notificationInfo = Invoke-RestMethod -Uri "$ApiBaseUrl/notifications/service-info" -Method Get
Write-Host "Auth: $($authInfo.service)"
Write-Host "Finance: $($financeInfo.service)"
Write-Host "Insights: $($insightsInfo.service)"
Write-Host "Notifications: $($notificationInfo.service)"

Write-Step "Registering smoke-test user through AuthService"
$email = "smoke-$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())@trackmint.local"
$registerBody = @{
    email = $email
    password = "SmokePass123"
    displayName = "Smoke User"
} | ConvertTo-Json

$auth = Invoke-RestMethod -Uri "$ApiBaseUrl/auth/register" -Method Post -ContentType "application/json" -Body $registerBody
Write-Host "Registered user: $($auth.email)"

Write-Step "Checking NotificationService with JWT"
Start-Sleep -Seconds 3
$headers = @{
    Authorization = "Bearer $($auth.accessToken)"
}

$notifications = Invoke-RestMethod -Uri "$ApiBaseUrl/notifications" -Method Get -Headers $headers
$welcome = $notifications | Where-Object { $_.type -eq "welcome" } | Select-Object -First 1

if ($welcome) {
    Write-Host "Welcome notification received: $($welcome.title)" -ForegroundColor Green
} else {
    Write-Warning "No welcome notification found yet. RabbitMQ consumer may still be starting; check NotificationService logs."
}

Write-Step "Smoke test completed"
