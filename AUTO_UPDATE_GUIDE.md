# Hướng Dẫn Setup Auto-Update

## Bước 1: Tạo GitHub Repository

1. Tạo một repository mới trên GitHub (ví dụ: `CpuTempApp`)
2. Upload source code lên repository

## Bước 2: Cập nhật UpdateChecker.cs

Mở file `UpdateChecker.cs` và xác nhận URLs đã được cấu hình:

```csharp
private const string VERSION_CHECK_URL = "https://raw.githubusercontent.com/HuyTran1002/CpuTempApp/main/version.txt";
private const string DOWNLOAD_URL = "https://github.com/HuyTran1002/CpuTempApp/releases/latest";
```

✅ URLs đã được cấu hình sẵn với username **HuyTran1002**.

## Bước 3: Tạo file version.txt

Tạo file `version.txt` trong root của repository với nội dung:
```
1.0.0
```

Commit và push file này lên GitHub.

## Bước 4: Build và Release

1. **Build project**:
   ```powershell
   dotnet build -c Release
   ```

2. **Build installer** với Inno Setup:
   ```powershell
   & "D:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
   ```

3. **Tạo GitHub Release**:
   - Vào GitHub repository
   - Click "Releases" → "Create a new release"
   - Tag version: `v1.0.0`
   - Upload file `CpuTempSetup.exe` từ folder `Output/`
   - Click "Publish release"

## Bước 5: Cập nhật Version Mới

Khi có phiên bản mới:

1. **Cập nhật version trong `CpuTempApp.csproj`**:
   ```xml
   <Version>1.0.1</Version>
   <AssemblyVersion>1.0.1.0</AssemblyVersion>
   <FileVersion>1.0.1.0</FileVersion>
   ```

2. **Cập nhật `version.txt` trên GitHub**:
   - Edit file trên GitHub
   - Thay đổi nội dung thành `1.0.1`
   - Commit changes

3. **Build và tạo Release mới**:
   - Build project
   - Build installer
   - Tạo GitHub Release mới với tag `v1.0.1`
   - Upload file installer mới

## Cách Hoạt Động

- **Khi khởi động**: App sẽ tự động kiểm tra version sau 3 giây
- **Kiểm tra thủ công**: Click chuột phải vào tray icon → "Check for Updates"
- **Có update**: Hiển thị dialog hỏi có muốn download không
- **Click Yes**: Mở browser đến trang GitHub Release để download

## Alternative: Dùng Local Server

Nếu không muốn dùng GitHub, bạn có thể host `version.txt` trên server riêng:

1. Upload `version.txt` lên web server của bạn
2. Cập nhật URL trong `UpdateChecker.cs`:
   ```csharp
   private const string VERSION_CHECK_URL = "https://yourdomain.com/cputempapp/version.txt";
   private const string DOWNLOAD_URL = "https://yourdomain.com/cputempapp/download";
   ```

## Testing

Để test auto-update:

1. Set version hiện tại là `1.0.0` trong `.csproj`
2. Build và chạy app
3. Cập nhật `version.txt` trên GitHub/server thành `1.0.1`
4. Đợi 3 giây hoặc click "Check for Updates"
5. Should see update notification

## Notes

- Auto-update chỉ **thông báo** có version mới, không tự động cài đặt
- User phải tự download và cài đặt file setup mới
- Cần internet connection để check update
- Nếu không có internet, app vẫn hoạt động bình thường
