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

Cần MySQL chạy sẵn cục bộ, cập nhật connection string trong `backend/FoodDelivery.Api/appsettings.json` (`ConnectionStrings:Default`) trước khi chạy.

```bash
cd backend
dotnet restore
dotnet ef database update --project FoodDelivery.Api
dotnet run --project FoodDelivery.Api
```

Mặc định chạy tại `http://localhost:3000`, Swagger UI tại `http://localhost:3000/swagger` khi ở môi trường Development.

## Tài liệu

- [`docs/api-flow.md`](docs/api-flow.md) — luồng gọi API của frontend (`HttpService`, interceptors, chuẩn hóa lỗi).
