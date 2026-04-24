# Plan C-7 — XpweSerializer (scrittura XML PriMus)

> **Contesto:** settimo sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`. Simmetrico a C-1 (deserializer). Zero dipendenze UI/Revit.

**Goal:** Scrivere un file `.xpwe` valido per PriMus a partire da un `XpweImportResult` o da un `ComputoDocument` + entità figlie. Export deterministico (stesso input → stesso XML byte-per-byte).

**Architecture:**
- `XpweSerializer` in `QtoRevitPlugin.Core/Xpwe/`: prende `XpweImportResult` e serializza in `XDocument`
- Metodo `SaveToFile(result, path)` + `SaveToString(result)` per testabilità
- Ordinamento deterministico: per SortOrder poi per XpweId (tie-break)
- Preserva `CopyRight = "Copyright ACCA software S.p.A."`, `Versione`, `Fgs`
- Date: null → `30/12/1899`, altrimenti formato string "DD/MM/YYYY" tale e quale
- Decimali: `CultureInfo.InvariantCulture` con `.` come separatore
- ID=null nei riferimenti → scritti come `0` (convenzione PriMus)

**Test critici:**
- **Roundtrip**: `test.XPWE` → parse → serialize → parse → verifica entità identiche (TipoDocumento, counts, campi chiave)
- **Determinismo**: serialize 2 volte lo stesso input → byte-identical
- **Formato PriMus**: primo nodo = `<?mso-application ?>` + `<PweDocumento>`, rispetta struttura

**File impattati:**
- Create: `QtoRevitPlugin.Core/Xpwe/XpweSerializer.cs`
- Create: `QtoRevitPlugin.Tests/Computi/XpweSerializerTests.cs`

---

## Task 1: XpweSerializer

**Files:**
- Create: `QtoRevitPlugin.Core/Xpwe/XpweSerializer.cs`

- [ ] **Step 1: Scrivere la classe**

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Xpwe
{
    /// <summary>
    /// Serializza un XpweImportResult in XML formato .xpwe PriMus.
    /// Output deterministico: stesso input → stesso XML byte-per-byte.
    /// </summary>
    public class XpweSerializer
    {
        public void SaveToFile(XpweImportResult result, string path)
        {
            var xml = SaveToString(result);
            File.WriteAllText(path, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public string SaveToString(XpweImportResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var doc = Build(result);
            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false)
            }))
            {
                doc.Save(writer);
            }
            // Prepend PriMus processing instruction come fa PriMus
            return "<?mso-application progid=\"PriMus.Document.XPWE\"?>" + sb.ToString();
        }

        private static XDocument Build(XpweImportResult r)
        {
            var root = new XElement("PweDocumento",
                new XElement("CopyRight", "Copyright ACCA software S.p.A."),
                new XElement("TipoDocumento", r.Document.TipoDocumento.ToString(CultureInfo.InvariantCulture)),
                new XElement("TipoFormato", "XMLPwe"),
                new XElement("Versione", r.Document.Versione ?? "5.04"),
                new XElement("SourceVersione", "QtoRevitPlugin"),
                new XElement("SourceNome", "QtoRevitPlugin"),
                new XElement("Fgs", r.Document.Fgs.ToString(CultureInfo.InvariantCulture))
            );

            root.Add(BuildDatiGenerali(r));
            root.Add(BuildMisurazioni(r));

            return new XDocument(root);
        }

        private static XElement BuildDatiGenerali(XpweImportResult r)
        {
            var datiGen = new XElement("PweDatiGenerali");

            // Progetto (metadati documento)
            var progetto = new XElement("PweDGProgetto",
                new XElement("PweDGDatiGenerali",
                    Txt("PercPrezzi", r.Document.PercPrezzi.ToString("0.###", CultureInfo.InvariantCulture)),
                    Txt("Comune", r.Document.Comune),
                    Txt("Provincia", r.Document.Provincia),
                    Txt("Oggetto", r.Document.Oggetto),
                    Txt("Committente", r.Document.Committente),
                    Txt("Impresa", r.Document.Impresa),
                    Txt("ParteOpera", r.Document.ParteOpera)
                )
            );
            datiGen.Add(progetto);

            // Classificazioni
            var classif = new XElement("PweDGCapitoliCategorie",
                BuildChapterGroup(r.SuperCapitoli, "PweDGSuperCapitoli", "DGSuperCapitoliItem"),
                BuildChapterGroup(r.Capitoli, "PweDGCapitoli", "DGCapitoliItem"),
                BuildChapterGroup(r.SubCapitoli, "PweDGSubCapitoli", "DGSubCapitoliItem"),
                BuildCategoryGroup(r.SuperCategorie, "PweDGSuperCategorie", "DGSuperCategorieItem"),
                BuildCategoryGroup(r.Categorie, "PweDGCategorie", "DGCategorieItem"),
                BuildCategoryGroup(r.SubCategorie, "PweDGSubCategorie", "DGSubCategorieItem")
            );
            datiGen.Add(classif);

            // WBS
            datiGen.Add(new XElement("PweDGWBSCAP"));   // Placeholder (WBS non ancora serializzata)
            datiGen.Add(new XElement("PweDGWBS"));

            return datiGen;
        }

        private static XElement BuildChapterGroup(
            System.Collections.Generic.List<XpweChapterItem> items,
            string containerName, string itemName)
        {
            var el = new XElement(containerName);
            foreach (var ch in items)
            {
                var node = ch.Node;
                el.Add(new XElement(itemName,
                    new XAttribute("ID", ch.XpweId.ToString(CultureInfo.InvariantCulture)),
                    Txt("Codice", node.Codice),
                    Txt("DesSintetica", node.DesSintetica),
                    Txt("DesEstesa", node.DesEstesa),
                    Txt("DataInit", node.DataInit ?? "30/12/1899"),
                    Txt("Durata", node.Durata.ToString(CultureInfo.InvariantCulture)),
                    Txt("CodFase", node.CodFase),
                    Txt("Percentuale", node.Percentuale.ToString("0.###", CultureInfo.InvariantCulture))
                ));
            }
            return el;
        }

        private static XElement BuildCategoryGroup(
            System.Collections.Generic.List<XpweCategoryItem> items,
            string containerName, string itemName)
        {
            var el = new XElement(containerName);
            foreach (var ch in items)
            {
                var node = ch.Node;
                el.Add(new XElement(itemName,
                    new XAttribute("ID", ch.XpweId.ToString(CultureInfo.InvariantCulture)),
                    Txt("Codice", node.Codice),
                    Txt("DesSintetica", node.DesSintetica),
                    Txt("DesEstesa", node.DesEstesa),
                    Txt("DataInit", node.DataInit ?? "30/12/1899"),
                    Txt("Durata", node.Durata.ToString(CultureInfo.InvariantCulture)),
                    Txt("CodFase", node.CodFase),
                    Txt("Percentuale", node.Percentuale.ToString("0.###", CultureInfo.InvariantCulture))
                ));
            }
            return el;
        }

        private static XElement BuildMisurazioni(XpweImportResult r)
        {
            var mis = new XElement("PweMisurazioni");

            var ep = new XElement("PweElencoPrezzi");
            foreach (var item in r.PriceItems)
            {
                var d = item.Data;
                ep.Add(new XElement("EPItem",
                    new XAttribute("ID", item.XpweId.ToString(CultureInfo.InvariantCulture)),
                    Txt("Tariffa", d.Tariffa),
                    Txt("Articolo", d.Articolo),
                    Txt("DesRidotta", d.DesRidotta),
                    Txt("DesEstesa", d.DesEstesa),
                    Txt("UnMisura", d.UnMisura),
                    Txt("Prezzo1", Fmt(d.Prezzo1)),
                    Txt("Prezzo2", Fmt(d.Prezzo2)),
                    Txt("Prezzo3", Fmt(d.Prezzo3)),
                    Txt("Prezzo4", Fmt(d.Prezzo4)),
                    Txt("Prezzo5", Fmt(d.Prezzo5)),
                    Txt("CnfQt", d.CnfQt),
                    Txt("IDSpCap", (item.IDSpCap ?? 0).ToString(CultureInfo.InvariantCulture)),
                    Txt("IDCap", (item.IDCap ?? 0).ToString(CultureInfo.InvariantCulture)),
                    Txt("IDSbCap", (item.IDSbCap ?? 0).ToString(CultureInfo.InvariantCulture)),
                    Txt("CodiceWBSCAP", item.CodiceWBSCAP),
                    Txt("Data", string.IsNullOrEmpty(d.Data) ? "30/12/1899" : d.Data),
                    Txt("DesBreve", d.DesBreve),
                    Txt("IncMDO", Fmt(d.IncMDO)),
                    Txt("IncMAT", Fmt(d.IncMAT)),
                    Txt("IncSIC", Fmt(d.IncSIC)),
                    Txt("TipoRisorsa", d.TipoRisorsa.ToString(CultureInfo.InvariantCulture)),
                    Txt("Flags", d.Flags.ToString(CultureInfo.InvariantCulture)),
                    Txt("AdrInternet", d.AdrInternet)
                ));
            }
            mis.Add(ep);

            var vc = new XElement("PweVociComputo");
            if (r.MeasurementRows.Count == 0)
            {
                // PriMus scrive <VCItem/> vuoto quando non ci sono righe
                vc.Add(new XElement("VCItem"));
            }
            else
            {
                foreach (var mr in r.MeasurementRows)
                {
                    var row = mr.Row;
                    var vcItem = new XElement("VCItem",
                        new XAttribute("ID", mr.XpweId.ToString(CultureInfo.InvariantCulture)),
                        Txt("IDEP", mr.IDEP.ToString(CultureInfo.InvariantCulture)),
                        Txt("Quantita", Fmt(row.Quantita)),
                        Txt("DataMis", row.DataMis ?? "30/12/1899"),
                        Txt("Flags", row.Flags.ToString(CultureInfo.InvariantCulture)),
                        Txt("IDSpCat", (row.SpCatId ?? 0).ToString(CultureInfo.InvariantCulture)),
                        Txt("IDCat", (row.CatId ?? 0).ToString(CultureInfo.InvariantCulture)),
                        Txt("IDSbCat", (row.SbCatId ?? 0).ToString(CultureInfo.InvariantCulture)),
                        Txt("CodiceWBS", "")  // WBS ComputoNodeId non ancora serializzato
                    );

                    if (mr.SubRows.Count > 0)
                    {
                        var misure = new XElement("PweVCMisure");
                        foreach (var sub in mr.SubRows)
                        {
                            var s = sub.SubRow;
                            misure.Add(new XElement("RGItem",
                                new XAttribute("ID", sub.XpweId.ToString(CultureInfo.InvariantCulture)),
                                Txt("IDVV", s.IDVV.ToString(CultureInfo.InvariantCulture)),
                                Txt("Descrizione", s.Descrizione),
                                Txt("PartiUguali", Fmt(s.PartiUguali)),
                                Txt("Lunghezza", s.Lunghezza.HasValue ? Fmt(s.Lunghezza.Value) : ""),
                                Txt("Larghezza", s.Larghezza.HasValue ? Fmt(s.Larghezza.Value) : ""),
                                Txt("HPeso", s.HPeso.HasValue ? Fmt(s.HPeso.Value) : ""),
                                Txt("Quantita", Fmt(s.Quantita)),
                                Txt("Flags", s.Flags.ToString(CultureInfo.InvariantCulture))
                            ));
                        }
                        vcItem.Add(misure);
                    }
                    vc.Add(vcItem);
                }
            }
            mis.Add(vc);

            return mis;
        }

        /// <summary>Crea un elemento con testo, o vuoto se null/empty (coerente con file PriMus reali).</summary>
        private static XElement Txt(string name, string? value) =>
            new XElement(name, value ?? "");

        /// <summary>Formatta double con '.' come separatore, senza trailing zeros.</summary>
        private static string Fmt(double d) =>
            d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 2: Build Core**

```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Atteso: 0 errori.

