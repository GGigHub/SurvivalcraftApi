using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine.Serialization;
using MessagePack;
using MessagePack.Resolvers;
using XmlUtilities;

namespace TemplatesDatabase {
    public class ValuesDictionary : IEnumerable<KeyValuePair<string, object>> {
        public Dictionary<string, object> m_dictionary = [];

        public DatabaseObject m_databaseObject;

        public int Count => m_dictionary.Count;

        public IEnumerable<string> Keys => m_dictionary.Keys;

        public IEnumerable<object> Values => m_dictionary.Values;

        public DatabaseObject DatabaseObject {
            get => m_databaseObject;
            set => m_databaseObject = value;
        }

        public object this[string key] {
            get => GetValue<object>(key);
            set => SetValue(key, value);
        }

        public bool ContainsKey(string key) => m_dictionary.ContainsKey(key);

        public bool ContainsValue(object value) => m_dictionary.ContainsValue(value);

        public void EnsureCapacity(int capacity) {
            m_dictionary.EnsureCapacity(capacity);
        }

        public T GetValue<T>(string key) {
            if (m_dictionary.TryGetValue(key, out object value)) {
                return (T)value;
            }
            throw new InvalidOperationException($"Required value \"{key}\" not found in values dictionary");
        }

        public T GetValue<T>(string key, T defaultValue) {
            if (m_dictionary.TryGetValue(key, out object value)) {
                return (T)value;
            }
            return defaultValue;
        }

        public void SetValue<T>(string key, T value) {
            m_dictionary[key] = value;
        }

        public void Add<T>(string key, T value) {
            m_dictionary.Add(key, value);
        }

        public void Clear() {
            m_dictionary.Clear();
        }

        public void Remove(string key) {
            m_dictionary.Remove(key);
        }

        public void Remove(string key, out object value) {
            m_dictionary.Remove(key, out value);
        }

        public void TrimExcess() {
            m_dictionary.TrimExcess();
        }

        public void TrimExcess(int capacity) {
            m_dictionary.TrimExcess(capacity);
        }

        public bool TryAdd<T>(string key, T value) => m_dictionary.TryAdd(key, value);

        public bool TryGetValue<T>(string key, out T value) {
            if (m_dictionary.TryGetValue(key, out object value2)) {
                value = (T)value2;
                return true;
            }
            value = default;
            return false;
        }

        public void Save(XElement node) {
            foreach (KeyValuePair<string, object> item in m_dictionary) {
                if (item.Value is ValuesDictionary valuesDictionary) {
                    XElement node2 = XmlUtils.AddElement(node, "Values");
                    XmlUtils.SetAttributeValue(node2, "Name", item.Key);
                    valuesDictionary.Save(node2);
                }
                else if (item.Value != null) {
                    XElement node3 = XmlUtils.AddElement(node, "Value");
                    XmlUtils.SetAttributeValue(node3, "Name", item.Key);
                    XmlUtils.SetAttributeValue(node3, "Type", TypeCache.GetShortTypeName(item.Value.GetType().FullName));
                    XmlUtils.SetAttributeValue(node3, "Value", item.Value);
                }
            }
        }

        public void PopulateFromDatabaseObject(DatabaseObject databaseObject) {
            m_databaseObject = databaseObject;
            foreach (DatabaseObject effectiveNestingChild in databaseObject.GetEffectiveNestingChildren(null, true)) {
                if (effectiveNestingChild.Type.SupportsValue) {
                    if (effectiveNestingChild.Value is ProceduralValue proceduralValue) {
                        object value = proceduralValue.Parse(databaseObject);
                        SetValue(effectiveNestingChild.Name, value);
                    }
                    else {
                        SetValue(effectiveNestingChild.Name, effectiveNestingChild.Value);
                    }
                }
                else {
                    ValuesDictionary valuesDictionary = [];
                    valuesDictionary.PopulateFromDatabaseObject(effectiveNestingChild);
                    SetValue(effectiveNestingChild.Name, valuesDictionary);
                }
            }
        }

        public void ApplyOverrides(ValuesDictionary overridesValuesDictionary) {
            foreach (KeyValuePair<string, object> item in overridesValuesDictionary) {
                if (item.Value is ValuesDictionary valuesDictionary) {
                    if (GetValue<object>(item.Key, null) is not ValuesDictionary valuesDictionary2) {
                        valuesDictionary2 = [];
                        SetValue(item.Key, valuesDictionary2);
                    }
                    valuesDictionary2.ApplyOverrides(valuesDictionary);
                }
                else {
                    SetValue(item.Key, item.Value);
                }
            }
        }

        public void ApplyOverrides(XElement overridesNode) {
            ApplyOverrides(overridesNode, false);
        }

