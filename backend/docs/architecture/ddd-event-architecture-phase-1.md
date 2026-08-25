# Thiết kế DDD và Event Architecture — Core Phase 1

## 1. Mục tiêu

Xây core cho nền tảng đặt đồ ăn online dạng SaaS đa tenant:

- Một `Tenant` sở hữu nhiều `Shop`.
- Tenant quản lý catalog `HangHoa` dùng chung.
- Mỗi Shop có `ThucDonShop`, được phép tùy chỉnh hoặc đồng bộ từ catalog Tenant.
- `DonHang` được quản lý theo từng Shop.
- `Tenant Admin`, `Shop Staff` và `Customer` sử dụng hệ thống qua web.
- Host chung dùng cho đăng nhập/quản trị; subdomain xác định Tenant trên website riêng.

Phase 1 ưu tiên ranh giới nghiệp vụ, tính đúng đắn và đường nâng cấp rõ ràng. Hệ thống chưa trả chi phí vận hành của microservices, message broker hay Event Sourcing.

## 2. Quy ước ngôn ngữ

- Keyword kỹ thuật giữ English: `Modular Monolith`, `Aggregate Root`, `Domain Event`, `Command`, `Handler`, `Transaction`, `Outbox`.
- Tên nghiệp vụ trong code dùng tiếng Việt không dấu: `HangHoa`, `ThucDonShop`, `DonHang`, `ChiTietDonHang`.
- Thuật ngữ sản phẩm phổ biến giữ nguyên: `Tenant`, `Shop`.
- Chỉ thêm hậu tố khi thể hiện đúng vai trò kỹ thuật: `TaoDonHangCommand`, `DonHangDto`, `DonHangDaTaoDomainEvent`.
- Không thêm `Entity` vào mọi Domain type. `DonHang` đã thể hiện một Entity/Aggregate trong Domain.

## 3. Quyết định kiến trúc

### 3.1 Modular Monolith

Phase 1 dùng một backend deployment và một MySQL database:

```text
Angular
   │ HTTP
   ▼
FoodDelivery.Api
   │
   ├── Tenancy
   ├── Shops
   ├── Catalog
   └── Ordering
        │
        ▼
      MySQL
```

Các module là business boundary trong cùng solution, chưa phải service độc lập. Chỉ tách service khi có bằng chứng về nhu cầu scale, release cycle hoặc team ownership riêng.

### 3.2 Dependency direction

```text
Api ───────────────► Application ───────────────► Domain
 │                         ▲
 └──► Infrastructure ──────┘
              │
              └───────────────────────────────► Domain
```

- `Domain`: không phụ thuộc project khác, EF Core, HTTP hay message broker.
- `Application`: điều phối use case và phụ thuộc `Domain`.
- `Infrastructure`: triển khai abstraction của `Application`, chứa EF Core và technical adapters.
- `Api`: composition root, authentication, middleware và HTTP mapping.

### 3.3 Không dùng Generic CRUD làm core

Các business operation được đặt tên theo use case như `TaoDonHang`, `DongBoThucDonShop`, `CapNhatTrangThaiDonHang`. Không dùng generic service/controller để che mất invariant và quyền dữ liệu.

`DbContext` đã cung cấp Repository và Unit of Work behavior ở mức hạ tầng. Chỉ tạo repository riêng khi Aggregate có persistence behavior hoặc query đủ đặc thù.

## 4. Business boundary

| Module | Trách nhiệm | Không chịu trách nhiệm |
|---|---|---|
| `Tenancy` | Tenant, trạng thái Tenant, nhận diện subdomain | Menu, đơn hàng, nghiệp vụ Shop |
| `Shops` | Shop thuộc Tenant, trạng thái hoạt động, phân quyền Shop Staff | Catalog chuẩn, tính tiền đơn |
| `Catalog` | `HangHoa` cấp Tenant, `ThucDonShop`, tùy chỉnh và đồng bộ menu | Vòng đời đơn hàng |
| `Ordering` | Tạo đơn, snapshot món/giá, tổng tiền, trạng thái đơn | Sửa giá menu, quản lý Tenant |
| `Identity` | Đăng nhập và ánh xạ ba vai trò Phase 1 | Trở thành một domain phức tạp |

