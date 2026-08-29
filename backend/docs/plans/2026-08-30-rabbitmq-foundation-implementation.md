# RabbitMQ Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kiểm chứng RabbitMQ reliability bằng test trên RabbitMQ/MySQL thật, triển khai messaging foundation tối thiểu cho `food-delivery`, rồi viết chương tài liệu dựa trên bằng chứng đã đo.

**Architecture:** `FoodDelivery.Application` sở hữu integration-event contract; `FoodDelivery.Infrastructure` dùng `RabbitMQ.Client` để quản lý một connection dài hạn trên mỗi process và một publisher channel được serialize an toàn. Test-only reference models triển khai Outbox, Inbox và strict FEFO để kiểm chứng correctness, multi-instance và load mà không đưa business giả vào production projects.

**Tech Stack:** .NET 6, RabbitMQ.Client 7.2.2, RabbitMQ Management, RabbitMQ quorum queues, MySQL 8, xUnit, Angular documentation site.

**Spec:** `backend/docs/plans/2026-08-30-rabbitmq-foundation-design.md`

## Global Constraints

- Production projects vẫn target `.NET 6`.
- Business identifiers dùng tiếng Việt không dấu theo `camelCase`; technical identifiers giữ canonical English.
- Không thêm `DonHang`, `TonKho` hoặc `LoHang` vào production code trong phase này.
- Không dùng mock để kết luận ACK, Publisher Confirm, redelivery, recovery, quorum failover hoặc inventory correctness.
- Một connection được tái sử dụng trên mỗi process; publisher channel không được dùng đồng thời.
- Tài liệu không gắn nhãn cấp độ cho con người; so sánh `thiết kế trực giác` với `thiết kế bảo vệ invariant`.
- Phần kết quả trong tài liệu chỉ được viết sau khi profile tương ứng đã chạy thật.
- Giữ nguyên mọi thay đổi không liên quan đang tồn tại trong cả hai repositories.

---

## File structure

### `food-delivery`

- `backend/FoodDelivery.Application/Abstractions/Messaging/IIntegrationEvent.cs`: metadata bắt buộc của durable event contract.
- `backend/FoodDelivery.Application/Abstractions/Messaging/IIntegrationEventPublisher.cs`: capability publish mà application sử dụng.
- `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqOptions.cs`: configuration contract.
- `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqConnectionManager.cs`: sở hữu connection theo process.
- `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqPublisher.cs`: serialize, declare exchange, publish persistent message và chờ confirm.
- `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqHealthCheck.cs`: readiness probe dùng passive declaration.
- `backend/FoodDelivery.Infrastructure.Tests/Messaging/Unit/*`: serialization/options tests.
- `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/*`: broker integration, recovery và multi-instance tests.
- `backend/FoodDelivery.Infrastructure.Tests/Messaging/Simulation/*`: test-only Outbox/Inbox/FEFO schema và repositories.
- `backend/FoodDelivery.Infrastructure.Tests/Messaging/Load/*`: parameterized load runner và result writer.
- `backend/docker-compose.yml`: MySQL, single-node RabbitMQ profile và management ports.
- `backend/docker-compose.rabbitmq-cluster.yml`: ba RabbitMQ nodes cho quorum failover profile.
- `backend/test-results/rabbitmq/.gitkeep`: vị trí output; JSON/Markdown kết quả runtime không commit nếu chứa environment-specific data ngoài reference report đã review.

### `base-angular`

- `src/assets/docs/dotnet-core/rabbitmq-reliable-messaging.md`: chương kỹ thuật từ cơ bản đến production.
- `src/app/app-routing.module.ts`: route Markdown.
- `src/app/layout/shell/menu.config.ts`: entry trong `2. Nâng cao`.

---

### Task 1: Dựng môi trường kiểm chứng RabbitMQ thật

**Files:**
- Modify: `backend/docker-compose.yml`
- Create: `backend/docker-compose.rabbitmq-cluster.yml`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/RabbitMqEnvironment.cs`
- Modify: `backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj`

**Interfaces:**
- Produces: single-node endpoint `amqp://food_app:food_dev@localhost:5673/food`, Management API `http://localhost:15673`, cluster endpoints `5674..5676`.
- Consumes: Docker Compose và `RabbitMQ.Client` 7.2.2.

- [ ] **Step 1: Thêm failing environment test**

