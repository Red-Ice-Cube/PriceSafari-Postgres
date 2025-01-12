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
        style.Font.Size = 10; // Reduced font size

        var headerStyle = document.Styles.AddStyle("Header", "Normal");
        headerStyle.Font.Size = 18;
        headerStyle.Font.Bold = true;

        var boldStyle = document.Styles.AddStyle("Bold", "Normal");
        boldStyle.Font.Bold = true;
    }

    private void CreatePage(Document document)
    {
        var vatRate = 0.23m; // 23% VAT

        var discountedNet = _invoice.NetAmount;
        var discountedVat = discountedNet * vatRate;
        var discountedGross = discountedNet + discountedVat;

        var originalNet = discountedNet + _invoice.AppliedDiscountAmount;
        var originalVat = originalNet * vatRate;
        var originalGross = originalNet + originalVat;

        var section = document.AddSection();
        section.PageSetup.TopMargin = "2cm";
        section.PageSetup.BottomMargin = "2cm";
        section.PageSetup.LeftMargin = "1.5cm";
        section.PageSetup.RightMargin = "1.5cm";

        AddLogoAndLine(section);

        var headerTitle = _invoice.IsPaid ? "Faktura VAT" : "ProForma";
        var header = section.AddParagraph($"{headerTitle} nr {_invoice.InvoiceNumber}");
        header.Style = "Header";
        header.Format.SpaceAfter = "0.5cm";

        // Jeśli faktura jest opłacona i posiada oryginalny numer proformy, wyświetlamy tę informację
        if (_invoice.IsPaid && !string.IsNullOrEmpty(_invoice.OriginalProformaNumber))
        {
            var fakturaDoProformyParagraph = section.AddParagraph($"Faktura do proformy: {_invoice.OriginalProformaNumber}");
            fakturaDoProformyParagraph.Format.Font.Bold = true;
            fakturaDoProformyParagraph.Format.SpaceAfter = "0.5cm";
        }

        // Date and Status Info
        var dateTable = section.AddTable();
        dateTable.Borders.Visible = false;
        dateTable.AddColumn("8cm");
        dateTable.AddColumn("8cm");

        var dateRow = dateTable.AddRow();



        var cell = dateRow.Cells[0];
        cell.AddParagraph($"Data wystawienia: {_invoice.IssueDate:yyyy-MM-dd}");

    
        if (_invoice.PaymentDate != null)
        {
            cell.AddParagraph($"Data sprzedaży: {_invoice.PaymentDate:yyyy-MM-dd}");
        }

        cell.AddParagraph($"Status: {(_invoice.IsPaid ? "Opłacona" : "Nieopłacona")}");


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

        // Columns
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
        dataRow.Cells[2].AddParagraph($"{discountedNet:C}").Format.Alignment = ParagraphAlignment.Right;
        dataRow.Cells[3].AddParagraph($"{vatRate:P0}").Format.Alignment = ParagraphAlignment.Right;
        dataRow.Cells[4].AddParagraph($"{discountedVat:C}").Format.Alignment = ParagraphAlignment.Right;
        dataRow.Cells[5].AddParagraph($"{discountedGross:C}").Format.Alignment = ParagraphAlignment.Right;

        section.AddParagraph().AddLineBreak();

        // Rabat info
        if (_invoice.AppliedDiscountPercentage > 0)
        {
            var discountParagraph = section.AddParagraph();
            discountParagraph.AddFormattedText("Ceny przed rabatem:\n", TextFormat.Bold);
            discountParagraph.AddText($"Netto: {originalNet:C}, Brutto: {originalGross:C}\n");
            discountParagraph.AddFormattedText("Zastosowano rabat: ", TextFormat.Bold);
            discountParagraph.AddText($"{_invoice.AppliedDiscountPercentage}% (Kwota rabatu: {_invoice.AppliedDiscountAmount:C})\n");
            discountParagraph.AddFormattedText("Ceny po rabacie:\n", TextFormat.Bold);
            discountParagraph.AddText($"Netto: {discountedNet:C}, Brutto: {discountedGross:C}");

            discountParagraph.Format.SpaceBefore = "0.5cm";
            discountParagraph.Format.SpaceAfter = "0.5cm";
        }

        // Sekcja z finalną płatnością/ZAPŁACONO
        var totalParagraph = section.AddParagraph();
        totalParagraph.Format.Borders.Top.Width = 0.75;
        totalParagraph.Format.Borders.Bottom.Width = 0.75;
        totalParagraph.Format.Borders.Color = Colors.Black;
        totalParagraph.Format.Borders.DistanceFromTop = "0.2cm";
        totalParagraph.Format.Borders.DistanceFromBottom = "0.2cm";
        totalParagraph.Format.Borders.DistanceFromLeft = "0.13cm";
        totalParagraph.Format.Borders.DistanceFromRight = "0.13cm";
        totalParagraph.Format.Alignment = ParagraphAlignment.Right;
        totalParagraph.Format.SpaceBefore = "0.5cm";
        totalParagraph.Format.SpaceAfter = "0.5cm";

        if (_invoice.IsPaid)
        {
            // Jeśli opłacone, zamiast "Razem do zapłaty" pokazujemy "ZAPŁACONO" na zielono i kwotę na czarno
            var zaplacono = totalParagraph.AddFormattedText("ZAPŁACONO\n", TextFormat.Bold);
            zaplacono.Color = Colors.Green;
            zaplacono.Font.Size = 11;

            var kwotaParagraph = totalParagraph.AddFormattedText($"{discountedGross:C}", TextFormat.Bold);
            kwotaParagraph.Color = Colors.Black;
            kwotaParagraph.Font.Size = 11;
        }
        else
        {
            // Jeśli nieopłacone, pokazujemy standardowo "Razem do zapłaty"
            totalParagraph.AddFormattedText($"Razem do zapłaty: {discountedGross:C}", TextFormat.Bold);
        }

        // Signatures
        var signatureTable = section.AddTable();
        signatureTable.Borders.Width = 0.75;
        signatureTable.AddColumn("9.13cm");
        signatureTable.AddColumn("9.13cm");

        var signatureRow = signatureTable.AddRow();

        var leftSignatureCell = signatureRow.Cells[0];
        leftSignatureCell.Borders.Width = 0.75;
        leftSignatureCell.Borders.Color = Colors.Black;
        leftSignatureCell.Shading.Color = Colors.White;
        leftSignatureCell.Format.SpaceAfter = "0.4cm";
        var wystawilParagraph = leftSignatureCell.AddParagraph("Wystawił(a)");
        wystawilParagraph.Format.Alignment = ParagraphAlignment.Center;
        wystawilParagraph.Format.SpaceBefore = "0.2cm";

        var stampParagraph = leftSignatureCell.AddParagraph();
        stampParagraph.Format.Alignment = ParagraphAlignment.Center;
        stampParagraph.Format.SpaceBefore = "0.1cm";

        var stampImage = stampParagraph.AddImage("wwwroot\\cid\\CD.png");
        stampImage.LockAspectRatio = true;
        stampImage.Height = "2.3cm";

        var rightSignatureCell = signatureRow.Cells[1];
        rightSignatureCell.Borders.Width = 0.75;
        rightSignatureCell.Borders.Color = Colors.Black;
        rightSignatureCell.Shading.Color = Colors.White;
        rightSignatureCell.Format.SpaceAfter = "0.4cm";
        var odebralParagraph = rightSignatureCell.AddParagraph("Odebrał(a)");
        odebralParagraph.Format.Alignment = ParagraphAlignment.Center;
        odebralParagraph.Format.SpaceBefore = "0.2cm";


        // Payment Info if not paid
        if (!_invoice.IsPaid)
        {
            section.AddParagraph().AddLineBreak();
            var paymentInfo = section.AddParagraph("Prosimy o dokonanie płatności na poniższy rachunek bankowy w terminie 7 dni:");
            paymentInfo.Style = "Bold";
            paymentInfo.Format.SpaceBefore = "1cm";

            section.AddParagraph("PKO Bank Polski");
            section.AddParagraph("Nr konta: PL88 1020 1664 0000 3302 0645 9798");
            section.AddParagraph($"Tytuł płatności: {_invoice.InvoiceNumber}");
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

        var logoCell = headerRow.Cells[0];
        var image = logoCell.AddImage("wwwroot\\cid\\PriceSafari.png");
        image.LockAspectRatio = true;
        image.Height = "0.6cm";
        image.RelativeVertical = RelativeVertical.Line;
        image.RelativeHorizontal = RelativeHorizontal.Margin;
        image.Top = ShapePosition.Top;
        image.Left = ShapePosition.Left;
        logoCell.VerticalAlignment = VerticalAlignment.Top;

        headerRow.Cells[1].AddParagraph("");

        var lineParagraph = section.AddParagraph();
        lineParagraph.Format.SpaceBefore = "0.5cm";
        lineParagraph.Format.Borders.Bottom.Width = 0.75;
        lineParagraph.Format.Borders.Bottom.Color = Colors.Black;
        lineParagraph.Format.Borders.Bottom.Style = BorderStyle.Single;
        lineParagraph.Format.SpaceAfter = "0.5cm";
    }
}
