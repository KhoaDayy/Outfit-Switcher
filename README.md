# Warudo Outfit Switcher

Hệ thống quản lý outfit, tóc và phụ kiện tháo rời cho **Warudo**. Plugin hỗ trợ scan avatar hierarchy, chuyển outfit dạng single-select, bật/tắt phụ kiện độc lập, preset nhiều nhóm và hiệu ứng glow trên Built-in Render Pipeline.

## Tính năng

- Không giới hạn số group và item trong dữ liệu.
- `Switch`: chỉ một item active trong group.
- `Toggle`: nhiều phụ kiện có thể active đồng thời.
- Nút `WEAR / TOGGLE`, `ENABLE`, `DISABLE` và `PREVIEW INFO` trên từng item.
- Scan một group hoặc `SCAN ALL GROUPS`.
- `FOLDER PATH` có autocomplete trực tiếp từ hierarchy của Character, không cần nhớ hoặc gõ path thủ công.
- `VALIDATE CONFIGURATION` phát hiện group trùng tên, path trùng hoặc không resolve được.
- Blueprint autocomplete cho group, item và preset.
- Blueprint có flow `Success`, `Failed`, output `Last Error`, đồng thời giữ `Exit` để tương thích graph cũ.
- Preset có thể thay đổi outfit, tóc và nhiều phụ kiện bằng một nút.
- Child Visibility Rules giữ tai/đuôi bật hoặc tắt khi chuyển outfit.
- Glow có thể bỏ qua child inactive hoặc các path bị loại trừ.
- State được lưu theo canonical avatar-relative path thay vì chỉ dựa vào tên hiển thị.
- Tên item do người dùng tùy chỉnh được giữ khi scan lại nếu path không đổi.

## Cài đặt bằng Playground

1. Cài **.NET 8 SDK** theo yêu cầu của Warudo Playground.
2. Sao chép các file sau vào:

   ```text
   <Warudo>/Warudo_Data/StreamingAssets/Playground/
   ```

   ```text
   OutfitGroup.cs
   OutfitSwitcherAsset.cs
   OutfitSwitchNode.cs
   GlowOutfitNode.cs
   OutfitSwitcherPlugin.cs
   ```

3. Mở hoặc khởi động lại Warudo và kiểm tra console compile.
4. Thêm asset **Outfit Switcher** trong category `Hasukatsu`.

Khi phát hành bằng Warudo Mod SDK, `OutfitSwitcherPlugin.cs` là plugin entry point và đăng ký asset/node types. Không cần tự tạo DLL .NET Framework rời khỏi quy trình Mod SDK.

## Thiết lập nhanh

1. Chọn `Character`.
2. Thêm group trong `Groups`.
3. Chọn scan mode:
   - `Avatar Folder`: chọn `Folder Path` từ dropdown hierarchy; mỗi child trực tiếp của folder là một item.
   - `Manual Paths`: nhập từng avatar-relative path.
4. Chọn group type:
   - `Switch`: outfit, tóc hoặc các lựa chọn loại trừ nhau.
   - `Toggle`: kính, mũ, áo khoác, tai, đuôi hoặc phụ kiện tháo rời.
5. Bấm `SCAN ITEMS` hoặc `SCAN ALL GROUPS`.
6. Bấm `VALIDATE CONFIGURATION`.
7. Mở `Items` và dùng các nút trên từng item.
8. Lưu Scene thủ công sau khi cấu hình vì Warudo không tự lưu scene.

## Cấu hình outfit và phụ kiện tháo rời

Hierarchy khuyến nghị:

```text
Avatar
├── Outfits
│   ├── Casual
│   ├── School
│   └── Maid
└── Accessories
    ├── Glasses
    ├── Hat
    └── Jacket
```

Cấu hình:

```text
Group Outfit
  Scan Mode: Avatar Folder
  Folder Path: Outfits
  Group Type: Switch

Group Accessories
  Scan Mode: Avatar Folder
  Folder Path: Accessories
  Group Type: Toggle
```

Nếu tai/đuôi nằm bên trong từng outfit:

```text
Outfits
├── Casual
│   ├── Clothes
│   ├── Ears
│   └── Tail
└── Maid
    ├── Clothes
    ├── Ears
    └── Tail
```

Thêm `Child Visibility Rules`:

```text
Rule Name: Ears
Child Names:
- Ears
Visible: false

Rule Name: Tail
Child Names:
- Tail
Visible: false
```

Các nút `SHOW`, `HIDE`, `TOGGLE` của rule sẽ áp dụng cho tất cả child có tên chính xác trên avatar, kể cả child thuộc outfit đang inactive. Khi đổi outfit, rule được tự áp dụng lại.

Nếu tai/đuôi là GameObject riêng và chỉ muốn điều khiển từng path, tạo group `Toggle` với `Manual Paths`:

```text
Outfits/Casual/Ears
Outfits/Casual/Tail
Outfits/Maid/Ears
Outfits/Maid/Tail
```

