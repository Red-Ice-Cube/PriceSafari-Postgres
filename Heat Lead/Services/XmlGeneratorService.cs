using Heat_Lead.Models;
using System.Xml.Linq;

namespace Heat_Lead.Services
{
    public class XmlGeneratorService
    {
        public string GenerateXML(Generator generator)
        {
            XNamespace ns = "http://www.w3.org/1999/xlink";

            XDocument doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(ns + "prestashop",
                new XAttribute(XNamespace.Xmlns + "xlink", ns),
                    new XElement("cart_rule",
                        new XElement("id_customer", new XCData("0")),
                        new XElement("date_from", new XCData("2023-07-03 13:00:00")),
                        new XElement("date_to", new XCData("2024-07-03 13:00:00")),
                        new XElement("description", new XCData("")),
                        new XElement("quantity", new XCData("3")),
                        new XElement("quantity_per_user", new XCData("10")),
                        new XElement("priority", new XCData("1")),
                        new XElement("partial_use", new XCData("1")),
                        new XElement("code", new XCData(generator.CodeAFI)),
                        new XElement("minimum_amount", new XCData("0.000000")),
                        new XElement("minimum_amount_tax", new XCData("1")),
                        new XElement("minimum_amount_currency", new XCData("1")),
                        new XElement("minimum_amount_shipping", new XCData("0")),
                        new XElement("country_restriction", new XCData("0")),
                        new XElement("carrier_restriction", new XCData("0")),
                        new XElement("group_restriction", new XCData("0")),
                        new XElement("cart_rule_restriction", new XCData("0")),
                        new XElement("product_restriction", new XCData("1")),
                        new XElement("shop_restriction", new XCData("0")),
                        new XElement("free_shipping", new XCData("0")),
                        new XElement("reduction_percent", new XCData("0.00")),
                        new XElement("reduction_amount", new XCData("1.00")),
                        new XElement("reduction_tax", new XCData("1")),
                        new XElement("reduction_currency", new XCData("1")),
                        new XElement("reduction_product", new XCData("0")),
                        new XElement("reduction_exclude_special", new XCData("0")),
                        new XElement("gift_product", new XCData("0")),
                        new XElement("gift_product_attribute", new XCData("0")),
                        new XElement("highlight", new XCData("0")),
                        new XElement("active", new XCData("1")),
                        new XElement("product_rule_group",
                        new XElement("product_rule",
                        new XAttribute("type", "products"),
                        new XElement("product_rule_item",
                        new XAttribute("id", "12844")
                                    )
                                )
                            ),

                        new XElement("name",
                            new XElement("language",
                                new XAttribute("id", "1"),
                                new XCData(generator.CodeAFI)
                            ),
                            new XElement("language",
                                new XAttribute("id", "2"),
                                new XCData(generator.CodeAFI)
                            )
                        )
                    )
                )
            );

            return doc.Declaration + doc.ToString();
        }
    }
}