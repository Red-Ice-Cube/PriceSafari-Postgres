using PriceSafari.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using System.IO;
using MigraDoc.DocumentObjectModel.Shapes;

public class InvoiceDocument
{
    private readonly InvoiceClass _invoice;
    private readonly string _logoImagePath;

    public InvoiceDocument(InvoiceClass invoice, string logoImagePath)
    {
        _invoice = invoice;
        _logoImagePath = logoImagePath;
    }

    public byte[] GeneratePdf()
    {
        var document = new Document();
        var headerTitle = _invoice.IsPaid ? "Faktura VAT" : "ProForma";
        document.Info.Title = $"{headerTitle} nr {_invoice.InvoiceNumber}";
        document.Info.Author = "Heated Box Sp. z o.o.";

        DefineStyles(document);
        CreatePage(document);

        var pdfRenderer = new PdfDocumentRenderer(unicode: true)
        {
            Document = document
        };
        pdfRenderer.RenderDocument();

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
        style.Font.Size = 10; // Reduced font size to fit more content

        var headerStyle = document.Styles.AddStyle("Header", "Normal");
        headerStyle.Font.Size = 18; // Reduced header font size
        headerStyle.Font.Bold = true;

        var boldStyle = document.Styles.AddStyle("Bold", "Normal");
        boldStyle.Font.Bold = true;
    }

