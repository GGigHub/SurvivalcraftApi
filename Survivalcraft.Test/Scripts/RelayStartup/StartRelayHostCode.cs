var relayEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, __RELAY_PORT__);
NetworkManager.StartRelayHost(relayEndPoint);
return true;
