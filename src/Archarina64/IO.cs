using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using ExtendedXmlSerializer.Configuration;
using ExtendedXmlSerializer.ExtensionModel.Xml;
using System.Drawing;
using System.Xml.Linq;

namespace SharpOcarina
{
    static class IO
    {
        public static void Export<T>(T Object, string Filename)
        {
            XmlWriterSettings XWS = new XmlWriterSettings();
            XWS.NamespaceHandling = NamespaceHandling.OmitDuplicates;
            XWS.NewLineChars = Environment.NewLine;
            XWS.Indent = true;

            //XmlSerializer XS = new XmlSerializer(Object.GetType());
            var XS = new ConfigurationContainer().ConfigureType<ZScene>()
                                             .EnableReferences(p => p.cloneid).Create();
            using (MemoryStream buffer = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(buffer, XWS))
                {
                    writer.WriteStartDocument();
                    writer.WriteComment("Created with " + Program.ApplicationTitle);
                    XS.Serialize(writer, Object);
                    writer.WriteEndDocument();
                }
                buffer.Position = 0;
                XDocument document = XDocument.Load(buffer);
                RemapAssemblyNamespace(document, "Archarina64", "SharpOcarina");
                document.Save(Filename);
            }
        }

        public static T Import<T>(string Filename)
        {
            try
            {
                var XS = new ConfigurationContainer().ConfigureType<ZScene>()
                                 .EnableReferences(p => p.cloneid).Create();
                XDocument document = XDocument.Load(Filename);
                RemapAssemblyNamespace(document, "SharpOcarina", "Archarina64");
                using (XmlReader reader = document.CreateReader())
                    return (T)XS.Deserialize(reader);
            }
            catch (Exception e)
            {
                DebugConsole.WriteLine("Old XML detected, using old format to import it");
                // Preserve the document namespace when falling back to the legacy serializer.
                // New scene XML uses the SharpOcarina namespace; older files may have none.
                XmlDocument document = new XmlDocument();
                using (FileStream stream = File.OpenRead(Filename))
                {
                    document.Load(stream);
                }
                XmlElement rootElement = document.DocumentElement;
                XmlRootAttribute root = new XmlRootAttribute(rootElement?.LocalName ?? typeof(T).Name)
                {
                    Namespace = rootElement?.NamespaceURI ?? string.Empty
                };
                XmlSerializer XS = new XmlSerializer(typeof(T), root);
                using (StreamReader SR = new StreamReader(Filename))
                {
                    return (T)XS.Deserialize(SR);
                }
            }


        }
        private static void RemapAssemblyNamespace(XDocument document, string source, string target)
        {
            string before = "clr-namespace:SharpOcarina;assembly=" + source;
            string after = "clr-namespace:SharpOcarina;assembly=" + target;
            foreach (XElement element in document.Descendants())
            {
                if (element.Name.NamespaceName == before)
                    element.Name = XName.Get(element.Name.LocalName, after);
                foreach (XAttribute attribute in element.Attributes().ToList())
                {
                    if (attribute.IsNamespaceDeclaration && attribute.Value == before)
                        attribute.Value = after;
                    else if (attribute.Name.NamespaceName == before)
                    {
                        element.SetAttributeValue(XName.Get(attribute.Name.LocalName, after), attribute.Value);
                        attribute.Remove();
                    }
                }
            }
        }

        /*
        public static void BinExport<T>(T Object, string Filename)
        {
            Stream stream = File.Open(Filename, FileMode.Create);
            BinaryFormatter bFormatter = new BinaryFormatter();
            bFormatter.Serialize(stream, Object);
            stream.Close();
        }*/
    }
}
