namespace Game.NetWork.Packages {
    public class ComponentSicknessPackage : IPackage {
        public byte ID => (byte)PackageType.ComponentSickness;
        public Client To { get; set; }
        public Client Except { get; set; }
        public Client From { get; set; }
        public ClientStage MinNeedState => ClientStage.ProjectLoaded;
        public enum EventType {
            SyncStat,
            NauseaEffect
        }

        public float m_sicknessDuration;

        public bool m_isNausea;

        public EventType m_eventType;

        public ushort entityId;

        public ComponentSicknessPackage() {

        }
        public ComponentSicknessPackage(ComponentSickness sickness) {
            entityId = sickness.Entity.EntityId;
            m_sicknessDuration = sickness.m_sicknessDuration;
        }

        public void WriteData(PackageStreamWriter writer) {
            writer.Write(entityId);
            writer.Write(m_sicknessDuration);
        }
        public void ReadData(PackageStreamReader reader) {
            entityId = reader.ReadUInt16();
            m_sicknessDuration = reader.ReadSingle();
        }

        public void Handle(ProjectNet projectNet, bool isServer) {
            if (isServer) {
                return;
            }
            projectNet.FindEntityById(entityId, (entity) => {
                var sickness = entity.FindComponent<ComponentSickness>();
                if (sickness != null) {
                    sickness.m_sicknessDuration = m_sicknessDuration;
                    if (m_sicknessDuration > 0f) sickness.NauseaEffect();
                }
            });
        }
    }
}
