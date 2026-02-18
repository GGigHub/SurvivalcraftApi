using System;
using Engine;

namespace Game.NetWork.Packages
{
    /// <summary>
    /// 基础包模板复制
    /// </summary>
    public class ProjectPackage : IPackage
    {
        public byte ID => (byte)PackageType.Project;
        public Client To { get; set; }
        public Client Except { get; set; }
        public Client From { get; set; }

        public bool hasTexture;
        public byte[] m_textureData;
        public byte[] m_projectData;

        public ClientState MinNeedState => ClientState.NotConnected;

        public ProjectPackage() { }

        public ProjectPackage(byte[] textureData, byte[] projectData)
        {
            m_textureData = textureData;
            hasTexture = m_textureData != null;
            m_projectData = projectData;
        }

        public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
        {
            if (isServer)
            {
                return;
            }
            var s = ScreensManager.FindScreen<GameLoadingScreen>("GameLoading");
            s.ReplyCall(hasTexture, m_textureData, m_projectData);
        }

        public void ReadData(PackageStreamReader reader)
        {
            if (reader.ReadBoolean())
            {
                m_textureData = reader.ReadBytes(reader.ReadInt32());
            }
            m_projectData = reader.ReadBytes(reader.ReadInt32());
        }

        public void WriteData(PackageStreamWriter writer)
        {
            writer.Write(hasTexture);
            if (hasTexture)
            {
                writer.Write(m_textureData.Length);
                writer.Write(m_textureData);
            }
            writer.Write(m_projectData.Length);
            writer.Write(m_projectData);
        }
    }
}