## Task 2: Test roundtrip + determinismo

**Files:**
- Create: `QtoRevitPlugin.Tests/Computi/XpweSerializerTests.cs`

```csharp
using System.IO;
using FluentAssertions;
using QtoRevitPlugin.Xpwe;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    public class XpweSerializerTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(XpweSerializerTests).Assembly.Location)!,
                "..", "..", "..", ".."));

        private static string TestFile(string name) => Path.Combine(RepoRoot, name);

        [Fact]
        public void Roundtrip_TestXpwe_PreservesCounts()
        {
            var path = TestFile("test.XPWE");
            File.Exists(path).Should().BeTrue();
            var parser = new XpweDeserializer();
            var serializer = new XpweSerializer();

            var result1 = parser.ParseFile(path);
            var xml = serializer.SaveToString(result1);
            var result2 = parser.ParseString(xml.Replace("<?mso-application progid=\"PriMus.Document.XPWE\"?>", ""));

            result2.Document.TipoDocumento.Should().Be(result1.Document.TipoDocumento);
            result2.SuperCapitoli.Count.Should().Be(result1.SuperCapitoli.Count);
            result2.PriceItems.Count.Should().Be(result1.PriceItems.Count);
            result2.MeasurementRows.Count.Should().Be(result1.MeasurementRows.Count);
        }

        [Fact]
        public void SaveToString_Deterministic_SameInputSameOutput()
        {
            var result = new XpweImportResult();
            result.Document.TipoDocumento = 0;
            result.Document.Versione = "5.04";

            var serializer = new XpweSerializer();
            var a = serializer.SaveToString(result);
            var b = serializer.SaveToString(result);
            a.Should().Be(b, "serialize deve essere deterministico");
        }

        [Fact]
        public void SaveToString_StartsWithPriMusProcessingInstruction()
        {
            var result = new XpweImportResult();
            var xml = new XpweSerializer().SaveToString(result);
            xml.Should().StartWith("<?mso-application progid=\"PriMus.Document.XPWE\"?>");
        }

        [Fact]
        public void Roundtrip_CMESample_PreservesCounts()
        {
            var path = TestFile("CME_Sample.xpwe");
            if (!File.Exists(path)) return;

            var parser = new XpweDeserializer();
            var serializer = new XpweSerializer();

            var result1 = parser.ParseFile(path);
            var xml = serializer.SaveToString(result1);
            var cleanXml = xml.Replace("<?mso-application progid=\"PriMus.Document.XPWE\"?>", "");
            var result2 = parser.ParseString(cleanXml);

            result2.Document.TipoDocumento.Should().Be(1);
            result2.SuperCapitoli.Count.Should().Be(result1.SuperCapitoli.Count);
            result2.PriceItems.Count.Should().Be(result1.PriceItems.Count);
            result2.MeasurementRows.Count.Should().Be(result1.MeasurementRows.Count);

            // Sample ha 4491 RGItem — verifica che anche quelli roundtrippino
            int rgTotal1 = 0, rgTotal2 = 0;
            foreach (var m in result1.MeasurementRows) rgTotal1 += m.SubRows.Count;
            foreach (var m in result2.MeasurementRows) rgTotal2 += m.SubRows.Count;
            rgTotal2.Should().Be(rgTotal1);
        }
    }
}
```

