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

        Heading("Fußschalter");
        var pedals = settings.PedalBindings;
        if (pedals.Count == 0) Text("Noch keine Pedale eingerichtet.");
        for (var i = 0; i < pedals.Count; i++)
        {
            HelpText.Inlines.Add(new Bold(new Run($"Pedal {i + 1}: ")));
            HelpText.Inlines.Add(new Run($"{PlayerActions.DisplayName(pedals[i].Action)}  ({pedals[i].Describe()})\n"));
        }

        Heading("Tasten");
        foreach (var action in PlayerActions.All)
        {
            var keys = settings.KeyBindings.Where(b => b.Action == action).Select(KeyNames.Describe).ToList();
            if (keys.Count == 0) continue;
            HelpText.Inlines.Add(new Bold(new Run(string.Join(", ", keys) + ": ")));
            HelpText.Inlines.Add(new Run(PlayerActions.DisplayName(action) + "\n"));
        }

        Heading("Die Funktionen");
        Text("• Abspielen, solange gedrückt: spielt, während das Pedal getreten wird; beim Loslassen wird angehalten.\n" +
             "• Abspielen / Anhalten: jeder Tritt startet bzw. hält an.\n" +
             $"• Beim Anhalten springt die Wiedergabe automatisch {settings.BackstepSeconds:0.#} Sek. zurück, damit der Anschluss nicht verloren geht.\n" +
             $"• Schnell zurück-/vorspulen: spult mit {settings.WindSpeed:0.#}-facher Geschwindigkeit, solange gedrückt. " +
             "Lief die Wiedergabe vorher (oder wird das Abspiel-Pedal gehalten), geht sie danach weiter.\n" +
             $"• Kurz zurück-/vorspringen: springt bei jedem Tritt um {settings.SkipSeconds:0.#} Sek.; die Wiedergabe läuft dabei weiter.\n" +
             "• Die Geschwindigkeit lässt sich zwischen 30 % und 300 % einstellen, ohne dass sich die Tonhöhe ändert (Doppelklick auf den Regler = 100 %).\n" +
             "• „Zeitmarke eintippen“ schreibt die aktuelle Position, z. B. [00:12:34], in das Programm im Vordergrund.\n" +
             "• Sprungweite, Spulgeschwindigkeit und Rücksprung lassen sich unter Einstellungen → Wiedergabe ändern.");

        Heading("Fußschalter einrichten");
        Text("Ältere Fußschalter mit 9-poligem (seriellem) Stecker: zuerst unter Einstellungen → „Geräte“ den COM-Anschluss wählen.\n" +
             "Unter Einstellungen → „Fußschalter“ für jedes Pedal die gewünschte Funktion wählen, auf „Anlernen“ klicken und das " +
             "Pedal treten und loslassen (falls nötig ein zweites Mal). Das funktioniert mit üblichen USB-Fußschaltern (z. B. Infinity IN-USB-1/2/3, " +
             "Olympus RS-27/28/31, Philips LFH/ACC 2310/2320/2330 und vielen anderen). Fußschalter, die sich als Tastatur ausgeben, werden " +
             "unter Einstellungen → „Tasten“ eingerichtet.\n\n" +
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