    private void CreatePage(Document document)
    {
        var vatRate = 0.23m; // 23% VAT
        var vatAmount = _invoice.NetAmount * vatRate;
        var grossAmount = _invoice.NetAmount + vatAmount;

        // Each MigraDoc document needs at least one section
        var section = document.AddSection();

        // Adjust page margins
        section.PageSetup.TopMargin = "2cm";
        section.PageSetup.BottomMargin = "2cm";
        section.PageSetup.LeftMargin = "1.5cm";
        section.PageSetup.RightMargin = "1.5cm";

        // Add the logo and horizontal line at the top
        AddLogoAndLine(section);

        // Header
        var headerTitle = _invoice.IsPaid ? "Faktura VAT" : "ProForma";
        var header = section.AddParagraph($"{headerTitle} nr {_invoice.InvoiceNumber}");
        header.Style = "Header";
        header.Format.SpaceAfter = "0.5cm"; // Reduced space after header

        // Date and Status Info
        var dateTable = section.AddTable();
        dateTable.Borders.Visible = false;
        dateTable.AddColumn("8cm");
        dateTable.AddColumn("8cm");

        var dateRow = dateTable.AddRow();

        // Left cell: Dates and status
        var cell = dateRow.Cells[0];
        cell.AddParagraph($"Data wystawienia: {_invoice.IssueDate:yyyy-MM-dd}");

        // Termin płatności 7 dni
        var paymentTerm = 7;
        cell.AddParagraph($"Termin płatności: {_invoice.IssueDate.AddDays(paymentTerm):yyyy-MM-dd}");

        cell.AddParagraph($"Status: {(_invoice.IsPaid ? "Opłacona" : "Nieopłacona")}");

        // Right cell: Empty or additional info
        dateRow.Cells[1].AddParagraph("");

        section.AddParagraph().AddLineBreak();

        // Buyer and Seller Info Table
        var infoTable = section.AddTable();
        infoTable.Borders.Visible = true;
        infoTable.AddColumn("9.13cm");
        infoTable.AddColumn("9.13cm");
        infoTable.Format.Borders.DistanceFromTop = "0.07cm";
        infoTable.Format.Borders.DistanceFromBottom = "0.07cm";
        infoTable.Format.Borders.DistanceFromLeft = "0.05cm";
        infoTable.Format.Borders.DistanceFromRight = "0.05cm";

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
        table.AddColumn("1cm");    // L.p.
        table.AddColumn("7.8cm");  // Usługa
        table.AddColumn("2.5cm");  // Cena netto
        table.AddColumn("1.5cm");  // VAT %
        table.AddColumn("2.5cm");  // Kwota VAT
        table.AddColumn("3cm");    // Cena brutto

        // Header Row
        var headerRow = table.AddRow();
        headerRow.Shading.Color = Colors.LightGray;
        headerRow.Cells[0].AddParagraph("L.p.").Format.Font.Bold = true;
        headerRow.Cells[1].AddParagraph("Usługa").Format.Font.Bold = true;
        headerRow.Cells[2].AddParagraph("Cena netto").Format.Font.Bold = true;
        headerRow.Cells[2].Format.Alignment = ParagraphAlignment.Right;
        headerRow.Cells[3].AddParagraph("VAT %").Format.Font.Bold = true;
        headerRow.Cells[3].Format.Alignment = ParagraphAlignment.Right;
        headerRow.Cells[4].AddParagraph("Kwota VAT").Format.Font.Bold = true;
        headerRow.Cells[4].Format.Alignment = ParagraphAlignment.Right;
        headerRow.Cells[5].AddParagraph("Cena brutto").Format.Font.Bold = true;
        headerRow.Cells[5].Format.Alignment = ParagraphAlignment.Right;

        // Data Row
        var dataRow = table.AddRow();
        dataRow.Cells[0].AddParagraph("1");

        var serviceDescription = $"PriceSafari {_invoice.Plan.PlanName}\n" +
                                 $"Ilość analiz: {_invoice.ScrapesIncluded}\n" +
                                 $"Maksymalna ilość produktów: {_invoice.UrlsIncluded}";
        dataRow.Cells[1].AddParagraph(serviceDescription);

        // Cena netto
        dataRow.Cells[2].AddParagraph($"{_invoice.NetAmount:C}");
        dataRow.Cells[2].Format.Alignment = ParagraphAlignment.Right;

        // VAT %
        dataRow.Cells[3].AddParagraph($"{vatRate:P0}");
        dataRow.Cells[3].Format.Alignment = ParagraphAlignment.Right;

        // Kwota VAT
        dataRow.Cells[4].AddParagraph($"{vatAmount:C}");
        dataRow.Cells[4].Format.Alignment = ParagraphAlignment.Right;

        // Cena brutto
        dataRow.Cells[5].AddParagraph($"{grossAmount:C}");
        dataRow.Cells[5].Format.Alignment = ParagraphAlignment.Right;

        section.AddParagraph().AddLineBreak();

        // Jeśli zastosowano rabat, wyświetlamy informację o rabacie
        if (_invoice.AppliedDiscountPercentage > 0)
        {
            var discountParagraph = section.AddParagraph();
            discountParagraph.AddFormattedText("Zastosowano rabat: ", TextFormat.Bold);
            discountParagraph.AddText($"{_invoice.AppliedDiscountPercentage}%");
            discountParagraph.AddLineBreak();
            discountParagraph.AddFormattedText("Kwota rabatu: ", TextFormat.Bold);
            discountParagraph.AddText($"{_invoice.AppliedDiscountAmount:C}");
            discountParagraph.Format.SpaceBefore = "0.5cm";
            discountParagraph.Format.SpaceAfter = "0.5cm";
        }

        var totalParagraph = section.AddParagraph();
        totalParagraph.Format.Borders.Top.Width = 0.75;
        totalParagraph.Format.Borders.Bottom.Width = 0.75;
        totalParagraph.Format.Borders.Color = Colors.Black;
        totalParagraph.Format.Borders.DistanceFromTop = "0.2cm";
        totalParagraph.Format.Borders.DistanceFromBottom = "0.2cm";
        totalParagraph.Format.Borders.DistanceFromLeft = "0.13cm";
        totalParagraph.Format.Borders.DistanceFromRight = "0.13cm";

        totalParagraph.AddFormattedText($"Razem do zapłaty: {grossAmount:C}", TextFormat.Bold);
        totalParagraph.Format.Alignment = ParagraphAlignment.Right;
        totalParagraph.Format.SpaceBefore = "0.5cm";
        totalParagraph.Format.SpaceAfter = "0.5cm";

        // Signature Rectangles
        var signatureTable = section.AddTable();
        signatureTable.Borders.Width = 0.75;
        signatureTable.AddColumn("9.13cm");
        signatureTable.AddColumn("9.13cm");

        var signatureRow = signatureTable.AddRow();

        // Lewa komórka: Wystawił(a)
        var leftSignatureCell = signatureRow.Cells[0];
        leftSignatureCell.Borders.Width = 0.75;
        leftSignatureCell.Borders.Color = Colors.Black;
        leftSignatureCell.Shading.Color = Colors.White;
        leftSignatureCell.Format.SpaceAfter = "2.4cm";
        var wystawilParagraph = leftSignatureCell.AddParagraph("Wystawił(a)");
        wystawilParagraph.Format.Alignment = ParagraphAlignment.Center;
        wystawilParagraph.Format.SpaceBefore = "0.2cm";

        // Prawa komórka: Odebrał(a)
        var rightSignatureCell = signatureRow.Cells[1];
        rightSignatureCell.Borders.Width = 0.75;
        rightSignatureCell.Borders.Color = Colors.Black;
        rightSignatureCell.Shading.Color = Colors.White;
        rightSignatureCell.Format.SpaceAfter = "2.4cm";
        var odebralParagraph = rightSignatureCell.AddParagraph("Odebrał(a)");
        odebralParagraph.Format.Alignment = ParagraphAlignment.Center;
        odebralParagraph.Format.SpaceBefore = "0.2cm";


        // Payment Info
        if (!_invoice.IsPaid)
        {
            section.AddParagraph().AddLineBreak();
            var paymentInfo = section.AddParagraph("Prosimy o dokonanie płatności na poniższy rachunek bankowy w terminie 7 dni:");
            paymentInfo.Style = "Bold";
            paymentInfo.Format.SpaceBefore = "1cm";

            section.AddParagraph("PKO Bank Polski");
            section.AddParagraph("Nr konta: PL88 1020 1664 0000 3302 0645 9798");
        }

        // Footer
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText("Strona ");
        footer.AddPageField();
        footer.AddText(" z ");
        footer.AddNumPagesField();
    }

    private void AddLogoAndLine(Section section)
    {
        var headerTable = section.AddTable();
        headerTable.Borders.Visible = false;
        headerTable.AddColumn("1cm");
        headerTable.AddColumn("16cm");

        var headerRow = headerTable.AddRow();

        // Logo cell
        var logoCell = headerRow.Cells[0];
        var image = logoCell.AddImage(_logoImagePath);
        image.LockAspectRatio = true;
        image.Height = "0.6cm";
        image.RelativeVertical = RelativeVertical.Line;
        image.RelativeHorizontal = RelativeHorizontal.Margin;
        image.Top = ShapePosition.Top;
        image.Left = ShapePosition.Left;
        logoCell.VerticalAlignment = VerticalAlignment.Top;

        var emptyCell = headerRow.Cells[1];
        emptyCell.AddParagraph("");

        // Horizontal line
        var lineParagraph = section.AddParagraph();
        lineParagraph.Format.SpaceBefore = "0.5cm";
        lineParagraph.Format.Borders.Bottom.Width = 0.75;
        lineParagraph.Format.Borders.Bottom.Color = Colors.Black;
        lineParagraph.Format.Borders.Bottom.Style = BorderStyle.Single;
        lineParagraph.Format.SpaceAfter = "0.5cm";
    }
}
