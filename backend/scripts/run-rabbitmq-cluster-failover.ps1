$ErrorActionPreference = 'Stop'

$composeFile = Join-Path $PSScriptRoot '..\docker-compose.rabbitmq-cluster.yml'
docker compose -f $composeFile up -d --wait

$status = (docker exec food-rabbit1 rabbitmqctl cluster_status) -join "`n"
if ($status -notmatch 'rabbit@rabbit2') {
    docker exec food-rabbit2 rabbitmqctl join_cluster rabbit@rabbit1
}
if ($status -notmatch 'rabbit@rabbit3') {
    docker exec food-rabbit3 rabbitmqctl join_cluster rabbit@rabbit1
}

$env:RABBIT_CLUSTER_FAILOVER = '1'
dotnet test (Join-Path $PSScriptRoot '..\FoodDelivery.Infrastructure.Tests\FoodDelivery.Infrastructure.Tests.csproj') `
    --filter 'FullyQualifiedName~QuorumFailoverTests' `
    --logger 'console;verbosity=detailed'
exit $LASTEXITCODE
