using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Graphs;
using Warudo.Plugins.McpBridge;

namespace Warudo.Plugins.McpBridge.Nodes {

    public enum OutfitSwitchAction {
        [Label("Wear / Toggle Item")]
        SwitchToItem,

        [Label("Enable Item")]
        EnableItem,

        [Label("Disable Item")]
        DisableItem,

        [Label("Disable All In Group")]
        DisableAll,

        [Label("Next Item")]
        NextItem,

        [Label("Previous Item")]
        PreviousItem,

        [Label("Random Item")]
        RandomItem,

        [Label("Apply Preset")]
        ApplyPreset
    }

    /// <summary>
    /// Blueprint adapter for Outfit Switcher. Group, item and preset fields are
    /// generated from the selected asset to prevent spelling/path mistakes.
    /// Exit remains for backward compatibility; Success/Failed are additional
    /// branch outputs and LastError explains failures.
    /// </summary>
    [NodeType(
        Id = "b8c4d2e5-f6a7-4b9c-0d1e-2f3a4b5c6d7e",
        Title = "Outfit Switcher",
        Category = "Hasukatsu"
    )]
    public class OutfitSwitchNode : Node {

        [DataInput]
        [Label("SWITCHER")]
        [Description("Asset điều khiển")]
        public OutfitSwitcherAsset Switcher;

        [DataInput]
        [Label("ACTION")]
        [Description("Hành động chuyển đổi")]
        public OutfitSwitchAction Action = OutfitSwitchAction.SwitchToItem;

        [DataInput]
        [Label("GROUP")]
        [AutoComplete(nameof(AutoCompleteGroupName), forceSelection: true)]
        [HiddenIf(nameof(IsPresetAction))]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("ITEM")]
        [Description("Tên hoặc path của item")]
        [AutoComplete(nameof(AutoCompleteItem), forceSelection: true)]
        [HiddenIf(nameof(ShouldHideItem))]
        public string ItemName = "";

        [DataInput]
        [Label("PRESET")]
        [AutoComplete(nameof(AutoCompletePreset), forceSelection: true)]
        [HiddenIf(nameof(IsNotPresetAction))]
        public string PresetName = "";

        [DataInput]
        [Hidden]
        public string LastError = "";

        protected bool IsPresetAction() => Action == OutfitSwitchAction.ApplyPreset;
        protected bool IsNotPresetAction() => Action != OutfitSwitchAction.ApplyPreset;
        protected bool ShouldHideItem() => Action != OutfitSwitchAction.SwitchToItem &&
                                           Action != OutfitSwitchAction.EnableItem &&
                                           Action != OutfitSwitchAction.DisableItem;

        protected UniTask<AutoCompleteList> AutoCompleteGroupName() {
            var entries = (Switcher?.GetGroupNames() ?? Array.Empty<string>())
                .Select(name => new AutoCompleteEntry { label = name, value = name }).ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        protected UniTask<AutoCompleteList> AutoCompleteItem() {
            var entries = (Switcher?.GetItems(GroupName) ?? Array.Empty<OutfitItem>())
                .Where(item => item != null)
                .Select(item => new AutoCompleteEntry {
                    label = string.IsNullOrWhiteSpace(item.DisplayName)
                        ? item.Path
                        : $"{item.DisplayName} — {item.Path}",
                    value = item.Path
                }).ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        protected UniTask<AutoCompleteList> AutoCompletePreset() {
            var entries = (Switcher?.GetPresetNames() ?? Array.Empty<string>())
                .Select(name => new AutoCompleteEntry { label = name, value = name }).ToList();
            return UniTask.FromResult(AutoCompleteList.Single(entries));
        }

        [DataOutput]
        [Label("LAST ERROR")]
        public string GetLastError() => LastError;

        [DataOutput]
        [Label("SUCCEEDED")]
        public bool Succeeded() => string.IsNullOrEmpty(LastError);

        [FlowInput]
        public Continuation Enter() {
            if (Switcher == null) return Complete(false, "Switcher asset chưa được gán.");

            try {
                bool success;
                string error;
                switch (Action) {
                    case OutfitSwitchAction.SwitchToItem:
                        success = Switcher.TryWearItem(GroupName, ItemName, out error);
                        break;
                    case OutfitSwitchAction.EnableItem:
                        success = Switcher.TrySetItemActive(GroupName, ItemName, true, out error);
                        break;
                    case OutfitSwitchAction.DisableItem:
                        success = Switcher.TrySetItemActive(GroupName, ItemName, false, out error);
                        break;
                    case OutfitSwitchAction.DisableAll:
                        success = Switcher.DisableAllItems(GroupName, out error);
                        break;
                    case OutfitSwitchAction.NextItem:
                        success = Switcher.TrySwitchNextItem(GroupName, out error);
                        break;
                    case OutfitSwitchAction.PreviousItem:
                        success = Switcher.TrySwitchPreviousItem(GroupName, out error);
                        break;
                    case OutfitSwitchAction.RandomItem:
                        success = Switcher.TrySwitchRandomItem(GroupName, out error);
                        break;
                    case OutfitSwitchAction.ApplyPreset:
                        success = Switcher.TryApplyPreset(PresetName, out error);
                        break;
                    default:
                        success = false;
                        error = "Action không được hỗ trợ.";
                        break;
                }
                return Complete(success, error);
            } catch (Exception e) {
                return Complete(false, "Switch thất bại: " + e.Message);
            }
        }

        private Continuation Complete(bool success, string error) {
            SetDataInput(nameof(LastError), success ? "" : error, broadcast: true);
            if (success) InvokeFlow(nameof(Success));
            else {
                Debug.LogWarning("[OutfitSwitch] " + error);
                InvokeFlow(nameof(Failed));
            }
            return Exit;
        }

        [FlowOutput]
        [Label("EXIT (COMPATIBILITY)")]
        public Continuation Exit;

        [FlowOutput]
        public Continuation Success;

        [FlowOutput]
        public Continuation Failed;
    }
}
