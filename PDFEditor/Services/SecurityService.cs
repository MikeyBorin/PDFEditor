using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Security;

namespace PDFEditor.Services;

public class SecurityService
{
    /// <summary>Sets a user password (required to open) and/or owner password (required to change security).</summary>
    public byte[] Protect(byte[] pdfBytes, string? userPassword, string? ownerPassword,
                          bool permitPrint = true, bool permitCopy = true,
                          bool permitAnnotations = true, bool permitModify = true)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var s = doc.SecuritySettings;
        s.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
        if (!string.IsNullOrEmpty(userPassword)) s.UserPassword = userPassword;
        if (!string.IsNullOrEmpty(ownerPassword)) s.OwnerPassword = ownerPassword;
        s.PermitPrint = permitPrint;
        s.PermitExtractContent = permitCopy;
        s.PermitAnnotations = permitAnnotations;
        s.PermitModifyDocument = permitModify;

        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Removes encryption. Requires the current owner password if the file is protected.</summary>
    public byte[] RemoveProtection(byte[] pdfBytes, string? ownerPassword = null)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = ownerPassword is null
            ? PdfReader.Open(input, PdfDocumentOpenMode.Modify)
            : PdfReader.Open(input, ownerPassword, PdfDocumentOpenMode.Modify);
        doc.SecuritySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.None;
        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Strips common metadata fields (Title/Author/Subject/Keywords/Creator/Producer) from Info dict.</summary>
    public byte[] SanitizeMetadata(byte[] pdfBytes)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        doc.Info.Title = "";
        doc.Info.Author = "";
        doc.Info.Subject = "";
        doc.Info.Keywords = "";
        doc.Info.Creator = "";
        // Producer is often set by the writer; we can wipe it too.
        try { doc.Info.Elements.Remove("/Producer"); } catch { }
        try { doc.Info.Elements.Remove("/CreationDate"); } catch { }
        try { doc.Info.Elements.Remove("/ModDate"); } catch { }

        // Best-effort XMP wipe (Catalog /Metadata stream).
        try { doc.Internals.Catalog.Elements.Remove("/Metadata"); } catch { }

        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }

    public record Metadata(string? Title, string? Author, string? Subject, string? Keywords, string? Creator, string? Producer);

    public Metadata ReadMetadata(byte[] pdfBytes)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.InformationOnly);
        return new Metadata(doc.Info.Title, doc.Info.Author, doc.Info.Subject, doc.Info.Keywords, doc.Info.Creator,
            doc.Info.Elements.GetString("/Producer"));
    }
}
