using PriceSafari.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using System.IO;

public class InvoiceDocument
{
    private readonly InvoiceClass _invoice;

    public InvoiceDocument(InvoiceClass invoice)
    {
        _invoice = invoice;
    }

    public byte[] GeneratePdf()
    {
        // Create a new MigraDoc document
        var document = new Document();
        var headerTitle = _invoice.IsPaid ? "Faktura VAT" : "ProForma";
        document.Info.Title = $"{headerTitle} nr {_invoice.InvoiceId}";
        document.Info.Author = "Twoja Firma Sp. z o.o.";

        DefineStyles(document);
        CreatePage(document);

        // Render the PDF
        var pdfRenderer = new PdfDocumentRenderer(unicode: true)
        {
            Document = document
        };
        pdfRenderer.RenderDocument();

        // Save the document into a byte array
        using (var stream = new MemoryStream())
        {
            pdfRenderer.PdfDocument.Save(stream, closeStream: false);
            return stream.ToArray();
        }
    }

    private void DefineStyles(Document document)
    {
        // Define styles
        var style = document.Styles["Normal"];
        style.Font.Name = "Arial";
        style.Font.Size = 12;

        var headerStyle = document.Styles.AddStyle("Header", "Normal");
        headerStyle.Font.Size = 20;
        headerStyle.Font.Bold = true;

        var boldStyle = document.Styles.AddStyle("Bold", "Normal");
        boldStyle.Font.Bold = true;
    }

    private void CreatePage(Document document)
    {
        var grossAmount = _invoice.NetAmount * 1.23m; // Assuming 23% VAT

        // Each MigraDoc document needs at least one section
        var section = document.AddSection();

        // Header
        var headerTitle = _invoice.IsPaid ? "Faktura VAT" : "ProForma";
        var header = section.AddParagraph($"{headerTitle} nr {_invoice.InvoiceId}");
        header.Style = "Header";
        header.Format.SpaceAfter = "1cm";

        // Date and Status Info
        var dateTable = section.AddTable();
        dateTable.Borders.Visible = false;
        dateTable.AddColumn("8cm");
        dateTable.AddColumn("8cm");

        var dateRow = dateTable.AddRow();

        // Left cell: Dates and status
        var cell = dateRow.Cells[0];
        cell.AddParagraph($"Data wystawienia: {_invoice.IssueDate:yyyy-MM-dd}");
        cell.AddParagraph($"Data płatności: {_invoice.PaymentDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
        cell.AddParagraph($"Status: {(_invoice.IsPaid ? "Opłacona" : "Nieopłacona")}");

        // Right cell: Empty (or you can add additional info)
        dateRow.Cells[1].AddParagraph("");

        section.AddParagraph().AddLineBreak();

        // Buyer and Seller Info Table
        var infoTable = section.AddTable();
        infoTable.Borders.Visible = false;
        infoTable.AddColumn("8cm"); // Left column
        infoTable.AddColumn("8cm"); // Right column

        var row = infoTable.AddRow();

        // Left cell: Buyer info
        cell = row.Cells[0];
        var buyerParagraph = cell.AddParagraph("Nabywca:");
        buyerParagraph.Style = "Bold";
        cell.AddParagraph(_invoice.CompanyName);
        cell.AddParagraph(_invoice.Address);
        cell.AddParagraph($"{_invoice.PostalCode} {_invoice.City}");
        cell.AddParagraph($"NIP: {_invoice.NIP}");

        // Right cell: Seller info
        cell = row.Cells[1];
        var sellerParagraph = cell.AddParagraph("Sprzedawca:");
        sellerParagraph.Style = "Bold";
        cell.AddParagraph("Heated Box Sp. z o.o.");
        cell.AddParagraph("Ul. Wojciecha Korfantego 16");
        cell.AddParagraph("42-202 Częstochowa");
        cell.AddParagraph("NIP: 9492247951");

        section.AddParagraph().AddLineBreak();

        // Invoice Items Table
        var table = section.AddTable();
        table.Borders.Width = 0.75;
        table.Format.Alignment = ParagraphAlignment.Left;

        // Define Columns
        table.AddColumn("2cm"); // L.p.
        table.AddColumn("8cm"); // Nazwa usługi
        table.AddColumn("3cm"); // Cena netto
        table.AddColumn("3cm"); // Cena brutto

        // Header Row
        var headerRow = table.AddRow();
        headerRow.Shading.Color = Colors.LightGray;
        headerRow.Cells[0].AddParagraph("L.p.").Format.Font.Bold = true;
        headerRow.Cells[1].AddParagraph("Nazwa usługi").Format.Font.Bold = true;
        headerRow.Cells[2].AddParagraph("Cena netto").Format.Font.Bold = true;
        headerRow.Cells[2].Format.Alignment = ParagraphAlignment.Right;
        headerRow.Cells[3].AddParagraph("Cena brutto").Format.Font.Bold = true;
        headerRow.Cells[3].Format.Alignment = ParagraphAlignment.Right;

        // Data Row
        var dataRow = table.AddRow();
        dataRow.Cells[0].AddParagraph("1");
        dataRow.Cells[1].AddParagraph("PriceSafari " + _invoice.Plan.PlanName);
        dataRow.Cells[2].AddParagraph($"{_invoice.NetAmount:C}");
        dataRow.Cells[2].Format.Alignment = ParagraphAlignment.Right;
        dataRow.Cells[3].AddParagraph($"{grossAmount:C}");
        dataRow.Cells[3].Format.Alignment = ParagraphAlignment.Right;

        section.AddParagraph().AddLineBreak();

        // Total Amount
        var totalParagraph = section.AddParagraph($"Do zapłaty: {grossAmount:C}");
        totalParagraph.Format.Alignment = ParagraphAlignment.Right;
        totalParagraph.Format.SpaceBefore = "1cm";

        // Payment Info
        if (!_invoice.IsPaid)
        {
            section.AddParagraph().AddLineBreak();
            var paymentInfo = section.AddParagraph("Prosimy o dokonanie płatności na poniższy rachunek bankowy:");
            paymentInfo.Style = "Bold";
            paymentInfo.Format.SpaceBefore = "1cm";

            section.AddParagraph("Bank XYZ");
            section.AddParagraph("Nr konta: 00 0000 0000 0000 0000 0000 0000");
        }

        // Footer
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText("Strona ");
        footer.AddPageField();
        footer.AddText(" z ");
        footer.AddNumPagesField();
    }
}
