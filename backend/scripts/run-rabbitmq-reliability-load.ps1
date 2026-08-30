$ErrorActionPreference = 'Stop'

$env:RABBIT_RELIABILITY_LOAD = '1'
if (-not $env:RABBIT_RELIABILITY_MESSAGES) { $env:RABBIT_RELIABILITY_MESSAGES = '100000' }
if (-not $env:RABBIT_RELIABILITY_SHOPS) { $env:RABBIT_RELIABILITY_SHOPS = '100' }
if (-not $env:RABBIT_RELIABILITY_PUBLISHERS) { $env:RABBIT_RELIABILITY_PUBLISHERS = '3' }
if (-not $env:RABBIT_RELIABILITY_CONSUMERS) { $env:RABBIT_RELIABILITY_CONSUMERS = '32' }
if (-not $env:RABBIT_RELIABILITY_DUPLICATE_PERCENT) { $env:RABBIT_RELIABILITY_DUPLICATE_PERCENT = '5' }
if (-not $env:RABBIT_RELIABILITY_TRANSIENT_PERCENT) { $env:RABBIT_RELIABILITY_TRANSIENT_PERCENT = '2' }
if (-not $env:RABBIT_RELIABILITY_POST_COMMIT_CRASH_PERCENT) { $env:RABBIT_RELIABILITY_POST_COMMIT_CRASH_PERCENT = '1' }

dotnet test (Join-Path $PSScriptRoot '..\FoodDelivery.Infrastructure.Tests\FoodDelivery.Infrastructure.Tests.csproj') `
    --filter 'FullyQualifiedName~ReliabilityLoadTests' `
    --no-restore `
    --logger 'console;verbosity=detailed'
exit $LASTEXITCODE
