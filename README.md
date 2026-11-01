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

### 3.1. Biên dịch toàn bộ Solution:
```bash
dotnet build SmartWallet.sln
```

### 3.2. Chạy từng service riêng lẻ:
```bash
# 1. API Gateway (Cổng :8000) - Cổng định tuyến API và xác thực token tập trung
dotnet run --project src/Gateway/ApiGateway.Yarp

# 2. Identity Service (Cổng :8001) - Đăng ký, đăng nhập, JWT, kích hoạt email & đổi mật khẩu
dotnet run --project src/Services/IdentityService

# 3. User Profile Service (Cổng :8002) - Quản lý hồ sơ người dùng, đơn vị tiền tệ & avatar
dotnet run --project src/Services/UserProfileService

# 4. Wallet Service (Cổng :8003) - Quản lý ví tài khoản & điều phối Saga chuyển tiền
dotnet run --project src/Services/WalletService

# 5. Transaction Service (Cổng :8004) - Ghi nhận thu/chi, lịch sử giao dịch & đồng bộ số dư
dotnet run --project src/Services/TransactionService

# 6. Category Service (Cổng :8005) - Quản lý danh mục thu/chi phân cấp cha-con
dotnet run --project src/Services/CategoryService

# 7. Budget Service (Cổng :8006) - Thiết lập ngân sách & giám sát ngưỡng cảnh báo 80%/100%
dotnet run --project src/Services/BudgetService

# 8. Saving Goal Service (Cổng :8007) - Mục tiêu tích lũy & theo dõi tiến độ tiết kiệm
dotnet run --project src/Services/SavingGoalService

# 9. Recurring & Debt Service (Cổng :8008) - Giao dịch định kỳ, quản lý sổ nợ & nhắc hẹn
dotnet run --project src/Services/RecurringDebtService

# 10. Report Service (Cổng :8009) - Tổng hợp dữ liệu thống kê, biểu đồ & báo cáo tài chính
dotnet run --project src/Services/ReportService

# 11. Notification Service (Cổng :8010) - Quản lý thông báo in-app & gửi email cảnh báo
dotnet run --project src/Services/NotificationService

# 12. Audit Service (Cổng :8011) - Ghi nhật ký kiểm toán bất biến & theo dõi an ninh hệ thống
dotnet run --project src/Services/AuditService

# 13. File Service (Cổng :8012) - Lưu trữ và phục vụ tệp tin, chứng từ & hóa đơn
dotnet run --project src/Services/FileService
```

### 3.3. Khởi động nhanh toàn bộ 13 services cùng lúc:

**Trên Windows (PowerShell):**
```powershell
# Chạy đồng thời Gateway và 12 Microservices trong các cửa sổ độc lập
Get-ChildItem -Path "src/Gateway/ApiGateway.Yarp", "src/Services/*" -Directory | ForEach-Object {
    Start-Process dotnet -ArgumentList "run --project $($_.FullName)"
}
```

**Trên Linux / macOS (Bash):**
```bash
# Chạy nền tất cả services
for proj in src/Gateway/ApiGateway.Yarp src/Services/*/; do
    dotnet run --project "$proj" &
done
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