Nếu tai/đuôi nằm chung trong một `SkinnedMeshRenderer`, `SetActive` không thể tách riêng. Cần tách mesh trong Blender/Unity hoặc điều khiển bằng blendshape.

## Glow không làm hiện lại tai/đuôi đã tháo

Trong group Outfit:

```text
Ignore Inactive Children: true
Glow Excluded Paths:
- Ears
- Tail
```

`Ignore Inactive Children` bỏ qua renderer inactive. `Glow Excluded Paths` hỗ trợ:

- Full path: `Outfits/Casual/Ears`
- Tên child: `Ears`

Với group phụ kiện Toggle, plugin vẫn có thể dựng overlay cho item đang tắt để chạy hiệu ứng trước khi bật.

## Allow None

Với group `Switch`, bật `ALLOW NONE` nếu muốn cho phép tắt toàn bộ item, ví dụ tháo mọi loại mũ. Sau đó dùng `DISABLE ALL` hoặc Blueprint action tương ứng.

Group `Toggle` luôn có thể tắt toàn bộ item.

## Preset

Thêm một entry trong `Presets`:

```text
Preset Name: Casual Full
Entries:
- Group Name: Outfit
  Item Name Or Path: Outfits/Casual
  Action: Enable
- Group Name: Hair
  Item Name Or Path: Hair/Ponytail
  Action: Enable
- Group Name: Accessories
  Item Name Or Path: Accessories/Glasses
  Action: Enable
- Group Name: Accessories
  Item Name Or Path: Accessories/Hat
  Action: Disable
```

Bấm `APPLY PRESET` trên preset hoặc dùng Blueprint action `Apply Preset`.

Nên dùng path trong preset để tránh nhầm khi nhiều item có cùng display name. Action `Toggle` chỉ hợp lệ với group `Toggle`; với group `Switch`, dùng `Enable` để chọn item hoặc bật `Allow None` trước khi dùng `Disable`.

## Blueprint

Thêm node `Hasukatsu / Outfit Switcher`, chọn asset và action:

- `Wear / Toggle Item`
- `Enable Item`
- `Disable Item`
- `Disable All In Group`
- `Next Item`
- `Previous Item`
- `Random Item`
- `Apply Preset`

Group, item và preset được lấy bằng autocomplete từ asset đã chọn. Item lưu path ổn định dù label hiển thị cả tên và path.

Outputs:

- `Exit (Compatibility)`: luôn chạy để graph cũ tiếp tục hoạt động.
- `Success`: chạy khi thao tác hợp lệ.
- `Failed`: chạy khi có lỗi.
- `Last Error`: nội dung lỗi gần nhất.
- `Succeeded`: trạng thái thao tác gần nhất.

`Next`, `Previous` và `Random` chỉ dành cho group `Switch`. Với group `Toggle`, dùng action xác định `Enable`, `Disable` hoặc `Wear / Toggle`.

## Preview và xử lý lỗi

`PREVIEW INFO` không thay đổi avatar. Nó hiển thị:

- Canonical path.
- Số renderer.
- Tổng vertex.
- Số mesh readable.
- Khả năng dùng full sweep hay uniform fallback.

Các lỗi phổ biến:

### Folder hoặc item không tìm thấy

- Chọn lại path từ dropdown `Folder Path`, danh sách được lấy trực tiếp từ hierarchy của Character.
- Có thể gõ path tương đối từ root avatar cho cấu trúc đặc biệt.
- Nếu có nhiều object cùng leaf name, phải dùng full path. Plugin không còn tự chọn object đầu tiên.

### Glow làm hiện phụ kiện đã tắt

- Bật `Ignore Inactive Children`.
- Thêm tên hoặc path vào `Glow Excluded Paths`.

### Glow gây giảm FPS

- Giảm số renderer trong item.
- Dùng `Instant` cho avatar nặng.
- Dùng `PREVIEW INFO` để kiểm tra vertex count.
- Tránh chạy nhiều transition cùng group liên tục.

### Mesh không readable

Plugin vẫn dùng uniform glow fallback nhưng không có sweep theo chiều cao.

### Đổi character

Group chỉ restore state khi character hiện tại khớp character đã scan. Hãy scan lại các group sau khi chọn avatar khác.

## Cấu trúc source

```text
Outfit-Switcher/
├── OutfitGroup.cs
├── OutfitSwitcherAsset.cs
├── OutfitSwitchNode.cs
├── GlowOutfitNode.cs
├── OutfitSwitcherPlugin.cs
└── README.md
```

## Yêu cầu

- Warudo với Built-in Render Pipeline.
- Unity 2021.3 runtime của Warudo.
- .NET 8 SDK cho Playground.
- UniTask có sẵn trong Warudo Core.

## Tác giả

KhoaDayy / Hasukatsu — phát triển cho cộng đồng Warudo VTubers.
