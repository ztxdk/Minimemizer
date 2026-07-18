# Issues i Minimemizer v0.5.3

Analyse af kodebasen — 14 identificerede problemer med symptomer, alvor og rettelsesforslag.

---

## 1. Race condition i hændelsesdispatchen
**Fil:** `WindowManager.cs:42-46`
**Alvor:** Medium

`OnWinEvent` dispatcher til UI-tråden med `BeginInvoke`, men mellem hændelsestidspunkt og kørsel kan vinduets tilstand ændre sig.

**Symptom:** En thumbnail oprettes, men vinduet er allerede gendannet. Thumbnailen bliver siddende som et spøgelsesbillede i op til 500 ms.

**Forslag:** Verificér vindues tilstand inde i `Add()`/`Remove()` med et ekstra kald til `IsIconic()`/`IsWindow()` før handling. Hvis tilstanden ikke matcher, spring over. Alternativt: brug en `ConcurrentDictionary<nint, DateTime>` med timestamp, så `ScanWindows` kan fjerne thumbnails ældre end f.eks. 2 sekunder.

---

## 2. ScanWindows kører evigt uden thumbnails
**Fil:** `WindowManager.cs:25`
**Alvor:** Lav

Timeren kører hvert 500. ms og enummererer alle vinduer, også når der ingen thumbnails er.

**Symptom:** Unødigt CPU-forbrug på batteridrevne enheder. Kan give mikro-hak med mange åbne vinduer.

**Forslag:** Stop timeren når `_thumbnails` er tom. Genstart den ved næste `Add()`-kald. Sæt intervallet til 1000-2000 ms for at reducere belastning yderligere.

---

## 3. Manglende håndtering af DWM cloaked windows
**Fil:** `WindowManager.cs`
**Alvor:** Medium

`EVENT_OBJECT_UNCLOAKED`/`EVENT_OBJECT_CLOAKED` overvåges ikke.

**Symptom:** Ved skift af virtuelt skrivebord bliver thumbnails siddende som sorte/hvide bokse. Minimeres et vindue på et andet virtuelt skrivebord, registreres det ikke.

**Forslag:** Tilføj et ekstra WinEvent-hook for `EVENT_OBJECT_CLOAKED` (0x8016) og `EVENT_OBJECT_UNCLOAKED` (0x8017). Ved cloaked: fjern thumbnailen. Ved uncloaked + minimeret: opret den. Konstant-numre varierer på Windows version; bør testes.

---

## 4. Ghost thumbnails ved Alt+Tab / Task View
**Fil:** `WindowManager.cs`
**Alvor:** Medium-Høj

`EVENT_SYSTEM_MINIMIZEEND` udløses ikke af alle gendannelsesmetoder (Alt+Tab, Task View, klik på taskbar).

**Symptom:** Thumbnail forbliver synlig, selvom programmet er gendannet. Op til 500 ms før `ScanWindows` rydder op.

**Forslag:** Sænk `ScanWindows` interval til ~300 ms, eller brug `EVENT_OBJECT_LOCATIONCHANGE` (0x800B) + `EVENT_OBJECT_STATE_CHANGE` (0x800A) som supplement. Tjek om vinduet stadig er minimeret (`IsIconic`) i `UpdateThumbnail` og kald `Remove()` hvis ikke.

---

## 5. Reflection i Save()
**Fil:** `SettingsWindow.cs:246`
**Alvor:** Lav (vedligehold)

Properties kopieres med `GetProperties()`/`SetValue()`. Ingen kompileringstidskontrol.

**Symptom:** Ændring eller fjernelse af en property i `AppSettings` giver kryptisk `NullReferenceException` ved gem.

**Forslag:** Erstat reflection-loopet med enten (a) en manuel kopi-metode `AppSettings.CopyTo(AppSettings other)` eller (b) en JSON round-trip (serialize draft, deserialize til `Current`), som draft allerede laves med på linje 49.

---

## 6. Mangelfuld validering ved Save
**Fil:** `SettingsWindow.cs:243-246`
**Alvor:** Medium

