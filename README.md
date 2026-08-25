# FoodDelivery

Monorepo gồm 2 phần:

- [`frontend/`](frontend/) — Angular 19 (xem `frontend/README.md` cho lệnh Angular CLI, hoặc `docs/api-flow.md` cho lớp gọi API).
- [`backend/`](backend/) — .NET 6 Web API (Controllers/Services/Repositories, EF Core + Dapper, MySQL).

## Chạy frontend

```bash
cd frontend
npm install
npm start
```

Mặc định chạy tại `http://localhost:4200`, gọi API tới `http://localhost:3000/api` (xem `frontend/src/environments/environment.development.ts`).

## Chạy backend

MySQL chạy qua Docker Compose (`backend/docker-compose.yml`, map ra cổng host `3307` để tránh đụng MySQL cài sẵn trên máy ở `3306`):

```bash
cd backend
docker compose up -d
dotnet restore
dotnet ef database update --project FoodDelivery.Infrastructure --startup-project FoodDelivery.Api --context ApplicationDbContext
dotnet run --project FoodDelivery.Api
```

Connection string mặc định đã khớp sẵn trong `backend/FoodDelivery.Api/appsettings.json` (`ConnectionStrings:Default`, port `3307`) — chỉ cần đổi nếu bạn dùng MySQL khác.

Mặc định chạy tại `http://localhost:3000`, Swagger UI tại `http://localhost:3000/swagger` khi ở môi trường Development.

## Tài liệu

- [`docs/api-flow.md`](docs/api-flow.md) — luồng gọi API của frontend (`HttpService`, interceptors, chuẩn hóa lỗi).

### Tenant resolution

`TenantResolution:BaseDomain` là domain gốc theo môi trường. Request tới host trong
`SharedHosts` lấy Tenant từ claim `tenant_id`; request tới `<subdomain>.<BaseDomain>`
lấy Tenant theo subdomain. Client không được truyền `TenantId` để tự chọn Tenant.
