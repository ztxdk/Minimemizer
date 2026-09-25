# Backlog

Aktuelle, verificerede opgaver for Minimemizer. Løste versionshistoriske punkter hører til i Git-historikken og release-noterne.

## Vis “What’s new” efter en opdatering

Vis en themed dialog én gang efter første vellykkede start af en ny version.

- Gem senest viste version i `RuntimeStateStore`.
- Brug release-noter med en lokal fallback.
- Gør dialogen tilgængelig igen fra **Settings > About**.
- Understøt både dansk og engelsk.

## Fjern reflection fra gemning af indstillinger

`SettingsWindow.PersistChanges` kopierer fortsat alle properties fra kladden med reflection. Erstat det med en eksplicit kopi- eller clone-metode på `AppSettings`, så nye og ændrede felter bliver kontrolleret ved kompilering.

## Håndtér fejl fra DWM-opdatering

`ThumbnailWindow` ignorerer returværdien fra `DwmUpdateThumbnailProperties`. Hvis kaldet fejler, skal thumbnailen enten genregistreres eller fjernes sikkert gennem `WindowManager`, så en ugyldig DWM-thumbnail ikke bliver stående indtil næste scan.
