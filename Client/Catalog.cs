using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace FileHostClient
{
    // C# classes matching the structure of dummy.xml.
    // XmlSerializer fills these in by matching element and attribute names.

    [XmlRoot("Catalog")]
    public class Catalog
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("updated")]
        public DateTime Updated { get; set; }

        [XmlElement("Product")]
        public List<Product> Products { get; set; } = new List<Product>();
    }

    public class Product
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlAttribute("category")]
        public string Category { get; set; }

        public string Name { get; set; }
        public Price Price { get; set; }
        public int Quantity { get; set; }
        public bool InStock { get; set; }

        [XmlArray("Tags")]
        [XmlArrayItem("Tag")]
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class Price
    {
        [XmlAttribute("currency")]
        public string Currency { get; set; }

        [XmlText]
        public decimal Amount { get; set; }
    }
}
