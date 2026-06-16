using System;
using System.Xml.Linq;
using Engine;

namespace Game; 
public class NetworkModEntity : ModEntity {
    public NetworkModEntity() {
        modInfo = new ModInfo {
            Name = "Network",
            PackageName = "network",
            Version = "1.0",
            Description = "Survivalcraft Network Subsystem",
            LoadOrder = (int)LoadOrder.Survivalcraft + 2
        };
    }

    public override void LoadXdb(ref XElement xElement) {
        base.LoadXdb(ref xElement);

        if (ModsManager.FindElementByGuid(xElement, "85023bf8-1c90-4dd1-9442-e6c13691d078", out XElement projectTemplate)) {
            if (ModsManager.FindElement(projectTemplate, e => e.Name.LocalName == "MemberSubsystemTemplate" && e.Attribute("Name")?.Value == "Network", out _)) {
                return;
            }

            string networkTemplateGuid = Guid.NewGuid().ToString();

            projectTemplate.Add(new XElement("MemberSubsystemTemplate",
                new XAttribute("Name", "Network"),
                new XAttribute("Guid", Guid.NewGuid().ToString()),
                new XAttribute("InheritanceParent", networkTemplateGuid)));

            XElement subsystemTemplate = new XElement("SubsystemTemplate",
                new XAttribute("Name", "Network"),
                new XAttribute("Guid", networkTemplateGuid),
                new XAttribute("InheritanceParent", "a3914c13-7801-46a6-b23a-9e2b36e9defb"),
                new XElement("Parameter",
                    new XAttribute("Name", "Class"),
                    new XAttribute("Guid", Guid.NewGuid().ToString()),
                    new XAttribute("Value", "Game.Network.SubsystemNetwork"),
                    new XAttribute("Type", "string")));

            projectTemplate.Parent?.Add(subsystemTemplate);
        }
    }
}
