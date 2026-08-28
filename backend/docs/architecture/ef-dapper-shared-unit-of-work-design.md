# EF Core + Dapper Shared Unit of Work Design

## 1. Mục tiêu

Cho phép một Application use case vừa dùng EF Core vừa dùng Dapper để ghi dữ liệu nhưng vẫn bảo đảm tính atomic:

```text
EF Core write + Dapper write
        │
        ▼
same DbConnection + same DbTransaction
        │
        ├── commit tất cả
        └── rollback tất cả
```

Thiết kế dành cho Modular Monolith Phase 1, không tạo distributed transaction, generic repository hoặc transaction framework riêng.

## 2. Quyết định

`EfDapperUnitOfWork` sở hữu toàn bộ lifetime của:

- `DbConnection`.
- `DbTransaction`.
- `ApplicationDbContext`.
- Commit, rollback và dispose.
- Domain Event lifecycle trong transaction.

`IUnitOfWorkFactory` là nơi duy nhất khởi tạo một write Unit of Work mới. Factory không được dùng như service locator và không tạo repository.

## 3. Bản chất connection của EF Core và Dapper

### 3.1 Nền thấp nhất là ADO.NET

Cả EF Core và Dapper cuối cùng đều chạy trên ADO.NET:

```text
Application code
   ├── EF Core
   └── Dapper
          │
          ▼
DbConnection + DbCommand + DbTransaction
          │
          ▼
MySqlConnector
          │
          ▼
MySQL
```

`MySqlConnection` kế thừa `DbConnection`; `MySqlTransaction` kế thừa `DbTransaction`. Đây mới là tài nguyên thật giữ database session và transaction state.

### 3.2 EF Core quản lý connection như thế nào

`DbContext` không phải connection. Nó giữ change tracker, model metadata và một relational provider. Provider dùng một `DbConnection` bên dưới để tạo `DbCommand`.

Khi cấu hình bằng connection string:

```csharp
options.UseMySql(connectionString, serverVersion);
```

EF Core thường tự tạo connection, mở ngay trước database operation và đóng sau operation nếu chính EF đã mở nó. Khi gọi `BeginTransaction`, EF tạo transaction trên connection đó và giữ transaction trong `Database.CurrentTransaction`.

Khi cấu hình bằng connection instance:

```csharp
options.UseMySql(connection, serverVersion, contextOwnsConnection: false);
```

EF Core dùng đúng instance được truyền vào. `contextOwnsConnection: false` nói rằng DbContext không được dispose connection vì Unit of Work mới là owner.

Sau khi transaction được tạo ngoài DbContext, lời gọi:

```csharp
dbContext.Database.UseTransaction(transaction);
```

cho EF Core biết mọi command của nó phải chạy trong transaction đó.

### 3.3 Dapper quản lý connection như thế nào

Dapper không sở hữu connection manager hay change tracker. Phần lớn API của Dapper là extension method trên `IDbConnection`/`DbConnection`:

```csharp
await connection.ExecuteAsync(
    new CommandDefinition(sql, parameters, transaction));
```

Dapper tạo `DbCommand` từ connection được caller đưa vào. Nếu caller truyền transaction, Dapper gán transaction đó cho command. Nếu không truyền, command chạy ngoài transaction hiện tại hoặc provider sẽ từ chối tùy trạng thái connection.

Dapper có thể tự mở một connection đang đóng cho một operation rồi đóng lại, nhưng write Unit of Work không dựa vào hành vi ngầm này. Factory mở connection trước và giữ nó mở tới commit/rollback.

### 3.4 Tại sao EF Core và Dapper dùng chung được

Chúng không “tích hợp đặc biệt” với nhau. Chúng dùng chung được vì cùng nói chuyện qua ADO.NET:

```text
same object reference: DbConnection
same object reference: DbTransaction
```

Hai connection có cùng connection string vẫn là hai database session khác nhau và không tạo atomic transaction:

```text
Connection A + Transaction A → EF write
Connection B + Transaction B → Dapper write
```

Muốn commit/rollback cùng nhau, cả EF command và Dapper command phải gắn vào **cùng transaction instance**, mà transaction đó chỉ hợp lệ trên **chính connection đã tạo ra nó**.

