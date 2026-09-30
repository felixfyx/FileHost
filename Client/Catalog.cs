using System.Xml.Serialization;

// C# model for the XML files the server hosts (see Server/SharedFiles/dummy.xml).
// XmlSerializer maps elements and attributes onto these properties by name.

[XmlRoot("Catalog")]
public class Catalog
{
    [XmlAttribute("name")] public string Name { get; set; } = "";
    [XmlAttribute("updated")] public DateTime Updated { get; set; }

    [XmlElement("Product")] public List<Product> Products { get; set; } = [];
}

public class Product
{
    [XmlAttribute("id")] public int Id { get; set; }
    [XmlAttribute("category")] public string Category { get; set; } = "";

    public string Name { get; set; } = "";
    public Price Price { get; set; } = new();
    public int Quantity { get; set; }
    public bool InStock { get; set; }

    [XmlArray("Tags"), XmlArrayItem("Tag")] public List<string> Tags { get; set; } = [];
}

public class Price
{
    [XmlAttribute("currency")] public string Currency { get; set; } = "";
    [XmlText] public decimal Amount { get; set; }
}