Kun få numeriske felter klemmes. Nye felter i `AppSettings` kan gemme ekstreme værdier.

**Symptom:** Ugyldige indstillinger kan give layout-fejl eller usynlige thumbnails.

**Forslag:** Flyt valideringslogikken ind i `AppSettings` selv via property setters med `Math.Clamp`, eller indfør `[Range(min, max)]` attributter og læs dem generisk i `Save()`.

---

## 7. Flash ved thumbnail-oprettelse
**Fil:** `WindowManager.cs:54-57`
**Alvor:** Lav

`window.Show()` kaldes før `Relayout()`, så vinduet vises ved (0,0) først.

**Symptom:** Kort blink øverst til venstre på skærmen, når en thumbnail oprettes.

**Forslag:** Beregn positionen før `Show()` og sæt `Left`/`Top`/`Width`/`Height` på vinduet inden det vises. Alternativt: sæt `WindowStartupLocation = Manual` og `Visibility = Hidden`, positionér, kald `Show()`.

---

## 8. Unødvendig lambda-allokering i hændelseshandler
**Fil:** `WindowManager.cs:42-46`
**Alvor:** Lav (performance)

Hver WinEvent-hændelse opretter en ny lambda og et nyt `BeginInvoke`-kald.

**Symptom:** Øget GC-pres ved mange minimeringer på kort tid.

**Forslag:** Gem delegaten i en privat instansvariabel:
```csharp
private readonly Action<nint, uint> _onEventAction;
// I constructor: _onEventAction = (hwnd, eventType) => { ... };
// I OnWinEvent: Application.Current.Dispatcher.BeginInvoke(_onEventAction, hwnd, eventType);
```

---

## 9. Død kode i IconBadgeWindow
**Fil:** `IconBadgeWindow.cs:32-36`
**Alvor:** Minimal

Hvis `LoadIcon` returnerer null, oprettes en `Border`-variabel der aldrig bruges.

**Symptom:** Ingen — ubetydeligt resource-spild.

**Forslag:** Indsæt `if (image is null) return;` før `Border`-oprettelsen, så hele konstruktionen undgås.

---

## 10. HWND-genbrug giver manglende thumbnail
**Fil:** `WindowManager.cs`
**Alvor:** Høj

Windows genbruger HWND-værdier. Hvis et gammelt vindues lukning ikke registreres, blokerer `ContainsKey` for en ny thumbnail.

**Symptom:** Program B (der får samme HWND som program A) får ingen thumbnail, når det minimeres.

**Forslag:** Tjek om den eksisterende ThumbnailWindow's source stadig er et validt, minimeret vindue ved HWND-match. Hvis ikke: fjern den gamle og opret ny. F.eks. i `Add()`:
```csharp
if (_thumbnails.TryGetValue(hwnd, out var existing))
{
    if (NativeMethods.IsWindow(existing.Source) && NativeMethods.IsIconic(existing.Source))
        return; // stadig gyldig
    Remove(hwnd); // forældet, fjern
}
```

---

## 11. Timer thrashing ved hurtig minimer/gendan
**Fil:** `WindowManager.cs:95-100`
**Alvor:** Lav

Ved hurtig minimering/gendannelse kan `Remove()` og `Add()` for samme HWND nå at køre i samme tick.

**Symptom:** Thumbnailen flimrer kortvarigt.

**Forslag:** Tjek om HWND allerede findes i `Add()` (gør den allerede på linje 51). I `ScanWindows`: saml først hwnd's der skal fjernes/to add, udfør derefter ændringerne under ét. Undgå at `Relayout()` kaldes to gange i samme scan.

---

## 12. DwmUpdateThumbnailProperties fejler lydløst
**Fil:** `ThumbnailWindow.cs:189`
**Alvor:** Høj

Return-værdien fra `DwmUpdateThumbnailProperties` ignoreres.

**Symptom:** Hvis source-vinduet er lukket, men `EVENT_OBJECT_DESTROY` tabes, bliver thumbnailet siddende som en død, sort eller gennemsigtig boks.

