using System;
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
    }

    // ═══════════════════════════════════════════════════════
    // OUTFIT GROUP — một nhóm outfit (folder hoặc manual paths)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Một nhóm outfit: quét folder/manual paths → danh sách OutfitItem[].
    /// Bấm trigger ScanItems để quét. Các item được hiển thị như trigger
    /// buttons trên OutfitSwitcherAsset (G1/G2/G3).
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
        [Description("Path tới folder chứa các outfit, vd 'Clothes/Outfits'")]
        [HiddenIf(nameof(IsManualMode))]
        public string FolderPath = "";

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
        [HiddenIf(nameof(IsInstantTransition))]
        public float Intensity = 3f;

        [DataInput]
        [Label("DURATION (MS)")]
        [HiddenIf(nameof(IsInstantTransition))]
        public int DurationMs = 600;

        [DataInput]
        [Label("PEAK (0-1)")]
        [Description("Thời điểm lóa cực đại, 0.42 ≈ 0.25s với 600ms")]
        [HiddenIf(nameof(IsInstantTransition))]
        public float PeakPercent = 0.42f;

        [DataInput]
        [Hidden]
        public string LastActiveItem = "";

        [DataInput]
        [Hidden]
        public string[] LastActiveItems = Array.Empty<string>();

        [DataInput]
        [Hidden]
        public OutfitItem[] Items = Array.Empty<OutfitItem>();

        // Runtime links — được set lại bởi OutfitSwitcherAsset.LinkGroups()
        public OutfitSwitcherAsset OwnerAsset;
        public int GroupIndex = -1;

        // ── Trigger ──

        [Trigger]
        [Label("Scan Items")]
        [Description("Quét danh sách outfit theo folder/manual paths")]
        public void ScanItems() {
            OwnerAsset?.ScanGroup(this);
        }

        // ── HiddenIf conditions ──

        protected bool IsManualMode() => ScanMode == OutfitScanMode.Manual;
        protected bool IsAvatarFolderMode() => ScanMode == OutfitScanMode.AvatarFolder;
        protected bool IsInstantTransition() => Transition == OutfitTransition.Instant;
    }
}
