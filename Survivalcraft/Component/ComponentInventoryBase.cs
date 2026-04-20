using System.Globalization;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;
using Game.NetWork;

namespace Game {
    public abstract class ComponentInventoryBase : Component, IInventory {
        public class Slot {
            public int Value;

            public int Count;
        }

        public List<Slot> m_slots = [];

        public Random m_random = new();

        Project IInventory.Project => Project;

        public int Id { get; private set; }

        public virtual int SlotsCount => m_slots.Count;

        public virtual int VisibleSlotsCount {
            get => SlotsCount;
            set { }
        }

        public virtual int ActiveSlotIndex {
            get => -1;
            set { }
        }

        public static int FindAcquireSlotForItem(IInventory inventory, int value) {
            for (int i = 0; i < inventory.SlotsCount; i++) {
                if (inventory.GetSlotCount(i) > 0
                    && inventory.GetSlotValue(i) == value
                    && inventory.GetSlotCount(i) < inventory.GetSlotCapacity(i, value)) {
                    return i;
                }
            }
            for (int j = 0; j < inventory.SlotsCount; j++) {
                if (inventory.GetSlotCount(j) == 0
                    && inventory.GetSlotCapacity(j, value) > 0) {
                    return j;
                }
            }
            return -1;
        }

        public static int AcquireItems(IInventory inventory, int value, int count) {
            while (count > 0) {
                int num = FindAcquireSlotForItem(inventory, value);
                if (num < 0) {
                    break;
                }
                inventory.AddSlotItems(num, value, 1);
                count--;
            }
            return count;
        }

        public ComponentPlayer FindInteractingPlayer() {
            ComponentPlayer componentPlayer = Entity.FindComponent<ComponentPlayer>();
            if (componentPlayer == null) {
                ComponentBlockEntity componentBlockEntity = Entity.FindComponent<ComponentBlockEntity>();
                if (componentBlockEntity != null) {
                    Vector3 position = new(componentBlockEntity.Coordinates);
                    componentPlayer = Project.FindSubsystem<SubsystemPlayers>(true).FindNearestPlayer(position);
                }
            }
            return componentPlayer;
        }

        public static void DropSlotItems(IInventory inventory, int slotIndex, Vector3 position, Vector3 velocity) {
            int slotCount = inventory.GetSlotCount(slotIndex);
            if (slotCount > 0) {
                int slotValue = inventory.GetSlotValue(slotIndex);
                int num = inventory.RemoveSlotItems(slotIndex, slotCount);
                if (num > 0) {
                    Entity entity = null;
                    if (inventory is Component component) {
                        entity = component.Entity;
                    }
                    inventory.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(slotValue, num, position, velocity, null, entity);
                }
            }
        }

        public virtual void DropSlotItems(int slotIndex, Vector3 position, Vector3 velocity) {
            DropSlotItems(this, slotIndex, position, velocity);
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            Id = valuesDictionary.GetValue("Id", -1);
            SubsystemInventories subsystemInventories = Project.FindSubsystem<SubsystemInventories>();
            if (Id == -1) {
                Id = subsystemInventories.ProduceInventoryId(this);
            }
            else {
                Id = subsystemInventories.RegisterInventory(this);
            }
            int value = valuesDictionary.GetValue<int>("SlotsCount");
            for (int i = 0; i < value; i++) {
                m_slots.Add(new Slot());
            }
            ValuesDictionary value2 = valuesDictionary.GetValue<ValuesDictionary>("Slots");
            for (int j = 0; j < m_slots.Count; j++) {
                ValuesDictionary value3 = value2.GetValue<ValuesDictionary>($"Slot{j.ToString(CultureInfo.InvariantCulture)}", null);
                if (value3 != null) {
                    Slot slot = m_slots[j];
                    slot.Value = value3.GetValue<int>("Contents");
                    slot.Count = value3.GetValue<int>("Count");
                }
            }
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) {
            ValuesDictionary valuesDictionary2 = new();
            valuesDictionary.SetValue("Slots", valuesDictionary2);
            valuesDictionary.SetValue("Id", Id);
            for (int i = 0; i < m_slots.Count; i++) {
                Slot slot = m_slots[i];
                if (slot.Count > 0) {
                    ValuesDictionary valuesDictionary3 = new();
                    valuesDictionary2.SetValue($"Slot{i.ToString(CultureInfo.InvariantCulture)}", valuesDictionary3);
                    valuesDictionary3.SetValue("Contents", slot.Value);
                    valuesDictionary3.SetValue("Count", slot.Count);
                }
            }
        }

        public virtual int GetSlotValue(int slotIndex) {
            if (slotIndex >= 0
                && slotIndex < m_slots.Count) {
                if (m_slots[slotIndex].Count <= 0) {
                    return 0;
                }
                return m_slots[slotIndex].Value;
            }
            return 0;
        }

