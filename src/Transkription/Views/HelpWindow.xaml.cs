using System.Windows;
using System.Windows.Documents;
using Transkription.Core;
using Transkription.Settings;

namespace Transkription.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(AppSettings settings)
    {
        InitializeComponent();

        Heading("So wird transkribiert");
        Text("1. Audiodateien mit „Laden“ hinzufügen oder in das Fenster ziehen und in der Liste auswählen.\n" +
             "2. Word (oder ein anderes Schreibprogramm) öffnen und dort schreiben.\n" +
             "3. Die Wiedergabe mit Fußschalter oder Tastenkürzeln steuern – Transkription muss dafür nicht " +
             "im Vordergrund sein und kann über das ✕ in den Infobereich neben der Uhr verkleinert werden.");

        Heading("Aktuelle Belegung");
        foreach (var action in PlayerActions.All)
        {
            var keys = settings.KeyBindings.Where(b => b.Action == action).Select(b => b.Describe()).ToList();
            var pedals = settings.PedalBindings.Where(b => b.Action == action).Select(b => b.Describe()).ToList();
            var all = keys.Concat(pedals).ToList();
            HelpText.Inlines.Add(new Bold(new Run(PlayerActions.DisplayName(action) + ": ")));
            HelpText.Inlines.Add(new Run((all.Count > 0 ? string.Join(", ", all) : "nicht belegt") + "\n"));
        }

        Heading("Wiedergabe und Spulen");
        Text($"• Wiedergabe-Pedal: {(settings.PedalPlayMode == PlayMode.Hold ? "spielt, solange es getreten wird" : "schaltet bei jedem Tritt um")}.\n" +
             $"• Beim Anhalten springt die Wiedergabe automatisch {settings.BackstepSeconds:0.#} Sek. zurück, damit der Anschluss nicht verloren geht.\n" +
             $"• Kurzes Antippen von Zurück-/Vorspulen springt {settings.SkipSeconds:0.#} Sek.; gedrückt halten spult mit {settings.WindSpeed:0.#}-facher Geschwindigkeit. " +
             "Lief die Wiedergabe vorher, geht sie nach dem Loslassen weiter.\n" +
             "• Die Geschwindigkeit lässt sich zwischen 30 % und 300 % einstellen (Doppelklick auf den Regler = 100 %).\n" +
             "• „Zeitmarke eintippen“ schreibt die aktuelle Position, z. B. [00:12:34], in das Programm im Vordergrund.\n" +
             "• Die Position jeder Datei wird gemerkt.");

        Heading("Fußschalter einrichten");
        Text("Unter Einstellungen → „Tasten & Fußschalter“ bei der gewünschten Aktion auf „Anlernen“ klicken und das " +
             "Pedal treten und loslassen (falls nötig ein zweites Mal). Das funktioniert mit üblichen USB-Fußschaltern (z. B. Infinity IN-USB-1/2/3, " +
             "Olympus RS-27/28/31, Philips ACC2310/2320 und vielen anderen). Fußschalter, die sich als Tastatur ausgeben, werden " +
             "über „Taste ändern“ eingerichtet.\n\n" +
             "Hinweis: Ist ein Programm, in dem geschrieben wird, „als Administrator“ gestartet, verhindert Windows, dass Tasten " +
             "dort abgefangen werden. In diesem Fall auch Transkription als Administrator starten.");
    }

    private void Heading(string text)
    {
        if (HelpText.Inlines.Count > 0) HelpText.Inlines.Add(new LineBreak());
        HelpText.Inlines.Add(new Bold(new Run(text)) { FontSize = 15 });
        HelpText.Inlines.Add(new LineBreak());
    }

    private void Text(string text) => HelpText.Inlines.Add(new Run(text + "\n"));
}
