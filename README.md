# SmartWallet

Hệ thống quản lý chi tiêu cá nhân theo kiến trúc microservices: .NET 10, Angular 21, YARP Gateway, Consul, RabbitMQ, Redis. Kế hoạch và quyết định kiến trúc nằm ở `../plan/` (xem ADR-010 về cấu trúc thư mục, ADR-011 về phiên bản).

## Yêu cầu

- .NET SDK 10
- Node.js + npm (Angular CLI 21)
- Docker Desktop (cho Consul, RabbitMQ, Redis, MailHog)

## 1. Thiết lập khóa JWT (bắt buộc)

Mọi `appsettings.json` để `Jwt:SigningKey` rỗng. Nếu thiếu khóa, hoặc khóa ngắn hơn 32 byte, service sẽ dừng ngay khi khởi động với lỗi `JWT SigningKey is missing`.

Gateway và 12 service phải dùng **cùng một khóa**. Sinh khóa mới rồi ghi vào user-secrets của cả 13 project:

```bash
cd SmartWallet
KEY=$(openssl rand -base64 48)
for p in src/Gateway/ApiGateway.Yarp src/Services/*/; do
  dotnet user-secrets set "Jwt:SigningKey" "$KEY" --project "$p"
done
unset KEY
```

Nếu không dùng user-secrets, bạn có thể đặt biến môi trường `JWT_SIGNING_KEY` cho tất cả các process.

> Không commit khóa vào repo. Khóa từng nằm trong `appsettings.json` ở các commit cũ (vd: `f08ca54`) coi như đã lộ và không được dùng lại.

## 2. Chạy hạ tầng

```bash
docker compose up -d
```

## 3. Chạy backend

Mỗi service tự đăng ký vào Consul khi khởi động. Gateway chạy ở cổng 8000, các service ở 8001–8012 (xem ADR-010).

```bash
dotnet build SmartWallet.sln
dotnet run --project src/Gateway/ApiGateway.Yarp
dotnet run --project src/Services/IdentityService
# ... các service còn lại tương tự
```

## 4. Chạy frontend

```bash
cd fe
npm install
npm start      # ng serve, proxy /api -> http://localhost:8000 (fe/proxy.conf.json)
```

## 5. Kiểm thử

```bash
dotnet test SmartWallet.sln
cd fe && npx ng test
```
