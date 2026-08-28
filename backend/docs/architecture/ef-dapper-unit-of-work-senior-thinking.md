# Tư duy senior khi thiết kế EF Core + Dapper Unit of Work

## 1. Đừng bắt đầu từ pattern

Câu hỏi yếu là:

> Làm sao tạo Unit of Work và Factory?

Câu hỏi đúng là:

> Những thay đổi nào phải cùng thành công hoặc cùng thất bại, và tài nguyên nào quyết định điều đó?

Trong case này:

```text
EF Core write
Dapper write
Domain Event handler write
```

phải atomic. Tài nguyên quyết định atomicity không phải repository hay service, mà là một `DbTransaction` trên một `DbConnection` cụ thể.

## 2. Đi từ dưới lên

### Bước 1 — Xác định primitive thật

EF Core và Dapper đều đi qua ADO.NET:

```text
DbConnection
DbCommand
DbTransaction
```

Vì vậy cần kiểm chứng:

- Ai tạo connection?
- Ai mở và đóng connection?
- Ai tạo transaction?
- EF Core có dùng đúng transaction đó không?
- Dapper command có truyền transaction không?
- Ai commit, rollback và dispose?

Nếu mỗi câu có nhiều hơn một owner, thiết kế dễ lỗi.

### Bước 2 — Xác định transaction boundary

Boundary phải là một business use case, không phải một repository method:

```text
TaoDonHang
├── EF thêm DonHang
├── Dapper cập nhật counter/report table
├── Domain Event handler cập nhật dữ liệu liên quan
└── Commit một lần
```

Repository tự commit sẽ phá boundary vì use case không còn rollback toàn bộ được.

### Bước 3 — Chọn owner duy nhất

Thiết kế chọn `EfDapperUnitOfWork` làm owner:

| Trách nhiệm | Owner |
|---|---|
| Connection | Unit of Work |
| Transaction | Unit of Work |
| DbContext | Unit of Work |
| Commit/Rollback | Unit of Work |
| Domain Event lifecycle | Unit of Work |
| Tạo bộ tài nguyên | Factory |

Factory chỉ tạo. Unit of Work chỉ quản lý một transaction. Repository chỉ thực hiện persistence operation.

## 3. Vì sao Factory tồn tại

Factory có lý do thật khi mỗi use case cần một bộ tài nguyên mới và việc tạo bộ đó có nhiều bước dễ fail:

```text
new connection
→ open
→ begin transaction
→ build DbContext options trên connection
→ create DbContext
→ enlist EF vào transaction
```

Nếu lỗi ở bước giữa, Factory dispose những gì đã tạo. Đây là abstraction che giấu lifecycle thật, không phải Factory “cho đúng pattern”.

Nếu chỉ có `new Service()` thì Factory là thừa.

## 4. Điều kiện để EF và Dapper dùng chung transaction

Phải đồng thời đúng cả hai:

```text
ReferenceEquals(EF connection, Dapper connection)
ReferenceEquals(EF transaction, Dapper transaction)
```

Cùng connection string không đủ:

```text
new MySqlConnection(cs) // session A
new MySqlConnection(cs) // session B
```

Đây vẫn là hai session và hai transaction khác nhau.

EF được enlist bằng `Database.UseTransaction(transaction)`. Dapper nhận transaction qua `CommandDefinition` hoặc tham số `transaction` của `ExecuteAsync`.

## 5. Thiết kế happy path sau cùng

```text
Factory.CreateAsync
  → open connection
  → begin transaction
  → create/enlist DbContext

Use case
  → EF tracks changes
  → Dapper executes with uow.Transaction

Uow.CommitAsync
  → SaveChanges
  → dispatch all pending Domain Events
  → SaveChanges các thay đổi từ handlers
  → repeat nếu handler phát event mới
  → commit transaction
  → clear events
```

`ApplicationDbContext.SaveChangesAsync` không tự commit nữa. Nếu cả DbContext và UOW đều sở hữu transaction, sẽ có nested transaction hoặc commit không rõ owner.

## 6. Phải thiết kế failure path trước code

