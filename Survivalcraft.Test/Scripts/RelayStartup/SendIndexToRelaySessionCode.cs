var target = Game.NetworkManager.ServerSessions.FirstOrDefault(
    s => s.PlayerIndex == __PLAYER_INDEX__);
if (target == null) {
    return false;
}
Game.NetworkManager.Queue(new Game.Network.IndexPacket(__TEST_INDEX__) { To = target });
return true;
