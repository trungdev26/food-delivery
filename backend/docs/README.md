# Backend Technical Documentation

Đây là entry point và reading order chính thức của backend. Mỗi chủ đề chỉ có một source of truth.

## Reading order

1. [Architecture Overview](architecture/architecture-overview.md) — hệ thống được chia như thế nào và vì sao.
2. [DDD Layering and Code Placement](architecture/ddd-layering-and-code-placement.md) — quyết định code thuộc project/folder nào.
3. [Backend Runtime Flow](architecture/backend-runtime-flow.md) — request chạy xuyên suốt backend ra sao.
4. [Multi-tenancy Foundation](architecture/multi-tenancy-foundation.md) — resolve và isolate Tenant.
5. [Persistence, Transactions and Concurrency](architecture/persistence-transactions-and-concurrency.md) — EF Core, Dapper, UOW, MySQL isolation và locking.
6. [Event Architecture](architecture/event-architecture.md) — Domain Event hiện tại và đường nâng cấp Outbox.

## Cách đọc quyết định kỹ thuật

Mỗi tài liệu trả lời theo chuỗi:

```text
Context → Senior reasoning → Decision → Problem solved
        → Business example → Trade-off/limit → When to upgrade
```

Business type như `Shop`, `HangHoa`, `DonHang` là ví dụ để kiểm tra thiết kế. Chúng không đồng nghĩa module đó đã được implement.

## Current foundation

- Clean Architecture bốn project: Domain, Application, Infrastructure, API.
- Domain primitives: Entity, Aggregate Root, Value Object, Domain Event, Domain Exception.
- Multi-tenant request context từ authenticated claim hoặc subdomain.
- EF Core và Dapper dùng cùng connection/transaction cho write use case.
- In-process Domain Event trong cùng database transaction.
- Optimistic concurrency bằng `ConcurrencyToken` trên Aggregate Root.
- Centralized exception response và composition root tại API.

## Chưa thêm có chủ đích

Generic Repository, CQRS/Mediator framework, message broker, Outbox, distributed cache, automatic retry và business module chưa có use case thật. Chúng là upgrade path, không phải foundation mặc định.