Đó là lý do Factory phải tạo connection và transaction trước, sau đó truyền đúng hai instance này cho cả DbContext và Dapper.

### 3.5 Ai được mở, đóng và dispose

Trong thiết kế này ownership là duy nhất:

| Tài nguyên | Owner | EF Core | Dapper |
|---|---|---|---|
| `DbConnection` | Unit of Work | Mượn | Mượn |
| `DbTransaction` | Unit of Work | Enlist bằng `UseTransaction` | Nhận qua `CommandDefinition` |
| `ApplicationDbContext` | Unit of Work | Sở hữu tracking | Không dùng |
| Commit/Rollback | Unit of Work | Không tự commit | Không tự commit |

Quy tắc một owner ngăn double-dispose, commit nhầm transaction và connection bị đóng giữa use case.

### 3.6 Giới hạn vận hành

- Không chạy song song EF và Dapper commands trên cùng connection; một connection không mặc định hỗ trợ nhiều active command.
- Không giữ Unit of Work qua nhiều HTTP request.
- Không trả lazy EF query ra ngoài lifetime Unit of Work.
- Không để repository tự mở connection mới trong write flow.
- Connection pooling vẫn do MySqlConnector quản lý; dispose connection trả physical connection về pool, không nhất thiết đóng socket thật.

## 4. Interfaces

```csharp
public interface IUnitOfWork : IAsyncDisposable
{
    IApplicationDbContext DbContext { get; }
    DbConnection Connection { get; }
    DbTransaction Transaction { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> CreateAsync(
        CancellationToken cancellationToken = default);
}
```

Application phụ thuộc `System.Data.Common`, không phụ thuộc `MySqlConnection`, EF transaction implementation hoặc Dapper concrete type.

## 5. Ownership và lifetime

```text
IUnitOfWorkFactory.CreateAsync
  │
  ├── create MySqlConnection
  ├── OpenAsync
  ├── BeginTransactionAsync
  ├── create ApplicationDbContext trên connection đó
  ├── Database.UseTransaction(transaction)
  └── return EfDapperUnitOfWork
```

Một Unit of Work phục vụ đúng một business transaction. Không đăng ký `IUnitOfWork` dạng singleton hoặc tái sử dụng qua nhiều request.

`DisposeAsync` giải phóng theo thứ tự:

```text
ApplicationDbContext
→ DbTransaction
→ DbConnection
```

Nếu caller thoát mà chưa commit, dispose phải rollback transaction đang active.

## 6. Application flow

```csharp
await using var uow = await unitOfWorkFactory.CreateAsync(cancellationToken);

uow.DbContext.Tenants.Add(tenant);

await uow.Connection.ExecuteAsync(
    new CommandDefinition(
        sql,
        parameters,
        transaction: uow.Transaction,
        cancellationToken: cancellationToken));

await uow.CommitAsync(cancellationToken);
```

Mọi Dapper write trong use case bắt buộc truyền `uow.Transaction`. Không gọi `new MySqlConnection`, connection factory khác hoặc `TransactionScope` bên trong transaction.

## 7. Commit flow

`CommitAsync` chỉ được gọi một lần:

```text
EF Core SaveChanges
→ collect Domain Events từ tracked Aggregates
→ dispatch Domain Events in-process
→ EF Core SaveChanges cho thay đổi từ handlers
→ commit DbTransaction
→ clear Domain Events
```

Domain Events chỉ được clear sau khi database commit thành công. Nếu SaveChanges, event handler hoặc commit lỗi:

```text
rollback DbTransaction
→ giữ Domain Events trên Aggregate
→ throw lại exception gốc
```

Side effect ngoài database như email, webhook và broker không chạy trực tiếp trong flow này. Khi cần reliability cho side effect ngoài, event handler ghi `OutboxMessage` trong cùng transaction.

## 8. Refactor Core hiện tại

`ApplicationDbContext.SaveChangesAsync` hiện đang tự mở transaction và dispatch Domain Events. Sau thay đổi:

- `ApplicationDbContext` trở lại nhiệm vụ tracking/persistence của EF Core.
- `EfDapperUnitOfWork.CommitAsync` sở hữu transaction và Domain Event lifecycle.
- Các write use case dùng `IUnitOfWorkFactory`.
- Read-only middleware/query có thể dùng scoped `ApplicationDbContext`; không bắt buộc mở transaction.
- Migration vẫn dùng `ApplicationDbContext` qua DI.

Chỉ có một nơi commit business transaction: `EfDapperUnitOfWork.CommitAsync`.

## 9. Factory

Production factory dùng connection string `ConnectionStrings:Default` và MySQL server version hiện có. Factory tạo instance mới mỗi lần gọi, không giữ mutable state.

Factory chịu trách nhiệm cleanup nếu lỗi xảy ra giữa các bước tạo connection, transaction và DbContext. Tài nguyên đã tạo phải được dispose trước khi throw lại.

Không tạo `IDbConnectionFactory` thứ hai trong Phase 1. Read-only Dapper query chưa có consumer thật; khi xuất hiện, có thể dùng connection ngắn hạn riêng mà không liên quan write Unit of Work.

## 10. Failure cases

| Failure | Kết quả bắt buộc |
|---|---|
| Open connection lỗi | Không tạo transaction/DbContext; throw |
| Begin transaction lỗi | Dispose connection; throw |
| EF SaveChanges lỗi | Rollback; không dispatch event |
| Dapper command lỗi trước commit | Caller thoát; rollback khi dispose |
| Domain Event handler lỗi | Rollback EF và Dapper writes; giữ event |
| Database commit lỗi | Rollback nếu provider còn cho phép; giữ event; throw |
| Commit gọi lần hai | Throw `InvalidOperationException` |
| Rollback gọi sau commit | Không thay đổi dữ liệu đã commit; throw để lộ misuse |
| Dispose khi chưa commit | Rollback rồi dispose |
| Cancellation | Truyền token xuyên suốt; rollback và throw cancellation |

## 11. Concurrency và thread safety

`IUnitOfWork` không thread-safe. Một use case không chạy song song nhiều EF/Dapper commands trên cùng connection. Optimistic concurrency của EF Core vẫn được xử lý tại Aggregate/use case; Dapper update cần kiểm tra affected rows khi dùng concurrency token.

## 12. Testing

### Unit tests

- Commit chỉ được gọi một lần.
- Commit thành công clear Domain Events.
- Dispatch lỗi giữ Domain Events và rollback.
- Dispose trước commit gọi rollback.

### Shared-transaction integration tests

Dùng SQLite in-memory để kiểm chứng provider-neutral transaction behavior:

```text
Dapper INSERT A
EF Core INSERT B
rollback
→ A và B đều không tồn tại
```

```text
Dapper INSERT A
EF Core INSERT B
commit
→ A và B đều tồn tại
→ Domain Event dispatch đúng một lần
```

Migration/MySQL smoke test tiếp tục chạy riêng với Docker Compose.

## 13. Dependency registration

```text
IUnitOfWorkFactory → EfDapperUnitOfWorkFactory (scoped, stateless)
```

Factory lấy configuration và `IDomainEventDispatcher` từ DI. Unit of Work do factory tạo và caller `await using`; không đăng ký instance Unit of Work trực tiếp vào DI container.

## 14. Không làm trong Phase 1

- Generic Repository hoặc Generic Dapper DAO.
- Ambient transaction/`TransactionScope`.
- Nested Unit of Work.
- Savepoint API.
- Automatic retry trong transaction.
- Read/write database splitting.
- Distributed transaction.
- Outbox cho tới khi có external side effect thật.

## 15. Tiêu chí hoàn thành

- EF Core và Dapper dùng cùng `DbConnection` và `DbTransaction` trong write use case.
- Factory tạo Unit of Work độc lập và cleanup đúng khi khởi tạo lỗi.
- Chỉ `CommitAsync` sở hữu commit business transaction.
- Domain Events dispatch trước commit và clear sau commit.
- Bất kỳ failure nào cũng không để partial write.
- Integration tests chứng minh commit/rollback cho cả EF và Dapper.
- Tài liệu senior-thinking riêng được tạo sau implementation dựa trên code và test đã xác minh.