```csharp
public sealed class RabbitMqEnvironmentTests
{
    [Fact]
    public async Task BrokerAndManagementApi_AreReachable()
    {
        await using var connection = await RabbitMqEnvironment.ConnectAsync();
        Assert.True(connection.IsOpen);

        using var response = await RabbitMqEnvironment.ManagementClient
            .GetAsync("api/overview");
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận failure trước khi có broker**

Run:

```powershell
dotnet test backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj --filter RabbitMqEnvironmentTests
```

Expected: FAIL vì `localhost:5673` chưa có RabbitMQ hoặc helper chưa tồn tại.

- [ ] **Step 3: Bổ sung RabbitMQ single-node**

Thêm service dùng image RabbitMQ Management được pin, khai báo user/vhost riêng, volume, health check, ports `5673:5672` và `15673:15672`. Không dùng guest credentials cho application.

- [ ] **Step 4: Bổ sung cluster profile**

Tạo ba nodes dùng cùng Erlang cookie, hostname ổn định và management ports riêng. Script initialization join node 2/3 vào node 1; test topology khai báo quorum queue với replication factor ba.

- [ ] **Step 5: Implement environment helper**

```csharp
internal static class RabbitMqEnvironment
{
    internal const string HostName = "localhost";
    internal const int Port = 5673;
    internal const string VirtualHost = "food";
    internal const string UserName = "food_app";
    internal const string Password = "food_dev";

    internal static Task<IConnection> ConnectAsync() =>
        new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            VirtualHost = VirtualHost,
            UserName = UserName,
            Password = Password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        }.CreateConnectionAsync("food-delivery-tests");
}
```

- [ ] **Step 6: Khởi động single-node và chạy test**

Run:

```powershell
docker compose -f backend/docker-compose.yml up -d --wait
dotnet test backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj --filter RabbitMqEnvironmentTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add backend/docker-compose.yml backend/docker-compose.rabbitmq-cluster.yml backend/FoodDelivery.Infrastructure.Tests
git commit -m "test: add RabbitMQ integration environment"
```

---

### Task 2: Viết baseline tests tái hiện lỗi phổ biến

**Files:**
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Simulation/TestMessagingSchema.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Simulation/ConcurrencyCheckpoint.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/DirectPublishBaselineTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/AckBeforeCommitBaselineTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/ReadModifyWriteBaselineTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/FefoSkipLockedBaselineTests.cs`

**Interfaces:**
- Produces: deterministic evidence rằng direct publish, early ACK, read-modify-write và approximate FEFO phá guarantee nào.
- Consumes: MySQL test database và RabbitMQ single-node từ Task 1.

- [ ] **Step 1: Tạo schema test biệt lập**

Schema gồm `TestOrder`, `TestInventoryLot`, `TestReservation`, `TestAllocation`, `TestOutbox`, `TestInbox`. Mỗi test dùng `runId` riêng; teardown chỉ xóa rows mang `runId`, không drop production database.

- [ ] **Step 2: Tái hiện lost event**

```csharp
[Fact]
public async Task CommitThenCrashBeforePublish_LeavesMissingIntent()
{
    var orderId = await _baseline.CommitOrderAsync();
    // Failure checkpoint: process stops before BasicPublishAsync.

    Assert.True(await _baseline.OrderExistsAsync(orderId));
    Assert.False(await _baseline.MessageExistsAsync(orderId));
}
```

- [ ] **Step 3: Tái hiện ACK-before-commit**

Consumer ACK delivery, checkpoint dừng trước MySQL commit, sau đó consumer thứ hai được mở. Assert queue `Ready = 0`, `Unacked = 0` nhưng business row không tồn tại.

- [ ] **Step 4: Tái hiện lost update bằng deterministic barrier**

```csharp
var checkpoint = new ConcurrencyCheckpoint(participantCount: 2);

var first = _baseline.ReserveWithReadModifyWriteAsync(lotId, 7, checkpoint);
var second = _baseline.ReserveWithReadModifyWriteAsync(lotId, 7, checkpoint);
await Task.WhenAll(first, second);

Assert.NotEqual(initialQuantity - 14, await _baseline.GetAvailableAsync(lotId));
```

Barrier đặt sau `SELECT` và trước `UPDATE` để hai transactions chắc chắn đọc cùng giá trị; test không phụ thuộc timing ngẫu nhiên.

