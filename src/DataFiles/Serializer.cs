using System;
using System.IO;
using System.Xml.Serialization;

namespace fam.DataFiles
{
    internal static class Serializer
    {
        public static T LoadFromStream<T>(Stream stream)
        {
            var serializerObjs = CreateSerializer<T>();
            var ns = serializerObjs.Item1;
            var serializer = serializerObjs.Item2;
            var configData = (T) serializer.Deserialize(stream);
            return configData;
        }

        public static void SaveToStream<T>(Stream stream, T configData)
        {
            var serializerObjs = CreateSerializer<T>();
            var ns = serializerObjs.Item1;
            var serializer = serializerObjs.Item2;
            serializer.Serialize(stream, configData, ns);
        }

        private static Tuple<XmlSerializerNamespaces, XmlSerializer> CreateSerializer<T>()
        {
            var ns = new XmlSerializerNamespaces();
            ns.Add("", "");
            var serializer = new XmlSerializer(typeof(T));
            return new Tuple<XmlSerializerNamespaces, XmlSerializer>(ns, serializer);
        }
    }
}