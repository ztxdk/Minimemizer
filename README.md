# Minimemizer

Minimemizer gør minimerede programmer synlige som små thumbnails på Windows 11-skrivebordet. Klik på en thumbnail for hurtigt at gendanne programmet.

Programikonet er indlejret i alle officielle ARM64- og x64-udgaver og bruges også i systembakken.

## Kom godt i gang

1. Start `Minimemizer.exe`.
2. Minimér et almindeligt program.
3. En thumbnail af programmet vises på skrivebordet.
4. Enkelt- eller dobbeltklik på thumbnailen for at åbne programmet igen.

Minimemizer kører i baggrunden og vises som et ikon i systembakken ved siden af Windows-uret. Højreklik på ikonet for at åbne **Indstillinger** eller afslutte programmet.

> Hvis systembakkeikonet ikke er synligt, kan det ligge under pilen **Vis skjulte ikoner**.

## Systemkrav

- Windows 11
- En framework-afhængig udgave kræver Microsoft .NET 8 Desktop Runtime
- En selvstændig udgave kræver ikke en separat .NET-installation

Programmet kræver normalt ikke administratorrettigheder.

## Indstillinger

Indstillingsvinduet følger Windows' dark/light mode og kan vises på dansk eller engelsk. Ændringer træder først i kraft, når du vælger **Gem**. **Annuller** lukker vinduet uden at gemme.

### Generelt

- **Sprog:** Vælg dansk eller engelsk.
- **Start med Windows:** Start automatisk, når du logger ind.
- **Åbn thumbnail:** Vælg enkeltklik eller dobbeltklik.
- **Højrekliksmenu:** Vis programmets klassiske vinduesmenu med blandt andet Gendan, Maksimér og Luk.

### Udseende

- Vælg thumbnailens maksimale bredde og højde.
- Brug **Adaptiv** størrelse, eller giv alle thumbnails **Ens størrelse**.
- Ved ens størrelse kan vinduet beskæres til rammen eller vises komplet.
- Vælg ingen ramme, skarpe hjørner eller afrundede Windows 11-hjørner.
- Justér gennemsigtigheden med slideren.
- Vis eller skjul programmets ikon, og placér det langs thumbnailens top eller bund.

Et live preview viser, hvordan ændringerne kommer til at se ud.

### Placering

- Vælg hvilken skærm thumbnails skal vises på.
- Vælg et hjørne som startpunkt.
- Placér thumbnails vandret eller lodret.
- Justér afstanden mellem thumbnails og afstanden til skærmens kant.

Hvis der ikke er plads til alle thumbnails, skaleres de automatisk ned.

### Programmer

Tilføj programmer, som Minimemizer skal ignorere. Ekskludering gemmes ud fra programmets `.exe`-fil.

### Om

Viser Minimemizers versionsnummer og arkitekturen for både programudgaven og Windows-systemet. Version 0.5 udgives til ARM64 og x64.

## Daglig brug

- Når et program minimeres, oprettes thumbnailen automatisk.
- Når programmet gendannes eller lukkes, fjernes thumbnailen.
- Højreklik på en thumbnail for at åbne programmets klassiske Windows-vinduesmenu, hvis funktionen er aktiveret.
- Afslut altid Minimemizer gennem systembakkeikonet. Hvis programmet ikke reagerer, kan det afsluttes via Jobliste.

## Begrænsninger

- Windows tilbyder ikke et officielt API til at placere tredjepartselementer direkte mellem skrivebordsbaggrunden og skrivebordsikonerne. Thumbnails placeres derfor nederst blandt almindelige vinduer.
- Beskyttet video, DRM-indhold og enkelte specialrenderede programmer kan vise en sort, tom eller fastfrosset forhåndsvisning.
- Programmer med højere administratorrettigheder kan i enkelte tilfælde ikke styres fuldt ud fra en normalt startet Minimemizer.
- Windows 11's moderne taskbar-jumplist kan ikke åbnes for andre programmer via et offentligt Windows-API. Højreklik bruger derfor den klassiske vinduesmenu.

## Fejlfinding

### Der kommer ingen thumbnail

- Kontrollér, at Minimemizer stadig kører i systembakken.
- Kontrollér under **Programmer**, at programmet ikke er ekskluderet.
- Gendan programmet og minimér det igen.
- Genstart Minimemizer.

### Thumbnailen er sort eller frossen

Programmet bruger sandsynligvis beskyttet eller specialiseret rendering, som Windows ikke stiller til rådighed som en live thumbnail. Dette kan ikke altid omgås.

### Indstillingerne ser forkerte ud efter en opdatering

Afslut alle ældre Minimemizer-processer via systembakken eller Jobliste, og start derefter den nyeste `Minimemizer.exe`.

Indstillinger gemmes for den aktuelle Windows-bruger i:

```text
%APPDATA%\Minimemizer\settings.json
```

Hvis filen slettes, gendannes standardindstillingerne ved næste start.

## For udviklere

Kør fra kildekoden:

```powershell
dotnet run
```

Byg en selvstændig Windows x64-version:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output findes under `bin/Release/net8.0-windows/win-x64/publish`.
