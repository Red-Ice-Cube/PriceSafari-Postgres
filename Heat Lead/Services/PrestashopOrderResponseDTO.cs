using System.Xml.Serialization;

namespace Heat_Lead.Services
{
    [XmlRoot(ElementName = "prestashop")]
    public class PrestashopOrderResponseDTO
    {
        [XmlElement(ElementName = "order")]
        public OrderId OrderId { get; set; }
    }

    public class OrderId
    {
        [XmlElement(ElementName = "id")]
        public int Id { get; set; }

        [XmlElement(ElementName = "associations")]
        public OrderAssociations Associations { get; set; }

        [XmlElement(ElementName = "reference")]
        public string Reference { get; set; }

        [XmlElement(ElementName = "secure_key")]
        public string SecureKey { get; set; }
    }

    public class OrderAssociations
    {
        [XmlElement(ElementName = "order_rows")]
        public OrderRows OrderRows { get; set; }
    }

    public class OrderRows
    {
        [XmlElement(ElementName = "order_row")]
        public List<OrderRow> OrderRowItems { get; set; }
    }

    public class OrderRow
    {
        [XmlElement(ElementName = "product_id")]
        public string ProductId { get; set; }

        [XmlElement(ElementName = "product_ean13")]
        public string ProductEan13 { get; set; }

        [XmlElement(ElementName = "product_quantity")]
        public int ProductQuantity { get; set; }
    }
}