## 5. Domain model

```text
Tenant
 ├── Shop A
 │    ├── ThucDonShop ──► HangHoa của Tenant
 │    └── DonHang
 └── Shop B
      ├── ThucDonShop ──► HangHoa của Tenant
      └── DonHang
```

### 5.1 Tenant

- Là trust boundary cao nhất của dữ liệu.
- Có trạng thái `Active` hoặc `Inactive`.
- Có subdomain duy nhất trong phạm vi base domain.
- Tenant này không được đọc hoặc sửa dữ liệu Tenant khác.

### 5.2 Shop

- Thuộc đúng một Tenant.
- Có trạng thái hoạt động.
- Không giữ collection toàn bộ menu hoặc đơn hàng trong Aggregate để tránh tải và ghi một Aggregate quá lớn.

### 5.3 HangHoa

- Là món chuẩn thuộc catalog của Tenant.
- Chứa tên, mô tả, ảnh, danh mục và giá đề xuất.
- Có thể ngừng kinh doanh nhưng không xóa vật lý khi đã xuất hiện trong đơn.
- Thay đổi `HangHoa` không mặc định ghi đè cấu hình tại Shop.

### 5.4 ThucDonShop

`ThucDonShop` thể hiện dữ liệu bán thực tế tại một Shop:

```text
TenantId
ShopId
HangHoaId
TenGhiDe?
GiaBan
TrangThaiBan
CheDoDongBo
LanDongBoCuoi?
```

`CheDoDongBo` Phase 1 gồm:

- `Linked`: cho phép nhận các trường được quy định từ catalog Tenant.
- `Independent`: Shop tự quản lý, thao tác đồng bộ không ghi đè.

Không sao chép toàn bộ `HangHoa` xuống Shop. Chỉ lưu những trường Shop thực sự sở hữu hoặc override.

### 5.5 DonHang

`DonHang` là Aggregate Root và bảo vệ các invariant:

- Thuộc đúng một `TenantId` và một `ShopId`.
- Chỉ nhận món đang bán trong `ThucDonShop` của Shop đó.
- Giá và tổng tiền được tính ở backend.
- Không nhận `GiaBan`, `ThanhTien` hoặc `TongTien` từ frontend làm nguồn tin cậy.
- Chuyển trạng thái qua behavior của `DonHang`, không gán enum trực tiếp từ Controller.

`ChiTietDonHang` lưu snapshot tại thời điểm đặt:

```text
HangHoaId
TenHangHoa
DonGia
SoLuong
ThanhTien
```

Việc thay đổi tên hoặc giá menu sau đó không làm thay đổi đơn cũ.

## 6. Đồng bộ thực đơn

Tenant Admin chọn phạm vi đồng bộ rõ ràng:

- Chỉ cập nhật catalog.
- Đồng bộ tới một số Shop.
- Đồng bộ tới toàn bộ Shop có menu đang `Linked`.

```text
Tenant Admin cập nhật HangHoa
                │
                ▼
      HangHoaDaThayDoiDomainEvent
                │
                ▼
  DongBoThucDonShopHandler nhận phạm vi
                │
                ▼
 Chỉ cập nhật ThucDonShop đang Linked
```

Không tự động fan-out mọi thay đổi tới mọi Shop. Một lần sửa giá nhầm không được âm thầm ảnh hưởng toàn bộ điểm bán.

Phase 1 dùng một `Command` và một database `Transaction`; chưa xây sync engine tổng quát.

## 7. Tenant resolution và isolation

### 7.1 Hai đường xác định Tenant

```text
live.food.com.vn
  └── TenantId lấy từ tài khoản đăng nhập

banhmycay.food.com.vn
  └── Tenant lấy từ subdomain
```

Base domain được cấu hình theo môi trường, không hard-code. Custom domain để Phase sau.

Middleware tạo `TenantContext` sau khi xác minh host hoặc authenticated user. ASP.NET Core Authentication chịu trách nhiệm xác thực; `ICurrentUser` cung cấp UserId/role cho Application. Frontend không được tự khai báo một `TenantId` rồi coi đó là quyền truy cập.

### 7.2 Các lớp bảo vệ

