using Engine;

using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemFurnaceBlockBehavior : SubsystemEntityBlockBehavior, IUpdateable {
        public SubsystemParticles m_subsystemParticles;
        public SubsystemBlockEntities m_subsystemBlockEntities;
        public Dictionary<Point3, FireParticleSystem> m_particleSystemsByCell = [];
        public static event Action<SubsystemFurnaceBlockBehavior, List<ComponentFurnace>> OnFurnacesUpdate;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override int[] HandledBlocks => [64, 65];

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            if (Terrain.ExtractContents(oldValue) != 64
                && Terrain.ExtractContents(oldValue) != 65) {
                base.OnBlockAdded(value, oldValue, x, y, z);
            }
            if (Terrain.ExtractContents(value) == 65) {
                AddFire(value, x, y, z);
            }
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            if (Terrain.ExtractContents(newValue) != 64
                && Terrain.ExtractContents(newValue) != 65) {
                base.OnBlockRemoved(value, newValue, x, y, z);
            }
            if (Terrain.ExtractContents(value) == 65) {
                RemoveFire(x, y, z);
            }
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            if (Terrain.ExtractContents(value) == 65) {
                AddFire(value, x, y, z);
            }
        }

        public override void OnChunkDiscarding(TerrainChunk chunk) {
            List<Point3> list = new();
            foreach (Point3 key in m_particleSystemsByCell.Keys) {
                if (key.X >= chunk.Origin.X
                    && key.X < chunk.Origin.X + 16
                    && key.Z >= chunk.Origin.Y
                    && key.Z < chunk.Origin.Y + 16) {
                    list.Add(key);
                }
            }
            foreach (Point3 item in list) {
                RemoveFire(item.X, item.Y, item.Z);
            }
        }

        public override bool InteractBlockEntity(ComponentBlockEntity blockEntity, ComponentMiner componentMiner) {
            if (blockEntity != null
                && componentMiner.ComponentPlayer != null) {
                ComponentFurnace componentFurnace = blockEntity.Entity.FindComponent<ComponentFurnace>(true);
                componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new FurnaceWidget(componentMiner.Inventory, componentFurnace);
                AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                return true;
            }
            return false;
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            if (NetworkManager.IsClientRunning && IsMainPlayer?.Invoke(componentMiner.ComponentPlayer) == true) {
                InvokeOnOpenInventoryRequest(new Point3(raycastResult.CellFace.X, raycastResult.CellFace.Y, raycastResult.CellFace.Z));
                return true;
            }
            return base.OnInteract(raycastResult, componentMiner);
        }

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemParticles = Project.FindSubsystem<SubsystemParticles>(true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>();
            m_databaseObject = Project.GameDatabase.Database.FindDatabaseObject("Furnace", Project.GameDatabase.EntityTemplateType, true);
        }

        public void AddFire(int value, int x, int y, int z) {
            /*
            var v = new Vector3(0.5f, 0.2f, 0.5f);
            float size = 0.15f;
            var fireParticleSystem = new FireParticleSystem(new Vector3(x, y, z) + v, size, 16f);
            m_subsystemParticles.AddParticleSystem(fireParticleSystem);
            m_particleSystemsByCell[new Point3(x, y, z)] = fireParticleSystem;
        */
        }

        public void RemoveFire(int x, int y, int z) {
            /*
            var key = new Point3(x, y, z);
            FireParticleSystem particleSystem = m_particleSystemsByCell[key];
            m_subsystemParticles.RemoveParticleSystem(particleSystem);
            m_particleSystemsByCell.Remove(key);
            */
        }

        public void Update(float dt) {
            if (Time.PeriodicEvent(5.0, 0.0) && !NetworkManager.IsClientRunning) {
                List<ComponentFurnace> componentFurnaces = new List<ComponentFurnace>();
                SubsystemBlockEntities subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>();
                foreach (var pair in subsystemBlockEntities.m_blockEntities) {
                    var furnace = pair.Value.Entity.FindComponent<ComponentFurnace>();
                    if (furnace != null) {
                        componentFurnaces.Add(furnace);
                    }
                }
                if (componentFurnaces.Count > 0) {
                    OnFurnacesUpdate?.Invoke(this, componentFurnaces);
                }
            }
        }
    }
}