| Điểm lỗi | Điều phải xảy ra |
|---|---|
| Connection open | Không còn resource sống |
| Begin transaction | Dispose connection |
| Dapper command | Dispose UOW sẽ rollback |
| EF SaveChanges | Rollback Dapper writes trước đó |
| Event handler | Rollback EF + Dapper; giữ events |
| Nested event handler | Không commit khi còn event chưa xử lý |
| Commit database | Giữ events nếu commit không thành công |
| Caller quên commit | Dispose tự rollback |
| Commit hai lần | Throw, không chạy lại event |
| Cancellation | Truyền token và rollback |

Senior không chỉ vẽ happy path. Phần lớn giá trị của UOW nằm ở failure path và ownership.

## 7. Domain Events nằm ở đâu trong transaction

Event nội bộ có handler ghi database phải chạy trước commit để cùng transaction.

Không clear event ngay sau dispatch vì database commit vẫn có thể lỗi. Chỉ clear sau commit thành công.

Event handler có thể phát sinh event mới. Vì vậy commit dùng tập các event đã dispatch theo object reference và lặp đến khi không còn pending event.

Email, webhook và broker là external side effect. Không gọi trực tiếp trước commit vì database có thể rollback sau khi email đã gửi. Khi case đó xuất hiện, handler ghi Outbox trong transaction.

## 8. Concurrency và hiệu năng

Một connection không được dùng đồng thời bởi nhiều EF/Dapper operations. Không dùng `Task.WhenAll` trên cùng UOW.

UOW phải ngắn:

```text
open → query/write cần thiết → commit → dispose
```

Không gọi API ngoài hoặc chờ thao tác người dùng khi transaction đang mở. Transaction dài giữ lock và giảm throughput.

Connection pooling do MySqlConnector quản lý. Dispose connection thường trả physical connection về pool; vì vậy tạo UOW theo use case không đồng nghĩa mở TCP socket mới mỗi lần.

## 9. Testing phải chứng minh điều gì

Mock `IUnitOfWork` không chứng minh hai công nghệ dùng chung transaction. Test quan trọng phải chạy trên relational provider thật hoặc SQLite in-memory:

```text
Dapper INSERT A
EF INSERT B
Commit
→ A và B tồn tại
```

```text
Dapper INSERT A
EF INSERT B
Event handler throw
→ A và B không tồn tại
→ Domain Event vẫn còn
```

SQLite test nhanh chứng minh shared-transaction contract. MySQL smoke test chứng minh production provider wiring và migration; hai loại test trả lời hai câu hỏi khác nhau.

## 10. Các hướng bị loại

### EF tự mở transaction, Dapper tự mở connection

Không atomic. Cùng connection string không cứu được.

### Generic Repository + Generic UOW

Chỉ bọc lại EF, làm mất khả năng query rõ ràng và không giải quyết connection ownership.

### TransactionScope

Ẩn transaction boundary, dễ vô tình enlist connection thứ hai và khó đọc failure path. Chưa cần cho một database.

### Factory tạo repository/service graph

Biến Factory thành service locator. Factory hiện chỉ tạo resource bundle có lifetime phức tạp.

### Automatic retry

Retry transaction có thể chạy lại Dapper command và Domain Event handler. Chỉ thêm khi đã thiết kế idempotency và có lỗi transient thực tế.

## 11. Checklist dùng cho thiết kế tương tự

1. Business operation nào phải atomic?
2. Primitive thấp nhất đảm bảo atomicity là gì?
3. Ai sở hữu connection và transaction?
4. Có đường nào mở connection thứ hai không?
5. Commit nằm đúng một nơi chưa?
6. Caller quên commit thì chuyện gì xảy ra?
7. Mỗi điểm failure rollback và dispose thế nào?
8. Event chạy trước hay sau commit? Vì sao?
9. External side effect có cần Outbox chưa?
10. Concurrency có dùng chung connection song song không?
11. Test nào chứng minh cùng transaction thật, không chỉ mock?
12. Abstraction nào chưa có consumer thật và có thể bỏ?

Nếu trả lời rõ 12 câu này, code thường trở nên ngắn. Nếu chưa trả lời được, thêm interface hay pattern chỉ che mờ vấn đề.