- Dữ liệu tenant-owned có `TenantId`, kể cả khi có thể suy ra qua `ShopId`.
- EF Core global query filter bảo vệ truy vấn thông thường.
- Command ghi dữ liệu vẫn kiểm tra trực tiếp ownership, ví dụ `Shop.TenantId == TenantContext.TenantId`.
- Không tìm thấy dữ liệu trong Tenant hiện tại trả `404`, không tiết lộ dữ liệu của Tenant khác.
- Composite index bắt đầu bằng `TenantId` cho các query path chính.

Việc lặp `TenantId` có chủ đích: filter rõ ràng, index hiệu quả, giảm nguy cơ truy cập chéo và hỗ trợ chuyển dữ liệu về sau.

## 8. Request flow

### 8.1 Tạo đơn hàng

```text
Angular
  │ POST /api/shops/{shopId}/don-hang
  ▼
DonHangController
  ▼
TaoDonHangHandler
  ├── lấy TenantContext
  ├── kiểm tra Shop thuộc Tenant và đang hoạt động
  ├── đọc ThucDonShop
  ├── kiểm tra món còn bán
  ├── DonHang.Tao(...)
  ├── SaveChanges trong một Transaction
  └── dispatch DonHangDaTaoDomainEvent
```

Request từ frontend chỉ chứa:

```text
ShopId
Danh sách HangHoaId + SoLuong
Thông tin nhận hàng
```

### 8.2 Đồng bộ menu

```text
Tenant Admin chọn HangHoa + phạm vi Shop
  → DongBoThucDonShopCommand
  → kiểm tra quyền Tenant Admin
  → lấy các ThucDonShop đang Linked
  → áp dụng trường được phép đồng bộ
  → SaveChanges trong một Transaction
  → ThucDonShopDaDongBoDomainEvent
```

### 8.3 Theo dõi đơn

Phase 1 dùng HTTP query và refresh/polling khi cần. Chưa thêm SignalR chỉ để thể hiện Event Architecture. Realtime được thêm khi trải nghiệm thực tế yêu cầu.

## 9. Trạng thái đơn hàng

```text
MoiTao
  → DaXacNhan
  → DangChuanBi
  → SanSangGiao
  → HoanThanh

MoiTao / DaXacNhan
  → DaHuy
```

Transition nằm trong `DonHang`. Mỗi transition thành công có thể phát `DonHangDaThayDoiTrangThaiDomainEvent`.

## 10. Event Architecture Phase 1

### 10.1 Domain Event in-process

Domain Event mô tả việc đã xảy ra, ví dụ:

- `HangHoaDaThayDoiDomainEvent`
- `ThucDonShopDaDongBoDomainEvent`
- `DonHangDaTaoDomainEvent`
- `DonHangDaThayDoiTrangThaiDomainEvent`

Phase 1 dispatch event trong application process. Event không được dùng thay cho lời gọi hàm thông thường khi không có business fact cần phát đi.

```text
Command
  → bắt đầu Transaction
  → thay đổi Aggregate
  → SaveChanges
  → dispatch Domain Event in-process
  → SaveChanges thay đổi từ handler
  → commit Transaction
```

Handler cần nhất quán tuyệt đối với command chạy trước commit trong cùng `Transaction`. Domain Event chỉ được dispatch một lần trong application flow; handler mới phát thêm event thì dispatcher tiếp tục xử lý trước commit. Side effect bên ngoài không chạy theo cơ chế này và chưa được coi là đáng tin cậy ở Phase 1.

### 10.2 Giới hạn được chấp nhận

Phase 1 chưa bảo đảm delivery/retry cho email, webhook hoặc hệ thống ngoài. Đây là giới hạn có chủ đích, không phải cam kết reliability.

Khi xuất hiện integration cần retry, nâng cấp sang Outbox:

```text
Transaction
  ├── ghi Aggregate
  └── ghi OutboxMessage

Background Worker
  → đọc Outbox
  → publish
  → retry khi lỗi
```

Domain Event hiện có không cần biết transport là RabbitMQ, Kafka hay dịch vụ khác.

## 11. Error handling và concurrency