- [ ] **Step 5: Chứng minh `SKIP LOCKED` không phải strict FEFO**

Transaction A khóa lô hết hạn sớm. Transaction B dùng `FOR UPDATE SKIP LOCKED`, allocate lô sau. Assert lô sau được chọn khi lô trước vẫn còn available tại thời điểm lock.

- [ ] **Step 6: Chạy baseline suite**

Run:

```powershell
dotnet test backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj --filter BaselineTests
```

Expected: PASS vì mỗi test assert anomaly đã được tái hiện.

- [ ] **Step 7: Commit**

```powershell
git add backend/FoodDelivery.Infrastructure.Tests/Messaging
git commit -m "test: reproduce messaging and inventory failure windows"
```

---

### Task 3: Định nghĩa integration-event contract và configuration

**Files:**
- Create: `backend/FoodDelivery.Application/Abstractions/Messaging/IIntegrationEvent.cs`
- Create: `backend/FoodDelivery.Application/Abstractions/Messaging/IIntegrationEventPublisher.cs`
- Create: `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqOptions.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Unit/RabbitMqOptionsTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Unit/IntegrationEventSerializationTests.cs`
- Modify: `backend/FoodDelivery.Infrastructure/FoodDelivery.Infrastructure.csproj`

**Interfaces:**
- Produces:

```csharp
public interface IIntegrationEvent
{
    Guid MessageId { get; }
    string EventName { get; }
    int ContractVersion { get; }
    DateTimeOffset OccurredAtUtc { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
```

- [ ] **Step 1: Viết options validation tests**

Test thiếu host, credentials, vhost, exchange hoặc connection name; test port ngoài `1..65535`; test configuration hợp lệ.

- [ ] **Step 2: Chạy unit tests để xác nhận failure**

Expected: FAIL vì contracts/options chưa tồn tại.

- [ ] **Step 3: Thêm package chính thức**

```xml
<PackageReference Include="RabbitMQ.Client" Version="7.2.2" />
```

- [ ] **Step 4: Implement contracts và options**

`RabbitMqOptions` chứa `SectionName = "RabbitMq"`, `HostName`, `Port`, `UserName`, `Password`, `VirtualHost`, `ConnectionName`, `ExchangeName`, `NetworkRecoverySeconds` và `RequestedHeartbeatSeconds`.

- [ ] **Step 5: Chạy unit tests**

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/FoodDelivery.Application backend/FoodDelivery.Infrastructure backend/FoodDelivery.Infrastructure.Tests
git commit -m "feat: add integration event contracts"
```

---

### Task 4: Implement connection manager và confirmed publisher

**Files:**
- Create: `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqConnectionManager.cs`
- Create: `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqPublisher.cs`
- Create: `backend/FoodDelivery.Infrastructure/Messaging/RabbitMqHealthCheck.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/RabbitMqPublisherTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/RabbitMqConnectionRecoveryTests.cs`
- Modify: `backend/FoodDelivery.Infrastructure/DependencyInjection.cs`
- Modify: `backend/FoodDelivery.Api/Program.cs`
- Modify: `backend/FoodDelivery.Api/appsettings.json`

**Interfaces:**
- Consumes: `IIntegrationEvent`, `IIntegrationEventPublisher`, `RabbitMqOptions`.
- Produces: singleton connection owner, serialized confirmed publisher và `/health/ready`.

- [ ] **Step 1: Viết publisher integration tests trước**

Test các guarantee:

- 100 concurrent calls vẫn dùng một `IConnection`.
- Exchange được declare durable, direct và non-auto-delete.
- Message có `DeliveryMode = 2`, `ContentType = application/json`, message ID, type, correlation và version headers.
- `mandatory = true`; routing key không có binding tạo returned-message outcome quan sát được.
- Broker dừng trong publish làm call thất bại; client không báo thành công giả.
- Broker khởi động lại, connection recovery xong thì publish tiếp theo thành công.

- [ ] **Step 2: Chạy tests để xác nhận failure**

Expected: FAIL vì publisher chưa tồn tại.

- [ ] **Step 3: Implement connection manager**

Connection manager dùng `SemaphoreSlim` để initial connect một lần, trả connection đang mở, cấu hình automatic recovery/topology recovery và dispose khi host dừng. Initial connection failure đi ra caller; không retry vô hạn trong startup.

- [ ] **Step 4: Implement publisher channel ownership**

Publisher giữ một long-lived channel và một `SemaphoreSlim(1, 1)`. Channel bật publisher-confirmation tracking, declare exchange và publish tuần tự. Đây là deliberate baseline: một channel/process; tăng channel pool chỉ khi confirm throughput đo được không đạt target.

- [ ] **Step 5: Implement wire contract**

Body dùng `System.Text.Json`. Properties và headers:

```text
MessageId       = event.MessageId
Type            = event.EventName
ContentType     = application/json
DeliveryMode    = persistent
CorrelationId   = event.CorrelationId
x-event-version = event.ContractVersion
x-tenant-id     = event.TenantId nếu có
```

- [ ] **Step 6: Register DI và readiness**

Bind/validate `RabbitMq` section, register connection manager/publisher singleton, register `IIntegrationEventPublisher`, thêm health check và map `/health/ready`.

- [ ] **Step 7: Chạy publisher integration suite**

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add backend/FoodDelivery.Application backend/FoodDelivery.Infrastructure backend/FoodDelivery.Api backend/FoodDelivery.Infrastructure.Tests
git commit -m "feat: add confirmed RabbitMQ publisher"
```

