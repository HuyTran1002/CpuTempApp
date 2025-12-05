# Hướng Dẫn Upload Lên GitHub

## Cách 1: Dùng GitHub Desktop (Dễ nhất)

1. **Download và cài đặt GitHub Desktop**: https://desktop.github.com/
2. **Đăng nhập** với tài khoản `HuyTran1002`
3. Click **File** → **Add local repository**
4. Chọn folder: `D:\Program Files\Code\CpuTempApp`
5. Click **Publish repository**
   - Repository name: `CpuTempApp`
   - Bỏ tick "Keep this code private" (để public)
   - Click **Publish repository**

## Cách 2: Dùng Git command line

### Bước 1: Cài đặt Git
Download từ: https://git-scm.com/download/win

### Bước 2: Upload code
```powershell
cd "D:\Program Files\Code\CpuTempApp"
git init
git add .
git commit -m "Initial commit"
git branch -M main
git remote add origin https://github.com/HuyTran1002/CpuTempApp.git
git push -u origin main
```

## Cách 3: Upload trực tiếp trên GitHub (Nhanh nhất nếu chưa có Git)

1. Vào https://github.com/new
2. Repository name: `CpuTempApp`
3. Public repository
4. Click **Create repository**
5. Click **uploading an existing file**
6. Kéo thả các file này vào (QUAN TRỌNG):
   ```
   ✅ version.txt
   ✅ README.md
   ✅ .gitignore
   ✅ AppSettings.cs
   ✅ ControlForm.cs
   ✅ OverlayForm.cs
   ✅ Program.cs
   ✅ SensorOptionsForm.cs
   ✅ UpdateChecker.cs
   ✅ WelcomeForm.cs
   ✅ CpuTempApp.csproj
   ✅ setup.iss
   ✅ app.manifest
   ✅ temperature_icon_175973.ico
   ✅ AUTO_UPDATE_GUIDE.md
   ```
7. Commit message: "Initial commit"
8. Click **Commit changes**

## Bước tiếp theo: Tạo Release

1. Vào repository: https://github.com/HuyTran1002/CpuTempApp
2. Click tab **Releases** → **Create a new release**
3. Điền thông tin:
   - **Tag**: `v1.0.0`
   - **Release title**: `Version 1.0.0`
   - **Description**:
     ```
     🎉 Phiên bản đầu tiên của CpuTempApp
     
     ### Tính năng:
     - Hiển thị nhiệt độ CPU và GPU
     - Overlay trong suốt
     - Tùy chỉnh màu sắc và vị trí
     - Tự động kiểm tra cập nhật
     - Khởi động cùng Windows
     ```
4. Click **Choose files** → Upload file `Output\CpuTempSetup.exe`
5. Click **Publish release**

## Kiểm tra Auto-Update

Sau khi upload xong:

1. **Chạy app** đã cài đặt
2. Đợi 3 giây hoặc click chuột phải tray icon → **Check for Updates**
3. Nếu thấy thông báo "You are using the latest version (1.0.0)" → Thành công! ✅

## Test Update Flow (Tương lai)

Khi có version 1.0.1:

1. Cập nhật `version.txt` trên GitHub → đổi thành `1.0.1`
2. Cập nhật version trong `CpuTempApp.csproj` → `<Version>1.0.1</Version>`
3. Build và tạo installer mới
4. Tạo release v1.0.1 với installer mới
5. Mở app cũ (v1.0.0) → Sẽ thấy thông báo có update mới

---

**Lưu ý**: File `version.txt` phải có mặt ở root của repository để auto-update hoạt động!
