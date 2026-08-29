using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

public record FormField(string Name, string? Value, string TypeName);

public class FormService
{
    public IReadOnlyList<FormField> GetFields(byte[] pdfBytes)
    {
        var list = new List<FormField>();
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        var form = doc.AcroForm;
        if (form == null) return list;

        foreach (var name in form.Fields.Names)
        {
            var f = form.Fields[name];
            list.Add(new FormField(name, f?.Value?.ToString(), f?.GetType().Name ?? "Field"));
        }
        return list;
    }

    public byte[] SetFieldValues(byte[] pdfBytes, IDictionary<string, string> values)
    {
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        var form = doc.AcroForm;
        if (form != null)
        {
            // Ensure form appearances get regenerated so viewers show the values.
            if (!form.Elements.ContainsKey("/NeedAppearances"))
                form.Elements.Add("/NeedAppearances", new PdfBoolean(true));
            else
                form.Elements["/NeedAppearances"] = new PdfBoolean(true);

            foreach (var kv in values)
            {
                if (!form.Fields.Names.Contains(kv.Key)) continue;
                var field = form.Fields[kv.Key];
                if (field is PdfTextField tf)
                    tf.Value = new PdfString(kv.Value);
                else if (field is PdfCheckBoxField cb)
                    cb.Checked = kv.Value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
            }
        }
        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }
}
