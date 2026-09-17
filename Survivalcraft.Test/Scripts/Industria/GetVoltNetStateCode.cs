var project = Game.GameManager.Project;
if (project == null) return "no-project";
var voltNet = project.FindSubsystem<SCIENEW.VoltNet.SubsystemVoltNet>();
if (voltNet == null) return "no-voltnet";
var elements = voltNet.m_elements
    .Select(e => new { P = e.Key, T = e.Value.GetType().Name, W = e.Value.CurrentWorkState })
    .OrderBy(e => (long)e.P.X + (long)e.P.Y * 100000 + (long)e.P.Z * 1000000000)
    .Select(e => string.Format("{0},{1},{2},{3},{4}", e.P.X, e.P.Y, e.P.Z, e.T, e.W))
    .ToList();
return string.Format("elements={0}|connections={1}|wires={2};{3}",
    voltNet.m_elements.Count,
    voltNet.m_connections.Count,
    voltNet.m_wireConnections.Count,
    string.Join(";", elements));