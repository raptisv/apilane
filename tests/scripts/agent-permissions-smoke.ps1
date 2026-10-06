#requires -Version 7.0
<#
Runs the real Portal and API on loopback, with new temporary SQLite databases and no
repository appsettings files. Requires built Debug/net10.0 binaries and free Orleans
localhost ports 11111/30000. Does not use Docker or mock either HTTP direction.

    pwsh tests/scripts/agent-permissions-smoke.ps1
    pwsh tests/scripts/agent-permissions-smoke.ps1 -KeepRunning

KeepRunning prints a temporary stop-file path after verification; create that file
to stop both owned processes and remove their temporary data after browser checks.
#>
[CmdletBinding()]
param([switch]$KeepRunning)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$apiDll = Join-Path $repo 'src/Apilane.Api/bin/Debug/net10.0/Apilane.Api.dll'
$portalDll = Join-Path $repo 'src/Apilane.Portal/bin/Debug/net10.0/Apilane.Portal.dll'
foreach ($dll in @($apiDll, $portalDll)) {
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "Build the API and Portal in Debug before running this smoke test. Missing: $dll"
    }
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port }
    finally { $listener.Stop() }
}

# UseLocalhostClustering currently chooses the standard ports. Refuse to touch an
# existing localhost silo instead of risking interaction with a developer's server.
foreach ($port in @(11111, 30000)) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    $listener.Server.ExclusiveAddressUse = $true
    try { $listener.Start() }
    catch { throw "Orleans port $port is occupied; stop this test without using that cluster." }
    finally { $listener.Stop() }
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeRoot = Join-Path $temporaryParent ('apilane-agent-smoke-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($smokeRoot) | Out-Null
$portalDirectory = [IO.Directory]::CreateDirectory((Join-Path $smokeRoot 'portal')).FullName
$apiDirectory = [IO.Directory]::CreateDirectory((Join-Path $smokeRoot 'api')).FullName
$portalUrl = 'http://127.0.0.1:' + (Get-FreePort)
do { $apiUrl = 'http://127.0.0.1:' + (Get-FreePort) } while ($apiUrl -eq $portalUrl)
$installationKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$ownerEmail = 'smoke-owner@portal.test'
$ownerPassword = 'admin' # The seeded password exists only in this throw-away database.
$processes = [Collections.Generic.List[object]]::new()
$checks = [Collections.Generic.List[string]]::new()
$clients = [Collections.Generic.List[Net.Http.HttpClient]]::new()

function Start-IsolatedHost([string]$name, [string]$dll, [string]$directory, [hashtable]$settings) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command dotnet).Source
    $start.ArgumentList.Add($dll)
    $start.WorkingDirectory = $directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment.Clear()
    foreach ($nameToKeep in @('PATH', 'SystemRoot', 'WINDIR', 'TEMP', 'TMP', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'DOTNET_ROOT')) {
        $value = [Environment]::GetEnvironmentVariable($nameToKeep)
        if ($value) { $start.Environment[$nameToKeep] = $value }
    }
    $start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $start.Environment['DOTNET_ENVIRONMENT'] = 'Production'
    $start.Environment['InstallationKey'] = $installationKey
    $start.Environment['FilesPath'] = $directory
    $start.Environment['FileStorage__Provider'] = 'LocalFileSystem'
    $start.Environment['OpenTelemetry__Tracing__Enabled'] = 'false'
    $start.Environment['OpenTelemetry__Metrics__Enabled'] = 'false'
    $start.Environment['Serilog__MinimumLevel__Default'] = 'Warning'
    $start.Environment['Serilog__WriteTo__0__Name'] = 'Console'
    $start.Environment['Serilog__Using__0'] = 'Serilog.Sinks.Console'
    foreach ($key in $settings.Keys) { $start.Environment[$key] = [string]$settings[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    Assert-True ($process.Start()) "Could not start $name."
    $processes.Add(@{
        Name = $name; Process = $process
        Output = $process.StandardOutput.ReadToEndAsync()
        Error = $process.StandardError.ReadToEndAsync()
    })
}

function New-SmokeClient([string]$url, [bool]$cookie = $false) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseCookies = $cookie
    $client = [Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [Uri]$url
    $client.Timeout = [TimeSpan]::FromSeconds(45)
    $clients.Add($client)
    return $client
}

function Send-Smoke([Net.Http.HttpClient]$client, [string]$method, [string]$path, $body = $null, [int]$expected = 200) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method), $path)
    if ($null -ne $body) {
        $request.Content = [Net.Http.StringContent]::new(($body | ConvertTo-Json -Depth 30 -Compress), [Text.Encoding]::UTF8, 'application/json')
    }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert-True ([int]$response.StatusCode -eq $expected) "$method $path returned $([int]$response.StatusCode), expected $expected. $text"
            if ($text) { return ($text | ConvertFrom-Json -Depth 40) }
        }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

