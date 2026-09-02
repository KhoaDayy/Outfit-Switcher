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
        [Description("Tối đa 3 group (vd Outfit, Hair, Accessories). " +
                     "Bấm + để thêm group, cấu hình folder path rồi bấm Scan. " +
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

            group.Items = items.ToArray();
            group.SetDataInput(nameof(OutfitGroup.Items), items.ToArray(), broadcast: true);
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
        // TRIGGER BUTTONS — mỗi group có trigger buttons cho items
        // ═══════════════════════════════════════════════════════

        // Group 1 triggers (tối đa 20 items cho mỗi group)
        [Trigger] [Label("G1: ⏭️ Next Item")] [HiddenIf(nameof(ShouldHideG1_Group))] public void G1_Next()  => SwitchNextItem(0);
        [Trigger] [Label("G1: ⏮️ Prev Item")] [HiddenIf(nameof(ShouldHideG1_Group))] public void G1_Prev()  => SwitchPreviousItem(0);
        [Trigger] [Label("G1: Wear #1")]  [HiddenIf(nameof(ShouldHideG1_0))]  public void G1_Wear0()  => WearByIndex(0, 0);
        [Trigger] [Label("G1: Wear #2")]  [HiddenIf(nameof(ShouldHideG1_1))]  public void G1_Wear1()  => WearByIndex(0, 1);
        [Trigger] [Label("G1: Wear #3")]  [HiddenIf(nameof(ShouldHideG1_2))]  public void G1_Wear2()  => WearByIndex(0, 2);
        [Trigger] [Label("G1: Wear #4")]  [HiddenIf(nameof(ShouldHideG1_3))]  public void G1_Wear3()  => WearByIndex(0, 3);
        [Trigger] [Label("G1: Wear #5")]  [HiddenIf(nameof(ShouldHideG1_4))]  public void G1_Wear4()  => WearByIndex(0, 4);
        [Trigger] [Label("G1: Wear #6")]  [HiddenIf(nameof(ShouldHideG1_5))]  public void G1_Wear5()  => WearByIndex(0, 5);
        [Trigger] [Label("G1: Wear #7")]  [HiddenIf(nameof(ShouldHideG1_6))]  public void G1_Wear6()  => WearByIndex(0, 6);
        [Trigger] [Label("G1: Wear #8")]  [HiddenIf(nameof(ShouldHideG1_7))]  public void G1_Wear7()  => WearByIndex(0, 7);
        [Trigger] [Label("G1: Wear #9")]  [HiddenIf(nameof(ShouldHideG1_8))]  public void G1_Wear8()  => WearByIndex(0, 8);
        [Trigger] [Label("G1: Wear #10")] [HiddenIf(nameof(ShouldHideG1_9))]  public void G1_Wear9()  => WearByIndex(0, 9);
        [Trigger] [Label("G1: Wear #11")] [HiddenIf(nameof(ShouldHideG1_10))] public void G1_Wear10() => WearByIndex(0, 10);
        [Trigger] [Label("G1: Wear #12")] [HiddenIf(nameof(ShouldHideG1_11))] public void G1_Wear11() => WearByIndex(0, 11);
        [Trigger] [Label("G1: Wear #13")] [HiddenIf(nameof(ShouldHideG1_12))] public void G1_Wear12() => WearByIndex(0, 12);
        [Trigger] [Label("G1: Wear #14")] [HiddenIf(nameof(ShouldHideG1_13))] public void G1_Wear13() => WearByIndex(0, 13);
        [Trigger] [Label("G1: Wear #15")] [HiddenIf(nameof(ShouldHideG1_14))] public void G1_Wear14() => WearByIndex(0, 14);
        [Trigger] [Label("G1: Wear #16")] [HiddenIf(nameof(ShouldHideG1_15))] public void G1_Wear15() => WearByIndex(0, 15);
        [Trigger] [Label("G1: Wear #17")] [HiddenIf(nameof(ShouldHideG1_16))] public void G1_Wear16() => WearByIndex(0, 16);
        [Trigger] [Label("G1: Wear #18")] [HiddenIf(nameof(ShouldHideG1_17))] public void G1_Wear17() => WearByIndex(0, 17);
        [Trigger] [Label("G1: Wear #19")] [HiddenIf(nameof(ShouldHideG1_18))] public void G1_Wear18() => WearByIndex(0, 18);
        [Trigger] [Label("G1: Wear #20")] [HiddenIf(nameof(ShouldHideG1_19))] public void G1_Wear19() => WearByIndex(0, 19);

        // Group 2 triggers
        [Trigger] [Label("G2: ⏭️ Next Item")] [HiddenIf(nameof(ShouldHideG2_Group))] public void G2_Next()  => SwitchNextItem(1);
        [Trigger] [Label("G2: ⏮️ Prev Item")] [HiddenIf(nameof(ShouldHideG2_Group))] public void G2_Prev()  => SwitchPreviousItem(1);
        [Trigger] [Label("G2: Wear #1")]  [HiddenIf(nameof(ShouldHideG2_0))]  public void G2_Wear0()  => WearByIndex(1, 0);
        [Trigger] [Label("G2: Wear #2")]  [HiddenIf(nameof(ShouldHideG2_1))]  public void G2_Wear1()  => WearByIndex(1, 1);
        [Trigger] [Label("G2: Wear #3")]  [HiddenIf(nameof(ShouldHideG2_2))]  public void G2_Wear2()  => WearByIndex(1, 2);
        [Trigger] [Label("G2: Wear #4")]  [HiddenIf(nameof(ShouldHideG2_3))]  public void G2_Wear3()  => WearByIndex(1, 3);
        [Trigger] [Label("G2: Wear #5")]  [HiddenIf(nameof(ShouldHideG2_4))]  public void G2_Wear4()  => WearByIndex(1, 4);
        [Trigger] [Label("G2: Wear #6")]  [HiddenIf(nameof(ShouldHideG2_5))]  public void G2_Wear5()  => WearByIndex(1, 5);
        [Trigger] [Label("G2: Wear #7")]  [HiddenIf(nameof(ShouldHideG2_6))]  public void G2_Wear6()  => WearByIndex(1, 6);
        [Trigger] [Label("G2: Wear #8")]  [HiddenIf(nameof(ShouldHideG2_7))]  public void G2_Wear7()  => WearByIndex(1, 7);
        [Trigger] [Label("G2: Wear #9")]  [HiddenIf(nameof(ShouldHideG2_8))]  public void G2_Wear8()  => WearByIndex(1, 8);
        [Trigger] [Label("G2: Wear #10")] [HiddenIf(nameof(ShouldHideG2_9))]  public void G2_Wear9()  => WearByIndex(1, 9);
        [Trigger] [Label("G2: Wear #11")] [HiddenIf(nameof(ShouldHideG2_10))] public void G2_Wear10() => WearByIndex(1, 10);
        [Trigger] [Label("G2: Wear #12")] [HiddenIf(nameof(ShouldHideG2_11))] public void G2_Wear11() => WearByIndex(1, 11);
        [Trigger] [Label("G2: Wear #13")] [HiddenIf(nameof(ShouldHideG2_12))] public void G2_Wear12() => WearByIndex(1, 12);
        [Trigger] [Label("G2: Wear #14")] [HiddenIf(nameof(ShouldHideG2_13))] public void G2_Wear13() => WearByIndex(1, 13);
        [Trigger] [Label("G2: Wear #15")] [HiddenIf(nameof(ShouldHideG2_14))] public void G2_Wear14() => WearByIndex(1, 14);
        [Trigger] [Label("G2: Wear #16")] [HiddenIf(nameof(ShouldHideG2_15))] public void G2_Wear15() => WearByIndex(1, 15);
        [Trigger] [Label("G2: Wear #17")] [HiddenIf(nameof(ShouldHideG2_16))] public void G2_Wear16() => WearByIndex(1, 16);
        [Trigger] [Label("G2: Wear #18")] [HiddenIf(nameof(ShouldHideG2_17))] public void G2_Wear17() => WearByIndex(1, 17);
        [Trigger] [Label("G2: Wear #19")] [HiddenIf(nameof(ShouldHideG2_18))] public void G2_Wear18() => WearByIndex(1, 18);
        [Trigger] [Label("G2: Wear #20")] [HiddenIf(nameof(ShouldHideG2_19))] public void G2_Wear19() => WearByIndex(1, 19);

        // Group 3 triggers
        [Trigger] [Label("G3: ⏭️ Next Item")] [HiddenIf(nameof(ShouldHideG3_Group))] public void G3_Next()  => SwitchNextItem(2);
        [Trigger] [Label("G3: ⏮️ Prev Item")] [HiddenIf(nameof(ShouldHideG3_Group))] public void G3_Prev()  => SwitchPreviousItem(2);
        [Trigger] [Label("G3: Wear #1")]  [HiddenIf(nameof(ShouldHideG3_0))]  public void G3_Wear0()  => WearByIndex(2, 0);
        [Trigger] [Label("G3: Wear #2")]  [HiddenIf(nameof(ShouldHideG3_1))]  public void G3_Wear1()  => WearByIndex(2, 1);
        [Trigger] [Label("G3: Wear #3")]  [HiddenIf(nameof(ShouldHideG3_2))]  public void G3_Wear2()  => WearByIndex(2, 2);
        [Trigger] [Label("G3: Wear #4")]  [HiddenIf(nameof(ShouldHideG3_3))]  public void G3_Wear3()  => WearByIndex(2, 3);
        [Trigger] [Label("G3: Wear #5")]  [HiddenIf(nameof(ShouldHideG3_4))]  public void G3_Wear4()  => WearByIndex(2, 4);
        [Trigger] [Label("G3: Wear #6")]  [HiddenIf(nameof(ShouldHideG3_5))]  public void G3_Wear5()  => WearByIndex(2, 5);
        [Trigger] [Label("G3: Wear #7")]  [HiddenIf(nameof(ShouldHideG3_6))]  public void G3_Wear6()  => WearByIndex(2, 6);
        [Trigger] [Label("G3: Wear #8")]  [HiddenIf(nameof(ShouldHideG3_7))]  public void G3_Wear7()  => WearByIndex(2, 7);
        [Trigger] [Label("G3: Wear #9")]  [HiddenIf(nameof(ShouldHideG3_8))]  public void G3_Wear8()  => WearByIndex(2, 8);
        [Trigger] [Label("G3: Wear #10")] [HiddenIf(nameof(ShouldHideG3_9))]  public void G3_Wear9()  => WearByIndex(2, 9);
        [Trigger] [Label("G3: Wear #11")] [HiddenIf(nameof(ShouldHideG3_10))] public void G3_Wear10() => WearByIndex(2, 10);
        [Trigger] [Label("G3: Wear #12")] [HiddenIf(nameof(ShouldHideG3_11))] public void G3_Wear11() => WearByIndex(2, 11);
        [Trigger] [Label("G3: Wear #13")] [HiddenIf(nameof(ShouldHideG3_12))] public void G3_Wear12() => WearByIndex(2, 12);
        [Trigger] [Label("G3: Wear #14")] [HiddenIf(nameof(ShouldHideG3_13))] public void G3_Wear13() => WearByIndex(2, 13);
        [Trigger] [Label("G3: Wear #15")] [HiddenIf(nameof(ShouldHideG3_14))] public void G3_Wear14() => WearByIndex(2, 14);
        [Trigger] [Label("G3: Wear #16")] [HiddenIf(nameof(ShouldHideG3_15))] public void G3_Wear15() => WearByIndex(2, 15);
        [Trigger] [Label("G3: Wear #17")] [HiddenIf(nameof(ShouldHideG3_16))] public void G3_Wear16() => WearByIndex(2, 16);
        [Trigger] [Label("G3: Wear #18")] [HiddenIf(nameof(ShouldHideG3_17))] public void G3_Wear17() => WearByIndex(2, 17);
        [Trigger] [Label("G3: Wear #19")] [HiddenIf(nameof(ShouldHideG3_18))] public void G3_Wear18() => WearByIndex(2, 18);
        [Trigger] [Label("G3: Wear #20")] [HiddenIf(nameof(ShouldHideG3_19))] public void G3_Wear19() => WearByIndex(2, 19);

        // ── Trigger helper ──

        private void WearByIndex(int groupIndex, int itemIndex) {
            if (groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || itemIndex >= group.Items.Length) return;
            var item = group.Items[itemIndex];
            if (item == null) return;
            WearItem(groupIndex, item.DisplayName);
        }

        // ── HiddenIf conditions — ẩn triggers khi group/item chưa tồn tại ──
        protected bool ShouldHideG1_Group() => !HasGroup(0);
        protected bool ShouldHideG2_Group() => !HasGroup(1);
        protected bool ShouldHideG3_Group() => !HasGroup(2);

        private bool HasGroup(int groupIndex) {
            if (Groups == null || groupIndex >= Groups.Length) return false;
            var g = Groups[groupIndex];
            return g != null && g.Items != null && g.Items.Length > 0;
        }

        // Group 1
        protected bool ShouldHideG1_0()  => !HasItem(0, 0);
        protected bool ShouldHideG1_1()  => !HasItem(0, 1);
        protected bool ShouldHideG1_2()  => !HasItem(0, 2);
        protected bool ShouldHideG1_3()  => !HasItem(0, 3);
        protected bool ShouldHideG1_4()  => !HasItem(0, 4);
        protected bool ShouldHideG1_5()  => !HasItem(0, 5);
        protected bool ShouldHideG1_6()  => !HasItem(0, 6);
        protected bool ShouldHideG1_7()  => !HasItem(0, 7);
        protected bool ShouldHideG1_8()  => !HasItem(0, 8);
        protected bool ShouldHideG1_9()  => !HasItem(0, 9);
        protected bool ShouldHideG1_10() => !HasItem(0, 10);
        protected bool ShouldHideG1_11() => !HasItem(0, 11);
        protected bool ShouldHideG1_12() => !HasItem(0, 12);
        protected bool ShouldHideG1_13() => !HasItem(0, 13);
        protected bool ShouldHideG1_14() => !HasItem(0, 14);
        protected bool ShouldHideG1_15() => !HasItem(0, 15);
        protected bool ShouldHideG1_16() => !HasItem(0, 16);
        protected bool ShouldHideG1_17() => !HasItem(0, 17);
        protected bool ShouldHideG1_18() => !HasItem(0, 18);
        protected bool ShouldHideG1_19() => !HasItem(0, 19);
        // Group 2
        protected bool ShouldHideG2_0()  => !HasItem(1, 0);
        protected bool ShouldHideG2_1()  => !HasItem(1, 1);
        protected bool ShouldHideG2_2()  => !HasItem(1, 2);
        protected bool ShouldHideG2_3()  => !HasItem(1, 3);
        protected bool ShouldHideG2_4()  => !HasItem(1, 4);
        protected bool ShouldHideG2_5()  => !HasItem(1, 5);
        protected bool ShouldHideG2_6()  => !HasItem(1, 6);
        protected bool ShouldHideG2_7()  => !HasItem(1, 7);
        protected bool ShouldHideG2_8()  => !HasItem(1, 8);
        protected bool ShouldHideG2_9()  => !HasItem(1, 9);
        protected bool ShouldHideG2_10() => !HasItem(1, 10);
        protected bool ShouldHideG2_11() => !HasItem(1, 11);
        protected bool ShouldHideG2_12() => !HasItem(1, 12);
        protected bool ShouldHideG2_13() => !HasItem(1, 13);
        protected bool ShouldHideG2_14() => !HasItem(1, 14);
        protected bool ShouldHideG2_15() => !HasItem(1, 15);
        protected bool ShouldHideG2_16() => !HasItem(1, 16);
        protected bool ShouldHideG2_17() => !HasItem(1, 17);
        protected bool ShouldHideG2_18() => !HasItem(1, 18);
        protected bool ShouldHideG2_19() => !HasItem(1, 19);
        // Group 3
        protected bool ShouldHideG3_0()  => !HasItem(2, 0);
        protected bool ShouldHideG3_1()  => !HasItem(2, 1);
        protected bool ShouldHideG3_2()  => !HasItem(2, 2);
        protected bool ShouldHideG3_3()  => !HasItem(2, 3);
        protected bool ShouldHideG3_4()  => !HasItem(2, 4);
        protected bool ShouldHideG3_5()  => !HasItem(2, 5);
        protected bool ShouldHideG3_6()  => !HasItem(2, 6);
        protected bool ShouldHideG3_7()  => !HasItem(2, 7);
        protected bool ShouldHideG3_8()  => !HasItem(2, 8);
        protected bool ShouldHideG3_9()  => !HasItem(2, 9);
        protected bool ShouldHideG3_10() => !HasItem(2, 10);
        protected bool ShouldHideG3_11() => !HasItem(2, 11);
        protected bool ShouldHideG3_12() => !HasItem(2, 12);
        protected bool ShouldHideG3_13() => !HasItem(2, 13);
        protected bool ShouldHideG3_14() => !HasItem(2, 14);
        protected bool ShouldHideG3_15() => !HasItem(2, 15);
        protected bool ShouldHideG3_16() => !HasItem(2, 16);
        protected bool ShouldHideG3_17() => !HasItem(2, 17);
        protected bool ShouldHideG3_18() => !HasItem(2, 18);
        protected bool ShouldHideG3_19() => !HasItem(2, 19);

        private bool HasItem(int groupIndex, int itemIndex) {
            if (Groups == null || groupIndex >= Groups.Length) return false;
            var g = Groups[groupIndex];
            if (g?.Items == null || itemIndex >= g.Items.Length) return false;
            return true;
        }

        // ═══════════════════════════════════════════════════════
        // ONUPDATE — Warudo lifecycle callback
        // ═══════════════════════════════════════════════════════

        public override void OnUpdate() {
            base.OnUpdate();
            // Note: [Trigger] attribute labels trong Warudo là metadata tĩnh tại compile-time,
            // không thể đổi label runtime. Các trigger G1_Wear0..19 được ánh xạ theo index
            // và ẩn/hiện động qua HiddenIf gating.
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
  