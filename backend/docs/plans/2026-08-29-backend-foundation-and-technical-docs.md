# Backend Foundation and Technical Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện technical foundation có nhu cầu dự đoán chắc chắn và tổ chức lại toàn bộ architecture docs thành các tài liệu kỹ thuật có một nguồn sự thật.

**Architecture:** Giữ Clean Architecture/DDD bốn project hiện tại. Bổ sung Domain primitive và optimistic concurrency ở điểm dùng chung; giữ transaction ownership trong EF/Dapper UOW. Business names chỉ dùng làm ví dụ, không scaffold business module.

**Tech Stack:** .NET 6, ASP.NET Core, EF Core 6, Dapper, Pomelo MySQL, MySqlConnector, Mermaid Markdown.

**Spec:** Yêu cầu đã chốt trong hội thoại; tài liệu chuẩn sau thay đổi nằm tại `backend/docs/README.md`.

## Global Constraints

- Keyword kỹ thuật dùng English; business examples dùng tiếng Việt không dấu trong code.
- Không thêm Generic Repository, mediator framework, broker, Outbox, cache hoặc business API.
- Không viết thêm test documentation; vẫn chạy test hiện có để chống regression.
- Mỗi technical concept có owner, vấn đề giải quyết, giới hạn và case minh họa.
- Mỗi quyết định trong docs theo format `Context → Senior reasoning → Decision → Problem solved → Business example → Trade-off/limit → When to upgrade`.
- Không dùng business example để hợp thức hóa abstraction chưa có consumer; phân biệt rõ current foundation với future example.

---

### Task 1: Domain and concurrency foundation

**Files:**
- Create: `backend/FoodDelivery.Domain/Common/ValueObject.cs`
- Modify: `backend/FoodDelivery.Domain/Common/AggregateRoot.cs`
- Modify: `backend/FoodDelivery.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `backend/FoodDelivery.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`
- Create: EF migration for concurrency token

- [x] Add structural equality base for future Value Objects.
- [x] Add a concurrency token to Aggregate Root.
- [x] Rotate the token before modified aggregates are saved and configure it as an EF concurrency token.
- [x] Create migration and run current build/tests.

### Task 2: Canonical technical documentation

**Files:**
- Create: `backend/docs/README.md`
- Create: `backend/docs/architecture/architecture-overview.md`
- Create: `backend/docs/architecture/ddd-layering-and-code-placement.md`
- Create: `backend/docs/architecture/backend-runtime-flow.md`
- Create: `backend/docs/architecture/persistence-transactions-and-concurrency.md`
- Create: `backend/docs/architecture/event-architecture.md`
- Create: `backend/docs/architecture/multi-tenancy-foundation.md`
- Delete: superseded architecture files

- [x] Write a reading order and source-of-truth map.
- [x] Explain dependency direction and abstraction placement with business examples.
- [x] Document end-to-end backend request, success and failure flows.
- [x] Consolidate EF/Dapper UOW internals, MySQL isolation, locking, deadlock and concurrency strategy.
- [x] Separate Event Architecture and Multi-tenancy technical foundations.
- [x] Remove duplicated Phase 1/design/thinking documents and fix links.

### Task 3: Verification and delivery

- [x] Validate UTF-8 and Mermaid fences; scan placeholders and stale links.
- [x] Run full build, current tests and `git diff --check`.
- [x] Commit and push `dev`.