- Domain violation trả mã lỗi ổn định, ví dụ `DON_HANG_SHOP_NGUNG_HOAT_DONG`.
- Middleware ánh xạ lỗi Application/Domain sang HTTP response chuẩn.
- Lỗi kỹ thuật được log nhưng không lộ stack trace cho client.
- Dùng optimistic concurrency cho dữ liệu có khả năng bị nhiều người cùng sửa, trước hết là menu và trạng thái đơn.
- Một command thành công toàn bộ hoặc rollback toàn bộ.

## 12. Cấu trúc backend

Giữ bốn project hiện có; không tạo project cho từng module:

```text
backend/
├── FoodDelivery.slnx
├── FoodDelivery.Api/
│   ├── Controllers/
│   │   ├── HangHoaController.cs
│   │   ├── ShopsController.cs
│   │   └── DonHangController.cs
│   ├── Middleware/
│   │   ├── TenantResolutionMiddleware.cs
│   │   └── ExceptionHandlingMiddleware.cs
│   ├── Program.cs
│   └── appsettings.json
│
├── FoodDelivery.Application/
│   ├── Abstractions/
│   │   ├── IApplicationDbContext.cs
│   │   ├── ICurrentUser.cs
│   │   ├── ITenantContext.cs
│   │   └── IDomainEventDispatcher.cs
│   ├── Shops/
│   │   ├── TaoShop/
│   │   └── CapNhatShop/
│   ├── Catalog/
│   │   ├── TaoHangHoa/
│   │   ├── CapNhatHangHoa/
│   │   ├── CauHinhThucDonShop/
│   │   └── DongBoThucDonShop/
│   └── Ordering/
│       ├── TaoDonHang/
│       ├── LayDonHang/
│       └── CapNhatTrangThaiDonHang/
│
├── FoodDelivery.Domain/
│   ├── Common/
│   │   ├── Entity.cs
│   │   ├── AggregateRoot.cs
│   │   └── IDomainEvent.cs
│   ├── Tenancy/
│   │   └── Tenant.cs
│   ├── Shops/
│   │   └── Shop.cs
│   ├── Catalog/
│   │   ├── HangHoa.cs
│   │   ├── ThucDonShop.cs
│   │   └── ThucDonShopDaDongBoDomainEvent.cs
│   └── Ordering/
│       ├── DonHang.cs
│       ├── ChiTietDonHang.cs
│       ├── TrangThaiDonHang.cs
│       └── DonHangDaTaoDomainEvent.cs
│
└── FoodDelivery.Infrastructure/
    ├── Persistence/
    │   ├── ApplicationDbContext.cs
    │   ├── Configurations/
    │   └── Migrations/
    ├── Tenancy/
    │   └── TenantContext.cs
    └── Events/
        └── DomainEventDispatcher.cs
```

Một use case chỉ tạo những file thực sự cần:

```text
TaoDonHang/
├── TaoDonHangCommand.cs
├── TaoDonHangHandler.cs
└── TaoDonHangResult.cs
```

Không bắt buộc tạo `Validator`, `Mapper`, `Repository`, `Service` hoặc `Interface` cho mọi use case. Logic ngắn nằm trong Handler hoặc Domain; chỉ tách khi có trách nhiệm độc lập.

## 13. Cấu trúc frontend

Frontend chia theo nghiệp vụ nhưng không mô phỏng DDD backend:

```text
frontend/src/app/
├── core/
│   ├── api/
│   ├── auth/
│   └── tenant/
├── features/
│   ├── shops/
│   ├── catalog/
│   └── orders/
└── shared/
```

Frontend quản lý UI state. Backend là nơi duy nhất bảo vệ invariant, tính giá và tenant isolation.

## 14. Testing strategy

### 14.1 Domain tests

- Không tạo đơn cho Shop ngừng hoạt động.
- Không đặt món ngoài `ThucDonShop`.
- Không đặt món đang ngừng bán.
- Giá và tổng tiền do Domain tính đúng.
- `ChiTietDonHang` giữ snapshot sau khi giá menu thay đổi.
- Không chuyển trạng thái đơn sai thứ tự.

Các test này không mock database.

### 14.2 Application integration tests

- Không truy cập chéo Tenant.
- Shop Staff chỉ thao tác Shop được cấp quyền.
- Đồng bộ chỉ cập nhật menu có `CheDoDongBo.Linked`.
- Command thành công hoặc rollback toàn bộ.

