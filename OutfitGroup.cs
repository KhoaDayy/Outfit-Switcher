using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;

namespace Warudo.Plugins.McpBridge {

    // ── Enums ──

    public enum OutfitScanMode {
        [Label("Avatar Folder")]
        AvatarFolder,

        [Label("Manual Paths")]
        Manual
    }

    public enum OutfitGroupType {
        [Label("Switch (1 active)")]
        Single,

        [Label("Toggle (multi-wear)")]
        Toggle
    }

    public enum OutfitTransition {
        [Label("Instant")]
        Instant,

        [Label("Glow")]
        Glow
    }

    public enum OutfitPresetAction {
        [Label("Wear / Enable")]
        Enable,

        [Label("Disable")]
        Disable,

        [Label("Toggle (Toggle group only)")]
        Toggle
    }

    // ═══════════════════════════════════════════════════════
    // OUTFIT ITEM — một bộ đồ/accessory trong group
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Một item trong group — Path trỏ tới GameObject trong avatar,
    /// DisplayName hiển thị trong UI và trigger buttons.
    /// Được tạo bằng StructuredData.Create khi scan.
    /// </summary>
    public class OutfitItem : StructuredData {

        [DataInput]
        [Label("NAME")]
        public string DisplayName = "";

        [DataInput]
        [Label("PATH")]
        [Description("Path GameObject trong avatar, vd 'Clothes/Top A'")]
        public string Path = "";

        [DataInput]
        [Hidden]
        public bool IsActive = false;

        // Runtime links — được set lại bởi OutfitSwitcherAsset.LinkGroups()
        // (không phải DataInput — tránh serialize tham chiếu asset trong structured data)
        public OutfitSwitcherAsset OwnerAsset;
        public int GroupIndex = -1;

        [Trigger]
        [Label("👗 WEAR")]
        [Description("Mặc item này (Switch group: tắt toàn bộ item khác; Toggle group: bật/tắt độc lập)")]
        public void Wear() {
            OwnerAsset?.WearItemByPath(GroupIndex, Path);
        }
    }


    /// <summary>
    /// Quy tắc hiển thị child dùng chung giữa các outfit. Ví dụ một rule
    /// "Ears" với ChildNames = ["Ears"] có thể giữ tai tắt khi đổi outfit.
    /// </summary>
    public class OutfitChildVisibilityRule : StructuredData {

        [DataInput]
        [Label("RULE NAME")]
        public string RuleName = "Ears";

        [DataInput]
        [Label("CHILD NAMES")]
        [Description("Tên GameObject con cần điều khiển trong outfit đang active, vd Ears, Tail. So khớp tên chính xác, không phân biệt hoa thường.")]
        public string[] ChildNames = Array.Empty<string>();

        [DataInput]
        [Label("VISIBLE")]
        [Description("Trạng thái được giữ lại và tự áp dụng sau mỗi lần đổi outfit.")]
        public bool Visible = true;

        public OutfitSwitcherAsset OwnerAsset;
        public int RuleIndex = -1;

        [Trigger]
        [Label("TOGGLE")]
        public void Toggle() {
            OwnerAsset?.ToggleChildVisibilityRule(RuleIndex);
        }

        [Trigger]
        [Label("SHOW")]
        public void Show() {
            OwnerAsset?.SetChildVisibilityRule(RuleIndex, true);
        }

        [Trigger]
        [Label("HIDE")]
        public void Hide() {
            OwnerAsset?.SetChildVisibilityRule(RuleIndex, false);
        }
    }

    public class OutfitPresetEntry : StructuredData {

        [DataInput]
        [Label("GROUP NAME")]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("ITEM NAME OR PATH")]
        public string ItemNameOrPath = "";

