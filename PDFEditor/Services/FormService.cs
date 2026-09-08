using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

public enum FormFieldKind { Text, Checkbox, Other }

public record FormField(string Name, string? Value, string TypeName, FormFieldKind Kind = FormFieldKind.Other);

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
            if (f == null) continue;
            // Type detection is done from the raw dict, not just PdfSharpCore's
            // typed class, because a legacy field that lacks /FT still gets
            // wrapped as PdfGenericField even when its /V ('/Yes' / '/Off') and
            // its widget subtype clearly identify it as a checkbox.
            var ft = f.Elements.GetName("/FT");
            var ff = f.Elements.ContainsKey("/Ff") ? f.Elements.GetInteger("/Ff") : 0;
            bool isBtn = ft == "/Btn";
            bool isRadio = (ff & (1 << 15)) != 0;
            bool isPushButton = (ff & (1 << 16)) != 0;
            bool looksLikeCheckboxState = LooksLikeCheckboxStateValue(f);
            var kind = FormFieldKind.Other;
            if (ft == "/Tx")
                kind = FormFieldKind.Text;
            else if ((isBtn && !isRadio && !isPushButton) || looksLikeCheckboxState)
                kind = FormFieldKind.Checkbox;

            // Prefer the typed field's Value; fall back to the raw /V name for
            // /Off / /Yes-style checkbox values.
            var value = f.Value?.ToString();
            if (string.IsNullOrEmpty(value) && f.Elements.ContainsKey("/V"))
                value = f.Elements["/V"]?.ToString();

            list.Add(new FormField(name, value, f.GetType().Name, kind));
        }
        return list;
    }

    private static bool LooksLikeCheckboxStateValue(PdfSharpCore.Pdf.AcroForms.PdfAcroField f)
    {
        if (!f.Elements.ContainsKey("/V")) return false;
        var v = f.Elements["/V"]?.ToString() ?? "";
        // /Off, /Yes, /On plus the leading-slash raw forms.
        return v == "/Off" || v == "/Yes" || v == "/On"
            || v.Equals("Off", System.StringComparison.OrdinalIgnoreCase)
            || v.Equals("Yes", System.StringComparison.OrdinalIgnoreCase)
            || v.Equals("On",  System.StringComparison.OrdinalIgnoreCase);
    }

    public record CheckAllResult(byte[] Bytes, int TotalCheckboxes, int Changed);

    /// <summary>Sets every AcroForm checkbox in the document to Checked=true.</summary>
    public CheckAllResult CheckAllBoxes(byte[] pdfBytes) => SetAllCheckboxes(pdfBytes, checkedState: true);

    /// <summary>Sets every AcroForm checkbox in the document to Checked=false.</summary>
    public CheckAllResult UncheckAllBoxes(byte[] pdfBytes) => SetAllCheckboxes(pdfBytes, checkedState: false);

    /// <summary>Shared implementation for Check All / Uncheck All. Operates on
    /// raw dicts so legacy fields written without /FT are handled the same as
    /// typed PdfCheckBoxField instances. Reads each field's actual "on" state
    /// name from /AP /N so PDFs authored with export values other than "/Yes"
    /// still work.</summary>
    private CheckAllResult SetAllCheckboxes(byte[] pdfBytes, bool checkedState)
    {
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        var form = doc.AcroForm;
        if (form == null) return new CheckAllResult(pdfBytes, 0, 0);

        if (!form.Elements.ContainsKey("/NeedAppearances"))
            form.Elements.Add("/NeedAppearances", new PdfBoolean(true));
        else
            form.Elements["/NeedAppearances"] = new PdfBoolean(true);

        int total = 0, changed = 0;
        foreach (var name in form.Fields.Names)
        {
            var field = form.Fields[name];
            if (field == null) continue;
            var ft = field.Elements.GetName("/FT");
            var ff = field.Elements.ContainsKey("/Ff") ? field.Elements.GetInteger("/Ff") : 0;
            bool isBtnCheckbox =
                (ft == "/Btn" && (ff & (1 << 15)) == 0 && (ff & (1 << 16)) == 0)
                || (string.IsNullOrEmpty(ft) && LooksLikeCheckboxStateValue(field));
            if (!isBtnCheckbox) continue;

            total++;
            var onName = FindOnStateName(field) ?? "/Yes";
            var targetState = checkedState ? onName : "/Off";
            var currentV = field.Elements.ContainsKey("/V") ? field.Elements["/V"]?.ToString() : null;
            if (currentV == targetState) continue;

            if (string.IsNullOrEmpty(ft)) field.Elements.SetName("/FT", "/Btn");
            field.Elements.SetName("/V",  targetState);
            field.Elements.SetName("/AS", targetState);
            changed++;
        }
        if (total == 0) return new CheckAllResult(pdfBytes, 0, 0);
        using var output = new MemoryStream();
        doc.Save(output, false);
        return new CheckAllResult(output.ToArray(), total, changed);
    }

    /// <summary>Returns the side length (max(w,h) in PDF points) of every
    /// checkbox widget in the document. Filters to /FT /Btn widgets that are
    /// not radio buttons or pushbuttons.</summary>
    public double[] GetCheckboxSizes(byte[] pdfBytes)
    {
        var sizes = new List<double>();
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        for (int p = 0; p < doc.PageCount; p++)
        {
            var page = doc.Pages[p];
            if (!page.Elements.ContainsKey("/Annots")) continue;
            var annots = page.Elements.GetArray("/Annots");
            if (annots == null) continue;
            foreach (var el in annots.Elements)
            {
                var dict = ResolveDict(el);
                if (dict == null) continue;
                if (dict.Elements.GetName("/Subtype") != "/Widget") continue;
                if (dict.Elements.GetName("/FT") != "/Btn") continue;
                // Bit 15 = Radio, bit 16 = Pushbutton (both 1-indexed per PDF spec,
                // so masks 1 << 14 and 1 << 16 in 0-indexed terms).
                var ff = dict.Elements.ContainsKey("/Ff") ? dict.Elements.GetInteger("/Ff") : 0;
                bool isRadio     = (ff & (1 << 15)) != 0;
                bool isPushButton = (ff & (1 << 16)) != 0;
                if (isRadio || isPushButton) continue;
                var rect = dict.Elements.GetRectangle("/Rect");
                var w = System.Math.Abs(rect.X2 - rect.X1);
                var h = System.Math.Abs(rect.Y2 - rect.Y1);
                sizes.Add(System.Math.Max(w, h));
            }
        }
        return sizes.ToArray();
    }

    public record NormalizeCheckboxesResult(byte[] Bytes, int Total, int Changed, double SidePoints);

    /// <summary>Resizes every checkbox widget in the PDF to `sidePoints` × `sidePoints`,
    /// anchored at the widget's current top-left (in PDF coords, that's the same x1
    /// and the same y2 — width extends right, height extends downward from the top).</summary>
    public NormalizeCheckboxesResult SetAllCheckboxSizes(byte[] pdfBytes, double sidePoints)
    {
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        int total = 0, changed = 0;
        for (int p = 0; p < doc.PageCount; p++)
        {
            var page = doc.Pages[p];
            if (!page.Elements.ContainsKey("/Annots")) continue;
            var annots = page.Elements.GetArray("/Annots");
            if (annots == null) continue;
            foreach (var el in annots.Elements)
            {
                var dict = ResolveDict(el);
                if (dict == null) continue;
                if (dict.Elements.GetName("/Subtype") != "/Widget") continue;
                if (dict.Elements.GetName("/FT") != "/Btn") continue;
                var ff = dict.Elements.ContainsKey("/Ff") ? dict.Elements.GetInteger("/Ff") : 0;
                if ((ff & (1 << 15)) != 0 || (ff & (1 << 16)) != 0) continue;
                total++;
                var rect = dict.Elements.GetRectangle("/Rect");
                var currW = System.Math.Abs(rect.X2 - rect.X1);
                var currH = System.Math.Abs(rect.Y2 - rect.Y1);
                if (System.Math.Abs(currW - sidePoints) < 0.5 && System.Math.Abs(currH - sidePoints) < 0.5) continue;
                // Anchor top-left: (x1, y2) stays; y1 = y2 - size, x2 = x1 + size.
                var x1 = System.Math.Min(rect.X1, rect.X2);
                var y2 = System.Math.Max(rect.Y1, rect.Y2);
                var x2 = x1 + sidePoints;
                var y1 = y2 - sidePoints;
                dict.Elements["/Rect"] = new PdfSharpCore.Pdf.PdfArray(doc,
                    new PdfSharpCore.Pdf.PdfReal(x1), new PdfSharpCore.Pdf.PdfReal(y1),
                    new PdfSharpCore.Pdf.PdfReal(x2), new PdfSharpCore.Pdf.PdfReal(y2));
                changed++;
            }
        }
        if (changed == 0) return new NormalizeCheckboxesResult(pdfBytes, total, 0, sidePoints);
        using var output = new MemoryStream();
        doc.Save(output, false);
        return new NormalizeCheckboxesResult(output.ToArray(), total, changed, sidePoints);
    }

    private static PdfSharpCore.Pdf.PdfDictionary? ResolveDict(PdfSharpCore.Pdf.PdfItem item)
        => item as PdfSharpCore.Pdf.PdfDictionary
           ?? (item as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfDictionary;

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
                if (field == null) continue;
                var ft = field.Elements.GetName("/FT");
                var ff = field.Elements.ContainsKey("/Ff") ? field.Elements.GetInteger("/Ff") : 0;
                bool isBtnCheckbox =
                    (ft == "/Btn" && (ff & (1 << 15)) == 0 && (ff & (1 << 16)) == 0)
                    || (string.IsNullOrEmpty(ft) && LooksLikeCheckboxStateValue(field));

                if (field is PdfTextField tf)
                {
                    tf.Value = new PdfString(kv.Value);
                }
                else if (isBtnCheckbox)
                {
                    bool wantChecked = IsCheckedValue(kv.Value);
                    // Discover the field's "on" state name from /AP /N — most PDFs
                    // use /Yes but some form authors pick /1 or the field name.
                    var onName = FindOnStateName(field) ?? "/Yes";
                    var stateName = wantChecked ? onName : "/Off";
                    // Repair legacy fields written before /FT was set — without
                    // this, PdfSharpCore keeps returning them as PdfGenericField.
                    if (string.IsNullOrEmpty(ft))
                        field.Elements.SetName("/FT", "/Btn");
                    field.Elements.SetName("/V",  stateName);
                    field.Elements.SetName("/AS", stateName);
                }
            }
        }
        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }

    public record ToggleResult(byte[] Bytes, bool Changed, string? FieldName, bool NowChecked);

    /// <summary>Finds the topmost AcroForm checkbox widget on the given page
    /// whose /Rect contains the click point (in normalized top-left-origin
    /// page coords) and toggles its /V and /AS. Returns Changed=false when
    /// the click doesn't hit a checkbox — no rebuild needed.</summary>
    public ToggleResult TryToggleCheckboxAt(byte[] pdfBytes, int pageIndex, double xNorm, double yNorm)
    {
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        if (pageIndex < 0 || pageIndex >= doc.PageCount)
            return new ToggleResult(pdfBytes, false, null, false);
        var page = doc.Pages[pageIndex];
        var pageW = page.Width.Point;
        var pageH = page.Height.Point;
        // Click in PDF-space (bottom-left origin).
        var clickX = xNorm * pageW;
        var clickY = pageH - yNorm * pageH;

        if (!page.Elements.ContainsKey("/Annots")) return new ToggleResult(pdfBytes, false, null, false);
        var annots = page.Elements.GetArray("/Annots");
        if (annots == null) return new ToggleResult(pdfBytes, false, null, false);

        // Walk in reverse so the latest-drawn widget wins on overlap.
        for (int i = annots.Elements.Count - 1; i >= 0; i--)
        {
            var d = ResolveDict(annots.Elements[i]);
            if (d == null) continue;
            if (d.Elements.GetName("/Subtype") != "/Widget") continue;
            var ft = d.Elements.GetName("/FT");
            var ff = d.Elements.ContainsKey("/Ff") ? d.Elements.GetInteger("/Ff") : 0;
            bool isBtnCheckbox =
                (ft == "/Btn" && (ff & (1 << 15)) == 0 && (ff & (1 << 16)) == 0)
                || (string.IsNullOrEmpty(ft) && LooksLikeCheckboxStateValueDict(d));
            if (!isBtnCheckbox) continue;
            if (!d.Elements.ContainsKey("/Rect")) continue;
            var rect = d.Elements.GetRectangle("/Rect");
            if (clickX < rect.X1 || clickX > rect.X2) continue;
            if (clickY < rect.Y1 || clickY > rect.Y2) continue;

            // Hit — toggle.
            var onName = FindOnStateNameDict(d) ?? "/Yes";
            var currentV = d.Elements.ContainsKey("/V") ? d.Elements["/V"]?.ToString() : "/Off";
            bool wasChecked = currentV == onName;
            var newState = wasChecked ? "/Off" : onName;
            if (string.IsNullOrEmpty(ft)) d.Elements.SetName("/FT", "/Btn");
            d.Elements.SetName("/V",  newState);
            d.Elements.SetName("/AS", newState);
            var fieldName = d.Elements.ContainsKey("/T") ? d.Elements.GetString("/T") : "(unnamed)";
            if (doc.AcroForm != null)
                doc.AcroForm.Elements["/NeedAppearances"] = new PdfBoolean(true);
            using var output = new MemoryStream();
            doc.Save(output, false);
            return new ToggleResult(output.ToArray(), true, fieldName, !wasChecked);
        }
        return new ToggleResult(pdfBytes, false, null, false);
    }

    private static bool LooksLikeCheckboxStateValueDict(PdfSharpCore.Pdf.PdfDictionary d)
    {
        if (!d.Elements.ContainsKey("/V")) return false;
        var v = d.Elements["/V"]?.ToString() ?? "";
        return v == "/Off" || v == "/Yes" || v == "/On"
            || v.Equals("Off", System.StringComparison.OrdinalIgnoreCase)
            || v.Equals("Yes", System.StringComparison.OrdinalIgnoreCase)
            || v.Equals("On",  System.StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindOnStateNameDict(PdfSharpCore.Pdf.PdfDictionary d)
    {
        if (!d.Elements.ContainsKey("/AP")) return null;
        var ap = ResolveDict(d.Elements["/AP"]);
        if (ap is null || !ap.Elements.ContainsKey("/N")) return null;
        var n = ResolveDict(ap.Elements["/N"]);
        if (n is null) return null;
        foreach (var key in n.Elements.Keys)
            if (key != "/Off") return key;
        return null;
    }

    private static bool IsCheckedValue(string v)
    {
        if (string.IsNullOrEmpty(v)) return false;
        var s = v.Trim().TrimStart('/').ToLowerInvariant();
        return s == "true" || s == "yes" || s == "on" || s == "1" || s == "checked";
    }

    private static string? FindOnStateName(PdfSharpCore.Pdf.AcroForms.PdfAcroField f)
    {
        if (!f.Elements.ContainsKey("/AP")) return null;
        var apItem = f.Elements["/AP"];
        var ap = (apItem as PdfSharpCore.Pdf.PdfDictionary)
              ?? (apItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfDictionary;
        if (ap is null || !ap.Elements.ContainsKey("/N")) return null;
        var nItem = ap.Elements["/N"];
        var n = (nItem as PdfSharpCore.Pdf.PdfDictionary)
             ?? (nItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfDictionary;
        if (n is null) return null;
        foreach (var key in n.Elements.Keys)
        {
            if (key != "/Off") return key; // whichever isn't Off is the "on" state
        }
        return null;
    }
}
