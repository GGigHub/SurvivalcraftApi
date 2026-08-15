ScreensManager.SwitchScreen(
    "GameLoading",
    null,
    null,
    new Game.RelayJoinInfo(
        new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, __RELAY_PORT__),
        "__ROOM_CODE__"),
    null);
return true;