Không cần unit test riêng cho Controller, DTO getter hoặc EF Configuration không có logic.

## 15. Phạm vi Phase 1

### Có trong Phase 1

- Nhận diện Tenant bằng tài khoản hoặc subdomain.
- Tenant Admin quản lý Shop và catalog `HangHoa`.
- Cấu hình và đồng bộ `ThucDonShop`.
- Shop Staff quản lý trạng thái bán và đơn của Shop được cấp quyền.
- Customer xem menu, tạo đơn và theo dõi trạng thái.
- Domain Event in-process.
- Một MySQL database và một backend deployment.

### Chưa có trong Phase 1

- Microservices, message broker, Outbox và Event Sourcing.
- Thanh toán online.
- Tài xế và điều phối giao hàng.
- Khuyến mãi và giỏ hàng nhiều Shop.
- Custom domain.
- Realtime tracking.
- Generic CRUD framework nội bộ.

## 16. Lộ trình nâng cấp

| Giai đoạn | Khi nào cần | Thay đổi chính |
|---|---|---|
| Phase 1 | Xây core và kiểm chứng nghiệp vụ | Modular Monolith, Domain Event in-process |
| Phase 2 | Có email, webhook, payment hoặc delivery integration cần retry | Thêm Outbox và Background Worker |
| Phase 3 | Có consumer độc lập hoặc tải bất đồng bộ đáng kể | Thêm message broker |
| Phase 4 | Có nhu cầu scale/release/team ownership độc lập | Tách chọn lọc service phù hợp |

Notification, Payment hoặc Delivery thường là ứng viên tách trước. Không mặc định tách Ordering.

## 17. Tư duy phát triển từ Fresher đến Middle chuyên sâu

| Chủ đề | Cách nghĩ Fresher | Cách nghĩ Middle | Định hướng Middle chuyên sâu |
|---|---|---|---|
| Chia code | Chia theo bảng và CRUD | Chia Controller/Service/Repository | Chia theo business boundary và invariant |
| Multi-tenant | Nhận `TenantId` từ client | Middleware + global filter | Xem Tenant là trust boundary, kiểm tra ownership khi ghi và thiết kế index |
| Menu Shop | Copy toàn bộ menu cho mỗi Shop | Bảng liên kết Shop–Menu | Xác định data ownership, override field và sync policy |
| Đơn hàng | Lưu ID món rồi đọc giá hiện tại | Lưu chi tiết đơn | Snapshot dữ liệu lịch sử và bảo vệ transition trong Aggregate |
| Event | Gọi event cho mọi thứ | Thêm broker để “event-driven” | Event biểu diễn business fact; chọn reliability theo nhu cầu thật |
| Abstraction | Interface cho mọi class | Generic repository/service | Chỉ abstraction hóa điểm biến động thật |
| Tối ưu | Cache sớm | Tối ưu từng query rời rạc | Bắt đầu từ Aggregate size, tenant index và query path có số liệu |

Các nguyên tắc cần giữ:

1. Bắt đầu từ invariant và data ownership, không bắt đầu từ danh sách pattern.
2. Dùng Event để mô tả việc đã xảy ra, không thay mọi lời gọi hàm bằng Event.
3. Chấp nhận synchronous transaction khi phù hợp quy mô hiện tại.
4. Để sẵn đường nâng cấp lên Outbox nhưng chưa trả chi phí vận hành trước khi cần.
5. Một abstraction chỉ đáng tồn tại khi nó che giấu biến động thật.

## 18. Tiêu chí hoàn thành core

- Dependency giữa bốn project đúng chiều.
- Business entity/service/repository không còn nằm trong `Api`.
- Dữ liệu nghiệp vụ được scope bằng `TenantId`.
- Một `DonHang` chỉ thuộc một Shop.
- Giá đơn được snapshot và tính tại backend.
- Đồng bộ menu phân biệt `Linked` và `Independent`.
- Domain Event không phụ thuộc EF Core, HTTP hoặc message broker.
- Có test cho invariant và tenant isolation quan trọng.
- Các giới hạn Phase 1 được giữ rõ ràng, không biến core thành framework nội bộ.
