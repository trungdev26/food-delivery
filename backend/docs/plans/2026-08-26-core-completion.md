# Core Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện Core Phase 1 với DomainException, transactional Domain Event lifecycle và tài liệu backend tập trung.

**Architecture:** `AggregateRoot` giữ event; `ApplicationDbContext.SaveChangesAsync` ghi Aggregate và dispatch event trong cùng relational transaction, chỉ clear event sau commit. API middleware ánh xạ `DomainException` mà Domain không phụ thuộc HTTP.

**Tech Stack:** .NET 6, EF Core 6.0.36, Pomelo MySQL 6.0.2, xUnit.

**Spec:** `backend/docs/architecture/ddd-event-architecture-phase-1.md`

## Global Constraints

- Không thêm MediatR, Result framework, ValueObject base, Specification hoặc generic Repository.
- Không sửa các thay đổi người dùng ngoài file thuộc task.
- Test RED trước production code; full suite phải GREEN.

---

### Task 1: Consolidate backend docs

**Files:** Consolidate the architecture spec and existing plan under `backend/docs/architecture/` and `backend/docs/plans/`; update internal links.

- [ ] Move files with history-preserving Git rename.
- [ ] Verify old files no longer exist under the repository-root `docs/superpowers/` directory.

### Task 2: DomainException

**Files:** Create `FoodDelivery.Domain/Common/DomainException.cs`; test in `FoodDelivery.Api.Tests/Common/ExceptionHandlingMiddlewareTests.cs`; modify API exception middleware.

- [ ] Test that `new DomainException("SHOP_INACTIVE", "Shop inactive")` becomes HTTP 409 with the same code.
- [ ] Run the focused test and observe RED because middleware does not catch DomainException.
- [ ] Add `DomainException(string code, string message)` and catch it before the generic exception branch.
- [ ] Run focused test and observe GREEN.

### Task 3: Transactional Domain Event lifecycle

**Files:** Modify `ApplicationDbContext.cs`; create `ApplicationDbContextEventTests.cs`.

- [ ] Test that SaveChanges dispatches one event and clears it after success.
- [ ] Test that handler failure keeps the event for retry.
- [ ] Observe RED because current DbContext does not dispatch events.
- [ ] Inject `IDomainEventDispatcher`; for relational providers begin a transaction, save, dispatch, save handler changes, commit, then clear events; rollback and retain events on failure.
- [ ] Run focused tests and full solution tests GREEN.

### Task 4: Remove duplicate placeholder Core

**Files:** Delete unreferenced `backend/Core/` after verifying `FoodDelivery.slnx` has no reference.

- [ ] Run `rg -n "backend/Core|Core/FoodDelivery.Core|FoodDelivery.Core" backend --glob '!Core/**'` and require no runtime reference.
- [ ] Delete only `backend/Core/`.
- [ ] Run `dotnet build backend/FoodDelivery.slnx` and `dotnet test backend/FoodDelivery.slnx --no-build`.
