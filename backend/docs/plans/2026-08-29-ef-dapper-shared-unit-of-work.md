# EF Core + Dapper Shared Unit of Work Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tạo một Unit of Work dùng chung connection và transaction cho EF Core + Dapper writes.

**Architecture:** Factory mở MySQL connection/transaction, tạo `ApplicationDbContext` trên connection đó và enlist EF bằng `UseTransaction`. `EfDapperUnitOfWork` là owner duy nhất của commit, rollback, Domain Events và disposal.

**Tech Stack:** .NET 6, EF Core 6.0.36, Pomelo 6.0.2, MySqlConnector, Dapper 2.1.35, xUnit, SQLite integration tests.

**Spec:** `backend/docs/architecture/persistence-transactions-and-concurrency.md`

## Global Constraints

- Cùng object instance `DbConnection` và `DbTransaction` cho EF/Dapper writes.
- Không thêm generic repository, `TransactionScope`, nested UOW hoặc automatic retry.
- Test RED trước production code; full suite phải GREEN.

---

### Task 1: Application contracts

**Files:** Create `IUnitOfWork.cs`, `IUnitOfWorkFactory.cs` under `FoodDelivery.Application/Abstractions`.

- [x] Define `IUnitOfWork` exposing `IApplicationDbContext`, `DbConnection`, `DbTransaction`, `CommitAsync`, `RollbackAsync`, `IAsyncDisposable`.
- [x] Define `IUnitOfWorkFactory.CreateAsync(CancellationToken)`.
- [x] Build Application project.

### Task 2: Shared transaction behavior

**Files:** Create `EfDapperUnitOfWork.cs`; simplify `ApplicationDbContext.cs`; replace its old event lifecycle tests with `EfDapperUnitOfWorkTests.cs`.

- [x] Write SQLite test: Dapper insert + EF insert + commit persists both and dispatches event once.
- [x] Write SQLite test: Dapper insert + EF insert + dispatcher failure rolls both back and retains event.
- [x] Observe RED because `EfDapperUnitOfWork` does not exist.
- [x] Implement one-shot commit/rollback/dispose state machine and Domain Event lifecycle.
- [x] Observe focused tests GREEN.

### Task 3: Production factory

**Files:** Create `EfDapperUnitOfWorkFactory.cs`; modify `DependencyInjection.cs` and package references.

- [x] Factory opens `MySqlConnection`, begins transaction, creates DbContext on the open connection, enlists it with `UseTransactionAsync`, and cleans partial resources on failure.
- [x] Register `IUnitOfWorkFactory` scoped; do not register Unit of Work instances in DI.
- [x] Build full solution.

### Task 4: Senior thinking documentation

**Files:** Consolidated into `backend/docs/architecture/persistence-transactions-and-concurrency.md`.

- [x] Document reasoning sequence: requirements, ownership, transaction boundary, lifecycle, failure matrix, concurrency, testing and rejected alternatives.
- [ ] Run full tests, `git diff --check`, commit and push `dev`.
