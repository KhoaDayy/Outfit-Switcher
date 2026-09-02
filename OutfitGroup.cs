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
        [Label("Name")]
        public string DisplayName = "";

        [DataInput]
        [Label("Path")]
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
        [Label("Wear")]
        [Description("Mặc / bật tắt item")]
        public void Wear() {
            OwnerAsset?.WearItemByPath(GroupIndex, Path);
        }
    }


    public class OutfitBlendShapeRule : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("Skinned Mesh (Optional)")]
        [Description("Để trống để áp dụng cho tất cả mesh")]
        [AutoComplete(nameof(AutoCompleteSkinnedMesh), forceSelection: false)]
        public string SkinnedMeshName = "";

        protected UniTask<AutoCompleteList> AutoCompleteSkinnedMesh() {
            var entries = (OwnerAsset?.GetAvatarSkinnedMeshNames() ?? Array.Empty<string>())
                .Select(name => new AutoCompleteEntry { label = name, value = name })
                .ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataInput]
        [Label("BlendShape Name")]
        [Description("Tên BlendShape cần đổi")]
        [AutoComplete(nameof(AutoCompleteBlendShapeName), forceSelection: false)]
        public string BlendShapeName = "";

        protected UniTask<AutoCompleteList> AutoCompleteBlendShapeName() {
            var entries = (OwnerAsset?.GetAvatarBlendShapeNames(SkinnedMeshName) ?? Array.Empty<string>())
                .Select(name => new AutoCompleteEntry { label = name, value = name })
                .ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataInput]
        [Label("Visible Value")]
        [Description("Giá trị khi rule bật (0 - 100)")]
        [FloatSlider(0f, 100f, 1f)]
        public float VisibleValue = 100f;

        [DataInput]
        [Label("Hidden Value")]
        [Description("Giá trị khi rule tắt (0 - 100)")]
        [FloatSlider(0f, 100f, 1f)]
        public float HiddenValue = 0f;

        public OutfitSwitcherAsset OwnerAsset;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(BlendShapeName)
                ? "BlendShape"
                : $"{BlendShapeName} ({(string.IsNullOrWhiteSpace(SkinnedMeshName) ? "All" : SkinnedMeshName)}: {VisibleValue}/{HiddenValue})";
        }
    }

    /// <summary>
    /// Quy tắc hiển thị child dùng chung giữa các outfit. Ví dụ một rule
    /// "Ears" với ChildNames = ["Ears"] có thể giữ tai tắt khi đổi outfit.
    /// </summary>
    public class OutfitChildVisibilityRule : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("Rule Name")]
        public string RuleName = "Ears";

        [DataInput]
        [Label("Child Names")]
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
        [Label("BlendShapes")]
        [Description("Danh sách BlendShape tự đổi theo trạng thái")]
        public OutfitBlendShapeRule[] BlendShapes = Array.Empty<OutfitBlendShapeRule>();

        [DataInput]
        [Label("Visible")]
        [Description("Trạng thái hiển thị")]
        public bool Visible = true;

        public OutfitSwitcherAsset OwnerAsset;
        public int RuleIndex = -1;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(RuleName) ? "Child Visibility Rule" : $"{RuleName} ({(Visible ? "Visible" : "Hidden")})";
        }

        [Trigger]
        [Label("Toggle")]
        public void Toggle() {
            OwnerAsset?.ToggleChildVisibilityRule(RuleIndex);
        }
    }

    public class OutfitPresetEntry : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("Group Name")]
        [AutoComplete(nameof(AutoCompleteGroupName), forceSelection: false)]
        public string GroupName = "Outfit";

        protected UniTask<AutoCompleteList> AutoCompleteGroupName() {
            var entries = (OwnerAsset?.GetGroupNames() ?? Array.Empty<string>())
                .Select(name => new AutoCompleteEntry { label = name, value = name })
                .ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataInput]
        [Label("Item Name or Path")]
        [AutoComplete(nameof(AutoCompleteItemNameOrPath), forceSelection: false)]
        public string ItemNameOrPath = "";

        protected UniTask<AutoCompleteList> AutoCompleteItemNameOrPath() {
            var items = OwnerAsset?.GetItems(GroupName);
            if (items != null && items.Length > 0) {
                var entries = items
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                    .Select(item => new AutoCompleteEntry {
                        label = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Path : $"{item.DisplayName} ({item.Path})",
                        value = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Path : item.DisplayName
                    })
                    .ToList();
                return UniTask.FromResult(AutoCompleteList.Single(entries));
            }

            // Fallback: Liệt kê tất cả items đã scan của mọi group
            var allItems = (OwnerAsset?.Groups ?? Array.Empty<OutfitGroup>())
                .Where(g => g?.Items != null)
                .SelectMany(g => g.Items)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new AutoCompleteEntry {
                    label = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Path : $"{item.DisplayName} ({item.Path})",
                    value = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Path : item.DisplayName
                })
                .ToList();

            if (allItems.Count == 0) {
                allItems = (OwnerAsset?.GetAllGameObjectPaths() ?? Array.Empty<string>())
                    .Select(p => new AutoCompleteEntry { label = p, value = p })
                    .ToList();
            }

            return UniTask.FromResult(AutoCompleteList.Single(allItems));
        }

        [DataInput]
        [Label("Action")]
        public OutfitPresetAction Action = OutfitPresetAction.Enable;

        public OutfitSwitcherAsset OwnerAsset;

        public string GetHeader() {
            return $"{GroupName} / {(string.IsNullOrWhiteSpace(ItemNameOrPath) ? "(Item)" : ItemNameOrPath)} ({Action})";
        }
    }

    public class OutfitPreset : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("Preset Name")]
        public string PresetName = "New Preset";

        [DataInput]
        [Label("Entries")]
        public OutfitPresetEntry[] Entries = Array.Empty<OutfitPresetEntry>();

        public OutfitSwitcherAsset OwnerAsset;
        public int PresetIndex = -1;

        public string GetHeader() {
            return string.IsNullOrWhiteSpace(PresetName) ? "Preset" : PresetName;
        }

        [Trigger]
        [Label("Apply Preset")]
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
    /// WEAR riêng; Next/Prev nhanh cho 3 group đầu nằm trên Asset.
    /// </summary>
    public class OutfitGroup : StructuredData, ICollapsibleStructuredData {

        [DataInput]
        [Label("Group Name")]
        [Description("Tên nhóm (vd Outfit, Hair)")]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("Scan Mode")]
        public OutfitScanMode ScanMode = OutfitScanMode.AvatarFolder;

        [DataInput]
        [Label("Folder Path")]
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
        [Label("Manual Paths")]
        [Description("Danh sách path thủ công")]
        [AutoComplete(nameof(AutoCompleteManualPaths), forceSelection: false)]
        [HiddenIf(nameof(IsAvatarFolderMode))]
        public string[] ManualPaths = Array.Empty<string>();

        protected UniTask<AutoCompleteList> AutoCompleteManualPaths() {
            var entries = (OwnerAsset?.GetAllGameObjectPaths() ?? Array.Empty<string>())
                .Select(path => new AutoCompleteEntry { label = path, value = path })
                .ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataInput]
        [Label("Group Type")]
        [Description("Switch: 1 active | Toggle: Bật/tắt tự do")]
        public OutfitGroupType GroupType = OutfitGroupType.Single;

        [DataInput]
        [Label("Transition")]
        public OutfitTransition Transition = OutfitTransition.Glow;

        [DataInput]
        [Label("Glow Color")]
        [HiddenIf(nameof(IsInstantTransition))]
        public Color GlowColor = new Color(1f, 0.4f, 0.8f, 1f);

        [DataInput]
        [Label("Intensity")]
        [Description("Độ sáng đỉnh glow")]
        [FloatSlider(0f, 10f, 0.1f)]
        [HiddenIf(nameof(IsInstantTransition))]
        public float Intensity = 3f;

        [DataInput]
        [Label("Duration (ms)")]
        [IntegerSlider(100, 5000, 50)]
        [HiddenIf(nameof(IsInstantTransition))]
        public int DurationMs = 600;

        [DataInput]
        [Label("Peak (0-1)")]
        [Description("Thời điểm lóa đỉnh (0 - 1)")]
        [FloatSlider(0.05f, 0.95f, 0.01f)]
        [HiddenIf(nameof(IsInstantTransition))]
        public float PeakPercent = 0.42f;

        [DataInput]
        [Label("Ignore Inactive Children")]
        [Description("Không glow phần tử đang tắt")]
        [HiddenIf(nameof(IsInstantTransition))]
        public bool IgnoreInactiveChildren = true;

        [DataInput]
        [Label("Glow Excluded Paths")]
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
        [Label("Items")]
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
        [Label("Scan Items")]
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
 