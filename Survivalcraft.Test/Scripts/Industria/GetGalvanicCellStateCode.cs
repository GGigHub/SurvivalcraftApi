var project = Game.GameManager.Project;
if (project == null) return "no-project";
var voltNet = project.FindSubsystem<SCIENEW.VoltNet.SubsystemVoltNet>();
if (voltNet == null) return "no-voltnet";
var element = voltNet.GetVoltElement(new Engine.Point3(@@0@@, @@1@@, @@2@@)) as SCIENEW.VoltNet.GalvanicCellVoltElement;
if (element == null) return "no-galvanic-element";
return string.Format("energy={0:R};state={1}", element.m_energy, element.CurrentWorkState);