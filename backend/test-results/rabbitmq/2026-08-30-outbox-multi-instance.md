# Outbox Dispatcher multi-instance và lease recovery

## Invariant cần bảo vệ

Business transaction và Outbox row phải commit trong cùng MySQL transaction. Dispatcher không tạo event mới; nó chỉ chuyển durable intent đã tồn tại trong Outbox sang RabbitMQ.

Một Outbox row chưa hoàn thành phải thỏa một trong hai trạng thái:

- chưa có lease và có thể được claim;
- đang có lease còn hiệu lực và chỉ worker sở hữu lease được mark sent.

Nếu worker biến mất, `lockedUntil` hết hạn khiến row có thể được claim lại. Recovery có thể publish duplicate, nhưng không được làm mất intent.

## Claim flow

```mermaid
sequenceDiagram
    participant W1 as Worker 1
    participant W2 as Worker 2
    participant DB as MySQL Outbox
    participant MQ as RabbitMQ

    par Claim concurrent batches
        W1->>DB: SELECT ... FOR UPDATE SKIP LOCKED LIMIT 25
        W2->>DB: SELECT ... FOR UPDATE SKIP LOCKED LIMIT 25
    end
    DB-->>W1: Batch A + lease
    DB-->>W2: Batch B + lease
    W1->>MQ: Publish MessageId + wait confirm
    MQ-->>W1: Confirm
    W1->>DB: Mark sent where lockedBy = worker-1
```

Claim transaction dùng `READ COMMITTED`, giữ row locks trong thời gian chọn và ghi lease rồi commit ngay. Network publish không nằm trong MySQL transaction; nếu giữ database locks trong lúc chờ broker, throughput và deadlock risk sẽ phụ thuộc network latency.

## Composite index và lỗi được phát hiện

Thiết kế index đầu tiên là:

```text
(runId, sentAt, lockedUntil, occurredAt, messageId)
```

Bốn workers chạy đồng thời cho kết quả claim thực tế:

```text
[25, 0, 0, 0]
```

`lockedUntil` đứng trước claim order khiến MySQL không thể dùng index để duyệt trực tiếp theo `occurredAt, messageId`. Sau khi candidate bị sort và giới hạn, các workers còn lại skip 25 locked rows nhưng không refill đủ batch.

Index claim được sửa thành:

```text
(runId, sentAt, occurredAt, messageId)
```

`lockedUntil` vẫn là predicate xác định lease đã hết hạn, nhưng không phá equality prefix và deterministic order. Sau khi sửa, kết quả là bốn batches rời nhau, tổng cộng 100/100 intents được claim, publish và mark sent. Test chạy ổn định 20/20 lượt.

## Hai crash windows

### Worker chết trước publish

```mermaid
sequenceDiagram
    participant W1 as Worker lỗi
    participant DB as MySQL
    participant W2 as Worker phục hồi
    participant MQ as RabbitMQ

    W1->>DB: Claim row, attempts = 1
    W1--xW1: Dừng trước publish
    Note over DB: lockedUntil hết hạn
    W2->>DB: Claim lại, attempts = 2
    W2->>MQ: Publish + confirm
    W2->>DB: Mark sent, clear lease
```

Kết quả: một delivery, Outbox `attempts = 2`, `sentAt` có giá trị, `lockedBy` và `lockedUntil` được xóa.

### Worker chết sau confirm, trước mark sent

```mermaid
sequenceDiagram
    participant W1 as Worker lỗi
    participant MQ as RabbitMQ
    participant DB as MySQL
    participant W2 as Worker phục hồi

    W1->>MQ: Publish MessageId A
    MQ-->>W1: Confirm
    W1--xW1: Dừng trước UPDATE sentAt
    Note over DB: Lease hết hạn, row vẫn unsent
    W2->>MQ: Publish lại MessageId A
    MQ-->>W2: Confirm
    W2->>DB: Mark sent
```

Kết quả: hai physical deliveries có cùng một `MessageId`, Outbox `attempts = 2` và chỉ có một trạng thái sent. Đây là at-least-once delivery; Inbox của consumer phải biến hai deliveries thành một logical effect.

## Reconciliation

Reconciliation không cần một cơ chế đặc biệt để “unlock” rows. Query claim bình thường đã chọn cả rows có `lockedUntil < UTC_TIMESTAMP(6)`. Worker poll theo khoảng thời gian bounded và dùng đồng hồ database để quyết định lease hết hạn; không dùng một lần `Task.Delay(leaseDuration)` rồi giả định process clock trùng với MySQL clock.

Các chỉ số cần giám sát:

- số Outbox rows chưa sent;
- tuổi của row chưa sent cũ nhất;
- số rows có lease đã hết hạn;
- phân bố `attempts` và `lastError` khi production schema bổ sung error diagnostics;
- publish-confirm latency;
- tỷ lệ duplicate được Inbox hấp thụ.

## Phạm vi implementation

`TestOutboxDispatcher` là executable reference trong test project. Production foundation chưa có hosted dispatcher vì chưa có business transaction tạo Outbox thật. Khi triển khai business flow, schema production cần thêm `TenantId`, event contract metadata, payload version, error diagnostics và retention strategy; claim/lease/publish-confirm ordering đã được kiểm chứng ở đây sẽ được tái sử dụng.