---

### Task 5: Chứng minh Outbox, Inbox và strict FEFO bằng test-only reference implementation

**Files:**
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Simulation/TestOutboxDispatcher.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Simulation/TestInventoryConsumer.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Simulation/TestStrictFefoAllocator.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/OutboxRecoveryTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/InboxIdempotencyTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/StrictFefoContentionTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Integration/OrderingVersionTests.cs`

**Interfaces:**
- Test-only; production projects không reference các types này.
- Produces: executable proof cho Outbox claim lease, duplicate-safe Inbox và atomic multi-lot allocation.

- [ ] **Step 1: Viết correctness tests**

Các tests phải cover:

```text
business commit + Outbox atomic
dispatcher crash before publish
dispatcher crash after confirm before mark-sent
consumer commit before ACK then crash
same message delivered to two consumer instances
same business key across two tenant/shop scopes
two reservations competing for the same earliest lots
stale aggregate version arriving after newer version
```

- [ ] **Step 2: Chạy để xác nhận failure với baseline implementation**

Expected: ít nhất Outbox recovery, duplicate consumer và atomic FEFO tests FAIL.

- [ ] **Step 3: Implement test Outbox claim**

Claim dùng transaction, `FOR UPDATE SKIP LOCKED`, `lockedBy`, `lockedUntilUtc` và bounded batch. Publisher Confirm xảy ra trước mark-sent. Lease hết hạn cho phép instance khác claim lại; duplicate publish được chấp nhận.

- [ ] **Step 4: Implement Inbox transaction**

Unique key dùng `(consumerName, tenantId, shopId, messageId)`. Inbox insert, reservation/allocation và outgoing Outbox cùng commit. Duplicate đọc stored outcome và không áp dụng mutation lần hai.

- [ ] **Step 5: Implement strict FEFO reference allocator**

Candidate query có tenant/shop/product predicate, order `expiresAt`, `receivedAt`, `id`, dùng `FOR UPDATE` không `SKIP LOCKED`. Mọi instance lock cùng deterministic order. Conditional update kiểm tra available quantity.

- [ ] **Step 6: Chạy correctness suite nhiều lần**

```powershell
1..20 | ForEach-Object {
  dotnet test backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj --filter "OutboxRecoveryTests|InboxIdempotencyTests|StrictFefoContentionTests|OrderingVersionTests"
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: 20/20 runs PASS; inventory invariants và logical-effect count giữ nguyên.

- [ ] **Step 7: Commit**

```powershell
git add backend/FoodDelivery.Infrastructure.Tests/Messaging
git commit -m "test: prove reliable messaging invariants"
```

---

### Task 6: Chạy multi-instance, cluster failover và load profiles

**Files:**
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Load/RabbitMqLoadOptions.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Load/RabbitMqLoadResult.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Load/MultiInstanceLoadTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Load/QuorumFailoverTests.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Messaging/Load/ManagementSnapshotReader.cs`
- Create: `backend/scripts/run-rabbitmq-load.ps1`
- Create: `backend/test-results/rabbitmq/.gitkeep`

**Interfaces:**
- Inputs: environment variables `RABBIT_LOAD_*` tương ứng spec section 10.5.
- Output: timestamped JSON gồm environment, topology, parameters, Management API snapshots, percentiles và invariants.

- [ ] **Step 1: Viết load option parsing tests**

Test defaults, invalid negative/range values và environment overrides. Không bắt đầu I/O nếu options không hợp lệ.

- [ ] **Step 2: Implement metrics collection**

Đo bằng `Stopwatch.GetTimestamp`; giữ histogram samples cho reference profile 10.000 messages. Result gồm publish-confirm p50/p95/p99, end-to-end p50/p95/p99, throughput, Ready/Unacked peaks, redelivery, retry, DLQ, deadlock và business outcomes.

- [ ] **Step 3: Chạy smoke profile**

```powershell
$env:RABBIT_LOAD_MESSAGE_COUNT='1000'
$env:RABBIT_LOAD_CONSUMER_INSTANCES='2'
$env:RABBIT_LOAD_CONSUMERS_PER_INSTANCE='2'
backend/scripts/run-rabbitmq-load.ps1 -Profile Smoke
```

Expected: zero missing logical intents, zero duplicate logical effects, zero negative lots.

- [ ] **Step 4: Chạy reference profile**

Sử dụng defaults trong spec: 10.000 messages, 3 publishers, 4 consumer instances, 4 consumers/instance, prefetch 16, 5% duplicate, 2% transient failure và 1% post-commit crash.

- [ ] **Step 5: Chạy quorum failover profile**

Khởi động ba nodes, declare quorum queue, bắt đầu load, dừng queue leader trong khi publish/consume, chờ election/recovery rồi kiểm tra confirm failures, redelivery và invariants. Không tự động xóa cluster logs trước khi snapshot được ghi.

- [ ] **Step 6: Đánh giá feasibility**

Không chỉ báo throughput. So sánh global concurrency, maximum unacked, MySQL pool, transaction p95, queue age và broker node resource. Ghi bottleneck quan sát được và parameter nào làm kết quả xấu đi.

- [ ] **Step 7: Commit runner, không commit raw secrets/logs**

```powershell
git add backend/FoodDelivery.Infrastructure.Tests/Messaging/Load backend/scripts backend/test-results/rabbitmq/.gitkeep
git commit -m "test: add RabbitMQ multi-instance load harness"
```

---

### Task 7: Viết tài liệu từ bằng chứng đã kiểm chứng

**Files:**
- Create: `C:/Workspace/MyProject/base-angular/src/assets/docs/dotnet-core/rabbitmq-reliable-messaging.md`
- Modify: `C:/Workspace/MyProject/base-angular/src/app/app-routing.module.ts`
- Modify: `C:/Workspace/MyProject/base-angular/src/app/layout/shell/menu.config.ts`

**Interfaces:**
- Consumes: spec, test source và JSON result từ Tasks 2, 5, 6.
- Produces: route `/dotnet-core/rabbitmq-reliable-messaging` trong nhóm `2. Nâng cao`.

- [ ] **Step 1: Viết chapter skeleton**

Giữ đúng dependency order:

```text
Job Queue và message broker
RabbitMQ runtime architecture
Connection, Channel, Exchange, Queue và Binding
Publish path và routing
Consumer lifecycle, ACK/NACK và prefetch
Durability và Publisher Confirm
At-least-once và idempotency
Retry, DLQ và poison message
Outbox, Inbox và reconciliation
Multi-instance và ordering
Multi-tenancy
Multi-lot inventory và strict FEFO
Capacity planning và cluster feasibility
Management UI, metrics và runbook
Security/deployment/compatibility
.NET 6 implementation
Business case catalogue
Verification và reference load run
Keyword reference
```

- [ ] **Step 2: Viết mỗi decision theo evidence flow**

Mỗi case trình bày `thiết kế trực giác → giả định → test tái hiện → observed values → invariant → mechanism → trade-off`. Không dùng nhãn cấp độ.

- [ ] **Step 3: Nhúng bốn test excerpts**

Chỉ nhúng barrier lost-update, commit-before-ACK duplicate, Inbox duplicate và strict FEFO contention. Link tới full test files; không sao chép fixture/schema boilerplate.

- [ ] **Step 4: Ghi RabbitMQ value thực tế**

Từ Management API snapshot, hiển thị message properties/headers, Ready/Unacked/Total/Consumers, rates, Redelivered và `x-death`. Mỗi bảng kết quả ghi environment, topology và timestamp của reference run.

- [ ] **Step 5: Viết load-parameter derivation**

Giải công thức global concurrency, maximum unacked, `arrivalRate * duration`, headroom, DB/downstream ceilings. So sánh ít nhất hai parameter sets đã chạy; không gọi default là production recommendation.

- [ ] **Step 6: Thêm tối thiểu tám Mermaid diagrams**

Bao phủ routing, publish confirm, ACK failure window, retry/DLQ, Outbox/Inbox, competing consumers, quorum failover và FEFO allocation.

- [ ] **Step 7: Thêm route/menu**

Route dùng `MarkdownDocComponent`; menu label `RabbitMQ và Reliable Messaging` đặt sau `Garbage Collector (GC)`.

- [ ] **Step 8: Build base-angular**

```powershell
& 'C:\Program Files\Volta\volta.exe' run --node 20.18.0 npm run build
```

Expected: build exit `0`; chỉ chấp nhận các warning baseline đã biết nếu không tăng.

- [ ] **Step 9: Commit base-angular riêng**

```powershell
git add -- src/assets/docs/dotnet-core/rabbitmq-reliable-messaging.md src/app/app-routing.module.ts src/app/layout/shell/menu.config.ts
git commit -m "docs: add RabbitMQ reliability guide"
git push origin dev
```

---

### Task 8: Cập nhật tài liệu kiến trúc food-delivery và chạy verification cuối

**Files:**
- Modify: `backend/docs/architecture/event-architecture.md`
- Modify: `backend/docs/architecture/backend-runtime-flow.md`
- Modify: `backend/docs/README.md`

**Interfaces:**
- Consumes: runtime behavior đã kiểm chứng ở Tasks 4–6.
- Produces: source of truth cho foundation hiện tại và upgrade path Outbox/consumer.

- [ ] **Step 1: Cập nhật Event Architecture**

Ghi rõ foundation hiện có: confirmed publisher, connection ownership và giới hạn direct publish. Outbox/Inbox vẫn là upgrade bắt buộc khi business transaction xuất hiện; không tuyên bố direct publisher giải quyết dual-write.

- [ ] **Step 2: Cập nhật runtime flow**

Thêm startup connection/readiness và publish flow. Business write flow vẫn không publish external message bên trong transaction.

- [ ] **Step 3: Chạy toàn bộ backend tests**

```powershell
dotnet test backend/FoodDelivery.Domain.Tests/FoodDelivery.Domain.Tests.csproj
dotnet test backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj
dotnet test backend/FoodDelivery.Api.Tests/FoodDelivery.Api.Tests.csproj
```

Expected: tất cả PASS khi MySQL/RabbitMQ profiles cần thiết đang chạy.

- [ ] **Step 4: Build backend**

```powershell
dotnet build backend/FoodDelivery.Api/FoodDelivery.Api.csproj
```

Expected: exit `0`, không có compile error.

- [ ] **Step 5: Kiểm tra source và generated evidence**

```powershell
git diff --check
rg -n "TODO|TBD|junior|middle|senior" backend/docs C:/Workspace/MyProject/base-angular/src/assets/docs/dotnet-core/rabbitmq-reliable-messaging.md
```

Expected: không có placeholder hoặc nhãn cấp độ trong tài liệu.

- [ ] **Step 6: Commit và push food-delivery**

```powershell
git add -- backend
git commit -m "feat: add RabbitMQ messaging foundation"
git push origin dev
```

---

## Execution checkpoints

1. Sau Task 2: phải có bằng chứng baseline anomalies xuất hiện có chủ đích.
2. Sau Task 4: publisher foundation phải pass integration tests trên broker thật.
3. Sau Task 5: correctness tests phải pass 20 lần liên tiếp.
4. Sau Task 6: phải có reference load result và quorum failover result; nếu không chạy được, tài liệu không được ghi kết quả giả.
5. Task 7 chỉ bắt đầu sau bốn checkpoints trên.
6. Mọi claim cuối cùng phải dựa trên fresh build/test output và clean scoped diff.
