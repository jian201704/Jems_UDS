using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace TestCore
{
    /// <summary>
    /// Dependency-free XLSX reader (net48, no Office/COM, no NuGet).
    /// Reads cells by sheet name and A1-style reference. Supports shared strings,
    /// inline strings, numbers, booleans and cached formula values.
    /// </summary>
    public class MiniXlsx : IDisposable
    {
        private readonly ZipArchive _archive;
        private readonly Dictionary<string, string> _sheetTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sharedStrings = new List<string>();

        public MiniXlsx(string filePath)
        {
            // FileShare.ReadWrite: 允许读取正被 Excel 打开的 xlsx（Excel 默认带读共享）。
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _archive = new ZipArchive(stream, ZipArchiveMode.Read);
            LoadWorkbook();
            LoadSharedStrings();
        }

        public void Dispose()
        {
            try { _archive.Dispose(); } catch (Exception) { }
        }

        public List<string> SheetNames
        {
            get { return new List<string>(_sheetTargets.Keys); }
        }

        public bool HasSheet(string sheetName)
        {
            return _sheetTargets.ContainsKey(sheetName);
        }

        /// <summary>Get a cell value (1-based row/col). Returns "" for empty/missing cells.</summary>
        public string GetCell(string sheetName, int row, int col)
        {
            string target;
            if (!_sheetTargets.TryGetValue(sheetName, out target)) return "";
            ZipArchiveEntry entry = _archive.GetEntry(target);
            if (entry == null) return "";

            string cellRef = ColumnName(col) + row;
            using (Stream stream = entry.Open())
            using (var reader = XmlReader.Create(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "c") continue;
                    string refValue = reader.GetAttribute("r");
                    if (!string.Equals(refValue, cellRef, StringComparison.OrdinalIgnoreCase)) continue;
                    string type = reader.GetAttribute("t");
                    return ReadCellValue(reader, type);
                }
            }
            return "";
        }

        /// <summary>Read the full sheet as rows of column values (1-based rows, sparse -> "" filled).</summary>
        public List<List<string>> ReadSheet(string sheetName)
        {
            var rows = new List<List<string>>();
            string target;
            if (!_sheetTargets.TryGetValue(sheetName, out target)) return rows;
            ZipArchiveEntry entry = _archive.GetEntry(target);
            if (entry == null) return rows;

            using (Stream stream = entry.Open())
            using (var reader = XmlReader.Create(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "c") continue;
                    string refValue = reader.GetAttribute("r");
                    int row;
                    int col;
                    if (!TryParseCellRef(refValue, out row, out col)) continue;
                    while (rows.Count < row) rows.Add(new List<string>());
                    var cols = rows[row - 1];
                    while (cols.Count < col) cols.Add("");
                    string type = reader.GetAttribute("t");
                    cols[col - 1] = ReadCellValue(reader, type);
                }
            }
            return rows;
        }

        // ---- internals ----

        private void LoadWorkbook()
        {
            ZipArchiveEntry wb = _archive.GetEntry("xl/workbook.xml");
            if (wb == null) return;
            using (Stream stream = wb.Open())
            using (var reader = XmlReader.Create(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "sheet") continue;
                    string name = reader.GetAttribute("name");
                    string rid = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    if (rid == null) rid = reader.GetAttribute("r:id");
                    if (name != null && rid != null) _sheetTargets[name] = ResolveTarget(rid);
                }
            }
        }

        private string ResolveTarget(string rId)
        {
            ZipArchiveEntry rels = _archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (rels == null) return "";
            using (Stream stream = rels.Open())
            using (var reader = XmlReader.Create(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "Relationship") continue;
                    string id = reader.GetAttribute("Id");
                    string target = reader.GetAttribute("Target");
                    if (id == rId && target != null)
                    {
                        return NormalizePath("xl", target);
                    }
                }
            }
            return "";
        }

        private void LoadSharedStrings()
        {
            ZipArchiveEntry ss = _archive.GetEntry("xl/sharedStrings.xml");
            if (ss == null) return;
            using (Stream stream = ss.Open())
            using (var reader = XmlReader.Create(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "si") continue;
                    var sb = new StringBuilder();
                    ReadTextDeep(reader, sb);
                    _sharedStrings.Add(sb.ToString());
                }
            }
        }

        private static void ReadTextDeep(XmlReader reader, StringBuilder sb)
        {
            if (reader.IsEmptyElement) return;
            int depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA)
                    sb.Append(reader.Value);
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    return;
            }
        }

        private string ReadCellValue(XmlReader reader, string type)
        {
            if (reader.IsEmptyElement) return "";
            int depth = reader.Depth;
            var sb = new StringBuilder();
            string inlineString = null;
            string valueText = null;
            string formulaText = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Name == "is") inlineString = "";
                    else if (reader.Name == "v") { valueText = ""; formulaText = ""; }
                    else if (reader.Name == "f") formulaText = "";
                }
                else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA)
                {
                    if (inlineString != null) inlineString += reader.Value;
                    else if (valueText != null) valueText += reader.Value;
                    else if (formulaText != null) formulaText += reader.Value;
                    else sb.Append(reader.Value);
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }
            }

            if (type == "s")
            {
                int index;
                if (valueText != null && int.TryParse(valueText.Trim(), out index) &&
                    index >= 0 && index < _sharedStrings.Count)
                    return _sharedStrings[index];
                return "";
            }
            if (type == "inlineStr" && inlineString != null) return inlineString;
            if (type == "str") return valueText ?? formulaText ?? "";
            if (type == "b") return valueText == "1" ? "TRUE" : "FALSE";
            if (type == "e") return "";
            return valueText ?? "";
        }

        private static string NormalizePath(string baseDir, string target)
        {
            target = target.Replace('\\', '/');
            if (target.StartsWith("/", StringComparison.Ordinal))
                return target.TrimStart('/');
            if (target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                return target;
            return baseDir + "/" + target;
        }

        private static string ColumnName(int col)
        {
            var sb = new StringBuilder();
            while (col > 0)
            {
                int rem = (col - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                col = (col - 1) / 26;
            }
            return sb.ToString();
        }

        private static bool TryParseCellRef(string refValue, out int row, out int col)
        {
            row = 0;
            col = 0;
            if (string.IsNullOrEmpty(refValue)) return false;
            int i = 0;
            while (i < refValue.Length && char.IsLetter(refValue[i])) i++;
            if (i == 0 || i >= refValue.Length) return false;
            string colText = refValue.Substring(0, i);
            string rowText = refValue.Substring(i);
            if (!int.TryParse(rowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out row)) return false;
            col = 0;
            foreach (char ch in colText)
            {
                col = col * 26 + (ch - 'A' + 1);
            }
            return true;
        }
    }
}
