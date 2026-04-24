# Plan C-1 — XpweDeserializer

> **Contesto:** secondo sotto-progetto della spec `2026-04-24-modulo-computi-primus-xpwe-design.md`. Dipende da C-0 (schema DB + modelli).

**Goal:** Leggere un file `.xpwe` PriMus e costruire in memoria un `ComputoDocument` + alberi classificatori + PriceItems + (se TipoDocumento=1) MeasurementRows con SubRows.

**Architecture:** Parser basato su `System.Xml.Linq` (XDocument). Non persiste su DB — ritorna un `XpweImportResult` con le entità popolate. La persistenza è responsabilità del chiamante (plan C-7 Import UI). Mantenuto puro C# in `QtoRevitPlugin.Core/Xpwe/` senza dipendenze Revit/SQLite.

**Tech Stack:** System.Xml.Linq, xUnit per test di parsing sui 2 file reali (`test.XPWE`, `CME_Sample.xpwe`).

**Riferimenti schema** (estratti dai file reali):
- Root: `<PweDocumento>` con `<TipoDocumento>` (0=Prezziario, 1=Computo)
- `PweDatiGenerali`:
  - `PweDGProgetto/PweDGDatiGenerali` (Comune, Provincia, Oggetto, Committente, Impresa, ParteOpera)
  - `PweDGCapitoliCategorie` (6 liste: SuperCapitoli, Capitoli, SubCapitoli, SuperCategorie, Categorie, SubCategorie)
  - `PweDGWBSCAP`, `PweDGWBS`
- `PweMisurazioni`:
  - `PweElencoPrezzi/EPItem@ID` con campi Tariffa/Articolo/DesRidotta/DesEstesa/UnMisura/Prezzo1..5/IDSpCap/IDCap/IDSbCap/CodiceWBSCAP/Data/IncMDO/MAT/SIC/TipoRisorsa/Flags/CnfQt/AdrInternet
  - `PweVociComputo/VCItem@ID` con IDEP/Quantita/DataMis/Flags/IDSpCat/IDCat/IDSbCat/CodiceWBS/PweVCMisure
  - `PweVCMisure/RGItem@ID` con IDVV/Descrizione/PartiUguali/Lunghezza/Larghezza/HPeso/Quantita/Flags

**File impattati:**
- Create: `QtoRevitPlugin.Core/Xpwe/XpweImportResult.cs`
- Create: `QtoRevitPlugin.Core/Xpwe/XpweDeserializer.cs`
- Create: `QtoRevitPlugin.Tests/Computi/XpweDeserializerTests.cs`

---

## Task 1: XpweImportResult (DTO del parser)

**Files:**
- Create: `QtoRevitPlugin.Core/Xpwe/XpweImportResult.cs`

Scopo: contenitore puro di entità parsed. Non contiene l'ID database (ancora zero), ha gli ID XPWE originali per risolvere i riferimenti incrociati (EPItem.IDSpCap → DGSuperCapitoliItem.ID).

- [ ] **Step 1: Creare cartella + file**

- [ ] **Step 2: Scrivere la classe**

