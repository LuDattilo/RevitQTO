using System.Collections.Generic;
using QtoRevitPlugin.Models.Computi;

namespace QtoRevitPlugin.Xpwe
{
    /// <summary>
    /// Risultato parsing di un file .xpwe. Contiene entità pure (senza Id DB),
    /// con gli Id XPWE originali per facilitare il mapping da parte del chiamante.
    /// </summary>
    public class XpweImportResult
    {
        public ComputoDocument Document { get; set; } = new ComputoDocument();

        public List<XpweChapterItem> SuperCapitoli { get; } = new List<XpweChapterItem>();
        public List<XpweChapterItem> Capitoli { get; } = new List<XpweChapterItem>();
        public List<XpweChapterItem> SubCapitoli { get; } = new List<XpweChapterItem>();
        public List<XpweCategoryItem> SuperCategorie { get; } = new List<XpweCategoryItem>();
        public List<XpweCategoryItem> Categorie { get; } = new List<XpweCategoryItem>();
        public List<XpweCategoryItem> SubCategorie { get; } = new List<XpweCategoryItem>();
        public List<XpweWbsItem> WbsCap { get; } = new List<XpweWbsItem>();
        public List<XpweWbsItem> WbsComputo { get; } = new List<XpweWbsItem>();

        public List<XpwePriceItem> PriceItems { get; } = new List<XpwePriceItem>();
        public List<XpweMeasurementItem> MeasurementRows { get; } = new List<XpweMeasurementItem>();

        public List<string> Warnings { get; } = new List<string>();
    }

    public class XpweChapterItem
    {
        public int XpweId { get; set; }
        public ChapterNode Node { get; set; } = new ChapterNode();
    }

    public class XpweCategoryItem
    {
        public int XpweId { get; set; }
        public CategoryNode Node { get; set; } = new CategoryNode();
    }

    public class XpweWbsItem
    {
        public string Codice { get; set; } = "";
        public WbsNode Node { get; set; } = new WbsNode();
    }

    public class XpwePriceItem
    {
        public int XpweId { get; set; }
        public PriceItemXpwe Data { get; set; } = new PriceItemXpwe();
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
        public MeasurementRow Row { get; set; } = new MeasurementRow();
        public List<XpweMeasurementSubItem> SubRows { get; } = new List<XpweMeasurementSubItem>();
    }

    public class XpweMeasurementSubItem
    {
        public int XpweId { get; set; }
        public MeasurementSubRow SubRow { get; set; } = new MeasurementSubRow();
    }
}