        [DataInput]
        [Label("ACTION")]
        public OutfitPresetAction Action = OutfitPresetAction.Enable;
    }

    public class OutfitPreset : StructuredData {

        [DataInput]
        [Label("PRESET NAME")]
        public string PresetName = "New Preset";

        [DataInput]
        [Label("ENTRIES")]
        public OutfitPresetEntry[] Entries = Array.Empty<OutfitPresetEntry>();

        public OutfitSwitcherAsset OwnerAsset;
        public int PresetIndex = -1;

        [Trigger]
        [Label("APPLY PRESET")]
        public void Apply() {
            OwnerAsset?.ApplyPreset(PresetIndex);
        }
    }

    // ═══════════════════════════════════════════════════════
    // OUTFIT GROUP — một nhóm outfit (folder hoặc manual paths)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Một nhóm outfit: quét folder/manual paths → danh sách OutfitItem[].
    /// Bấm trigger ScanItems để quét. Mỗi item trong danh sách Items có nút
    /// 👗 WEAR riêng; Next/Prev nhanh cho 3 group đầu nằm trên Asset.
    /// </summary>
    public class OutfitGroup : StructuredData {

        [DataInput]
        [Label("GROUP NAME")]
        [Description("Tên group, vd 'Outfit', 'Hair', 'Accessories'")]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("SCAN MODE")]
        public OutfitScanMode ScanMode = OutfitScanMode.AvatarFolder;

        [DataInput]
        [Label("FOLDER PATH")]
        [Description("Chọn folder trực tiếp từ hierarchy của Character. Vẫn có thể gõ path nếu cần.")]
        [AutoComplete(nameof(AutoCompleteFolderPath), forceSelection: false)]
        [HiddenIf(nameof(IsManualMode))]
        public string FolderPath = "";

        protected UniTask<AutoCompleteList> AutoCompleteFolderPath() {
            var entries = (OwnerAsset?.GetFolderPaths() ?? Array.Empty<string>())
                .Select(path => new AutoCompleteEntry { label = path, value = path })
                .ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataInput]
        [Label("MANUAL PATHS")]
        [Description("Các path riêng lẻ, vd 'Clothes/Top A', 'Clothes/Bottom A'")]
        [HiddenIf(nameof(IsAvatarFolderMode))]
        public string[] ManualPaths = Array.Empty<string>();

        [DataInput]
        [Label("GROUP TYPE")]
        [Description("Switch: Chế độ đổi đồ (chỉ 1 outfit active trong group, chuyển sang bộ mới sẽ tự tắt toàn bộ các bộ cũ). Toggle: Bật/tắt độc lập.")]
        public OutfitGroupType GroupType = OutfitGroupType.Single;

        [DataInput]
        [Label("TRANSITION")]
        public OutfitTransition Transition = OutfitTransition.Glow;

        [DataInput]
        [Label("GLOW COLOR")]
        [HiddenIf(nameof(IsInstantTransition))]
        public Color GlowColor = new Color(1f, 0.4f, 0.8f, 1f);

        [DataInput]
        [Label("INTENSITY")]
        [Description("Độ chói tại đỉnh glow, nên 3~4")]
        [FloatSlider(0f, 10f, 0.1f)]
        [HiddenIf(nameof(IsInstantTransition))]
        public float Intensity = 3f;

        [DataInput]
        [Label("DURATION (MS)")]
        [IntegerSlider(100, 5000, 50)]
        [HiddenIf(nameof(IsInstantTransition))]
        public int DurationMs = 600;

        [DataInput]
        [Label("PEAK (0-1)")]
        [Description("Thời điểm lóa cực đại, 0.42 ≈ 0.25s với 600ms")]
        [FloatSlider(0.05f, 0.95f, 0.01f)]
        [HiddenIf(nameof(IsInstantTransition))]
        public float PeakPercent = 0.42f;

        [DataInput]
        [Label("IGNORE INACTIVE CHILDREN")]
        [Description("Không glow các child đang tắt, ví dụ tai/đuôi đã tháo. Nên bật cho outfit có phụ kiện con.")]
        [HiddenIf(nameof(IsInstantTransition))]
        public bool IgnoreInactiveChildren = true;

        [DataInput]
        [Label("GLOW EXCLUDED PATHS")]
        [Description("Các path cần loại khỏi glow. Có thể dùng path đầy đủ hoặc path con bên trong outfit, vd Ears, Tail.")]
        [HiddenIf(nameof(IsInstantTransition))]
        public string[] GlowExcludedPaths = Array.Empty<string>();

        // Legacy fields kept for backward-compatible scene migration.
        [DataInput]
        [Hidden]
        public string LastActiveItem = "";

        [DataInput]
        [Hidden]
        public string LastActivePath = "";

        // Nhận diện character đã scan — chặn restore/apply nhầm lên avatar khác
        [DataInput]
        [Hidden]
        public string ScannedCharacterId = "";

        [DataInput]
        [Hidden]
        public string[] LastActiveItems = Array.Empty<string>();

        [DataInput]
        [Hidden]
        public string[] LastActivePaths = Array.Empty<string>();

        [DataInput]
        [Label("ITEMS")]
        [Description("Danh sách item đã scan. Bấm 👗 WEAR trên từng item để đổi đồ.")]
        public OutfitItem[] Items = Array.Empty<OutfitItem>();

        // Runtime links — được set lại bởi OutfitSwitcherAsset.LinkGroups()
        public OutfitSwitcherAsset OwnerAsset;
        public int GroupIndex = -1;

        // ── Trigger ──

        [Trigger]
        [Label("SCAN ITEMS")]
        [Description("Quét danh sách outfit theo folder/manual paths")]
        public void ScanItems() {
            OwnerAsset?.ScanGroup(this);
        }

        // ── HiddenIf conditions ──

        protected bool IsManualMode() => ScanMode == OutfitScanMode.Manual;
        protected bool IsAvatarFolderMode() => ScanMode == OutfitScanMode.AvatarFolder;
        protected bool IsInstantTransition() => Transition == OutfitTransition.Instant;
        protected bool IsToggleGroup() => GroupType == OutfitGroupType.Toggle;
    }
}
