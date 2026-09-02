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
        [Label("CHILD VISIBILITY RULES")]
        [Description("Công tắc dùng chung cho child nằm trong outfit, vd Ears/Tail. Rule được tự áp dụng lại sau mỗi lần đổi outfit.")]
        public OutfitChildVisibilityRule[] ChildVisibilityRules = Array.Empty<OutfitChildVisibilityRule>();

        [DataInput]
        [Label("PRESETS")]
        [Description("Mỗi preset có thể mặc outfit và bật/tắt nhiều phụ kiện bằng một nút.")]
        public OutfitPreset[] Presets = Array.Empty<OutfitPreset>();

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
            Watch<CharacterAsset>(nameof(Character), (from, to) => OnCharacterChanged(from, to));
            WatchAll(new[] { nameof(Groups), nameof(ChildVisibilityRules), nameof(Presets) }, LinkRuntimeData);
            LinkRuntimeData();
        }

        /// <summary>
        /// Set OwnerAsset/GroupIndex on all groups and their items.
        /// Called on create, when Groups array changes, and after scan.
        /// </summary>
        private void LinkRuntimeData() {
            LinkGroups();
            if (ChildVisibilityRules != null) {
                for (var i = 0; i < ChildVisibilityRules.Length; i++) {
                    var rule = ChildVisibilityRules[i];
                    if (rule == null) continue;
                    rule.OwnerAsset = this;
                    rule.RuleIndex = i;
                }
            }
            if (Presets != null) {
                for (var i = 0; i < Presets.Length; i++) {
                    var preset = Presets[i];
                    if (preset == null) continue;
                    preset.OwnerAsset = this;
                    preset.PresetIndex = i;
                }
            }
        }

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

        private void OnCharacterChanged(CharacterAsset previous, CharacterAsset current) {
            if (previous != null && previous != current) GlowOutfitNode.CancelAll(previous);
            if (Character?.GameObject == null) {
                UpdateStatus("⚠️ Chưa chọn Character hoặc Character chưa load.");
                return;
            }
            UpdateStatus($"✅ Character: **{Character.Name}** — bấm Scan Items trên mỗi group.");

            // Auto-restore last active items khi đổi character
            foreach (var group in Groups ?? Array.Empty<OutfitGroup>()) {
                if (group == null) continue;
                RestoreLastActiveItem(group);
            }
            ApplyChildVisibilityRules();
        }

        protected override void OnDestroy() {
            GlowOutfitNode.CancelAll(Character);
            base.OnDestroy();
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
            // Giữ tên tùy chỉnh khi scan lại bằng cách merge theo path ổn định.
            var previousNames = (group.Items ?? Array.Empty<OutfitItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(set => set.Key, set => set.First().DisplayName, StringComparer.OrdinalIgnoreCase);

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
                        sd.DisplayName = previousNames.TryGetValue(path, out var previousName) && !string.IsNullOrWhiteSpace(previousName)
                            ? previousName : child.name;
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
                foreach (var configuredPath in group.ManualPaths ?? Array.Empty<string>()) {
                    if (string.IsNullOrWhiteSpace(configuredPath)) continue;
                    var target = FindByPath(root, configuredPath);
                    if (target == null) {
                        Debug.LogWarning($"[OutfitSwitcher] Manual path not found or ambiguous: {configuredPath}");
                        continue;
                    }
                    var path = GetRelativePath(root, target);
                    if (!seenPaths.Add(path)) {
                        Debug.LogWarning($"[OutfitSwitcher] Manual path trùng lặp: '{path}'. Bỏ qua.");
                        continue;
                    }
                    var item = StructuredData.Create<OutfitItem>(sd => {
                        sd.DisplayName = previousNames.TryGetValue(path, out var previousName) && !string.IsNullOrWhiteSpace(previousName)
                            ? previousName : target.name;
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

            // Rebuild runtime links cho items
            RebuildItemTriggers(group);

            var duplicateNames = items.GroupBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Where(set => set.Count() > 1).Select(set => set.Key).ToArray();
            var suffix = duplicateNames.Length == 0
                ? ""
                : $" Warning: duplicate names: {string.Join(", ", duplicateNames)}; use PATH in automation.";
            UpdateStatus($"Scanned **{group.GroupName}**: **{items.Count}** items.{suffix}");
        }

        // ═══════════════════════════════════════════════════════
        // DYNAMIC TRIGGERS — mỗi item = 1 trigger button
        // ═══════════════════════════════════════════════════════

        private void RebuildItemTriggers(OutfitGroup group) {
            LinkRuntimeData();
            Broadcast();
        }

        [Trigger]
        [Label("SCAN ALL GROUPS")]
        public void ScanAllGroups() {
            if (Character?.GameObject == null) {
                ReportError("Character chưa được gán hoặc chưa load.");
                return;
            }
            foreach (var group in Groups ?? Array.Empty<OutfitGroup>()) {
                if (group != null) ScanGroup(group);
            }
            ValidateConfiguration();
        }

        /// <summary>
        /// Danh sách folder thực tế trên character cho autocomplete FOLDER PATH.
        /// Chỉ lấy Transform có child vì mỗi child trực tiếp sẽ trở thành item.
        /// </summary>
        public string[] GetFolderPaths() {
            var root = Character?.GameObject?.transform;
            if (root == null) return Array.Empty<string>();
            return EnumerateDescendants(root)
                .Where(transform => transform.childCount > 0)
                .Select(transform => GetRelativePath(root, transform))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        [Trigger]
        [Label("VALIDATE CONFIGURATION")]
        public void ValidateConfiguration() {
            var errors = new List<string>();
            var warnings = new List<string>();
            if (Character?.GameObject == null) errors.Add("Character chưa được gán hoặc chưa load");
            if (Groups == null || Groups.Length == 0) errors.Add("Chưa có group nào");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups ?? Array.Empty<OutfitGroup>()) {
                if (group == null) continue;
                if (string.IsNullOrWhiteSpace(group.GroupName)) errors.Add("Có group chưa đặt tên");
                else if (!names.Add(group.GroupName)) errors.Add($"Trùng group name: {group.GroupName}");
                if (group.Items == null || group.Items.Length == 0) warnings.Add($"{group.GroupName}: chưa scan hoặc không có item");
                foreach (var item in group.Items ?? Array.Empty<OutfitItem>()) {
                    if (item == null) continue;
                    if (string.IsNullOrWhiteSpace(item.Path)) {
                        errors.Add($"{group.GroupName}: có item chưa có path");
                        continue;
                    }
                    if (paths.TryGetValue(item.Path, out var owner))
                        errors.Add($"Path '{item.Path}' nằm trong cả '{owner}' và '{group.GroupName}'");
                    else paths[item.Path] = group.GroupName;
                    if (Character?.GameObject != null && FindByPath(Character.GameObject.transform, item.Path) == null)
                        errors.Add($"Không resolve được path: {item.Path}");
                }
            }

            ValidateChildVisibilityRules(errors, warnings);
            ValidatePresets(errors, warnings);

            var message = errors.Count == 0
                ? $"Configuration valid. {warnings.Count} warning(s)."
                : $"Configuration has {errors.Count} error(s).";
            if (errors.Count > 0) message += "\n\n- " + string.Join("\n- ", errors);
            if (warnings.Count > 0) message += "\n\nWarnings:\n- " + string.Join("\n- ", warnings);
            UpdateStatus(message);
        }

        private void ValidateChildVisibilityRules(List<string> errors, List<string> warnings) {
            var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = Character?.GameObject?.transform;
            foreach (var rule in ChildVisibilityRules ?? Array.Empty<OutfitChildVisibilityRule>()) {
                if (rule == null) continue;
                if (string.IsNullOrWhiteSpace(rule.RuleName)) errors.Add("Có child visibility rule chưa đặt tên");
                else if (!ruleNames.Add(rule.RuleName)) errors.Add($"Trùng child visibility rule: {rule.RuleName}");

                var childNames = new HashSet<string>((rule.ChildNames ?? Array.Empty<string>())
                    .Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
                if (childNames.Count == 0) {
                    warnings.Add($"Rule '{rule.RuleName}': chưa có child name");
                    continue;
                }
                if (root != null && !EnumerateDescendants(root).Any(child => childNames.Contains(child.name))) {
                    warnings.Add($"Rule '{rule.RuleName}': không tìm thấy child name nào trên character");
                }
            }
        }

        private void ValidatePresets(List<string> errors, List<string> warnings) {
            var presetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in Presets ?? Array.Empty<OutfitPreset>()) {
                if (preset == null) continue;
                if (string.IsNullOrWhiteSpace(preset.PresetName)) errors.Add("Có preset chưa đặt tên");
                else if (!presetNames.Add(preset.PresetName)) errors.Add($"Trùng preset name: {preset.PresetName}");
                if (preset.Entries == null || preset.Entries.Length == 0) {
                    warnings.Add($"Preset '{preset.PresetName}': chưa có entry");
                    continue;
                }
                foreach (var entry in preset.Entries) {
                    if (entry == null) continue;
                    var groupIndex = FindGroupIndex(entry.GroupName);
                    if (groupIndex < 0) {
                        errors.Add($"Preset '{preset.PresetName}': không tìm thấy group '{entry.GroupName}'");
                        continue;
                    }
                    var group = Groups[groupIndex];
                    if (!TryFindItem(group, entry.ItemNameOrPath, out _, out var itemError)) {
                        errors.Add($"Preset '{preset.PresetName}': {itemError}");
                        continue;
                    }
                    if (entry.Action == OutfitPresetAction.Toggle && group.GroupType != OutfitGroupType.Toggle) {
                        errors.Add($"Preset '{preset.PresetName}': Toggle chỉ dùng cho Toggle group '{group.GroupName}'");
                    }
                    if (entry.Action == OutfitPresetAction.Disable &&
                        group.GroupType == OutfitGroupType.Single && !group.AllowNone) {
                        errors.Add($"Preset '{preset.PresetName}': group '{group.GroupName}' cần ALLOW NONE để Disable");
                    }
                }
            }
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
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) {
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
            if (!TryWearItem(groupName, itemName, out var error)) {
                ReportError(error);
            }
        }

        /// <summary>API có kết quả rõ ràng cho Blueprint/automation.</summary>
        public bool TryWearItem(string groupName, string itemName, out string error) {
            error = "";
            if (Character?.GameObject == null) {
                error = "Character chưa được gán hoặc chưa load.";
                return false;
            }
            var idx = FindGroupIndex(groupName);
            if (idx < 0) {
                error = $"Không tìm thấy group '{groupName}'.";
                return false;
            }
            var group = Groups[idx];
            if (!TryFindItem(group, itemName, out var item, out error)) return false;
            WearResolvedItem(group, item);
            return true;
        }

        public void SetItemActiveByPath(int groupIndex, string path, bool active) {
            if (!TrySetItemActiveByPath(groupIndex, path, active, out var error)) {
                ReportError(error);
            }
        }

        public bool TrySetItemActive(string groupName, string itemNameOrPath, bool active, out string error) {
            error = "";
            var groupIndex = FindGroupIndex(groupName);
            if (groupIndex < 0) {
                error = $"Không tìm thấy group '{groupName}'.";
                return false;
            }
            if (!TryFindItem(Groups[groupIndex], itemNameOrPath, out var item, out error)) return false;
            return TrySetItemActiveByPath(groupIndex, item.Path, active, out error);
        }

        private bool TrySetItemActiveByPath(int groupIndex, string path, bool active, out string error) {
            error = "";
            if (Character?.GameObject == null) {
                error = "Character chưa được gán hoặc chưa load.";
                return false;
            }
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) {
                error = $"Group index không hợp lệ: {groupIndex}.";
                return false;
            }
            var group = Groups[groupIndex];
            var item = group?.Items?.FirstOrDefault(candidate => candidate != null &&
                string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item == null) {
                error = $"Không tìm thấy item path '{path}'.";
                return false;
            }
            if (active && group.GroupType == OutfitGroupType.Single) {
                SwitchSingleItem(group, item);
                return true;
            }
            var target = FindByPath(Character.GameObject.transform, item.Path);
            if (target == null) {
                error = $"Không resolve được path '{item.Path}'.";
                return false;
            }
            if (!active && group.GroupType == OutfitGroupType.Single && !group.AllowNone) {
                error = $"Group '{group.GroupName}' chưa bật ALLOW NONE.";
                return false;
            }
            target.gameObject.SetActive(active);
            ApplyChildVisibilityRules();
            UpdateItemStates(group);
            SaveGroupActiveState(group);
            UpdateStatus($"{(active ? "Enabled" : "Disabled")} **{group.GroupName} / {item.DisplayName}**");
            return true;
        }

        public void DisableAllItems(int groupIndex) {
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group == null) return;
            if (group.GroupType == OutfitGroupType.Single && !group.AllowNone) {
                ReportError($"Group '{group.GroupName}' chưa bật ALLOW NONE.");
                return;
            }
            var root = Character?.GameObject?.transform;
            if (root == null) {
                ReportError("Character chưa được gán hoặc chưa load.");
                return;
            }
            foreach (var item in group.Items ?? Array.Empty<OutfitItem>()) {
                var target = item == null ? null : FindByPath(root, item.Path);
                if (target != null) target.gameObject.SetActive(false);
            }
            UpdateItemStates(group);
            SaveGroupActiveState(group);
            UpdateStatus($"Disabled all items in **{group.GroupName}**");
        }

        public bool DisableAllItems(string groupName, out string error) {
            error = "";
            if (Character?.GameObject == null) {
                error = "Character chưa được gán hoặc chưa load.";
                return false;
            }
            var groupIndex = FindGroupIndex(groupName);
            if (groupIndex < 0) {
                error = $"Không tìm thấy group '{groupName}'.";
                return false;
            }
            var group = Groups[groupIndex];
            if (group.GroupType == OutfitGroupType.Single && !group.AllowNone) {
                error = $"Group '{groupName}' chưa bật ALLOW NONE.";
                return false;
            }
            DisableAllItems(groupIndex);
            return true;
        }

        /// <summary>Chuyển sang item tiếp theo trong group (vòng tròn)</summary>
        public void SwitchNextItem(int groupIndex) {
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || group.Items.Length == 0) return;

            var currentIndex = GetActiveItemIndex(group);
            var nextIndex = (currentIndex + 1) % group.Items.Length;
            WearByIndex(groupIndex, nextIndex);
        }

        public void SwitchNextItem(string groupName) {
            if (!TrySwitchNextItem(groupName, out var error)) ReportError(error);
        }

        public bool TrySwitchNextItem(string groupName, out string error) {
            return TryNavigateGroup(groupName, "next", out error);
        }

        /// <summary>Chuyển sang item trước đó trong group (vòng tròn)</summary>
        public void SwitchPreviousItem(int groupIndex) {
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) return;
            var group = Groups[groupIndex];
            if (group?.Items == null || group.Items.Length == 0) return;

            var currentIndex = GetActiveItemIndex(group);
            var prevIndex = currentIndex <= 0 ? group.Items.Length - 1 : currentIndex - 1;
            WearByIndex(groupIndex, prevIndex);
        }

        public void SwitchPreviousItem(string groupName) {
            if (!TrySwitchPreviousItem(groupName, out var error)) ReportError(error);
        }

        public bool TrySwitchPreviousItem(string groupName, out string error) {
            return TryNavigateGroup(groupName, "previous", out error);
        }

        /// <summary>Chuyển sang item ngẫu nhiên khác trong group</summary>
        public void SwitchRandomItem(int groupIndex) {
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) return;
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
            if (!TrySwitchRandomItem(groupName, out var error)) ReportError(error);
        }

        public bool TrySwitchRandomItem(string groupName, out string error) {
            return TryNavigateGroup(groupName, "random", out error);
        }

        private bool TryNavigateGroup(string groupName, string direction, out string error) {
            error = "";
            var idx = FindGroupIndex(groupName);
            if (idx < 0) {
                error = $"Không tìm thấy group '{groupName}'.";
                return false;
            }
            var group = Groups[idx];
            if (group?.Items == null || group.Items.Length == 0) {
                error = $"Group '{groupName}' chưa có item; hãy scan trước.";
                return false;
            }
            if (group.GroupType == OutfitGroupType.Toggle) {
                error = $"Action {direction} không dành cho Toggle group '{groupName}'. Hãy dùng Enable/Disable/Toggle item.";
                return false;
            }
            switch (direction) {
                case "previous": SwitchPreviousItem(idx); break;
                case "random": SwitchRandomItem(idx); break;
                default: SwitchNextItem(idx); break;
            }
            return true;
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
                        ApplyChildVisibilityRules();
                        UpdateItemStates(group);
                        SaveGroupActiveState(group);
                    },
                    glowKey: "item:" + item.Path,
                    flushOnCancel: true,
                    debugLog: DebugLogs,
                    ignoreInactiveRenderers: false,
                    excludedPaths: group.GlowExcludedPaths
                ).Forget();
            } else {
                target.gameObject.SetActive(!target.gameObject.activeSelf);
                ApplyChildVisibilityRules();
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
                ApplyChildVisibilityRules();
                UpdateItemStates(group);
                SaveGroupActiveState(group);
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
                        ApplyChildVisibilityRules();
                        UpdateItemStates(group);
                        SaveGroupActiveState(group);
                    },
                    swapPaths: newPaths,
                    glowKey: "group:" + group.GroupName,
                    debugLog: DebugLogs,
                    ignoreInactiveRenderers: group.IgnoreInactiveChildren,
                    excludedPaths: group.GlowExcludedPaths
                ).Forget();

            } else if (group.Transition == OutfitTransition.Glow && activeOldItems.Count == 0) {
                // Chưa có outfit nào trong group active -> tắt các đồ khác (đảm bảo sạch), bật target và glow target
                foreach (var go in allOtherGos) {
                    if (go != null) go.SetActive(false);
                }
                targetGo.gameObject.SetActive(true);
                ApplyChildVisibilityRules();
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
                    debugLog: DebugLogs,
                    ignoreInactiveRenderers: group.IgnoreInactiveChildren,
                    excludedPaths: group.GlowExcludedPaths
                ).Forget();

            } else {
                // Instant: tắt toàn bộ đồ khác trong group, bật target ngay lập tức
                foreach (var go in allOtherGos) {
                    if (go != null) go.SetActive(false);
                }
                targetGo.gameObject.SetActive(true);
                ApplyChildVisibilityRules();
                UpdateItemStates(group);
                SaveGroupActiveState(group);
            }

            UpdateStatus($"👗 Switched **{group.GroupName}** → **{targetItem.DisplayName}**");

            if (DebugLogs)
                Debug.Log($"[OutfitSwitcher] Switched to '{targetItem.DisplayName}' in group '{group.GroupName}' (deactivated {allOtherGos.Count} other items)");
        }

        // ═══════════════════════════════════════════════════════
        // CHILD VISIBILITY (EARS / TAIL) & PRESETS
        // ═══════════════════════════════════════════════════════

        public void ToggleChildVisibilityRule(int ruleIndex) {
            if (ChildVisibilityRules == null || ruleIndex < 0 || ruleIndex >= ChildVisibilityRules.Length) return;
            var rule = ChildVisibilityRules[ruleIndex];
            if (rule != null) SetChildVisibilityRule(ruleIndex, !rule.Visible);
        }

        public void SetChildVisibilityRule(int ruleIndex, bool visible) {
            if (ChildVisibilityRules == null || ruleIndex < 0 || ruleIndex >= ChildVisibilityRules.Length) return;
            var rule = ChildVisibilityRules[ruleIndex];
            if (rule == null) return;
            rule.SetDataInput(nameof(OutfitChildVisibilityRule.Visible), visible, broadcast: true);
            ApplyChildVisibilityRule(rule);
            UpdateStatus($"**{rule.RuleName}**: {(visible ? "shown" : "hidden")}");
        }

        private void ApplyChildVisibilityRules() {
            foreach (var rule in ChildVisibilityRules ?? Array.Empty<OutfitChildVisibilityRule>()) {
                if (rule != null) ApplyChildVisibilityRule(rule);
            }
        }

        private void ApplyChildVisibilityRule(OutfitChildVisibilityRule rule) {
            var root = Character?.GameObject?.transform;
            if (root == null || rule?.ChildNames == null) return;
            var names = new HashSet<string>(rule.ChildNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) return;
            foreach (var child in EnumerateDescendants(root)) {
                if (names.Contains(child.name)) child.gameObject.SetActive(rule.Visible);
            }
        }

        public void ApplyPreset(int presetIndex) {
            if (!TryApplyPreset(presetIndex, out var error)) ReportError(error);
        }

        public bool TryApplyPreset(string presetName, out string error) {
            error = "";
            if (Presets == null) {
                error = "Chưa có preset nào.";
                return false;
            }
            for (var i = 0; i < Presets.Length; i++) {
                if (Presets[i] != null && string.Equals(Presets[i].PresetName, presetName, StringComparison.OrdinalIgnoreCase)) {
                    return TryApplyPreset(i, out error);
                }
            }
            error = $"Không tìm thấy preset '{presetName}'.";
            return false;
        }

        private bool TryApplyPreset(int presetIndex, out string error) {
            error = "";
            if (Presets == null || presetIndex < 0 || presetIndex >= Presets.Length) {
                error = "Preset index không hợp lệ.";
                return false;
            }
            var preset = Presets[presetIndex];
            if (preset == null) {
                error = "Preset không hợp lệ.";
                return false;
            }
            var errors = new List<string>();
            foreach (var entry in preset.Entries ?? Array.Empty<OutfitPresetEntry>()) {
                if (entry == null) continue;
                string entryError;
                bool success;
                switch (entry.Action) {
                    case OutfitPresetAction.Disable:
                        success = TrySetItemActive(entry.GroupName, entry.ItemNameOrPath, false, out entryError);
                        break;
                    case OutfitPresetAction.Toggle:
                        var groupIndex = FindGroupIndex(entry.GroupName);
                        if (groupIndex < 0) {
                            success = false;
                            entryError = $"Không tìm thấy group '{entry.GroupName}'.";
                        } else if (Groups[groupIndex].GroupType != OutfitGroupType.Toggle) {
                            success = false;
                            entryError = $"Toggle chỉ dùng cho Toggle group '{entry.GroupName}'.";
                        } else {
                            success = TryWearItem(entry.GroupName, entry.ItemNameOrPath, out entryError);
                        }
                        break;
                    default:
                        success = TrySetItemActive(entry.GroupName, entry.ItemNameOrPath, true, out entryError);
                        break;
                }
                if (!success) errors.Add(entryError);
            }
            if (errors.Count == 0) {
                UpdateStatus($"Applied preset **{preset.PresetName}**");
                return true;
            }
            error = $"Preset '{preset.PresetName}' partially failed: {string.Join("; ", errors)}";
            return false;
        }

        public string[] GetGroupNames() {
            return (Groups ?? Array.Empty<OutfitGroup>())
                .Where(group => group != null && !string.IsNullOrWhiteSpace(group.GroupName))
                .Select(group => group.GroupName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public OutfitItem[] GetItems(string groupName) {
            var index = FindGroupIndex(groupName);
            return index < 0 ? Array.Empty<OutfitItem>() : Groups[index].Items ?? Array.Empty<OutfitItem>();
        }

        public string[] GetPresetNames() {
            return (Presets ?? Array.Empty<OutfitPreset>())
                .Where(preset => preset != null && !string.IsNullOrWhiteSpace(preset.PresetName))
                .Select(preset => preset.PresetName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static IEnumerable<Transform> EnumerateDescendants(Transform root) {
            if (root == null) yield break;
            foreach (Transform child in root) {
                yield return child;
                foreach (var descendant in EnumerateDescendants(child)) yield return descendant;
            }
        }

        // ═══════════════════════════════════════════════════════
        // STATE PERSISTENCE & RESTORE
        // ═══════════════════════════════════════════════════════

        private void SaveGroupActiveState(OutfitGroup group) {
            if (group?.Items == null) return;
            var root = Character?.GameObject?.transform;
            if (root == null) return;

            var activeNames = new List<string>();
            var activePaths = new List<string>();
            foreach (var item in group.Items) {
                if (item == null) continue;
                var go = FindByPath(root, item.Path);
                if (go != null && go.gameObject.activeSelf) {
                    activeNames.Add(item.DisplayName);
                    activePaths.Add(item.Path);
                }
            }

            if (group.GroupType == OutfitGroupType.Single) {
                group.LastActiveItem = activeNames.Count == 1 ? activeNames[0] : "";
                group.LastActivePath = activePaths.Count == 1 ? activePaths[0] : "";
            }
            group.LastActiveItems = activeNames.ToArray();
            group.LastActivePaths = activePaths.ToArray();
            group.SetDataInput(nameof(OutfitGroup.LastActiveItem), group.LastActiveItem, broadcast: false);
            group.SetDataInput(nameof(OutfitGroup.LastActivePath), group.LastActivePath, broadcast: false);
            group.SetDataInput(nameof(OutfitGroup.LastActiveItems), group.LastActiveItems, broadcast: false);
            group.SetDataInput(nameof(OutfitGroup.LastActivePaths), group.LastActivePaths, broadcast: false);
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
                var savedPath = group.LastActivePath;
                if (string.IsNullOrEmpty(savedPath) && !string.IsNullOrEmpty(group.LastActiveItem)) {
                    savedPath = group.Items.FirstOrDefault(item => item != null &&
                        string.Equals(item.DisplayName, group.LastActiveItem, StringComparison.OrdinalIgnoreCase))?.Path;
                }
                if (string.IsNullOrEmpty(savedPath)) return;
                foreach (var item in group.Items) {
                    if (item == null) continue;
                    var go = FindByPath(root, item.Path);
                    if (go == null) continue;

                    bool shouldBeActive = string.Equals(item.Path, savedPath, StringComparison.OrdinalIgnoreCase);
                    go.gameObject.SetActive(shouldBeActive);
                    item.IsActive = shouldBeActive;
                }
            } else {
                // Restore cho Toggle group (multi-wear), ưu tiên path ổn định.
                var savedPaths = group.LastActivePaths;
                var activeSet = new HashSet<string>(savedPaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                if (activeSet.Count == 0 && group.LastActiveItems != null) {
                    foreach (var item in group.Items) {
                        if (item != null && group.LastActiveItems.Any(name =>
                            string.Equals(name, item.DisplayName, StringComparison.OrdinalIgnoreCase))) {
                            activeSet.Add(item.Path);
                        }
                    }
                }
                foreach (var item in group.Items) {
                    if (item == null) continue;
                    var go = FindByPath(root, item.Path);
                    if (go == null) continue;

                    bool shouldBeActive = activeSet.Contains(item.Path);
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
            return TryFindItem(group, nameOrPath, out var item, out _) ? item : null;
        }

        private static bool TryFindItem(OutfitGroup group, string nameOrPath,
                out OutfitItem item, out string error) {
            item = null;
            error = "";
            if (group?.Items == null || group.Items.Length == 0) {
                error = $"Group '{group?.GroupName ?? ""}' chưa có item; hãy scan trước.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(nameOrPath)) {
                error = "Item name/path đang trống.";
                return false;
            }

            // Path là định danh ổn định và luôn được ưu tiên trước display name.
            item = group.Items.FirstOrDefault(candidate => candidate != null &&
                string.Equals(candidate.Path, nameOrPath, StringComparison.OrdinalIgnoreCase));
            if (item != null) return true;

            var nameMatches = group.Items.Where(candidate => candidate != null &&
                string.Equals(candidate.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nameMatches.Length == 1) {
                item = nameMatches[0];
                return true;
            }
            if (nameMatches.Length > 1) {
                error = $"Item name '{nameOrPath}' bị trùng trong group '{group.GroupName}'. Hãy dùng full path.";
                return false;
            }
            error = $"Không tìm thấy item '{nameOrPath}' trong group '{group.GroupName}'.";
            return false;
        }

        public void PreviewItemByPath(int groupIndex, string path) {
            if (Groups == null || groupIndex < 0 || groupIndex >= Groups.Length) {
                ReportError("Group index không hợp lệ.");
                return;
            }
            var root = Character?.GameObject?.transform;
            var target = root == null ? null : FindByPath(root, path);
            if (target == null) {
                ReportError($"Không resolve được path '{path}'.");
                return;
            }
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var readableMeshes = 0;
            var meshCount = 0;
            var vertices = 0;
            foreach (var renderer in renderers) {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (renderer is MeshRenderer) mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                meshCount++;
                vertices += mesh.vertexCount;
                if (mesh.isReadable) readableMeshes++;
            }
            UpdateStatus($"Preview **{target.name}**\n\nPath: `{GetRelativePath(root, target)}`\n\n" +
                         $"Renderers: **{renderers.Length}**, meshes: **{meshCount}**, vertices: **{vertices}**, readable meshes: **{readableMeshes}**. " +
                         (meshCount > 0 && readableMeshes == meshCount ? "Full sweep available." : "Some meshes will use uniform glow fallback or cannot be overlaid."));
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

        private void ReportError(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;
            UpdateStatus($"Error: **{message}**");
            Debug.LogWarning("[OutfitSwitcher] " + message);
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