```csharp
using System.Collections.Generic;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Xpwe
{
    /// <summary>
    /// Risultato parsing di un file .xpwe. Contiene entità pure (senza Id DB),
    /// con gli Id XPWE originali (<c>Codice</c> o <c>XpweId</c>) per facilitare
    /// il mapping da parte del chiamante.
    /// </summary>
    public class XpweImportResult
    {
        public ComputoDocument Document { get; set; } = new();

        /// <summary>Nodi con XpweId originale per risolvere riferimenti.</summary>
        public List<XpweChapterItem> SuperCapitoli { get; } = new();
        public List<XpweChapterItem> Capitoli { get; } = new();
        public List<XpweChapterItem> SubCapitoli { get; } = new();
        public List<XpweCategoryItem> SuperCategorie { get; } = new();
        public List<XpweCategoryItem> Categorie { get; } = new();
        public List<XpweCategoryItem> SubCategorie { get; } = new();
        public List<XpweWbsItem> WbsCap { get; } = new();
        public List<XpweWbsItem> WbsComputo { get; } = new();

        public List<XpwePriceItem> PriceItems { get; } = new();
        public List<XpweMeasurementItem> MeasurementRows { get; } = new();

        public List<string> Warnings { get; } = new();
    }

    public class XpweChapterItem
    {
        public int XpweId { get; set; }
        public ChapterNode Node { get; set; } = new();
    }

    public class XpweCategoryItem
    {
        public int XpweId { get; set; }
        public CategoryNode Node { get; set; } = new();
    }

    public class XpweWbsItem
    {
        public string Codice { get; set; } = "";  // WBS usa string code per path
        public WbsNode Node { get; set; } = new();
    }

    public class XpwePriceItem
    {
        public int XpweId { get; set; }
        public PriceItemXpwe Data { get; set; } = new();
        public int? IDSpCap { get; set; }
        public int? IDCap { get; set; }
        public int? IDSbCap { get; set; }
        public string? CodiceWBSCAP { get; set; }
    }

    /// <summary>
    /// Rappresentazione intermedia della voce EP XPWE — non ancora salvata come PriceItem
    /// perché la tabella PriceItems richiede PriceListId. Il chiamante decide se creare
    /// una PriceList dedicata per l'import.
    /// </summary>
    public class PriceItemXpwe
    {
        public string Tariffa { get; set; } = "";
        public string Articolo { get; set; } = "";
        public string DesRidotta { get; set; } = "";
        public string DesEstesa { get; set; } = "";
        public string UnMisura { get; set; } = "";
        public double Prezzo1 { get; set; }
        public double Prezzo2 { get; set; }
        public double Prezzo3 { get; set; }
        public double Prezzo4 { get; set; }
        public double Prezzo5 { get; set; }
        public string CnfQt { get; set; } = "";
        public string Data { get; set; } = "";
        public string DesBreve { get; set; } = "";
        public double IncMDO { get; set; }
        public double IncMAT { get; set; }
        public double IncSIC { get; set; }
        public int TipoRisorsa { get; set; }
        public int Flags { get; set; }
        public string AdrInternet { get; set; } = "";
    }

    public class XpweMeasurementItem
    {
        public int XpweId { get; set; }
        public int IDEP { get; set; }
        public MeasurementRow Row { get; set; } = new();
        public List<XpweMeasurementSubItem> SubRows { get; } = new();
    }

    public class XpweMeasurementSubItem
    {
        public int XpweId { get; set; }
        public MeasurementSubRow SubRow { get; set; } = new();
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Atteso: 0 errori.

## Task 2: XpweDeserializer — parser XML

**Files:**
- Create: `QtoRevitPlugin.Core/Xpwe/XpweDeserializer.cs`

- [ ] **Step 1: Scrivere la classe**

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Xpwe
{
    /// <summary>
    /// Legge un file .xpwe (formato PriMus) e costruisce un XpweImportResult.
    /// Tollerante: attributi/elementi mancanti producono Warnings, non eccezioni.
    /// Convenzioni:
    /// - Decimali con '.' (<c>CultureInfo.InvariantCulture</c>)
    /// - Date "DD/MM/YYYY"; "30/12/1899" (Excel zero) = stringa vuota
    /// - ID "0" negli elementi di riferimento = "nessun riferimento" → null
    /// </summary>
    public class XpweDeserializer
    {
        public XpweImportResult ParseFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var doc = XDocument.Load(path);
            return Parse(doc);
        }

        public XpweImportResult ParseString(string xml)
        {
            var doc = XDocument.Parse(xml);
            return Parse(doc);
        }

        private XpweImportResult Parse(XDocument doc)
        {
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "PweDocumento")
                throw new InvalidDataException("Root element non trovato o non è <PweDocumento>");

            var result = new XpweImportResult();

            // Header
            result.Document.TipoDocumento = ToInt(Elem(root, "TipoDocumento"), 0);
            result.Document.Versione = Elem(root, "Versione") ?? "5.04";
            result.Document.Fgs = ToLong(Elem(root, "Fgs"), 2147614720L);
            result.Document.Currency = "EUR";
            result.Document.CreatedAt = DateTime.UtcNow;
            result.Document.UpdatedAt = DateTime.UtcNow;

            // DatiGenerali/Progetto
            var progetto = root.Element("PweDatiGenerali")?
                                .Element("PweDGProgetto")?
                                .Element("PweDGDatiGenerali");
            if (progetto != null)
            {
                result.Document.Comune = Elem(progetto, "Comune");
                result.Document.Provincia = Elem(progetto, "Provincia");
                result.Document.Oggetto = Elem(progetto, "Oggetto");
                result.Document.Committente = Elem(progetto, "Committente");
                result.Document.Impresa = Elem(progetto, "Impresa");
                result.Document.ParteOpera = Elem(progetto, "ParteOpera");
                result.Document.PercPrezzi = ToDouble(Elem(progetto, "PercPrezzi"));
            }

            // Classificatori
            var cc = root.Element("PweDatiGenerali")?.Element("PweDGCapitoliCategorie");
            if (cc != null)
            {
                ParseChapterList(cc.Element("PweDGSuperCapitoli"), "DGSuperCapitoliItem",
                                 "SpCap", result.SuperCapitoli, result.Warnings);
                ParseChapterList(cc.Element("PweDGCapitoli"), "DGCapitoliItem",
                                 "Cap", result.Capitoli, result.Warnings);
                ParseChapterList(cc.Element("PweDGSubCapitoli"), "DGSubCapitoliItem",
                                 "SbCap", result.SubCapitoli, result.Warnings);
                ParseCategoryList(cc.Element("PweDGSuperCategorie"), "DGSuperCategorieItem",
                                  "SpCat", result.SuperCategorie, result.Warnings);
                ParseCategoryList(cc.Element("PweDGCategorie"), "DGCategorieItem",
                                  "Cat", result.Categorie, result.Warnings);
                ParseCategoryList(cc.Element("PweDGSubCategorie"), "DGSubCategorieItem",
                                  "SbCat", result.SubCategorie, result.Warnings);
            }

            // Misurazioni
            var mis = root.Element("PweMisurazioni");
            if (mis != null)
            {
                ParsePriceItems(mis.Element("PweElencoPrezzi"), result);
                ParseMeasurements(mis.Element("PweVociComputo"), result);
            }

            return result;
        }

        private static void ParseChapterList(
            XElement? container, string itemName, string level,
            System.Collections.Generic.List<XpweChapterItem> outList,
            System.Collections.Generic.List<string> warnings)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var el in container.Elements(itemName))
            {
                int id = ToInt(el.Attribute("ID")?.Value, 0);
                if (id == 0) { warnings.Add($"{itemName} senza ID ignorato"); continue; }
                outList.Add(new XpweChapterItem
                {
                    XpweId = id,
                    Node = new ChapterNode
                    {
                        Level = level,
                        Codice = Elem(el, "Codice") ?? "",
                        DesSintetica = Elem(el, "DesSintetica") ?? "",
                        DesEstesa = Elem(el, "DesEstesa"),
                        DataInit = NormalizeDate(Elem(el, "DataInit")),
                        Durata = ToInt(Elem(el, "Durata"), 0),
                        CodFase = Elem(el, "CodFase"),
                        Percentuale = ToDouble(Elem(el, "Percentuale")),
                        SortOrder = sortOrder++,
                        IsActive = true
                    }
                });
            }
        }

        private static void ParseCategoryList(
            XElement? container, string itemName, string level,
            System.Collections.Generic.List<XpweCategoryItem> outList,
            System.Collections.Generic.List<string> warnings)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var el in container.Elements(itemName))
            {
                int id = ToInt(el.Attribute("ID")?.Value, 0);
                if (id == 0) { warnings.Add($"{itemName} senza ID ignorato"); continue; }
                outList.Add(new XpweCategoryItem
                {
                    XpweId = id,
                    Node = new CategoryNode
                    {
                        Level = level,
                        Codice = Elem(el, "Codice") ?? "",
                        DesSintetica = Elem(el, "DesSintetica") ?? "",
                        DesEstesa = Elem(el, "DesEstesa"),
                        DataInit = NormalizeDate(Elem(el, "DataInit")),
                        Durata = ToInt(Elem(el, "Durata"), 0),
                        CodFase = Elem(el, "CodFase"),
                        Percentuale = ToDouble(Elem(el, "Percentuale")),
                        SortOrder = sortOrder++,
                        IsActive = true
                    }
                });
            }
        }

        private static void ParsePriceItems(XElement? container, XpweImportResult result)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var el in container.Elements("EPItem"))
            {
                int id = ToInt(el.Attribute("ID")?.Value, 0);
                if (id == 0) { result.Warnings.Add("EPItem senza ID ignorato"); continue; }
                var pi = new XpwePriceItem
                {
                    XpweId = id,
                    Data = new PriceItemXpwe
                    {
                        Tariffa = Elem(el, "Tariffa") ?? "",
                        Articolo = Elem(el, "Articolo") ?? "",
                        DesRidotta = Elem(el, "DesRidotta") ?? "",
                        DesEstesa = Elem(el, "DesEstesa") ?? "",
                        UnMisura = Elem(el, "UnMisura") ?? "",
                        Prezzo1 = ToDouble(Elem(el, "Prezzo1")),
                        Prezzo2 = ToDouble(Elem(el, "Prezzo2")),
                        Prezzo3 = ToDouble(Elem(el, "Prezzo3")),
                        Prezzo4 = ToDouble(Elem(el, "Prezzo4")),
                        Prezzo5 = ToDouble(Elem(el, "Prezzo5")),
                        CnfQt = Elem(el, "CnfQt") ?? "",
                        Data = NormalizeDate(Elem(el, "Data")) ?? "",
                        DesBreve = Elem(el, "DesBreve") ?? "",
                        IncMDO = ToDouble(Elem(el, "IncMDO")),
                        IncMAT = ToDouble(Elem(el, "IncMAT")),
                        IncSIC = ToDouble(Elem(el, "IncSIC")),
                        TipoRisorsa = ToInt(Elem(el, "TipoRisorsa"), 0),
                        Flags = ToInt(Elem(el, "Flags"), 512),
                        AdrInternet = Elem(el, "AdrInternet") ?? ""
                    },
                    IDSpCap = ToNullableIntRef(Elem(el, "IDSpCap")),
                    IDCap = ToNullableIntRef(Elem(el, "IDCap")),
                    IDSbCap = ToNullableIntRef(Elem(el, "IDSbCap")),
                    CodiceWBSCAP = Elem(el, "CodiceWBSCAP")
                };
                result.PriceItems.Add(pi);
                sortOrder++;
            }
        }

        private static void ParseMeasurements(XElement? container, XpweImportResult result)
        {
            if (container == null) return;
            int sortOrder = 1;
            foreach (var vc in container.Elements("VCItem"))
            {
                int id = ToInt(vc.Attribute("ID")?.Value, 0);
                if (id == 0) continue;
                var item = new XpweMeasurementItem
                {
                    XpweId = id,
                    IDEP = ToInt(Elem(vc, "IDEP"), 0),
                    Row = new MeasurementRow
                    {
                        Quantita = ToDouble(Elem(vc, "Quantita")),
                        DataMis = NormalizeDate(Elem(vc, "DataMis")),
                        Flags = ToInt(Elem(vc, "Flags"), 0),
                        SortOrder = sortOrder++
                    }
                };
                // SubRows
                var mis = vc.Element("PweVCMisure");
                if (mis != null)
                {
                    int subOrder = 1;
                    foreach (var rg in mis.Elements("RGItem"))
                    {
                        int rgId = ToInt(rg.Attribute("ID")?.Value, 0);
                        item.SubRows.Add(new XpweMeasurementSubItem
                        {
                            XpweId = rgId,
                            SubRow = new MeasurementSubRow
                            {
                                IDVV = ToInt(Elem(rg, "IDVV"), 0),
                                Descrizione = Elem(rg, "Descrizione"),
                                PartiUguali = ToDouble(Elem(rg, "PartiUguali"), 1.0),
                                Lunghezza = ToNullableDouble(Elem(rg, "Lunghezza")),
                                Larghezza = ToNullableDouble(Elem(rg, "Larghezza")),
                                HPeso = ToNullableDouble(Elem(rg, "HPeso")),
                                Quantita = ToDouble(Elem(rg, "Quantita")),
                                Flags = ToInt(Elem(rg, "Flags"), 0),
                                SortOrder = subOrder++
                            }
                        });
                    }
                }
                result.MeasurementRows.Add(item);
            }
        }

        // ------------- Helpers -------------

        private static string? Elem(XElement el, string name) =>
            el.Element(name)?.Value?.Trim() is { Length: > 0 } s ? s : null;

        private static int ToInt(string? s, int fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static long ToLong(string? s, long fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static double ToDouble(string? s, double fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static double? ToNullableDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        /// <summary>XPWE usa ID=0 come "nessun riferimento" → null.</summary>
        private static int? ToNullableIntRef(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (!int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return null;
            return v == 0 ? null : v;
        }

        /// <summary>
        /// "30/12/1899" = Excel serial zero = non valorizzato → null.
        /// Altrimenti ritorna la stringa tale e quale (formato DD/MM/YYYY).
        /// </summary>
        private static string? NormalizeDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (s.Trim() == "30/12/1899") return null;
            return s.Trim();
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build QtoRevitPlugin.Core/QtoRevitPlugin.Core.csproj -c Debug -v q
```