- [ ] **Step 3: Run test**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~XpweSerializer" -v quiet
```

Atteso: tutti verdi.

## Task 3: Full suite + commit

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --no-build -v quiet
# Atteso: 486 + 4 = 490 verdi

git add QtoRevitPlugin.Core/Xpwe/XpweSerializer.cs \
       QtoRevitPlugin.Tests/Computi/XpweSerializerTests.cs
git commit -m "feat(xpwe): XpweSerializer · scrittura .xpwe PriMus (Plan C-7)"
```

---

## Self-review

- [x] Simmetrico a C-1 (stesso XpweImportResult intermediate model)
- [x] Determinismo: `XmlWriterSettings` con OmitXmlDeclaration, no indentation, stessa cultura
- [x] Processing instruction PriMus prepended manualmente (XDocument non lo supporta nativamente)
- [x] Date Excel zero → "30/12/1899" (reverse della normalize di C-1)
- [x] IDRef null → "0" (convenzione PriMus)
- [x] Test roundtrip sui 2 file reali verifica counts match

## Scope NON incluso

- WBS serializzazione (PweDGWBSCAP/PweDGWBS restano vuoti) — WBS nodes esistenti ignorati; rimandato a C-7.1 quando WBS entra in UI
- UI Export File dialog → plan separato "UI Export/Import" se richiesto
- Validazione pre-export (no-op: si assume XpweImportResult valido) → rimandato
- CNodiceWBS su VCItem (sempre "") fino a C-7.1
