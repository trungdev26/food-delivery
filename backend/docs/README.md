# Backend Technical Documentation

Đây là entry point và reading order chính thức của backend. Mỗi chủ đề có một source of truth; business type như `Shop`, `HangHoa`, `DonHang` được dùng để kiểm tra thiết kế, không khẳng định module đó đã được triển khai.

## Reading order

1. [Architecture Overview](architecture/architecture-overview.md) — ranh giới hệ thống và lý do phân chia.
2. [DDD Layering and Code Placement](architecture/ddd-layering-and-code-placement.md) — quyết định code thuộc project nào.
3. [Backend Runtime Flow](architecture/backend-runtime-flow.md) — startup, request, transaction và publish flow.
4. [Multi-tenancy Foundation](architecture/multi-tenancy-foundation.md) — resolve và isolate Tenant.
5. [Persistence, Transactions and Concurrency](architecture/persistence-transactions-and-concurrency.md) — EF Core, Dapper, Unit of Work, MySQL isolation và locking.
6. [Event Architecture](architecture/event-architecture.md) — Domain Event, Integration Event và reliability boundary.
7. [RabbitMQ Reliable Messaging](architecture/rabbitmq-reliable-messaging.md) — broker internals, ACK, retry, Outbox/Inbox, multi-instance, FEFO và load evidence.

## Cách đọc một quyết định kỹ thuật

```text
Business case → invariant → failure window → test tái hiện
              → mechanism → trade-off → tín hiệu cần nâng cấp
```

Cách trình bày này tránh chọn RabbitMQ, cache, lock hoặc một design pattern chỉ vì chúng phổ biến. Mỗi thành phần phải bảo vệ một invariant hoặc giải quyết một bottleneck đo được.

## Foundation đang có

- Clean Architecture gồm Domain, Application, Infrastructure và API.
- Domain primitives: Entity, Aggregate Root, Value Object, Domain Event và Domain Exception.
- Multi-tenant request context từ authenticated claim hoặc subdomain.
- EF Core và Dapper dùng chung connection/transaction cho write use case.
- In-process Domain Event trong cùng database transaction.
- Optimistic concurrency bằng `ConcurrencyToken` trên Aggregate Root.
- RabbitMQ long-lived connection, confirmed publisher, mandatory routing và readiness check.
- Centralized exception response và composition root tại API.

## Ranh giới chưa đưa vào production

Outbox, Inbox, retry/DLQ consumer, reconciliation và strict FEFO allocator đã được kiểm chứng bằng integration/load test trên MySQL và RabbitMQ thật, nhưng vẫn nằm trong test project. Chúng được đưa vào production cùng business flow sở hữu transaction và schema tương ứng; không tạo table, worker hoặc abstraction dự phòng chỉ để “có sẵn”.

Generic Repository, CQRS/Mediator framework, distributed cache và automatic business retry cũng chưa có use case buộc phải tồn tại.