Atteso: 0 errori.

## Task 3: Test deserializer sui 2 file reali

**Files:**
- Create: `QtoRevitPlugin.Tests/Computi/XpweDeserializerTests.cs`

- [ ] **Step 1: Scrivere i test**

```csharp
using System.IO;
using FluentAssertions;
using QtoRevitPlugin.Xpwe;
using Xunit;

namespace QtoRevitPlugin.Tests.Computi
{
    public class XpweDeserializerTests
    {
        // Path relativo alla root del repo (dotnet test viene eseguito dalla dir Tests)
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(XpweDeserializerTests).Assembly.Location)!,
                "..", "..", "..", ".."));

        private static string TestFile(string name) => Path.Combine(RepoRoot, name);

        [Fact]
        public void Parse_TestXpwe_IsPrezziario()
        {
            var path = TestFile("test.XPWE");
            File.Exists(path).Should().BeTrue($"file {path} non trovato");
            var parser = new XpweDeserializer();
            var result = parser.ParseFile(path);

            result.Document.TipoDocumento.Should().Be(0, "test.XPWE è un Prezziario");
            result.Document.Versione.Should().Be("5.01");
            result.SuperCapitoli.Should().HaveCount(1);
            result.PriceItems.Should().HaveCount(1);
            result.MeasurementRows.Should().BeEmpty("prezziario non ha voci di computo");
        }

        [Fact]
        public void Parse_CMESample_IsComputo()
        {
            var path = TestFile("CME_Sample.xpwe");
            if (!File.Exists(path))
            {
                // Test opzionale: salta se il file sample non è checked-in
                return;
            }
            var parser = new XpweDeserializer();
            var result = parser.ParseFile(path);

            result.Document.TipoDocumento.Should().Be(1, "CME_Sample è un Computo");
            result.SuperCapitoli.Should().HaveCount(6);
            result.SuperCategorie.Should().HaveCountGreaterThan(3);
            result.PriceItems.Should().HaveCount(119);
            result.MeasurementRows.Should().HaveCount(168);

            // Un VCItem con RGItem
            var withSub = System.Linq.Enumerable.FirstOrDefault(result.MeasurementRows, m => m.SubRows.Count > 0);
            withSub.Should().NotBeNull("almeno un VCItem ha RGItem");
            withSub!.IDEP.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Parse_DateExcelZero_NormalizesToNull()
        {
            var xml = @"<PweDocumento>
                <TipoDocumento>0</TipoDocumento>
                <Versione>5.04</Versione>
                <PweDatiGenerali>
                    <PweDGCapitoliCategorie>
                        <PweDGSuperCapitoli>
                            <DGSuperCapitoliItem ID='1'>
                                <Codice>01</Codice>
                                <DesSintetica>Test</DesSintetica>
                                <DataInit>30/12/1899</DataInit>
                            </DGSuperCapitoliItem>
                        </PweDGSuperCapitoli>
                    </PweDGCapitoliCategorie>
                </PweDatiGenerali>
            </PweDocumento>";
            var result = new XpweDeserializer().ParseString(xml);
            result.SuperCapitoli.Should().HaveCount(1);
            result.SuperCapitoli[0].Node.DataInit.Should().BeNull("30/12/1899 = Excel zero = null");
        }

        [Fact]
        public void Parse_IdZero_ResolvesToNullRef()
        {
            var xml = @"<PweDocumento>
                <TipoDocumento>0</TipoDocumento>
                <PweMisurazioni>
                    <PweElencoPrezzi>
                        <EPItem ID='1'>
                            <Tariffa>T1</Tariffa>
                            <DesRidotta>voce</DesRidotta>
                            <UnMisura>mc</UnMisura>
                            <Prezzo1>100</Prezzo1>
                            <IDSpCap>2</IDSpCap>
                            <IDCap>0</IDCap>
                            <IDSbCap>0</IDSbCap>
                        </EPItem>
                    </PweElencoPrezzi>
                </PweMisurazioni>
            </PweDocumento>";
            var result = new XpweDeserializer().ParseString(xml);
            result.PriceItems.Should().HaveCount(1);
            result.PriceItems[0].IDSpCap.Should().Be(2);
            result.PriceItems[0].IDCap.Should().BeNull("ID=0 → null");
            result.PriceItems[0].IDSbCap.Should().BeNull();
        }
    }
}
```

