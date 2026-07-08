var player = Game.GameManager.Project
                .FindSubsystem<Game.SubsystemPlayers>()
                .ComponentPlayers
                .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
            var widget = player?.ComponentGui?.ModalPanelWidget;
            if (widget is not Game.ChestWidget chestWidget) {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "opened=false;widget={0};inventoryId=0;slot0Value=0;slot0Count=0",
                    widget?.GetType().Name ?? "null");
            }

            var chest = chestWidget.m_componentChest;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "opened=true;widget={0};inventoryId={1};slot0Value={2};slot0Count={3}",
                widget.GetType().Name,
                chest.Id,
                chest.GetSlotValue(0),
                chest.GetSlotCount(0));