function Wait-Ready([Net.Http.HttpClient]$client, [string]$name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    do {
        foreach ($hostProcess in $processes) {
            Assert-True (-not $hostProcess.Process.HasExited) "$($hostProcess.Name) exited before readiness."
        }
        try {
            $response = $client.GetAsync('/health/liveness').GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode
            $response.Dispose()
            if ($ready) { return }
        }
        catch [Net.Http.HttpRequestException] { Write-Verbose "$name is starting." }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "$name did not become ready within 90 seconds."
}

function Grant([string]$resource, [bool]$read = $false, [bool]$write = $false, [bool]$delete = $false) {
    return @{ Resource = $resource; Read = $read; Write = $write; Delete = $delete }
}

function Save-Grants([object[]]$grants) {
    Send-Smoke $owner 'PUT' "$appPath/collaborators/$collaborationId/permissions" @{ Permissions = @($grants) } | Out-Null
}

function Check([string]$message) {
    $checks.Add($message)
    Write-Output "PASS $message"
}

try {
    # Serve the already-built UI from this isolated content root for optional browser verification.
    $builtUi = Join-Path $repo 'src/Apilane.Portal/wwwroot/ui'
    if (Test-Path -LiteralPath $builtUi) {
        [IO.Directory]::CreateDirectory((Join-Path $portalDirectory 'wwwroot')) | Out-Null
        Copy-Item -LiteralPath $builtUi -Destination (Join-Path $portalDirectory 'wwwroot/ui') -Recurse
    }
    Start-IsolatedHost 'Portal' $portalDll $portalDirectory @{
        Url = $portalUrl; ApiUrl = $apiUrl; AdminEmail = $ownerEmail; InstanceTitle = 'Isolated agent smoke'
    }
    $owner = New-SmokeClient $portalUrl $true
    $owner.DefaultRequestHeaders.Add('X-Apilane-Portal', '1')
    Wait-Ready $owner 'Portal'
    Start-IsolatedHost 'API' $apiDll $apiDirectory @{
        Url = $apiUrl; PortalUrl = $portalUrl; Clustering__Type = 'Localhost'
        Clustering__SiloPort = '11111'; Clustering__GatewayPort = '30000'
        Clustering__ClusterId = [Guid]::NewGuid().ToString('N'); Clustering__ServiceId = [Guid]::NewGuid().ToString('N')
    }
    $data = New-SmokeClient $apiUrl
    Wait-Ready $data 'API'
    Send-Smoke $owner 'POST' '/api/v1/session' @{ Email = $ownerEmail; Password = $ownerPassword } | Out-Null
    $servers = Send-Smoke $owner 'GET' '/api/v1/servers'
    $application = Send-Smoke $owner 'POST' '/api/v1/applications' @{
        Name = 'Isolated permission smoke'; ServerID = $servers.Data[0].ID; DatabaseType = 'SQLLite'
    } 201
    $appPath = '/api/v1/applications/' + $application.Token
    $createdAgent = Send-Smoke $owner 'POST' '/api/v1/admin/agents' @{ Name = 'permission-smoke-agent' } 201
    $shared = Send-Smoke $owner 'POST' "$appPath/collaborators" @{ Email = $createdAgent.Email } 201
    $collaborationId = $shared.ID
    $agent = New-SmokeClient $portalUrl
    $agent.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $createdAgent.Key)
    $apiToken = (Send-Smoke $owner 'GET' '/api/v1/session/api-token').Token
    $data.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $apiToken)
    $data.DefaultRequestHeaders.Add('x-application-token', $application.Token)
    $data.DefaultRequestHeaders.Add('x-client-id', 'portal')

    $discovery = Send-Smoke $agent 'GET' "$appPath/permissions"
    Assert-True (@($discovery.Permissions | Where-Object { $_.Write -or $_.Delete }).Count -eq 0) 'New agent should be read-only.'
    Send-Smoke $agent 'POST' "$appPath/entities" @{ Name = 'Denied'; RequireChangeTracking = $false } 403 | Out-Null
    $entities = Send-Smoke $owner 'GET' "$appPath/entities"
    Assert-True (-not ($entities.Data.Name -contains 'Denied')) 'Denied creation changed the Portal schema.'
    Check 'New agent is read-only; denied entity creation has no schema effect'

    Save-Grants @((Grant 'entities' $true $true))
    Send-Smoke $agent 'POST' "$appPath/entities" @{ Name = 'Orders'; RequireChangeTracking = $false } 201 | Out-Null
    Send-Smoke $agent 'POST' "$appPath/entities/Orders/properties" @{ Name = 'Amount'; Type = 'Number'; DecimalPlaces = 0 } 201 | Out-Null
    Send-Smoke $agent 'POST' "$appPath/entities/Orders/properties" @{ Name = 'Notes'; Type = 'String'; Maximum = 100 } 201 | Out-Null
    Send-Smoke $data 'POST' '/api/Data/Post?entity=Orders' @{ Amount = 7; Notes = 'real SQLite record' } | Out-Null
    $rows = Send-Smoke $data 'GET' '/api/Data/Get?entity=Orders&getTotal=true'
    Assert-True ($rows.Total -eq 1 -and $rows.Data[0].Amount -eq 7) 'Agent schema writes did not reach the real API database.'
    Check 'Granted entity/property creation reaches the real API and stores a SQLite record'

    Send-Smoke $agent 'DELETE' "$appPath/entities/Orders/properties/Notes" $null 403 | Out-Null
    $rows = Send-Smoke $data 'GET' '/api/Data/Get?entity=Orders&getTotal=true'
    Assert-True ($rows.Data[0].Notes -eq 'real SQLite record') 'Write-only grant unexpectedly deleted a property.'
    Save-Grants @((Grant 'entities' $true $false $true))
    Send-Smoke $agent 'DELETE' "$appPath/entities/Orders/properties/Notes" $null 204 | Out-Null
    $rows = Send-Smoke $data 'GET' '/api/Data/Get?entity=Orders&getTotal=true'
    Assert-True (-not ($rows.Data[0].PSObject.Properties.Name -contains 'Notes')) 'Granted property deletion did not remove the real column.'
    Assert-True ($rows.Total -eq 1 -and $rows.Data[0].Amount -eq 7) 'Property deletion corrupted the surviving data.'
    Check 'Delete requires its own grant; granted deletion removes the real column while preserving other data'

    Save-Grants @()
    Send-Smoke $agent 'POST' "$appPath/entities/Orders/properties" @{ Name = 'AfterRevoke'; Type = 'String' } 403 | Out-Null
    Send-Smoke $agent 'DELETE' "$appPath/entities/Orders" $null 403 | Out-Null
    Send-Smoke $agent 'POST' "$appPath/rebuild" $null 403 | Out-Null
    $rows = Send-Smoke $data 'GET' '/api/Data/Get?entity=Orders&getTotal=true'
    Assert-True ($rows.Total -eq 1 -and $rows.Data[0].Amount -eq 7) 'Revoked operations changed the real data.'
    Assert-True (-not ($rows.Data[0].PSObject.Properties.Name -contains 'AfterRevoke')) 'Revoked creation added a real column.'
    Check 'Revoked writes, deletes and rebuild are refused and real data remains unchanged'

    Save-Grants @((Grant 'rebuild' $false $true))
    Send-Smoke $agent 'POST' "$appPath/rebuild" $null 204 | Out-Null
    $rows = Send-Smoke $data 'GET' '/api/Data/Get?entity=Orders&getTotal=true'
    Assert-True ($rows.Total -eq 0) 'Granted rebuild did not clear the real database.'
    Check 'Explicit rebuild grant clears real SQLite data and retains the schema'

    Save-Grants @((Grant 'entities' $true $false $true))
    Send-Smoke $agent 'DELETE' "$appPath/entities/Orders" $null 204 | Out-Null
    Send-Smoke $owner 'GET' "$appPath/entities/Orders" $null 404 | Out-Null
    $missingEntity = Send-Smoke $data 'GET' '/api/Data/Get?entity=Orders' $null 400
    Assert-True ($missingEntity.Code -eq 'NOT_FOUND' -or $missingEntity.Code -eq 'ERROR') 'Real API still accepts the deleted entity.'
    Send-Smoke $agent 'DELETE' $appPath $null 403 | Out-Null
    Send-Smoke $agent 'GET' "$appPath/connection-info" $null 403 | Out-Null
    Send-Smoke $agent 'GET' '/api/v1/session/api-token' $null 403 | Out-Null
    Check 'Granted entity deletion reaches real API; application deletion and secrets remain refused'

    Save-Grants @((Grant 'security' $true))
    $result = [ordered]@{
        Passed = $checks.Count; Checks = @($checks); PortalUrl = $portalUrl; ApiUrl = $apiUrl
        ApplicationToken = $application.Token; Root = $smokeRoot; StopFile = (Join-Path $smokeRoot 'stop')
        OwnerEmail = $ownerEmail; OwnerPassword = $ownerPassword
    }
    $result | ConvertTo-Json -Depth 5 | Write-Output
    if ($KeepRunning) {
        Write-Output 'Browser verification is ready. Create StopFile to clean up this isolated stack.'
        while (-not (Test-Path -LiteralPath $result.StopFile)) {
            foreach ($hostProcess in $processes) {
                Assert-True (-not $hostProcess.Process.HasExited) "$($hostProcess.Name) exited during browser verification."
            }
            Start-Sleep -Seconds 1
        }
    }
}
catch {
    Write-Error $_ -ErrorAction Continue
    throw
}
finally {
    foreach ($client in $clients) { $client.Dispose() }
    foreach ($hostProcess in $processes) {
        if (-not $hostProcess.Process.HasExited) { $hostProcess.Process.Kill($true) }
        $hostProcess.Process.WaitForExit()
        if ($checks.Count -lt 6) {
            $log = $hostProcess.Output.GetAwaiter().GetResult() + $hostProcess.Error.GetAwaiter().GetResult()
            Write-Output ("$($hostProcess.Name) startup diagnostics:`n" + (($log -split "`n" | Select-Object -Last 25) -join "`n"))
        }
        $hostProcess.Process.Dispose()
    }
    # Resolve and check the exact owned temporary directory before recursive cleanup.
    $resolvedRoot = [IO.Path]::GetFullPath($smokeRoot)
    $expectedParent = $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedRoot.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedRoot) -match '^apilane-agent-smoke-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
    else { throw "Refused unexpected cleanup target: $resolvedRoot" }
}
