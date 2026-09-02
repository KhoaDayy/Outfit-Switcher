# 👗 Warudo Outfit Switcher

Hệ thống quản lý và chuyển đổi trang phục / phụ kiện (Outfit, Hair, Accessories) chuyên nghiệp dành cho **Warudo**, hỗ trợ hiệu ứng chuyển đổi phát sáng mượt mà (**BiRP Additive Glow Transition**), tự động quét hierarchy avatar và tích hợp toàn diện với Blueprint & Stream Deck.

---

## ✨ Tính năng nổi bật (Key Features)

- 🗂️ **Quản lý đa nhóm (Multi-Group Support)**:
  - Phân chia trang phục thành nhiều nhóm độc lập (ví dụ: *Outfit*, *Hair*, *Glasses*, *Hats*, *Accessories*) — không giới hạn số group / số item.
- 🔍 **Tự động quét Avatar Hierarchy (Smart Scanner)**:
  - **Avatar Folder Mode**: Chỉ cần nhập đường dẫn thư mục cha (ví dụ `Clothes/Outfits`), hệ thống tự động quét tất cả các GameObject con thành từng bộ đồ.
  - **Manual Paths Mode**: Nhập danh sách đường dẫn thủ công cho các avatar có cấu trúc phân tán.
- 🔄 **2 Chế độ hoạt động linh hoạt (Group Types)**:
  - **Switch (Single Active)**: Chế độ đổi đồ chuẩn — kích hoạt một bộ đồ mới sẽ tự động tắt toàn bộ các bộ đồ khác trong cùng nhóm.
  - **Toggle (Multi-Wear)**: Bật/tắt độc lập từng món — lý tưởng cho phụ kiện, áo khoác, kính mắt, nón...
- ✨ **Hiệu ứng chuyển đổi Glow tuyệt đẹp (BiRP Native Transition)**:
  - Tạo hiệu ứng quét sáng từ dưới lên trên qua vertex color và mesh overlay độc lập (`Particles/Additive`).
  - **Không** can thiệp hay thay đổi material gốc của avatar, không yêu cầu shader tự viết.
  - Tùy chỉnh tự do: Màu Glow (`GlowColor`), Độ sáng đỉnh (`Intensity`), Thời gian chuyển (`DurationMs`), Điểm lóa cực đại (`PeakPercent`).
  - Hỗ trợ fallback mượt mà cho các avatar tắt tính năng đọc mesh (`isReadable = false`).
- 🔘 **Giao diện Dynamic Triggers trực quan**:
  - Sau khi quét, từng món đồ hiển thị trong danh sách **Items** của group với đúng tên đồ và nút **👗 WEAR / TOGGLE** riêng ngay trên Asset Inspector.
  - Thêm nút **Next / Prev Item** cho 3 group đầu tiên để chuyển đồ nhanh.
- 🛡️ **Bảo vệ khi đổi avatar (Character Guard)**:
  - Trạng thái đã lưu chỉ được restore khi đúng character đã scan — tránh bật/tắt nhầm object trùng tên trên avatar khác. Đổi avatar mới chỉ cần bấm lại **Scan Items**.
- 🧩 **Tích hợp Blueprint Node mạnh mẽ**:
  - `Outfit Switcher` Node: Hỗ trợ `Switch To Item`, `Next Item`, `Previous Item`, và `Random Item`.
  - `GLOW OUTFIT` Node: Node độc lập để phát hiệu ứng glow chuyển đồ tùy ý trong graph.
- 🎮 **Tương thích Stream Deck & MCP / WebSocket**:
  - Dễ dàng gán phím tắt Stream Deck hoặc điều khiển tự động qua API.

---

## 📁 Cấu trúc mã nguồn (Repository Structure)

```text
Outfit-Switcher/
├── OutfitGroup.cs           # Định nghĩa cấu trúc dữ liệu, Enums, OutfitItem và OutfitGroup
├── OutfitSwitcherAsset.cs   # Asset chính: Quản lý quét, chuyển đổi trang phục và dynamic UI triggers
├── OutfitSwitchNode.cs      # Blueprint Node: Điều khiển chuyển đổi trang phục từ Node Graph
├── GlowOutfitNode.cs        # Engine hiệu ứng Glow BiRP và Blueprint Node chuyển đồ có glow
└── README.md                # Tài liệu hướng dẫn sử dụng
```

---

## 🚀 Hướng dẫn cài đặt (Installation)

### Cách 1: Sử dụng qua Playground (Khuyên dùng)
1. Tải về 4 file `.cs` trong repository này.
2. Sao chép cả 4 file vào thư mục Playground của Warudo:
   ```text
   <Đường_dẫn_cài_đặt_Warudo>/Warudo_Data/StreamingAssets/Playground/
   ```
3. Khởi động lại hoặc mở Warudo, hệ thống sẽ tự động biên dịch mã nguồn C# ngay khi khởi chạy.

### Cách 2: Biên dịch thành Plugin DLL
- Thêm 4 file vào dự án Warudo Plugin C# (.NET Framework 4.7.2 / Unity 2021.3) và build file `.dll` vào thư mục `Plugins/`.

---

## 📖 Hướng dẫn sử dụng (Quick Start)

### 1. Thêm Asset vào Scene
1. Trong Warudo, mở bảng điều khiển bên trái, chọn **Add Asset** ➔ tìm **Outfit Switcher** (Category: `Hasukatsu`).
2. Tại mục **Character**, chọn nhân vật avatar bạn muốn quản lý trang phục.

### 2. Thiết lập Group & Quét đồ
1. Trong danh sách **Groups**, bấm dấu `+` để thêm một nhóm mới.
2. Cấu hình thông số nhóm:
   - **Group Name**: Tên nhóm (ví dụ: `Outfit`, `Hair`, `Accessories`).
   - **Scan Mode**:
     - Chọn `Avatar Folder` và nhập đường dẫn thư mục cha trong avatar (ví dụ `Clothes/Outfits`).
     - Hoặc chọn `Manual Paths` và nhập từng đường dẫn GameObject.
   - **Group Type**: Chọn `Switch` (chỉ mặc 1 bộ) hoặc `Toggle` (mặc nhiều món tự do).
   - **Transition**: Chọn `Glow` hoặc `Instant`.
   - Cài đặt màu sắc và thời gian phát sáng nếu dùng `Glow`.
3. Bấm **Scan Items** để hệ thống quét toàn bộ danh sách đồ.

### 3. Đổi trang phục
- **Cách 1 (Trực tiếp)**: Mở danh sách **Items** trong group, bấm nút **👗 WEAR / TOGGLE** trên từng item; hoặc dùng **Next / Prev Item** để xoay vòng.
- **Cách 2 (Blueprint Graph)**:
  - Thêm node **Outfit Switcher** (`NodeType: Hasukatsu -> Outfit Switcher`).
  - Nối tham chiếu `Switcher` tới `OutfitSwitcherAsset`.
  - Chọn Action (`Switch To Item`, `Next Item`, `Previous Item`, `Random Item`) và kích hoạt từ bất kỳ sự kiện Flow nào (phím bấm, Stream Deck, trigger...).

---

## 🛠️ Yêu cầu hệ thống (Requirements)

- **Warudo** phiên bản hỗ trợ Built-in Render Pipeline (BiRP).
- Unity 2021.3 LTS Runtime.
- UniTask / Cysharp.Threading.Tasks (đã tích hợp sẵn trong Warudo Core).

---

## 📜 Giấy phép & Tác giả (Author & License)

- Tác giả: **KhoaDayy** / **Hasukatsu**
- Phát triển dành riêng cho cộng đồng Warudo VTubers.