- [ ] **Step 2: Run test**

```bash
dotnet test QtoRevitPlugin.Tests/QtoRevitPlugin.Tests.csproj --filter "FullyQualifiedName~XpweDeserializer" -v quiet
```

Atteso: tutti e 4 i test verdi.

## Task 4: Commit

```bash
git add QtoRevitPlugin.Core/Xpwe/ QtoRevitPlugin.Tests/Computi/XpweDeserializerTests.cs
git commit -m "feat(xpwe): deserializer PriMus .xpwe → ComputoDocument (Plan C-1)"
```

---

## Self-review

- [x] Parser tollerante: XML incompleto → Warnings, no throw (tranne root mancante)
- [x] Mapping 1:1 con schema estratto dai file reali (TipoDocumento, 6 classificatori, EPItem, VCItem, RGItem)
- [x] Date Excel zero → null (normalizzazione)
- [x] ID=0 nei riferimenti → null (semantica PriMus)
- [x] Test basati su file reali (test.XPWE piccolo + CME_Sample.xpwe grande opzionale)
- [x] Zero dipendenze esterne (solo System.Xml.Linq)

## Scope NON incluso

- Mapping a tabelle DB (C-7 UI import si occuperà di persistere)
- WBS parsing (PweDGWBSCAP / PweDGWBS) — rimandato a C-1.1 quando avremo un file con WBS popolato
- Validazione semantica (riferimenti broken, livelli incoerenti) — rimandato a C-7 ValidationService
- XpweSerializer (inverso) — Plan C-7
