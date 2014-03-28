﻿using JsonLD.Core;
using JsonLDIntegration;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Packaging;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using VDS.RDF.Query.Datasets;
using VDS.RDF.Writing;

namespace GatherMergeRewrite
{
    class Utils
    {
        public static IGraph CreateNuspecGraph(XDocument nuspec, string baseAddress)
        {
            nuspec = NormalizeNuspecNamespace(nuspec);

            string path = "xslt\\nuspec.xslt";

            XslCompiledTransform transform = CreateTransform(path);

            XsltArgumentList arguments = new XsltArgumentList();
            arguments.AddParam("base", "", baseAddress);
            arguments.AddParam("extension", "", ".json");

            arguments.AddExtensionObject("urn:helper", new XsltHelper());

            XDocument rdfxml = new XDocument();
            using (XmlWriter writer = rdfxml.CreateWriter())
            {
                transform.Transform(nuspec.CreateReader(), arguments, writer);
            }

            RdfXmlParser rdfXmlParser = new RdfXmlParser();
            XmlDocument doc = new XmlDocument();
            doc.Load(rdfxml.CreateReader());
            IGraph graph = new Graph();
            rdfXmlParser.Load(graph, doc);

            return graph;
        }

        static XslCompiledTransform CreateTransform(string path)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StreamReader(path)));
            return transform;
        }

        static void Dump(IGraph graph)
        {
            CompressingTurtleWriter turtleWriter = new CompressingTurtleWriter();
            turtleWriter.DefaultNamespaces.AddNamespace("nuget", new Uri("http://nuget.org/schema#"));
            turtleWriter.PrettyPrintMode = true;
            turtleWriter.CompressionLevel = 10;
            turtleWriter.Save(graph, Console.Out);
        }

        public static XDocument GetNuspec(Package package)
        {
            foreach (PackagePart part in package.GetParts())
            {
                if (part.Uri.ToString().EndsWith(".nuspec"))
                {
                    XDocument nuspec = XDocument.Load(part.GetStream());
                    return nuspec;
                }
            }
            throw new FileNotFoundException("nuspec");
        }

        public static Package GetPackage(Stream stream)
        {
            Package package = Package.Open(stream);
            return package;
        }

        public static XDocument NormalizeNuspecNamespace(XDocument original)
        {
            string path = "xslt\\normalizeNuspecNamespace.xslt";

            XDocument result = new XDocument();

            using (XmlWriter writer = result.CreateWriter())
            {
                XslCompiledTransform xslt = new XslCompiledTransform();
                xslt.Load(XmlReader.Create(new StreamReader(path)));
                xslt.Transform(original.CreateReader(), writer);
            }

            return result;
        }

        public static string GetName(Uri uri, string baseAddress, string container)
        {
            string address = string.Format("{0}/{1}/", baseAddress, container);
            string s = uri.ToString();
            string name = s.Substring(address.Length);
            return name;
        }

        public static string CreateHtmlView(Uri resource, string frame)
        {
            XDocument original = XDocument.Load(new StreamReader("html\\view.html"));
            XslCompiledTransform transform = CreateTransform("xslt\\view.xslt");
            XsltArgumentList arguments = new XsltArgumentList();
            arguments.AddParam("resource", "", resource.ToString());
            arguments.AddParam("frame", "", frame);
            arguments.AddParam("base", "", Config.BaseAddress);

            System.IO.StringWriter writer = new System.IO.StringWriter();
            using (XmlTextWriter xmlWriter = new XmlHtmlWriter(writer))
            {
                xmlWriter.Formatting = System.Xml.Formatting.Indented;
                transform.Transform(original.CreateReader(), arguments, xmlWriter);
            }

            return writer.ToString();
        }

        public static string CreateJson(IGraph graph, JToken frame = null)
        {
            System.IO.StringWriter writer = new System.IO.StringWriter();
            IRdfWriter rdfWriter = new JsonLdWriter();
            rdfWriter.Save(graph, writer);
            writer.Flush();

            if (frame == null)
            {
                return writer.ToString();
            }
            else
            {
                JToken flattened = JToken.Parse(writer.ToString());
                JObject framed = JsonLdProcessor.Frame(flattened, frame, new JsonLdOptions());
                JObject compacted = JsonLdProcessor.Compact(framed, framed["@context"], new JsonLdOptions());

                return compacted.ToString();
            }
        }

        public static IGraph CreateGraph(string json)
        {
            JToken compacted = JToken.Parse(json);
            JToken flattened = JsonLdProcessor.Flatten(compacted, new JsonLdOptions());

            IRdfReader rdfReader = new JsonLdReader();
            IGraph graph = new Graph();
            rdfReader.Load(graph, new StringReader(flattened.ToString()));

            return graph;
        }
    }
}
