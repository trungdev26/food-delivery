# RabbitMQ reference load — 100 shops và 100.000 thẻ kho

## Phạm vi lần chạy

Lần chạy này kiểm tra messaging foundation trên một máy phát triển Windows, Docker Desktop, MySQL 8 và RabbitMQ `4.1.8-management-alpine`. RabbitMQ chỉ có một node; queue dùng loại `quorum`, nhưng kết quả không chứng minh khả năng failover vì không có node thứ hai để bầu leader.

Profile sử dụng 100 shops, 100.000 thẻ kho, 100.000 persistent messages, ba publisher connections và tám consumer connections. Mỗi consumer có `prefetch = 32`, vì vậy số delivery chưa ACK tối đa theo cấu hình là 256.

Dữ liệu thẻ kho không được chia đều hoàn toàn. Một nửa số rows thuộc shop nóng nhất. Tổ hợp `shopId = 0, productId = 0` có 500 lô để truy vấn FEFO phải xử lý một hot key thực tế hơn.

## Kết quả quan sát

Thời điểm chạy: 2026-08-30, múi giờ Asia/Saigon.

| Chỉ số | Giá trị |
|---|---:|
| Shops | 100 |
| Thẻ kho | 100.000 |
| Messages | 100.000 |
| Ready trước khi consumer chạy | 100.000 |
| Publisher connections | 3 |
| Consumer connections | 8 |
| Seed MySQL | 1,09 giây |
| Publish và chờ confirm | 222,76 giây |
| Publish throughput | 448,91 messages/giây |
| Consume và ACK | 4,89 giây |
| Consume throughput | 20.443,69 messages/giây |
| Message còn lại sau consume | 0 |
| Missing messages | 0 |

Publisher là nút thắt của profile này. Mỗi publish chờ broker confirm trước khi tiếp tục, trong khi consumer xử lý nhanh hơn khoảng 45 lần. Kết quả này ủng hộ việc giữ confirmed publisher đơn giản trong foundation và chỉ bổ sung confirm window hoặc channel pool khi production throughput yêu cầu; tăng consumer không giải quyết được bottleneck đang nằm ở publish path.

## Truy vấn FEFO

Composite index được kiểm tra:

```text
(runId, shopId, productId, expiresAt, id)
```

`EXPLAIN ANALYZE` trên hot key báo index lookup ước lượng 500 rows và trả 10 lô đầu tiên trong khoảng `0,21 ms`. MySQL không scan toàn bộ 100.000 rows.

```sql
SELECT id
FROM TestStockCardLoad
WHERE runId = @runId
  AND shopId = 0
  AND productId = 0
  AND available > 0
ORDER BY expiresAt, id
LIMIT 10;
```

Kết quả này chỉ chứng minh đường đọc candidate sử dụng đúng index. Nó chưa chứng minh transaction allocation chịu được cùng lúc nhiều writers trên hot key; invariant đó được kiểm tra riêng bởi `ReliableMessagingReferenceTests` với row locking và strict FEFO.

## Cách chạy lại

```powershell
$env:RABBIT_LOAD_SHOPS = '100'
$env:RABBIT_LOAD_STOCK_CARDS = '100000'
$env:RABBIT_LOAD_MESSAGE_COUNT = '100000'
$env:RABBIT_LOAD_PUBLISHERS = '3'
$env:RABBIT_LOAD_CONSUMERS = '8'

dotnet test backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj `
  --filter FullyQualifiedName~LargeScaleLoadTests `
  --logger "console;verbosity=detailed"
```

Không dùng số throughput trên làm capacity mặc định cho production. Cần chạy lại trên hạ tầng tương đương production, với TLS, network latency, ba RabbitMQ nodes, database pool và business transaction thật.
