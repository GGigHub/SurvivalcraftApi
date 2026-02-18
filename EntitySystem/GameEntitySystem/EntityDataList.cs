using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
using XmlUtilities;

namespace GameEntitySystem {
    public class EntityDataList {
        public List<EntityData> EntitiesData;

        public EntityDataList() { }

        public EntityDataList(GameDatabase gameDatabase, XElement entitiesNode, bool ignoreInvalidEntities) {
            EntitiesData = new List<EntityData>(entitiesNode.Elements().Count());
            foreach (XElement item in entitiesNode.Elements()) {
                try {
                    EntitiesData.Add(new EntityData(gameDatabase, item));
                }
                catch (Exception ex) {
                    if (!ignoreInvalidEntities) {
                        throw;
                    }
                    Log.Warning("Ignoring invalid entity. Reason: {0}", ex);
                }
            }
        }

        public EntityDataList(GameDatabase gameDatabase, ValuesDictionary valuesDictionary, bool ignoreInvalidEntities) {
            EntitiesData = new List<EntityData>(valuesDictionary.Values.Count());
            foreach (ValuesDictionary item in valuesDictionary.Values) {
                try {
                    EntitiesData.Add(new EntityData(gameDatabase, item));
                }
                catch (Exception ex) {
                    if (!ignoreInvalidEntities) {
                        throw;
                    }
                    Log.Warning("Ignoring invalid entity. Reason: {0}", ex);
                }
            }
        }

        public void Save(XElement entitiesNode, int nextEntityID) {
            XmlUtils.SetAttributeValue(entitiesNode, "NextID", nextEntityID);
            //Log.Information("Save NextEntityID: " + nextEntityID);
            foreach (EntityData entitiesDatum in EntitiesData) {
                XElement entityNode = XmlUtils.AddElement(entitiesNode, "Entity");
                entitiesDatum.Save(entityNode);
            }
        }

        public void Save(ValuesDictionary valuesDictionary) {
            int i = 1;
            foreach (EntityData entitiesDatum in EntitiesData) {
                ValuesDictionary dict = new ValuesDictionary();
                valuesDictionary.SetValue(i.ToString(), dict);
                entitiesDatum.Save(dict);
                i++;
            }
        }
    }
}