# CpuTempApp

Ứng dụng hiển thị nhiệt độ CPU và GPU trên màn hình Windows.

## Tính năng

- 📊 Hiển thị nhiệt độ CPU và GPU real-time
- 🎯 Overlay trong suốt, luôn hiển thị trên cùng
- 🎨 Tùy chỉnh màu sắc, vị trí, font chữ
- 📍 Kéo thả để di chuyển vị trí
- 🔄 Tự động kiểm tra phiên bản mới
- ⚡ Khởi động cùng Windows

## Yêu cầu hệ thống

- Windows 10/11
- .NET 7.0 Runtime

## Cài đặt

1. Download file `CpuTempSetup.exe` từ [Releases](https://github.com/HuyTran1002/CpuTempApp/releases/latest)
2. Chạy file installer
3. Ứng dụng sẽ tự động khởi động

## Sử dụng

- **Hiển thị/Ẩn overlay**: Click chuột phải vào tray icon → Show/Hide Overlay
- **Cài đặt**: Click chuột phải vào tray icon → Settings
- **Di chuyển**: Kéo overlay đến vị trí mong muốn
- **Kiểm tra cập nhật**: Click chuột phải vào tray icon → Check for Updates

## Build từ source

```powershell
# Clone repository
git clone https://github.com/HuyTran1002/CpuTempApp.git
cd CpuTempApp

# Build project
dotnet build CpuTempApp.csproj -c Release

# Build installer (cần Inno Setup)
ISCC.exe setup.iss
```

## Công nghệ

- .NET 7.0 Windows Forms
- LibreHardwareMonitor - Đọc thông tin phần cứng
- Inno Setup - Tạo installer

## License

Free to use

## Version

Current version: 1.0.2
