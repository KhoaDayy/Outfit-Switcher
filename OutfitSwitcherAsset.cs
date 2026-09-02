using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Scenes;
using Warudo.Plugins.Core.Assets.Character;
using Warudo.Plugins.McpBridge.Nodes;

namespace Warudo.Plugins.McpBridge {

    /// <summary>
    /// OUTFIT SWITCHER — Asset quản lý outfit switching cho Character.
    ///
    /// Chỉ cần thêm asset, chọn Character, cấu hình group (folder path
    /// hoặc manual paths), bấm Scan → danh sách items tự hiện.
    /// Bấm trigger trên từng item → đổi outfit có glow effect.
    ///
    /// Không cần nối blueprint thủ công — tất cả logic nằm trong Asset.
    /// Hỗ trợ cả blueprint node (OutfitSwitchNode) cho tích hợp nâng cao.
    ///
    /// Dùng GlowOutfitNode.Glow() static method cho transition effect (BRP).
    /// </summary>
    [AssetType(
        Id = "a7b3c1d4-e5f6-4a8b-9c0d-1e2f3a4b5c6d",
        Title = "Outfit Switcher",
        Category = "Hasukatsu"
    )]
    public class OutfitSwitcherAsset : Asset {

        [DataInput]
        [Label("CHARACTER")]
        public CharacterAsset Character;

        [DataInput]
        [Label("GROUPS")]
        [Description("Thêm bao nhiêu group tùy ý (vd Outfit, Hair, Accessories). " +
                     "Bấm + để thêm group, cấu hình folder path rồi bấm Scan. " +
                     "Mỗi item sau scan có nút 👗 WEAR riêng. " +
                     "Lưu ý: Không đưa cùng 1 GameObject vào nhiều group khác nhau để tránh xung đột trạng thái.")]
        public OutfitGroup[] Groups = Array.Empty<OutfitGroup>();

        [DataInput]
        [Label("DEBUG LOGS")]
        [Description("Bật debug log chi tiết cho scan và switch")]
        public bool DebugLogs = false;

        [Markdown(Primary = true)]
        [DataInput]
        public string Status = "➕ Thêm group rồi bấm **Scan Items** để bắt đầu.";

        // ═══════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════
        protected override void OnCreate() {
            base.OnCreate();
            Watch<CharacterAsset>(nameof(Character), (_, _) => OnCharacterChanged());
            Watch(nameof(Groups), LinkGroups);
            LinkGroups();
        }

        /// <summary>
        /// Set OwnerAsset/GroupIndex on all groups and their items.
        /// Called on create, when Groups array changes, and after scan.
        /// </summary>
        private void LinkGroups() {
            if (Groups == null) return;
            for (int g = 0; g < Groups.Length; g++) {
                var group = Groups[g];
                if (group == null) continue;
                group.OwnerAsset = this;
                group.GroupIndex = g;
                if (group.Items == null) continue;
                for (int i = 0; i < group.Items.Length; i++) {
                    var item = group.Items[i];
                    if (item == null) continue;
                    item.OwnerAsset = this;
                    item.GroupIndex = g;
                }
            }
        }

        private void OnCharacterChanged() {
            if (Character?.GameObject == null) {
                UpdateStatus("⚠️ Chưa chọn Character hoặc Character chưa load.");
                return;
            }
            UpdateStatus($"✅ Character: **{Character.Name}** — bấm Scan Items trên mỗi group.");

            // Auto-restore last active items khi đổi character
            foreach (var group in Groups) {
                if (group == null) continue;
                RestoreLastActiveItem(group);
            }
        }

