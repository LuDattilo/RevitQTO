using System.Collections.Generic;

namespace QtoRevitPlugin.Models
{
    /// <summary>
    /// Elenco normativo statico dei codici SOA (D.Lgs. 36/2023 All. II.12).
    /// Usato dal DatabaseInitializer al primo creation per seedare la tabella
    /// <c>SoaCategories</c>. I valori sono <i>frozen</i> — un upgrade normativo
    /// richiederà una migration dedicata.
    /// </summary>
    public static class SoaCategorySeed
    {
        public static IReadOnlyList<SoaCategory> All => _all;

        private static readonly SoaCategory[] _all = new[]
        {
            // Opere Generali
            new SoaCategory { Code = "OG 1",  Type = "OG", SortOrder = 1,  Description = "Edifici civili e industriali" },
            new SoaCategory { Code = "OG 2",  Type = "OG", SortOrder = 2,  Description = "Restauro e manutenzione dei beni immobili sottoposti a tutela" },
            new SoaCategory { Code = "OG 3",  Type = "OG", SortOrder = 3,  Description = "Strade, autostrade, ponti, viadotti, ferrovie, metropolitane" },
            new SoaCategory { Code = "OG 4",  Type = "OG", SortOrder = 4,  Description = "Opere d'arte nel sottosuolo" },
            new SoaCategory { Code = "OG 5",  Type = "OG", SortOrder = 5,  Description = "Dighe" },
            new SoaCategory { Code = "OG 6",  Type = "OG", SortOrder = 6,  Description = "Acquedotti, gasdotti, oleodotti, opere di irrigazione ed evacuazione" },
            new SoaCategory { Code = "OG 7",  Type = "OG", SortOrder = 7,  Description = "Opere marittime e lavori di dragaggio" },
            new SoaCategory { Code = "OG 8",  Type = "OG", SortOrder = 8,  Description = "Opere fluviali, di difesa, di sistemazione idraulica e di bonifica" },
            new SoaCategory { Code = "OG 9",  Type = "OG", SortOrder = 9,  Description = "Impianti per la produzione di energia elettrica" },
            new SoaCategory { Code = "OG 10", Type = "OG", SortOrder = 10, Description = "Impianti per la trasformazione alta/media tensione e distribuzione" },
            new SoaCategory { Code = "OG 11", Type = "OG", SortOrder = 11, Description = "Impianti tecnologici" },
            new SoaCategory { Code = "OG 12", Type = "OG", SortOrder = 12, Description = "Opere ed impianti di bonifica e protezione ambientale" },
            new SoaCategory { Code = "OG 13", Type = "OG", SortOrder = 13, Description = "Opere di ingegneria naturalistica" },

            // Opere Specializzate
            new SoaCategory { Code = "OS 1",  Type = "OS", SortOrder = 101, Description = "Lavori in terra" },
            new SoaCategory { Code = "OS 2-A", Type = "OS", SortOrder = 102, Description = "Superfici decorate di beni immobili del patrimonio culturale" },
            new SoaCategory { Code = "OS 2-B", Type = "OS", SortOrder = 103, Description = "Beni culturali mobili di interesse storico, artistico, archeologico" },
            new SoaCategory { Code = "OS 3",  Type = "OS", SortOrder = 104, Description = "Impianti idrico-sanitario, cucine, lavanderie" },
            new SoaCategory { Code = "OS 4",  Type = "OS", SortOrder = 105, Description = "Impianti elettromeccanici trasportatori" },
            new SoaCategory { Code = "OS 5",  Type = "OS", SortOrder = 106, Description = "Impianti pneumatici e antintrusione" },
            new SoaCategory { Code = "OS 6",  Type = "OS", SortOrder = 107, Description = "Finiture di opere generali in materiali lignei, plastici, metallici e vetrosi" },
            new SoaCategory { Code = "OS 7",  Type = "OS", SortOrder = 108, Description = "Finiture di opere generali di natura edile e tecnica" },
            new SoaCategory { Code = "OS 8",  Type = "OS", SortOrder = 109, Description = "Opere di impermeabilizzazione" },
            new SoaCategory { Code = "OS 9",  Type = "OS", SortOrder = 110, Description = "Impianti per la segnaletica luminosa e la sicurezza del traffico" },
            new SoaCategory { Code = "OS 10", Type = "OS", SortOrder = 111, Description = "Segnaletica stradale non luminosa" },
            new SoaCategory { Code = "OS 11", Type = "OS", SortOrder = 112, Description = "Apparecchiature strutturali speciali" },
            new SoaCategory { Code = "OS 12-A", Type = "OS", SortOrder = 113, Description = "Barriere stradali di sicurezza" },
            new SoaCategory { Code = "OS 12-B", Type = "OS", SortOrder = 114, Description = "Barriere paramassi, fermaneve e simili" },
            new SoaCategory { Code = "OS 13", Type = "OS", SortOrder = 115, Description = "Strutture prefabbricate in cemento armato" },
            new SoaCategory { Code = "OS 14", Type = "OS", SortOrder = 116, Description = "Impianti di smaltimento e recupero rifiuti" },
            new SoaCategory { Code = "OS 15", Type = "OS", SortOrder = 117, Description = "Pulizia di acque marine, lacustri, fluviali" },
            new SoaCategory { Code = "OS 16", Type = "OS", SortOrder = 118, Description = "Impianti per centrali di produzione energia elettrica" },
            new SoaCategory { Code = "OS 17", Type = "OS", SortOrder = 119, Description = "Linee telefoniche ed impianti di telefonia" },
            new SoaCategory { Code = "OS 18-A", Type = "OS", SortOrder = 120, Description = "Componenti strutturali in acciaio" },
            new SoaCategory { Code = "OS 18-B", Type = "OS", SortOrder = 121, Description = "Componenti per facciate continue" },
            new SoaCategory { Code = "OS 19", Type = "OS", SortOrder = 122, Description = "Impianti di reti di telecomunicazione e di trasmissione dati" },
            new SoaCategory { Code = "OS 20-A", Type = "OS", SortOrder = 123, Description = "Rilevamenti topografici" },
            new SoaCategory { Code = "OS 20-B", Type = "OS", SortOrder = 124, Description = "Indagini geognostiche" },
            new SoaCategory { Code = "OS 21", Type = "OS", SortOrder = 125, Description = "Opere strutturali speciali" },
            new SoaCategory { Code = "OS 22", Type = "OS", SortOrder = 126, Description = "Impianti di potabilizzazione e depurazione" },
            new SoaCategory { Code = "OS 23", Type = "OS", SortOrder = 127, Description = "Demolizione di opere" },
            new SoaCategory { Code = "OS 24", Type = "OS", SortOrder = 128, Description = "Verde e arredo urbano" },
            new SoaCategory { Code = "OS 25", Type = "OS", SortOrder = 129, Description = "Scavi archeologici" },
            new SoaCategory { Code = "OS 26", Type = "OS", SortOrder = 130, Description = "Pavimentazioni e sovrastrutture speciali" },
            new SoaCategory { Code = "OS 27", Type = "OS", SortOrder = 131, Description = "Impianti per la trazione elettrica" },
            new SoaCategory { Code = "OS 28", Type = "OS", SortOrder = 132, Description = "Impianti termici e di condizionamento" },
            new SoaCategory { Code = "OS 29", Type = "OS", SortOrder = 133, Description = "Armamento ferroviario" },
            new SoaCategory { Code = "OS 30", Type = "OS", SortOrder = 134, Description = "Impianti interni elettrici, telefonici, radiotelefonici e televisivi" },
            new SoaCategory { Code = "OS 31", Type = "OS", SortOrder = 135, Description = "Impianti per la mobilità sospesa" },
            new SoaCategory { Code = "OS 32", Type = "OS", SortOrder = 136, Description = "Strutture in legno" },
            new SoaCategory { Code = "OS 33", Type = "OS", SortOrder = 137, Description = "Coperture speciali" },
            new SoaCategory { Code = "OS 34", Type = "OS", SortOrder = 138, Description = "Sistemi antirumore per infrastrutture di mobilità" },
            new SoaCategory { Code = "OS 35", Type = "OS", SortOrder = 139, Description = "Interventi a basso impatto ambientale" },
        };
    }
}
