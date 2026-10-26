// =====================================================================
//  lang.cs - localisation de l'interface
//  Langue par defaut : anglais. Choix persistant dans C:\Nvidia\language.txt.
//  Les textes sources du code sont en francais ; les autres langues sont
//  fournies par traduction, avec repli sur l'anglais puis sur le francais.
// =====================================================================
using System;
using System.Collections.Generic;
using System.IO;

internal static class Lang
{
    public static readonly string[] Available = { "en", "fr", "de", "es", "it", "pt" };
    public static readonly string[] Names = { "English", "Français", "Deutsch", "Español", "Italiano", "Português" };

    private static string _current = "en";

    public static string Current { get { return _current; } }
    public static bool French { get { return _current == "fr"; } }

    private static string FilePath
    {
        get { return Path.Combine(@"C:\Nvidia", "language.txt"); }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string code = File.ReadAllText(FilePath).Trim().ToLowerInvariant();
                if (Array.IndexOf(Available, code) >= 0) _current = code;
            }
        }
        catch { }
    }

    public static void Set(string code)
    {
        if (string.IsNullOrEmpty(code)) return;
        code = code.Trim().ToLowerInvariant();
        if (Array.IndexOf(Available, code) < 0) return;
        _current = code;
        try { File.WriteAllText(FilePath, code); } catch { }
    }

    public static int IndexOf(string code)
    {
        int i = Array.IndexOf(Available, code);
        return i < 0 ? 0 : i;
    }

    public static string T(string french)
    {
        if (string.IsNullOrEmpty(french)) return french;
        if (_current == "fr") return french;

        Dictionary<string, string> table = _current == "de" ? De
            : _current == "es" ? Es
            : _current == "it" ? It
            : _current == "pt" ? Pt
            : En;

        string value;
        if (table.TryGetValue(french, out value)) return value;
        if (En.TryGetValue(french, out value)) return value;
        return french;
    }

    // ---------------- anglais ----------------
    private static readonly Dictionary<string, string> En = new Dictionary<string, string>
    {
        { "Identite", "Identity" }, { "Frequences", "Clocks" }, { "Configuration", "Configuration" },
        { "Temps reel", "Live" }, { "Capteurs", "Sensors" }, { "Rapport", "Report" },
        { "Bus PCI Express", "PCI Express bus" }, { "Limites d'origine", "Stock limits" },
        { "Tensions de la courbe", "Curve voltages" }, { "Frequences moyennes (MHz)", "Average clocks (MHz)" },
        { "Configuration appliquee", "Applied configuration" },
        { "Version de nvidiaconf", "nvidiaconf version" }, { "Nom", "Name" }, { "Revision", "Revision" },
        { "Pilote", "Driver" }, { "Bus PCI", "PCI bus" }, { "Memoire video", "Video memory" },
        { "Bus PCIe (version)", "PCIe bus (version)" }, { "Bus PCIe (largeur)", "PCIe bus (width)" },
        { "Frequence GPU max", "Max GPU clock" }, { "Frequence memoire max", "Max memory clock" },
        { "Puissance min", "Min power" }, { "Puissance max", "Max power" },
        { "Puissance par defaut", "Default power" }, { "Coupure thermique", "Shutdown temp" },
        { "Ralentissement thermique", "Throttle temp" },
        { "Tension min (courbe)", "Min voltage (curve)" }, { "Tension max (courbe)", "Max voltage (curve)" },
        { "Tension du plateau", "Plateau voltage" },
        { "Legere - origine", "Light - stock" }, { "Legere - optimise", "Light - tuned" },
        { "Jeu - origine", "Gaming - stock" }, { "Jeu - optimise", "Gaming - tuned" },
        { "Soutenue - origine", "Sustained - stock" }, { "Soutenue - optimise", "Sustained - tuned" },
        { "Lourde - origine", "Heavy - stock" }, { "Lourde - optimise", "Heavy - tuned" },
        { "Undervolt", "Undervolt" }, { "Power limit", "Power limit" },
        { "Profil ventilation", "Fan profile" }, { "Courbe 831 mV", "831 mV curve" },
        { "Frequence GPU", "GPU clock" }, { "Frequence memoire", "Memory clock" },
        { "Tension", "Voltage" }, { "Puissance", "Power" },
        { "Temperature GPU", "GPU temperature" }, { "Temperature memoire", "Memory temperature" },
        { "Ventilateur 1", "Fan 1" }, { "Ventilateur 2", "Fan 2" }, { "Charge GPU", "GPU load" },
        { "Ventilateur 1 (tr/min)", "Fan 1 (rpm)" }, { "Ventilateur 1 (%)", "Fan 1 (%)" },
        { "Ventilateur 2 (tr/min)", "Fan 2 (rpm)" }, { "Ventilateur 2 (%)", "Fan 2 (%)" },
        { "Memoire utilisee", "Memory used" }, { "PCIe (largeur)", "PCIe (width)" },
        { "Capteur", "Sensor" }, { "Valeur", "Value" },
        { "Appliquer", "Apply" }, { "Restaurer l'origine", "Restore stock" },
        { "Rendre persistant", "Make persistent" }, { "Determiner le package optimal", "Find optimal package" },
        { "Ouvrir le rapport comparatif", "Open comparison report" },
        { "Historique des configurations", "Configuration history" },
        { "Actualiser", "Refresh" }, { "Actualiser le rapport", "Refresh report" },
        { "Comparer origine / applique", "Compare stock / applied" }, { "Exporter CSV", "Export CSV" },
        { "Installer PyTorch", "Install PyTorch" }, { "Fermer", "Close" },
        { "Restaurer la selection", "Restore selection" },
        { "Frequence cible (MHz) :", "Target frequency (MHz):" },
        { "Tension cible (mV) :", "Target voltage (mV):" },
        { "Power limit max (W) :", "Max power limit (W):" },
        { "(0 = deduite de l'origine)", "(0 = derived from stock)" },
        { "(0 = recherche automatique)", "(0 = automatic search)" },
        { "(0 = consomme + 10 W)", "(0 = draw + 10 W)" },
        { "Rapport comparatif", "Comparison report" },
        { "Rapport comparatif avant / apres", "Before / after comparison report" },
        { "Limites de la carte : calcul en cours...", "Card limits: computing..." },
        { "Toute valeur hors de ces plages est refusee.", "Any value outside these ranges is refused." },
        { "Preparation du package optimal...", "Preparing the optimal package..." },
        { "Mesure terminee.", "Measurement complete." },
        { "Comparatif termine - rapport affiche dans l'onglet Rapport.",
          "Comparison complete - report shown in the Report tab." },
        { "Valeur hors limites", "Value out of range" }, { "PyTorch requis", "PyTorch required" },
        { "Tension instable", "Unstable voltage" }, { "Moteur de test", "Test engine" },
        { "Export CSV", "CSV export" }, { "Langue", "Language" }
    };

    // ---------------- allemand ----------------
    private static readonly Dictionary<string, string> De = new Dictionary<string, string>
    {
        { "Identite", "Identität" }, { "Frequences", "Taktraten" }, { "Configuration", "Konfiguration" },
        { "Temps reel", "Live" }, { "Capteurs", "Sensoren" }, { "Rapport", "Bericht" },
        { "Bus PCI Express", "PCI-Express-Bus" }, { "Limites d'origine", "Werksgrenzen" },
        { "Tensions de la courbe", "Kurvenspannungen" }, { "Frequences moyennes (MHz)", "Durchschnittstaktraten (MHz)" },
        { "Configuration appliquee", "Angewandte Konfiguration" },
        { "Version de nvidiaconf", "nvidiaconf-Version" }, { "Nom", "Name" }, { "Revision", "Revision" },
        { "Pilote", "Treiber" }, { "Bus PCI", "PCI-Bus" }, { "Memoire video", "Videospeicher" },
        { "Bus PCIe (version)", "PCIe-Bus (Version)" }, { "Bus PCIe (largeur)", "PCIe-Bus (Breite)" },
        { "Frequence GPU max", "Max. GPU-Takt" }, { "Frequence memoire max", "Max. Speichertakt" },
        { "Puissance min", "Min. Leistung" }, { "Puissance max", "Max. Leistung" },
        { "Puissance par defaut", "Standardleistung" }, { "Coupure thermique", "Abschalttemperatur" },
        { "Ralentissement thermique", "Drosseltemperatur" },
        { "Tension min (courbe)", "Min. Spannung (Kurve)" }, { "Tension max (courbe)", "Max. Spannung (Kurve)" },
        { "Tension du plateau", "Plateauspannung" },
        { "Legere - origine", "Leicht - Werk" }, { "Legere - optimise", "Leicht - optimiert" },
        { "Jeu - origine", "Spiel - Werk" }, { "Jeu - optimise", "Spiel - optimiert" },
        { "Soutenue - origine", "Dauer - Werk" }, { "Soutenue - optimise", "Dauer - optimiert" },
        { "Lourde - origine", "Schwer - Werk" }, { "Lourde - optimise", "Schwer - optimiert" },
        { "Power limit", "Leistungslimit" }, { "Profil ventilation", "Lüfterprofil" },
        { "Courbe 831 mV", "831-mV-Kurve" },
        { "Frequence GPU", "GPU-Takt" }, { "Frequence memoire", "Speichertakt" },
        { "Tension", "Spannung" }, { "Puissance", "Leistung" },
        { "Temperature GPU", "GPU-Temperatur" }, { "Temperature memoire", "Speichertemperatur" },
        { "Ventilateur 1", "Lüfter 1" }, { "Ventilateur 2", "Lüfter 2" }, { "Charge GPU", "GPU-Last" },
        { "Ventilateur 1 (tr/min)", "Lüfter 1 (U/min)" }, { "Ventilateur 1 (%)", "Lüfter 1 (%)" },
        { "Ventilateur 2 (tr/min)", "Lüfter 2 (U/min)" }, { "Ventilateur 2 (%)", "Lüfter 2 (%)" },
        { "Memoire utilisee", "Belegter Speicher" }, { "PCIe (largeur)", "PCIe (Breite)" },
        { "Capteur", "Sensor" }, { "Valeur", "Wert" },
        { "Appliquer", "Anwenden" }, { "Restaurer l'origine", "Werk wiederherstellen" },
        { "Rendre persistant", "Dauerhaft machen" }, { "Determiner le package optimal", "Optimales Paket ermitteln" },
        { "Ouvrir le rapport comparatif", "Vergleichsbericht öffnen" },
        { "Historique des configurations", "Konfigurationsverlauf" },
        { "Actualiser", "Aktualisieren" }, { "Actualiser le rapport", "Bericht aktualisieren" },
        { "Comparer origine / applique", "Werk / angewandt vergleichen" }, { "Exporter CSV", "CSV exportieren" },
        { "Installer PyTorch", "PyTorch installieren" }, { "Fermer", "Schließen" },
        { "Restaurer la selection", "Auswahl wiederherstellen" },
        { "Frequence cible (MHz) :", "Zieltakt (MHz):" },
        { "Tension cible (mV) :", "Zielspannung (mV):" },
        { "Power limit max (W) :", "Max. Leistungslimit (W):" },
        { "(0 = deduite de l'origine)", "(0 = aus Werkswerten)" },
        { "(0 = recherche automatique)", "(0 = automatische Suche)" },
        { "(0 = consomme + 10 W)", "(0 = Verbrauch + 10 W)" },
        { "Langue", "Sprache" }
    };

    // ---------------- espagnol ----------------
    private static readonly Dictionary<string, string> Es = new Dictionary<string, string>
    {
        { "Identite", "Identidad" }, { "Frequences", "Frecuencias" }, { "Configuration", "Configuración" },
        { "Temps reel", "Tiempo real" }, { "Capteurs", "Sensores" }, { "Rapport", "Informe" },
        { "Bus PCI Express", "Bus PCI Express" }, { "Limites d'origine", "Límites de fábrica" },
        { "Tensions de la courbe", "Tensiones de la curva" }, { "Frequences moyennes (MHz)", "Frecuencias medias (MHz)" },
        { "Configuration appliquee", "Configuración aplicada" },
        { "Version de nvidiaconf", "Versión de nvidiaconf" }, { "Nom", "Nombre" }, { "Revision", "Revisión" },
        { "Pilote", "Controlador" }, { "Bus PCI", "Bus PCI" }, { "Memoire video", "Memoria de vídeo" },
        { "Bus PCIe (version)", "Bus PCIe (versión)" }, { "Bus PCIe (largeur)", "Bus PCIe (ancho)" },
        { "Frequence GPU max", "Frecuencia GPU máx." }, { "Frequence memoire max", "Frecuencia memoria máx." },
        { "Puissance min", "Potencia mín." }, { "Puissance max", "Potencia máx." },
        { "Puissance par defaut", "Potencia por defecto" }, { "Coupure thermique", "Temp. de apagado" },
        { "Ralentissement thermique", "Temp. de limitación" },
        { "Tension min (courbe)", "Tensión mín. (curva)" }, { "Tension max (courbe)", "Tensión máx. (curva)" },
        { "Tension du plateau", "Tensión de meseta" },
        { "Legere - origine", "Ligera - fábrica" }, { "Legere - optimise", "Ligera - optimizada" },
        { "Jeu - origine", "Juego - fábrica" }, { "Jeu - optimise", "Juego - optimizada" },
        { "Soutenue - origine", "Sostenida - fábrica" }, { "Soutenue - optimise", "Sostenida - optimizada" },
        { "Lourde - origine", "Pesada - fábrica" }, { "Lourde - optimise", "Pesada - optimizada" },
        { "Power limit", "Límite de potencia" }, { "Profil ventilation", "Perfil de ventilación" },
        { "Courbe 831 mV", "Curva 831 mV" },
        { "Frequence GPU", "Frecuencia GPU" }, { "Frequence memoire", "Frecuencia memoria" },
        { "Tension", "Tensión" }, { "Puissance", "Potencia" },
        { "Temperature GPU", "Temperatura GPU" }, { "Temperature memoire", "Temperatura memoria" },
        { "Ventilateur 1", "Ventilador 1" }, { "Ventilateur 2", "Ventilador 2" }, { "Charge GPU", "Carga GPU" },
        { "Ventilateur 1 (tr/min)", "Ventilador 1 (rpm)" }, { "Ventilateur 1 (%)", "Ventilador 1 (%)" },
        { "Ventilateur 2 (tr/min)", "Ventilador 2 (rpm)" }, { "Ventilateur 2 (%)", "Ventilador 2 (%)" },
        { "Memoire utilisee", "Memoria usada" }, { "PCIe (largeur)", "PCIe (ancho)" },
        { "Capteur", "Sensor" }, { "Valeur", "Valor" },
        { "Appliquer", "Aplicar" }, { "Restaurer l'origine", "Restaurar fábrica" },
        { "Rendre persistant", "Hacer permanente" }, { "Determiner le package optimal", "Buscar el paquete óptimo" },
        { "Ouvrir le rapport comparatif", "Abrir informe comparativo" },
        { "Historique des configurations", "Historial de configuraciones" },
        { "Actualiser", "Actualizar" }, { "Actualiser le rapport", "Actualizar informe" },
        { "Comparer origine / applique", "Comparar fábrica / aplicada" }, { "Exporter CSV", "Exportar CSV" },
        { "Installer PyTorch", "Instalar PyTorch" }, { "Fermer", "Cerrar" },
        { "Restaurer la selection", "Restaurar selección" },
        { "Frequence cible (MHz) :", "Frecuencia objetivo (MHz):" },
        { "Tension cible (mV) :", "Tensión objetivo (mV):" },
        { "Power limit max (W) :", "Límite de potencia máx. (W):" },
        { "(0 = deduite de l'origine)", "(0 = según fábrica)" },
        { "(0 = recherche automatique)", "(0 = búsqueda automática)" },
        { "(0 = consomme + 10 W)", "(0 = consumo + 10 W)" },
        { "Langue", "Idioma" }
    };

    // ---------------- italien ----------------
    private static readonly Dictionary<string, string> It = new Dictionary<string, string>
    {
        { "Identite", "Identità" }, { "Frequences", "Frequenze" }, { "Configuration", "Configurazione" },
        { "Temps reel", "Tempo reale" }, { "Capteurs", "Sensori" }, { "Rapport", "Report" },
        { "Bus PCI Express", "Bus PCI Express" }, { "Limites d'origine", "Limiti di fabbrica" },
        { "Tensions de la courbe", "Tensioni della curva" }, { "Frequences moyennes (MHz)", "Frequenze medie (MHz)" },
        { "Configuration appliquee", "Configurazione applicata" },
        { "Version de nvidiaconf", "Versione di nvidiaconf" }, { "Nom", "Nome" }, { "Revision", "Revisione" },
        { "Pilote", "Driver" }, { "Bus PCI", "Bus PCI" }, { "Memoire video", "Memoria video" },
        { "Bus PCIe (version)", "Bus PCIe (versione)" }, { "Bus PCIe (largeur)", "Bus PCIe (larghezza)" },
        { "Frequence GPU max", "Frequenza GPU max" }, { "Frequence memoire max", "Frequenza memoria max" },
        { "Puissance min", "Potenza min" }, { "Puissance max", "Potenza max" },
        { "Puissance par defaut", "Potenza predefinita" }, { "Coupure thermique", "Temp. di spegnimento" },
        { "Ralentissement thermique", "Temp. di throttling" },
        { "Tension min (courbe)", "Tensione min (curva)" }, { "Tension max (courbe)", "Tensione max (curva)" },
        { "Tension du plateau", "Tensione del plateau" },
        { "Legere - origine", "Leggera - fabbrica" }, { "Legere - optimise", "Leggera - ottimizzata" },
        { "Jeu - origine", "Gioco - fabbrica" }, { "Jeu - optimise", "Gioco - ottimizzata" },
        { "Soutenue - origine", "Sostenuta - fabbrica" }, { "Soutenue - optimise", "Sostenuta - ottimizzata" },
        { "Lourde - origine", "Pesante - fabbrica" }, { "Lourde - optimise", "Pesante - ottimizzata" },
        { "Power limit", "Limite di potenza" }, { "Profil ventilation", "Profilo ventole" },
        { "Courbe 831 mV", "Curva 831 mV" },
        { "Frequence GPU", "Frequenza GPU" }, { "Frequence memoire", "Frequenza memoria" },
        { "Tension", "Tensione" }, { "Puissance", "Potenza" },
        { "Temperature GPU", "Temperatura GPU" }, { "Temperature memoire", "Temperatura memoria" },
        { "Ventilateur 1", "Ventola 1" }, { "Ventilateur 2", "Ventola 2" }, { "Charge GPU", "Carico GPU" },
        { "Ventilateur 1 (tr/min)", "Ventola 1 (rpm)" }, { "Ventilateur 1 (%)", "Ventola 1 (%)" },
        { "Ventilateur 2 (tr/min)", "Ventola 2 (rpm)" }, { "Ventilateur 2 (%)", "Ventola 2 (%)" },
        { "Memoire utilisee", "Memoria usata" }, { "PCIe (largeur)", "PCIe (larghezza)" },
        { "Capteur", "Sensore" }, { "Valeur", "Valore" },
        { "Appliquer", "Applica" }, { "Restaurer l'origine", "Ripristina fabbrica" },
        { "Rendre persistant", "Rendi permanente" }, { "Determiner le package optimal", "Trova il pacchetto ottimale" },
        { "Ouvrir le rapport comparatif", "Apri report comparativo" },
        { "Historique des configurations", "Cronologia configurazioni" },
        { "Actualiser", "Aggiorna" }, { "Actualiser le rapport", "Aggiorna report" },
        { "Comparer origine / applique", "Confronta fabbrica / applicata" }, { "Exporter CSV", "Esporta CSV" },
        { "Installer PyTorch", "Installa PyTorch" }, { "Fermer", "Chiudi" },
        { "Restaurer la selection", "Ripristina selezione" },
        { "Frequence cible (MHz) :", "Frequenza obiettivo (MHz):" },
        { "Tension cible (mV) :", "Tensione obiettivo (mV):" },
        { "Power limit max (W) :", "Limite di potenza max (W):" },
        { "(0 = deduite de l'origine)", "(0 = da fabbrica)" },
        { "(0 = recherche automatique)", "(0 = ricerca automatica)" },
        { "(0 = consomme + 10 W)", "(0 = consumo + 10 W)" },
        { "Langue", "Lingua" }
    };

    // ---------------- portugais ----------------
    private static readonly Dictionary<string, string> Pt = new Dictionary<string, string>
    {
        { "Identite", "Identidade" }, { "Frequences", "Frequências" }, { "Configuration", "Configuração" },
        { "Temps reel", "Tempo real" }, { "Capteurs", "Sensores" }, { "Rapport", "Relatório" },
        { "Bus PCI Express", "Barramento PCI Express" }, { "Limites d'origine", "Limites de fábrica" },
        { "Tensions de la courbe", "Tensões da curva" }, { "Frequences moyennes (MHz)", "Frequências médias (MHz)" },
        { "Configuration appliquee", "Configuração aplicada" },
        { "Version de nvidiaconf", "Versão do nvidiaconf" }, { "Nom", "Nome" }, { "Revision", "Revisão" },
        { "Pilote", "Driver" }, { "Bus PCI", "Barramento PCI" }, { "Memoire video", "Memória de vídeo" },
        { "Bus PCIe (version)", "Barramento PCIe (versão)" }, { "Bus PCIe (largeur)", "Barramento PCIe (largura)" },
        { "Frequence GPU max", "Frequência GPU máx." }, { "Frequence memoire max", "Frequência memória máx." },
        { "Puissance min", "Potência mín." }, { "Puissance max", "Potência máx." },
        { "Puissance par defaut", "Potência padrão" }, { "Coupure thermique", "Temp. de desligamento" },
        { "Ralentissement thermique", "Temp. de limitação" },
        { "Tension min (courbe)", "Tensão mín. (curva)" }, { "Tension max (courbe)", "Tensão máx. (curva)" },
        { "Tension du plateau", "Tensão do patamar" },
        { "Legere - origine", "Leve - fábrica" }, { "Legere - optimise", "Leve - otimizada" },
        { "Jeu - origine", "Jogo - fábrica" }, { "Jeu - optimise", "Jogo - otimizada" },
        { "Soutenue - origine", "Sustentada - fábrica" }, { "Soutenue - optimise", "Sustentada - otimizada" },
        { "Lourde - origine", "Pesada - fábrica" }, { "Lourde - optimise", "Pesada - otimizada" },
        { "Power limit", "Limite de potência" }, { "Profil ventilation", "Perfil de ventoinha" },
        { "Courbe 831 mV", "Curva 831 mV" },
        { "Frequence GPU", "Frequência GPU" }, { "Frequence memoire", "Frequência memória" },
        { "Tension", "Tensão" }, { "Puissance", "Potência" },
        { "Temperature GPU", "Temperatura GPU" }, { "Temperature memoire", "Temperatura memória" },
        { "Ventilateur 1", "Ventoinha 1" }, { "Ventilateur 2", "Ventoinha 2" }, { "Charge GPU", "Carga GPU" },
        { "Ventilateur 1 (tr/min)", "Ventoinha 1 (rpm)" }, { "Ventilateur 1 (%)", "Ventoinha 1 (%)" },
        { "Ventilateur 2 (tr/min)", "Ventoinha 2 (rpm)" }, { "Ventilateur 2 (%)", "Ventoinha 2 (%)" },
        { "Memoire utilisee", "Memória usada" }, { "PCIe (largeur)", "PCIe (largura)" },
        { "Capteur", "Sensor" }, { "Valeur", "Valor" },
        { "Appliquer", "Aplicar" }, { "Restaurer l'origine", "Restaurar fábrica" },
        { "Rendre persistant", "Tornar permanente" }, { "Determiner le package optimal", "Encontrar o pacote ideal" },
        { "Ouvrir le rapport comparatif", "Abrir relatório comparativo" },
        { "Historique des configurations", "Histórico de configurações" },
        { "Actualiser", "Atualizar" }, { "Actualiser le rapport", "Atualizar relatório" },
        { "Comparer origine / applique", "Comparar fábrica / aplicada" }, { "Exporter CSV", "Exportar CSV" },
        { "Installer PyTorch", "Instalar PyTorch" }, { "Fermer", "Fechar" },
        { "Restaurer la selection", "Restaurar seleção" },
        { "Frequence cible (MHz) :", "Frequência alvo (MHz):" },
        { "Tension cible (mV) :", "Tensão alvo (mV):" },
        { "Power limit max (W) :", "Limite de potência máx. (W):" },
        { "(0 = deduite de l'origine)", "(0 = de fábrica)" },
        { "(0 = recherche automatique)", "(0 = busca automática)" },
        { "(0 = consomme + 10 W)", "(0 = consumo + 10 W)" },
        { "Langue", "Idioma" }
    };
}
