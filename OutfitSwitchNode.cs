using System;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using Warudo.Plugins.McpBridge;

namespace Warudo.Plugins.McpBridge.Nodes {

    public enum OutfitSwitchAction {
        [Label("Switch To Item")]
        SwitchToItem,

        [Label("Next Item")]
        NextItem,

        [Label("Previous Item")]
        PreviousItem,

        [Label("Random Item")]
        RandomItem
    }

    /// <summary>
    /// Blueprint node cho Outfit Switcher — cho phép trigger đổi outfit
    /// từ blueprint, Stream Deck, hoặc MCP command.
    ///
    /// Hỗ trợ: chọn item theo tên, chuyển sang item kế tiếp, item trước đó,
    /// hoặc chọn ngẫu nhiên.
    /// </summary>
    [NodeType(
        Id = "b8c4d2e5-f6a7-4b9c-0d1e-2f3a4b5c6d7e",
        Title = "Outfit Switcher",
        Category = "Hasukatsu"
    )]
    public class OutfitSwitchNode : Node {

        [DataInput]
        [Label("SWITCHER")]
        [Description("Outfit Switcher asset để điều khiển")]
        public OutfitSwitcherAsset Switcher;

        [DataInput]
        [Label("ACTION")]
        [Description("Hành động đổi outfit. Next / Previous / Random phù hợp nhất cho group dạng Switch (Single). Với group dạng Toggle, hành động sẽ bật/tắt item kế tiếp.")]
        public OutfitSwitchAction Action = OutfitSwitchAction.SwitchToItem;

        [DataInput]
        [Label("GROUP NAME")]
        [Description("Tên group (vd 'Outfit', 'Hair', 'Accessories')")]
        public string GroupName = "Outfit";

        [DataInput]
        [Label("ITEM NAME")]
        [Description("Tên item cần chuyển sang (DisplayName từ scan). Chỉ dùng khi Action là 'Switch To Item'")]
        [HiddenIf(nameof(IsNotSwitchToItem))]
        public string ItemName = "";

        protected bool IsNotSwitchToItem() => Action != OutfitSwitchAction.SwitchToItem;

        [FlowInput]
        public Continuation Enter() {
            if (Switcher == null) {
                Debug.LogWarning("[OutfitSwitch] Switcher asset chưa được gán!");
                return Exit;
            }

            try {
                switch (Action) {
                    case OutfitSwitchAction.SwitchToItem:
                        if (string.IsNullOrWhiteSpace(ItemName)) {
                            Debug.LogWarning("[OutfitSwitch] ItemName trống!");
                            return Exit;
                        }
                        Switcher.WearItem(GroupName, ItemName);
                        break;
                    case OutfitSwitchAction.NextItem:
                        Switcher.SwitchNextItem(GroupName);
                        break;
                    case OutfitSwitchAction.PreviousItem:
                        Switcher.SwitchPreviousItem(GroupName);
                        break;
                    case OutfitSwitchAction.RandomItem:
                        Switcher.SwitchRandomItem(GroupName);
                        break;
                }
            } catch (Exception e) {
                Debug.LogWarning($"[OutfitSwitch] Switch thất bại: {e.Message}");
            }

            return Exit;
        }

        [FlowOutput]
        public Continuation Exit;
    }
}