        // ═══════════════════════════════════════════════════════
        // SCAN
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Quét avatar theo scan mode của group, tạo danh sách OutfitItem[].
        /// Gọi từ OutfitGroup.ScanItems() trigger.
        /// </summary>
        public void ScanGroup(OutfitGroup group) {
            if (Character?.GameObject == null) {
                UpdateStatus("⚠️ Chưa chọn Character!");
                return;
            }

            var root = Character.GameObject.transform;
            var items = new List<OutfitItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (group.ScanMode == OutfitScanMode.AvatarFolder) {
                // Tìm folder bằng path
                var folder = FindByPath(root, group.FolderPath);
                if (folder == null) {
                    UpdateStatus($"❌ Không tìm thấy folder: **{group.FolderPath}**");
                    Debug.LogWarning($"[OutfitSwitcher] Folder not found: {group.FolderPath}");
                    return;
                }

                // Mỗi child trực tiếp = 1 outfit item
                for (int i = 0; i < folder.childCount; i++) {
                    var child = folder.GetChild(i);
                    var path = GetRelativePath(root, child);
                    if (!seenPaths.Add(path)) {
                        Debug.LogWarning($"[OutfitSwitcher] Phát hiện trùng path/tên '{path}' trong folder '{group.FolderPath}'. Bỏ qua item trùng.");
                        continue;
                    }
                    var item = StructuredData.Create<OutfitItem>(sd => {
                        sd.DisplayName = child.name;
                        sd.Path = path;
                        sd.IsActive = child.gameObject.activeSelf;
                        sd.OwnerAsset = this;
                        sd.GroupIndex = group.GroupIndex;
                    });
                    items.Add(item);
                }

                if (DebugLogs)
                    Debug.Log($"[OutfitSwitcher] Scanned folder '{group.FolderPath}' → {items.Count} items");

            } else {
                // Manual mode: từng path
                foreach (var path in group.ManualPaths ?? Array.Empty<string>()) {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!seenPaths.Add(path)) {
                        Debug.LogWarning($"[OutfitSwitcher] Manual path trùng lặp: '{path}'. Bỏ qua.");
                        continue;
                    }
                    var target = FindByPath(root, path);
                    if (target == null) {
                        Debug.LogWarning($"[OutfitSwitcher] Manual path not found: {path}");
                        continue;
                    }
                    var item = StructuredData.Create<OutfitItem>(sd => {
                        sd.DisplayName = target.name;
                        sd.Path = path;
                        sd.IsActive = target.gameObject.activeSelf;
                        sd.OwnerAsset = this;
                        sd.GroupIndex = group.GroupIndex;
                    });
                    items.Add(item);
                }

                if (DebugLogs)
                    Debug.Log($"[OutfitSwitcher] Manual scan → {items.Count} items");
            }

            var itemsArray = items.ToArray();
            group.Items = itemsArray;
            group.SetDataInput(nameof(OutfitGroup.Items), itemsArray, broadcast: true);

            // Ghi nhận character đã scan — dùng để chặn restore nhầm avatar khác
            group.ScannedCharacterId = GetCharacterId();
            group.SetDataInput(nameof(OutfitGroup.ScannedCharacterId), group.ScannedCharacterId, broadcast: true);

            SetDataInput(nameof(Groups), Groups, broadcast: true);

            // Rebuild dynamic triggers cho items
            RebuildItemTriggers(group);

            UpdateStatus($"✅ Group **{group.GroupName}**: tìm thấy **{items.Count}** items.");
        }

        // ═══════════════════════════════════════════════════════
        // DYNAMIC TRIGGERS — mỗi item = 1 trigger button
        // ═══════════════════════════════════════════════════════

        private void RebuildItemTriggers(OutfitGroup group) {
            LinkGroups();
            Broadcast();
        }

        // ═══════════════════════════════════════════════════════
        // WEAR / SWITCH
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Đổi outfit trong group — API chính, dùng từ Asset trigger
        /// và OutfitSwitchNode.
        /// groupIndex: index trong array Groups (0-based)
        /// itemName: DisplayName hoặc Path của item cần wear
        /// </summary>
        public void WearItem(int groupIndex, string itemName) {
            if (Character?.GameObject == null) {
                Debug.LogWarning("[OutfitSwitcher] Character null!");
                return;
            }
            if (groupIndex < 0 || groupIndex >= Groups.Length) {
                Debug.LogWarning($"[OutfitSwitcher] Invalid group index: {groupIndex}");
                return;
            }

            var group = Groups[groupIndex];
            if (group == null) return;

            // Tìm item theo tên hoặc path
            var targetItem = FindItem(group, itemName);
            if (targetItem == null) {
                Debug.LogWarning($"[OutfitSwitcher] Item not found: '{itemName}' in group '{group.GroupName}'");
                return;
            }

            WearResolvedItem(group, targetItem);
        }