        public virtual int GetSlotCount(int slotIndex) {
            if (slotIndex >= 0
                && slotIndex < m_slots.Count) {
                return m_slots[slotIndex].Count;
            }
            return 0;
        }

        public virtual int GetSlotCapacity(int slotIndex, int value) {
            if (slotIndex >= 0
                && slotIndex < m_slots.Count) {
                return BlocksManager.Blocks[Terrain.ExtractContents(value)].GetMaxStacking(value);
            }
            return 0;
        }

        public virtual int GetSlotProcessCapacity(int slotIndex, int value) {
            int slotCount = GetSlotCount(slotIndex);
            int slotValue = GetSlotValue(slotIndex);
            if (slotCount > 0
                && slotValue != 0) {
                SubsystemBlockBehavior[] blockBehaviors = Project.FindSubsystem<SubsystemBlockBehaviors>(true)
                    .GetBlockBehaviors(Terrain.ExtractContents(slotValue));
                for (int i = 0; i < blockBehaviors.Length; i++) {
                    int processInventoryItemCapacity = blockBehaviors[i].GetProcessInventoryItemCapacity(this, slotIndex, value);
                    if (processInventoryItemCapacity > 0) {
                        return processInventoryItemCapacity;
                    }
                }
            }
            return 0;
        }

        public virtual void AddSlotItems(int slotIndex, int value, int count) {
            AddNetSlotItems(slotIndex, value, count);
            OnSlotChange(slotIndex);
        }

        public virtual bool AddNetSlotItems(int slotIndex, int value, int count) {
            if (count > 0
                && slotIndex >= 0
                && slotIndex < m_slots.Count) {
                Slot slot = m_slots[slotIndex];
                int slotValue = GetSlotValue(slotIndex);
                int slotCount = GetSlotCount(slotIndex);
                int slotCapacity = GetSlotCapacity(slotIndex, value);
                if (slotCount != 0
                    && slotValue != value) {
                    return false;
                }
                if (slotCount + count > slotCapacity) {
                    return false;
                }
                slot.Value = value;
                slot.Count += count;
                return true;
            }
            return false;
        }

        public virtual void ProcessSlotItems(IInventory sourceInventory, int sourceSlotIndex, int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount) {
            ProcessSlotItems(slotIndex, value, count, processCount, out processedValue, out processedCount);
        }

        public virtual void ProcessSlotItems(int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount) {
            int slotCount = GetSlotCount(slotIndex);
            int slotValue = GetSlotValue(slotIndex);
            if (slotCount > 0
                && slotValue != 0) {
                SubsystemBlockBehavior[] blockBehaviors = Project.FindSubsystem<SubsystemBlockBehaviors>(true)
                    .GetBlockBehaviors(Terrain.ExtractContents(slotValue));
                foreach (SubsystemBlockBehavior subsystemBlockBehavior in blockBehaviors) {
                    int processInventoryItemCapacity = subsystemBlockBehavior.GetProcessInventoryItemCapacity(this, slotIndex, value);
                    if (processInventoryItemCapacity > 0) {
                        subsystemBlockBehavior.ProcessInventoryItem(
                            this,
                            slotIndex,
                            value,
                            count,
                            MathUtils.Min(processInventoryItemCapacity, processCount),
                            out processedValue,
                            out processedCount
                        );
                        return;
                    }
                }
            }
            processedValue = value;
            processedCount = count;
        }

        public virtual int RemoveSlotItems(int slotIndex, int count) {
            count = RemoveNetSlotItems(slotIndex, count);
            OnSlotChange(slotIndex);
            return count;
        }

        public virtual int RemoveNetSlotItems(int slotIndex, int count) {
            if (slotIndex >= 0
                && slotIndex < m_slots.Count) {
                Slot slot = m_slots[slotIndex];
                count = MathUtils.Min(count, GetSlotCount(slotIndex));
                slot.Count -= count;
                if (slot.Count == 0) {
                    slot.Value = 0;
                }
                return count;
            }
            return 0;
        }

        public virtual void SetSlotValue(int slotIndex, object obj) {
            Slot slot = (Slot)obj;
            m_slots[slotIndex].Count = slot.Count;
            m_slots[slotIndex].Value = slot.Value;
            OnSlotChange(slotIndex);
        }

        public virtual void OnSlotChange(int slotIndex) {
            if (NetworkManager.WorkType == WorkType.Server) {
                SubsystemInventories.pushSyncItem(this, slotIndex);
            }
        }

        public virtual void DropAllItems(Vector3 position) {
            for (int i = 0; i < SlotsCount; i++) {
                DropSlotItems(
                    this,
                    i,
                    position,
                    m_random.Float(5f, 10f) * Vector3.Normalize(new Vector3(m_random.Float(-1f, 1f), m_random.Float(1f, 2f), m_random.Float(-1f, 1f)))
                );
            }
        }
    }
}