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
    public class OutfitItem : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("NAME")]
        public string DisplayName = "";

        [DataInput]
        [Label("PATH")]
        [Description("Path GameObject trong avatar")]
        public string Path = "";

        [DataInput]
        [Hidden]
        public bool IsActive = false;

        // Runtime links — được set lại bởi OutfitSwitcherAsset.LinkGroups()
        // (không phải DataInput — tránh serialize tham chiếu asset trong structured data)
        public OutfitSwitcherAsset OwnerAsset;
        public int GroupIndex = -1;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(DisplayName) ? (string.IsNullOrWhiteSpace(Path) ? "Item" : Path) : DisplayName;
        }

        [Trigger]
        [Label("👗 WEAR")]
        [Description("Mặc / bật tắt item")]
        public void Wear() {
            OwnerAsset?.WearItemByPath(GroupIndex, Path);
        }
    }


    /// <summary>
    /// Quy tắc hiển thị child dùng chung giữa các outfit. Ví dụ một rule
    /// "Ears" với ChildNames = ["Ears"] có thể giữ tai tắt khi đổi outfit.
    /// </summary>
    public class OutfitChildVisibilityRule : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("RULE NAME")]
        public string RuleName = "Ears";

        [DataInput]
        [Label("CHILD NAMES")]
        [Description("Tên GameObject con (vd Ears, Tail)")]
        [AutoComplete(nameof(AutoCompleteChildNames), forceSelection: false)]
        public string[] ChildNames = Array.Empty<string>();

        protected UniTask<AutoCompleteList> AutoCompleteChildNames() {
            var entries = (OwnerAsset?.GetChildGameObjectNames() ?? Array.Empty<string>())
                .Select(name => new AutoCompleteEntry { label = name, value = name })
                .ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataInput]
        [Label("VISIBLE")]
        [Description("Trạng thái hiển thị")]
        public bool Visible = true;

        public OutfitSwitcherAsset OwnerAsset;
        public int RuleIndex = -1;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(RuleName) ? "Child Visibility Rule" : $"{RuleName} ({(Visible ? "Visible" : "Hidden")})";
        }

        [Trigger]
        [Label("TOGGLE")]
        public void Toggle() {
            OwnerAsset?.ToggleChildVisibilityRule(RuleIndex);
        }
    }

    public class OutfitPresetEntry : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("GROUP NAME")]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("ITEM NAME OR PATH")]
        public string ItemNameOrPath = "";

        [DataInput]
        [Label("ACTION")]
        public OutfitPresetAction Action = OutfitPresetAction.Enable;

        public string GetHeader() {
            return $"{GroupName} / {(string.IsNullOrWhiteSpace(ItemNameOrPath) ? "(Item)" : ItemNameOrPath)} ({Action})";
        }
    }

    public class OutfitPreset : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("PRESET NAME")]
        public string PresetName = "New Preset";

        [DataInput]
        [Label("ENTRIES")]
        public OutfitPresetEntry[] Entries = Array.Empty<OutfitPresetEntry>();

        public OutfitSwitcherAsset OwnerAsset;
        public int PresetIndex = -1;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(PresetName) ? "Preset" : PresetName;
        }

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
    public class OutfitGroup : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("GROUP NAME")]
        [Description("Tên nhóm (vd Outfit, Hair)")]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("SCAN MODE")]
        public OutfitScanMode ScanMode = OutfitScanMode.AvatarFolder;

        [DataInput]
        [Label("FOLDER PATH")]
        [Description("Folder chứa outfit trong hierarchy")]
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
        [Description("Danh sách path thủ công")]
        [HiddenIf(nameof(IsAvatarFolderMode))]
        public string[] ManualPaths = Array.Empty<string>();

        [DataInput]
        [Label("GROUP TYPE")]
        [Description("Switch: 1 active | Toggle: Bật/tắt tự do")]
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
        [Description("Độ sáng đỉnh glow")]
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
        [Description("Thời điểm lóa đỉnh (0 - 1)")]
        [FloatSlider(0.05f, 0.95f, 0.01f)]
        [HiddenIf(nameof(IsInstantTransition))]
        public float PeakPercent = 0.42f;

        [DataInput]
        [Label("IGNORE INACTIVE CHILDREN")]
        [Description("Không glow phần tử đang tắt")]
        [HiddenIf(nameof(IsInstantTransition))]
        public bool IgnoreInactiveChildren = true;

        [DataInput]
        [Label("GLOW EXCLUDED PATHS")]
        [Description("Path loại khỏi glow")]
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
        [Description("Danh sách item đã scan")]
        public OutfitItem[] Items = Array.Empty<OutfitItem>();

        // Runtime links — được set lại bởi OutfitSwitcherAsset.LinkGroups()
        public OutfitSwitcherAsset OwnerAsset;
        public int GroupIndex = -1;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(GroupName) ? "Outfit Group" : GroupName;
        }

        // ── Trigger ──

        [Trigger]
        [Label("SCAN ITEMS")]
        [Description("Quét danh sách item")]
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
 