**Forslag:** Tjek return-værdien. Hvis den er != 0 (fejl), og source-vinduet ikke længere eksisterer, kald lukning af thumbnailen via en event/callback til `WindowManager.Remove()`.

---

## 13. DPI-drift ved gentagne Relayout
**Fil:** `ThumbnailWindow.cs:75-86`
**Alvor:** Lav

`_pixelX`/`_pixelY` gemmer pixel-værdier, men `Left`/`Top` divideres med DPI-skala. Afrundingsfejl kan akkumuleres.

**Symptom:** Thumbnails kravler langsomt nogle pixels ved gentagne layout-opdateringer på 125%/150% skala.

**Forslag:** Gem positionen i WPF-enheder (logiske pixels) i stedet for fysiske pixels, så der ikke sker gentagen konvertering. Beregn `_pixelX`/`_pixelY` ud fra `Left`/`Top` * scale, ikke omvendt.

---

## 14. ToInt64() truncation på 32-bit
**Fil:** `WindowManager.cs:64`
**Alvor:** Lav

`GetWindowLongPtr` returnerer `nint`. `ToInt64()` er unødvendig og kan truncere på 32-bit.

**Symptom:** I praksis usandsynligt med nuværende flags, men ville give forkerte style-tjek på 32-bit.

**Forslag:** Brug `nint` direkte i stedet for `ToInt64()` og sammenlign med `nint`-konstanter:
```csharp
var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
if ((style & (nint)NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;
```

---

## 15. Gennemgå hjørnevælgeren i `...`-menuen
**Fil:** `WindowManager.cs`, `FluentMenu.cs`
**Alvor:** Løst i v0.7.0

Hjørnevælger-listen i thumbnailens `...`-menu skal gennemgås næste gang.

**Fokus:** Vurder struktur, overskuelighed og betjening af listen med skærme og hjørner, især når der er flere displays.

---

## 16. Notifikation om nye versioner og mulig opdateringsfunktion
**Fil:** `App.xaml.cs`, Settings/About
**Alvor:** Implementeret i v0.7.0 – afventer offentlig releasekilde

Minimemizer skal kunne kontrollere, om der findes en nyere stabil GitHub Release, og vise en diskret notifikation med versionsnummer og link til releasen.

**Status:** Kode, SHA-256-verifikation og update-flow er implementeret. Repositoryet er fortsat privat og returnerer derfor `404` til anonyme klienter. Før funktionen kan bruges af slutbrugere, skal repository/releases gøres offentlige eller API-adressen flyttes til en offentlig distributionskilde.

**Fokus:** Undersøg også en valgfri opdateringsfunktion, der vælger korrekt x64/ARM64-build, verificerer downloadens SHA-256, lukker den kørende udgave sikkert og erstatter samt starter programmet igen uden at miste settings. Opdatering må kun ske efter brugerens tydelige accept og skal kunne fravælges.

---

## 17. Førstegangsinstallation og installationsscope
**Fil:** Opstartsflow, installer/updater
**Alvor:** Løst i v0.7.0

Når en ikke-installeret udgave startes første gang, skal brugeren tilbydes at installere Minimemizer i en fast mappe eller fortsætte som portable udgave.

**Valg:**
- **Kun nuværende bruger:** Installér uden administratorrettigheder under `%LOCALAPPDATA%\Programs\Minimemizer`.
- **Alle brugere:** Installér under `%ProgramFiles%\Minimemizer` og anmod kun om UAC-elevation, når brugeren vælger dette.
- **Portable:** Kør fra den nuværende placering uden installation.

**Fokus:** Installationen skal vælge korrekt x64/ARM64-build, kunne oprette Startmenu-/skrivebordsgenvej efter brugerens valg og bevare settings i brugerens eksisterende `%APPDATA%\Minimemizer`-mappe. Update-funktionen fra punkt 16 skal kende installationsscope og opdatere den valgte placering med korrekt rettighedsniveau. Alle-brugere-installation må ikke gøre settings fælles mellem brugere.

Der skal desuden defineres en sikker afinstallation, som som standard bevarer brugerens settings, men tilbyder at fjerne dem eksplicit.