        /// <summary>
        /// Đổi outfit theo PATH chính xác (unique key sau scan) — dùng bởi
        /// OutfitItem.Wear() trigger và WearByIndex để tránh mặc nhầm item
        /// khi 2 path khác nhau có cùng DisplayName.
        /// </summary>
        public void WearItemByPath(int groupIndex, string path) {
            if (Character?.GameObject == null) {
                Debug.LogWarning("[OutfitSwitcher] Character null!");
                return;
            }
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) {
                Debug.LogWarning($"[OutfitSwitcher] Invalid group index: {groupIndex}");
                return;
            }

            var group = Groups[groupIndex];
            if (group?.Items == null) return;

            OutfitItem targetItem = null;
            foreach (var item in group.Items) {
                if (item != null && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) {
                    targetItem = item;
                    break;
                }
            }
            if (targetItem == null) {
                Debug.LogWarning($"[OutfitSwitcher] Item path not found: '{path}' in group '{group.GroupName}'");
                return;
            }

            WearResolvedItem(group, targetItem);
        }

        /// <summary>Dispatch chung sau khi đã resolve đúng item.</summary>
        private void WearResolvedItem(OutfitGroup group, OutfitItem targetItem) {
            if (group.GroupType == OutfitGroupType.Toggle) {
                // Toggle mode: đơn giản bật/tắt độc lập
                ToggleItem(group, targetItem);
            } else {
                // Switch mode: bật đồ được chọn, TẮT TOÀN BỘ các đồ khác trong group
                SwitchSingleItem(group, targetItem);
            }
        }

        /// <summary>Overload nhận tên group thay vì index</summary>
        public void WearItem(string groupName, string itemName) {
            var idx = FindGroupIndex(groupName);
            if (idx >= 0) {
                WearItem(idx, itemName);
            } else {
                Debug.LogWarning($"[OutfitSwitcher] Group not found: '{groupName}'");
            }
        }

        /// <summary>Chuyển sang item tiếp theo trong group (vòng tròn)</summary>
        public void SwitchNextItem(int groupIndex) {
            if (groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || group.Items.Length == 0) return;

            var currentIndex = GetActiveItemIndex(group);
            var nextIndex = (currentIndex + 1) % group.Items.Length;
            WearByIndex(groupIndex, nextIndex);
        }

        public void SwitchNextItem(string groupName) {
            var idx = FindGroupIndex(groupName);
            if (idx >= 0) SwitchNextItem(idx);
        }

        /// <summary>Chuyển sang item trước đó trong group (vòng tròn)</summary>
        public void SwitchPreviousItem(int groupIndex) {
            if (groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || group.Items.Length == 0) return;

            var currentIndex = GetActiveItemIndex(group);
            var prevIndex = currentIndex <= 0 ? group.Items.Length - 1 : currentIndex - 1;
            WearByIndex(groupIndex, prevIndex);
        }

        public void SwitchPreviousItem(string groupName) {
            var idx = FindGroupIndex(groupName);
            if (idx >= 0) SwitchPreviousItem(idx);
        }

        /// <summary>Chuyển sang item ngẫu nhiên khác trong group</summary>
        public void SwitchRandomItem(int groupIndex) {
            if (groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || group.Items.Length == 0) return;
            if (group.Items.Length == 1) {
                WearByIndex(groupIndex, 0);
                return;
            }

            var currentIndex = GetActiveItemIndex(group);
            int nextIndex;
            do {
                nextIndex = UnityEngine.Random.Range(0, group.Items.Length);
            } while (nextIndex == currentIndex && group.Items.Length > 1);

            WearByIndex(groupIndex, nextIndex);
        }

        public void SwitchRandomItem(string groupName) {
            var idx = FindGroupIndex(groupName);
            if (idx >= 0) SwitchRandomItem(idx);
        }

        private int GetActiveItemIndex(OutfitGroup group) {
            if (group?.Items == null || Character?.GameObject == null) return -1;
            var root = Character.GameObject.transform;
            for (int i = 0; i < group.Items.Length; i++) {
                var item = group.Items[i];
                if (item == null) continue;
                var go = FindByPath(root, item.Path);
                if (go != null && go.gameObject.activeSelf) return i;
            }
            return -1;
        }

        private int FindGroupIndex(string groupName) {
            if (Groups == null) return -1;
            for (int i = 0; i < Groups.Length; i++) {
                if (Groups[i] != null && string.Equals(Groups[i].GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // ── Toggle (multi-wear) ──

        private void ToggleItem(OutfitGroup group, OutfitItem item) {
            var root = Character.GameObject.transform;
            var target = FindByPath(root, item.Path);
            if (target == null) return;

            if (group.Transition == OutfitTransition.Glow) {
                // Glow toggle: flash rồi toggle tương đối theo trạng thái thực tế
                GlowOutfitNode.Glow(
                    Character,
                    new[] { item.Path },
                    group.GlowColor,
                    group.Intensity,
                    group.DurationMs,
                    group.PeakPercent,
                    onPeak: () => {
                        if (target != null)
                            target.gameObject.SetActive(!target.gameObject.activeSelf);
                        UpdateItemStates(group);
                        SaveGroupActiveState(group);
                    },
                    glowKey: "item:" + item.Path,
                    flushOnCancel: true,
                    debugLog: DebugLogs
                ).Forget();
            } else {
                target.gameObject.SetActive(!target.gameObject.activeSelf);
                UpdateItemStates(group);
                SaveGroupActiveState(group);
            }

            if (DebugLogs)
                Debug.Log($"[OutfitSwitcher] Toggle '{item.DisplayName}' triggered");
        }

        // ── Switch (1 active duy nhất — logic switch chuẩn) ──

        private void SwitchSingleItem(OutfitGroup group, OutfitItem targetItem) {
            if (group?.Items == null || targetItem == null) return;
            if (Character?.GameObject == null) {
                Debug.LogWarning("[OutfitSwitcher] Character GameObject is null!");
                return;
            }

            var root = Character.GameObject.transform;
            var targetGo = FindByPath(root, targetItem.Path);
            if (targetGo == null) {
                Debug.LogWarning($"[OutfitSwitcher] Target GameObject not found: {targetItem.Path}");
                return;
            }

            // Tìm tất cả GameObject của các item khác trong group và kiểm tra cái nào đang active thực tế trên avatar
            var activeOldItems = new List<OutfitItem>();
            var allOtherGos = new List<GameObject>();

            foreach (var it in group.Items) {
                if (it == null || string.Equals(it.Path, targetItem.Path, StringComparison.OrdinalIgnoreCase)) continue;
                var go = FindByPath(root, it.Path);
                if (go != null) {
                    allOtherGos.Add(go.gameObject);
                    if (go.gameObject.activeSelf) {
                        activeOldItems.Add(it);
                    }
                }
            }

            bool targetAlreadyActive = targetGo.gameObject.activeSelf;
            // Nếu target đã active VÀ không còn item nào khác trong group đang active -> Đã đúng trạng thái switch, không cần làm gì
            if (targetAlreadyActive && activeOldItems.Count == 0) {
                if (DebugLogs) Debug.Log($"[OutfitSwitcher] '{targetItem.DisplayName}' already active & all others off, skipping");
                UpdateItemStates(group);
                return;
            }

            if (group.Transition == OutfitTransition.Glow && activeOldItems.Count > 0) {
                // Glow transition: glow các outfit cũ đang active -> peak -> tắt hết cũ, bật mới -> glow outfit mới tan dần
                var oldPaths = activeOldItems.Select(x => x.Path).ToArray();
                var newPaths = new[] { targetItem.Path };

                GlowOutfitNode.Glow(
                    Character,
                    oldPaths,
                    group.GlowColor,
                    group.Intensity,
                    group.DurationMs,
                    group.PeakPercent,
                    onPeak: () => {
                        // Tại peak: TẮT TOÀN BỘ các item khác trong group, BẬT target
                        foreach (var go in allOtherGos) {
                            if (go != null) go.SetActive(false);
                        }
                        if (targetGo != null) targetGo.gameObject.SetActive(true);
                        UpdateItemStates(group);
                        SaveGroupActiveState(group);
                    },
                    swapPaths: newPaths,
                    glowKey: "group:" + group.GroupName,
                    debugLog: DebugLogs
                ).Forget();

            } else if (group.Transition == OutfitTransition.Glow && activeOldItems.Count == 0) {
                // Chưa có outfit nào trong group active -> tắt các đồ khác (đảm bảo sạch), bật target và glow target
                foreach (var go in allOtherGos) {
                    if (go != null) go.SetActive(false);
                }
                targetGo.gameObject.SetActive(true);
                UpdateItemStates(group);
                SaveGroupActiveState(group);

                GlowOutfitNode.Glow(
                    Character,
                    new[] { targetItem.Path },
                    group.GlowColor,
                    group.Intensity,
                    group.DurationMs,
                    group.PeakPercent,
                    onPeak: () => { },
                    glowKey: "group:" + group.GroupName,
                    debugLog: DebugLogs
                ).Forget();

            } else {
                // Instant: tắt toàn bộ đồ khác trong group, bật target ngay lập tức
                foreach (var go in allOtherGos) {
                    if (go != null) go.SetActive(false);
                }
                targetGo.gameObject.SetActive(true);
                UpdateItemStates(group);
                SaveGroupActiveState(group);
            }

            UpdateStatus($"👗 Switched **{group.GroupName}** → **{targetItem.DisplayName}**");

            if (DebugLogs)
                Debug.Log($"[OutfitSwitcher] Switched to '{targetItem.DisplayName}' in group '{group.GroupName}' (deactivated {allOtherGos.Count} other items)");
        }

        // ═══════════════════════════════════════════════════════
        // STATE PERSISTENCE & RESTORE
        // ═══════════════════════════════════════════════════════

        private void SaveGroupActiveState(OutfitGroup group) {
            if (group?.Items == null) return;
            var root = Character?.GameObject?.transform;
            if (root == null) return;

            var activeNames = new List<string>();
            foreach (var item in group.Items) {
                if (item == null) continue;
                var go = FindByPath(root, item.Path);
                if (go != null && go.gameObject.activeSelf) {
                    activeNames.Add(item.DisplayName);
                }
            }

            if (group.GroupType == OutfitGroupType.Single) {
                if (activeNames.Count == 1) group.LastActiveItem = activeNames[0];
                else if (activeNames.Count == 0) group.LastActiveItem = "";
            }
            group.LastActiveItems = activeNames.ToArray();
        }

        private void RestoreLastActiveItem(OutfitGroup group) {
            if (group?.Items == null || group.Items.Length == 0) return;
            if (Character?.GameObject == null) return;

            // GUARD: chỉ restore khi character hiện tại đúng là character đã scan.
            // Items[].Path scan từ avatar cũ + fallback tìm theo leaf name có thể
            // khớp nhầm object trùng tên (Body, Hair...) trên avatar khác và
            // SetActive nhầm nó. Group scan trước phiên bản có ScannedCharacterId
            // (chuỗi rỗng) cũng bị bỏ qua — cần re-scan để kích hoạt lại restore.
            var currentId = GetCharacterId();
            if (string.IsNullOrEmpty(group.ScannedCharacterId) ||
                !string.Equals(group.ScannedCharacterId, currentId, StringComparison.Ordinal)) {
                if (DebugLogs)
                    Debug.Log($"[OutfitSwitcher] Skip restore group '{group.GroupName}': scanned for '{group.ScannedCharacterId}', current is '{currentId}'. Re-scan để dùng với character này.");
                return;
            }

            var root = Character.GameObject.transform;

            if (group.GroupType == OutfitGroupType.Single) {
                if (string.IsNullOrEmpty(group.LastActiveItem)) return;
                foreach (var item in group.Items) {
                    if (item == null) continue;
                    var go = FindByPath(root, item.Path);
                    if (go == null) continue;

                    bool shouldBeActive = string.Equals(item.DisplayName, group.LastActiveItem,
                        StringComparison.OrdinalIgnoreCase);
                    go.gameObject.SetActive(shouldBeActive);
                    item.IsActive = shouldBeActive;
                }
            } else {
                // Restore cho Toggle group (multi-wear)
                if (group.LastActiveItems == null || group.LastActiveItems.Length == 0) return;
                var activeSet = new HashSet<string>(group.LastActiveItems, StringComparer.OrdinalIgnoreCase);
                foreach (var item in group.Items) {
                    if (item == null) continue;
                    var go = FindByPath(root, item.Path);
                    if (go == null) continue;

                    bool shouldBeActive = activeSet.Contains(item.DisplayName);
                    go.gameObject.SetActive(shouldBeActive);
                    item.IsActive = shouldBeActive;
                }
            }

            UpdateItemStates(group);
        }

        // ═══════════════════════════════════════════════════════
        // QUICK TRIGGERS — Next/Prev cho 3 group đầu (phím tắt tiện dụng).
        // Việc WEAR từng item nằm ngay trên mỗi OutfitItem (nút 👗 WEAR),
        // hiển thị đúng tên đồ và KHÔNG giới hạn số group / số item.
        // Group thứ 4 trở đi vẫn điều khiển đầy đủ qua Blueprint node.
        // ═══════════════════════════════════════════════════════

        [Trigger] [Label("G1: ⏭️ Next Item")] [HiddenIf(nameof(ShouldHideG1_Group))] public void G1_Next() => SwitchNextItem(0);
        [Trigger] [Label("G1: ⏮️ Prev Item")] [HiddenIf(nameof(ShouldHideG1_Group))] public void G1_Prev() => SwitchPreviousItem(0);
        [Trigger] [Label("G2: ⏭️ Next Item")] [HiddenIf(nameof(ShouldHideG2_Group))] public void G2_Next() => SwitchNextItem(1);
        [Trigger] [Label("G2: ⏮️ Prev Item")] [HiddenIf(nameof(ShouldHideG2_Group))] public void G2_Prev() => SwitchPreviousItem(1);
        [Trigger] [Label("G3: ⏭️ Next Item")] [HiddenIf(nameof(ShouldHideG3_Group))] public void G3_Next() => SwitchNextItem(2);
        [Trigger] [Label("G3: ⏮️ Prev Item")] [HiddenIf(nameof(ShouldHideG3_Group))] public void G3_Prev() => SwitchPreviousItem(2);

        // ── Trigger helper ──

        private void WearByIndex(int groupIndex, int itemIndex) {
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || itemIndex < 0 || itemIndex >= group.Items.Length) return;
            var item = group.Items[itemIndex];
            if (item == null) return;
            // Dùng Path (unique sau scan) — DisplayName có thể trùng giữa các item
            WearItemByPath(groupIndex, item.Path);
        }

        // ── HiddenIf conditions — ẩn triggers khi group chưa có items ──
        protected bool ShouldHideG1_Group() => !HasGroup(0);
        protected bool ShouldHideG2_Group() => !HasGroup(1);
        protected bool ShouldHideG3_Group() => !HasGroup(2);

        private bool HasGroup(int groupIndex) {
            if (Groups == null || groupIndex >= Groups.Length) return false;
            var g = Groups[groupIndex];
            return g != null && g.Items != null && g.Items.Length > 0;
        }

        // ═══════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════

        private OutfitItem FindItem(OutfitGroup group, string nameOrPath) {
            if (group?.Items == null) return null;
            foreach (var item in group.Items) {
                if (item == null) continue;
                if (string.Equals(item.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return item;
                if (string.Equals(item.Path, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private void UpdateItemStates(OutfitGroup group) {
            if (group?.Items == null) return;
            var root = Character?.GameObject?.transform;
            if (root == null) return;

            foreach (var item in group.Items) {
                if (item == null) continue;
                var go = FindByPath(root, item.Path);
                if (go != null) {
                    item.IsActive = go.gameObject.activeSelf;
                }
            }
            group.SetDataInput(nameof(OutfitGroup.Items), group.Items, broadcast: true);
        }

        private void UpdateStatus(string msg) {
            SetDataInput(nameof(Status), msg, broadcast: true);
        }

        /// <summary>
        /// Identifier ổn định cho character hiện tại — dùng để khớp group đã
        /// scan với đúng avatar trước khi restore/apply trạng thái.
        /// </summary>
        private string GetCharacterId() {
            if (Character == null) return "";
            // Name ổn định qua các lần load scene và đủ phân biệt avatar khác nhau
            return Character.Name ?? "";
        }

        // ── Path utilities — tìm Transform bằng path có dấu / ──

        /// <summary>
        /// Tìm Transform con theo path (kiểu "Assets/Outfits/SuriMukeki").
        /// Dùng Transform.Find (path chia bằng /).
        /// Nếu không tìm thấy bằng full path, thử tìm theo suffix (tương thích
        /// với convention của nhiều avatar model).
        /// </summary>
        private static Transform FindByPath(Transform root, string path) {
            if (root == null || string.IsNullOrWhiteSpace(path)) return null;
            var go = GlowOutfitNode.FindGameObjectByPath(root.gameObject, path);
            return go != null ? go.transform : null;
        }

        /// <summary>
        /// Lấy path relative từ root đến target (không bao gồm tên root).
        /// </summary>
        internal static string GetRelativePath(Transform root, Transform target) {
            if (target == root) return "";
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root) {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