        public void ApplyOverrides(XElement overridesNode, bool overrideExistOnly) {
            foreach (XElement item in overridesNode.Elements()) {
                if (item.Name == "Value") {
                    string key = XmlUtils.GetAttributeValue<string>(item, "Name");
                    if (overrideExistOnly && !m_dictionary.ContainsKey(key))
                        continue;
                    string typeName = XmlUtils.GetAttributeValue<string>(item, "Type", null);
                    Type type;
                    if (typeName == null) {
                        object value = GetValue<object>(key, null);
                        if (value == null)
                            throw new InvalidOperationException($"Type of override \"{key}\" cannot be determined.");
                        type = value.GetType();
                    }
                    else {
                        type = TypeCache.FindType(typeName, false, true);
                    }
                    object valueObj = XmlUtils.GetAttributeValue(item, "Value", type);
                    SetValue(key, valueObj);
                }
                else if (item.Name == "Values") {
                    string key = XmlUtils.GetAttributeValue<string>(item, "Name");
                    if (overrideExistOnly && !m_dictionary.ContainsKey(key))
                        continue;
                    ValuesDictionary valuesDictionary;
                    if (GetValue<object>(key, null) is ValuesDictionary vd) {
                        valuesDictionary = vd;
                    }
                    else {
                        valuesDictionary = new ValuesDictionary();
                        SetValue(key, valuesDictionary);
                    }
                    valuesDictionary.ApplyOverrides(item, overrideExistOnly);
                }
                else {
                    throw new InvalidOperationException($"Unrecognized element \"{item.Name}\" in values dictionary overrides XML.");
                }
            }
        }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => m_dictionary.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => m_dictionary.GetEnumerator();

        public void ApplyOverridesUseMessagePack(byte[] messagePackData) {
            var dynamicModel = MessagePackSerializer.Deserialize<IDictionary<object, object>>(messagePackData, ContractlessStandardResolver.Options);
            try {
                ApplyOverridesUseDictionary(dynamicModel);
            }
            catch (Exception ex) {
                throw new Exception("IDictionary to ValuesDictionary  error :" + ex.Message);
            }
        }

        private void ApplyOverridesUseDictionary(IDictionary<object, object> dictionary) {
            foreach (var keyValue in dictionary) {
                if (keyValue.Value is object[] objs) {
                    string keyName = keyValue.Key.ToString();
                    string keyType = objs[0].ToString();
                    Type type;
                    if (keyType == null) {
                        object value = GetValue<object>(keyName, null);
                        if (value == null) {
                            throw new InvalidOperationException($"Type of override \"{keyName}\" cannot be determined.");
                        }
                        type = value.GetType();
                    }
                    else {
                        type = TypeCache.FindType(keyType, skipSystemAssemblies: false, throwIfNotFound: true);
                    }
                    if (type == typeof(bool) || type.IsPrimitive || type == typeof(string)) {
                        object value = Convert.ChangeType(objs[1], type);

                        SetValue(keyName, value);
                    }
                    else {
                        string theData = objs[1].ToString();
                        object theValue = HumanReadableConverter.ConvertFromString(type, theData);
                        SetValue(keyName, theValue);
                    }
                }
                else if (keyValue.Value is IDictionary<object, object> subDic) {
                    string value2 = keyValue.Key.ToString();
                    ValuesDictionary valuesDictionary = GetValue<object>(value2, null) as ValuesDictionary;
                    if (valuesDictionary == null) {
                        valuesDictionary = new ValuesDictionary();
                        SetValue(value2, valuesDictionary);
                    }
                    valuesDictionary.ApplyOverridesUseDictionary(subDic);
                }
            }
        }

        public byte[] ToMessagePack() {
            Dictionary<object, object> dic = new Dictionary<object, object>();
            SaveToDictionary(dic);

            return MessagePackSerializer.Serialize(dic);
        }

        public string ToJsonText() {
            var jsonText = MessagePackSerializer.ConvertToJson(ToMessagePack());
            return jsonText;
        }

        public void ApplyOverridesUseJson(string jsonText, out byte[] data) {
            jsonText = jsonText.Replace("Infinity", "999999999");
            data = MessagePackSerializer.ConvertFromJson(jsonText);
            ApplyOverridesUseMessagePack(data);
        }

        public void SaveToDictionary(IDictionary<object, object> dictionary) {
            foreach (KeyValuePair<string, object> item in m_dictionary) {
                ValuesDictionary valuesDictionary = item.Value as ValuesDictionary;
                if (valuesDictionary != null) {
                    Dictionary<object, object> subDic = new Dictionary<object, object>();
                    dictionary[item.Key] = subDic;
                    valuesDictionary.SaveToDictionary(subDic);
                }
                else {
                    if (item.Value.GetType() == typeof(bool) || item.Value.GetType().IsPrimitive || item.Value.GetType() == typeof(string)) {
                        dictionary[item.Key] = new object[2] { TypeCache.GetShortTypeName(item.Value.GetType().FullName), item.Value };
                    }
                    else {
                        dictionary[item.Key] = new object[2] { TypeCache.GetShortTypeName(item.Value.GetType().FullName), HumanReadableConverter.ConvertToString(item.Value) };
                    }
                }
            }
        }
    }
}
