# Detector de deturnări de rute BGP

[English](README.md) | **Română**

Proiect pentru cursul PTIRC (masterul TCSI, Universitatea Națională de Știință și Tehnologie POLITEHNICA București), tema 9. Un detector defensiv: urmărește anunțurile BGP pentru prefixele monitorizate și ridică o alertă când un alt sistem autonom le anunță. Tehnica este cea folosită de ARTEMIS (FORTH-ICS) și BGPalerter (NTT).

Detectează trei situații:

- **origine falsă**: prefixul exact, anunțat de un AS care nu îl deține;
- **sub-prefix**: o parte mai specifică a prefixului, anunțată de un AS străin;
- **RPKI-invalid**: un anunț pe care nicio autorizație de origine (ROA) nu îl permite.

Componente: un laborator cu trei rutere FRR, care își raportează rutele prin BMP (BGP Monitoring Protocol) către o stație de monitorizare; aplicația BGPWatch, scrisă în C# (stația BMP, detectorul, paginile web); o bază de date Postgres pentru istoricul rutelor și al alertelor; un parser BGP scris în C, care decodează atât mesajele din laborator, cât și datele reale din arhiva RIPE RIS.

**Demo public:** <https://student-dev.ro/bgp/?lang=ro> (aplicația), [`/guide`](https://student-dev.ro/bgp/guide?lang=ro) (ghidul de citit), [`/references`](https://student-dev.ro/bgp/references?lang=ro) (lecturile). Site-ul este implicit în engleză; comutatorul **EN | RO** din bara de sus sau `?lang=ro` în adresă îl trec în română, iar alegerea rămâne memorată.

![Cum funcționează, pasul 3: Atacatorul pretinde 203.0.113.0/25, iar pachetul către 203.0.113.1 ajunge la el](media/ro/app-story.png)

## Capturi

| | |
|---|---|
| ![YouTube 2008: un rând pentru fiecare vecin RIS, roșu cât timp trimitea traficul către AS17557](media/ro/app-2008.png) | ![Istoricul prefixului 203.0.113.0/24 pe fiecare ruter](media/ro/app-prefixes.png) |
| **YouTube 2008**, redat din actualizările reale RIPE RIS rrc00 | **Prefixe**: unde trimite fiecare ruter prefixul, de-a lungul timpului |
| ![Verificarea unui prefix real prin RIPEstat, cu starea RPKI](media/ro/app-lookup.png) | ![Lecturile despre BGP, pe etape](media/ro/references.png) |
| **Verificare**: un prefix real, cu originea și starea RPKI de la RIPEstat | **Referințe**: 44 de surse în 9 etape |

Capturile se refac cu `python scripts/screenshots.py`, în engleză în `media/` și în română în `media/ro/` (implicit de pe instanța publică; `--url http://localhost:8080/` pentru cea locală).

## Tehnologii

| Strat | Ce folosește |
|---|---|
| Laborator | FRRouting 10.4 în Docker Compose, rețea internă `10.10.0.0/24`, BMP Loc-RIB (RFC 7854, RFC 9069) |
| Parser | C11, bibliotecă partajată (`libmrt.so`, `mrt.dll`): MRT (RFC 6396) și UPDATE BGP, IPv4 și IPv6, AS4_PATH; fiecare citire verificată la limite, fiecare eșec raportat |
| Aplicație | C# pe .NET 10: ASP.NET Core, Razor Pages, API JSON minimal, P/Invoke spre parser, Docker Engine API prin socket |
| Limbi | un serviciu de traducere în C# peste câte un fișier JSON pe limbă (`Localization/strings.en.json`, `strings.ro.json`), folosit de pagini și de modulele din browser |
| Date | PostgreSQL 16 prin Npgsql (COPY binar, fără ORM); RIPEstat Data API, cu cache de 10 minute |
| Pagină | module ES fără framework, SVG, fonturile Atkinson Hyperlegible Next și Mono |
| Teste și verificări | xUnit v3, Playwright pentru Python, ASan și UBSan pentru parser |

## Garanții de izolare

- Rețeaua laboratorului este internă (`internal: true` în `lab/docker-compose.yml`), fără uplink. Niciun anunț nu părăsește calculatorul.
- Ruterele folosesc numere AS private (65000, 65001, 65003) și prefixul de documentație `203.0.113.0/24` (RFC 5737). Stația BMP doar ascultă: nu are sesiuni BGP și nu anunță nimic.
- Deturnarea din laborator este un test: ruterul „atacator” anunță prefixul doar în această rețea închisă.
- Redarea incidentului din 2008 citește un fișier înregistrat și nu contactează nicio rețea.
- Local, pagina este publicată pe un port al calculatorului; pe server este servită sub prefixul `/bgp`. În ambele cazuri comandă laboratorul prin socket-ul Docker, deci oricine vede pagina poate declanșa și reseta deturnarea de test, și nimic altceva.
- Niciun pachet real nu traversează laboratorul: Docker aruncă pachetele adresate în afara subrețelei unei rețele interne. „Întreabă nucleele” citește decizia de rutare a fiecărui ruter în loc să trimită pachete.
- Vederea de verificare întreabă RIPEstat (`stat.ripe.net`) despre un prefix real; este singura cerere a paginii către Internet și doar citește.

## Instalare, pornire, oprire

Pentru rularea aplicației este nevoie doar de Docker. Celelalte dependențe (SDK-ul .NET 10, gcc, Python cu Playwright) servesc la teste și la scripturile din `scripts/`.

### Instalare

- **Windows 10/11:** dublu-clic pe `install.bat`. Instalează prin winget Docker Desktop, SDK-ul .NET 10, MSYS2 cu gcc și Python 3.12, apoi Playwright cu Chromium și pachetele NuGet, și sare peste ce există deja. După prima instalare a Docker Desktop este nevoie de o repornire a calculatorului (cere WSL 2).
- **Debian sau Ubuntu:** `sh scripts/install-linux.sh`, rulat din rădăcina proiectului. Instalează Docker Engine cu pluginul compose, `build-essential`, SDK-ul .NET 10 în `~/.dotnet` și un `.venv` cu Playwright și Chromium.

### Pornire și oprire

- **Windows:** dublu-clic pe `start.bat`. Pornește Docker Desktop dacă este oprit, apoi laboratorul și detectorul, și deschide pagina. Pagina păstrează portul unui stack deja pornit sau ia primul port local liber între 8080 și 8099. Prima pornire construiește imaginile și durează câteva minute. Oprirea se face cu dublu-clic pe `stop.bat`; istoricul rutelor și alertele rămân în volumul Docker.
- **Linux sau macOS**, din rădăcina proiectului (`BGP_PORT` alege portul local, 8080 dacă lipsește):

```sh
# pornire: laboratorul, apoi detectorul (stația BMP, Postgres, pagina)
docker compose -f lab/docker-compose.yml up -d
BGP_PORT=8080 docker compose up -d --build --remove-orphans

# oprire
docker compose down
docker compose -f lab/docker-compose.yml down
```

### Comenzi Docker utile

```sh
docker compose ps                                   # starea aplicației și a bazei de date
docker compose logs -f app                          # jurnalul aplicației
docker compose port app 8080                        # portul local pe care este publicată pagina
docker compose -f lab/docker-compose.yml ps         # cele trei rutere
docker exec lab-transit vtysh -c "show ip bgp"      # tabela BGP a ruterului de tranzit
docker exec lab-transit vtysh -c "show bmp"         # sesiunea lui BMP către stație
docker compose -f lab/docker-compose.yml up -d --force-recreate   # după o schimbare în lab/frr/
docker compose down -v                              # oprește și ȘTERGE istoricul din Postgres
```

Parola Postgres din `docker-compose.yml` (`bgp`) este valoarea implicită a demo-ului local; baza nu are port publicat.

## Ce face fiecare parte

Pagina de pornire este aplicația: patru vederi în bara de sus (Cum funcționează, YouTube 2008, Prefixe, Verificare) și butoanele „Ghid” și „Referințe”. Ghidul (`/guide`) are șase capitole scurte de citit de sus în jos, fiecare cu o figură fixă a laboratorului într-un moment ales și o legătură către pasul său din poveste; fiecare vedere a aplicației trimite la capitolul ei din ghid. În ghid, termenii subliniați punctat deschid o definiție. Referințele (`/references`) sunt lecturile pentru a învăța BGP, pe etape, cu genealogia specificației de la RFC 1105 la RFC 4271.

1. **Cum funcționează.** O deturnare de rută în cinci pași, pentru un cititor care n-a auzit de BGP: în stânga, o hartă fixă a laboratorului; în dreapta, un singur panou de text; „Înapoi” și „Următorul” (sau punctele numerotate) pentru a avansa. Pașii: o rețea își anunță adresele, un pachet merge unde spun rutele, atacatorul pretinde jumătate din bloc și pachetul ajunge la el, detectorul dă alarma (cu întârzierea reală), iar validarea originii la furnizor, față de RPKI, respinge rutele atacatorului. Fiecare fapt vine dintr-un ciclu înregistrat pe routerele reale ale laboratorului (`static/story/cycle.json`, scris de `scripts/record_story.py`), așa că povestea merge și cu laboratorul oprit. La ultimul pas, „Rulează atacul pe routerele reale” anunță sub-prefixul în laborator, spune când a dat detectorul alarma și unde a ajuns pachetul, apoi retrage atacul. `#step-N` deschide pasul N.
2. **YouTube 2008.** Tot incidentul pe un singur ecran: un rând pentru fiecare dintre cei 11 vecini ai colectorului rrc00, cu banda care arată cui îi trimitea traficul pentru blocul deturnat (roșu către AS17557, verde către YouTube) și, la dreapta, calea AS din momentul ales, cu sistemul autonom care predă ruta originii încadrat. Axa benzilor este împărțită în faze la cele cinci evenimente documentate: fiecare fază are lățimea după rădăcina pătrată a duratei, cu durata reală scrisă deasupra, iar faza comprimată mult (cele 80 de minute ale deturnării) poartă semnul de rupere //. Deasupra stau „Redă” (cursorul merge cu viteză constantă pe bandă, iar eticheta arată ritmul fazei curente), ora și câți vecini trimit spre fiecare origine, apoi evenimentul documentat de RIPE NCC din momentul ales, cu butoane spre cel anterior și cel următor. Tragerea pe benzi sau pe bara de sub ele derulează incidentul. Evenimentele, prima observare la fiecare vecin și datele brute stau închise sub tabel. Actualizările BGP reale (18:45-21:05 UTC, 136.344 de actualizări) se încarcă o singură dată la pornire; detectorul ridică 31 de alerte de sub-prefix, prima la 18:47:57 UTC. „Reîncarcă incidentul din fișiere”, în datele brute, le rescrie fără dubluri.
3. **Istoricul unui prefix.** Sus se aleg prefixul urmărit și fereastra (10 min, 1 oră, 24 h, tot). Verdictul spune unde trimite fiecare ruter prefixul acum. Dedesubt, fiecare ruter are câte un rând pentru fiecare prefix văzut (prefixul urmărit și sub-prefixele lui), cu banda în fereastră (verde spre originea așteptată, roșu spre alt AS, linie subțire fără rută) și calea de acum. Schimbările, alertele și datele brute stau închise dedesubt.
4. **Un prefix real.** Un prefix sau o adresă scrise în câmp, un exemplu sau „Adresa mea”. Răspunsul începe cu verdictul RPKI, apoi prefixul care acoperă adresa, originea, deținătorul, autorizațiile (ROA) și semnificația stării RPKI, de la RIPEstat. Este singura cerere a paginii către Internet și doar citește.

## Lecturi suplimentare

- `src/BGPWatch.Web/Reading/readings.ro.md` și `readings.en.md`: lista de lecturi despre BGP, în română și în engleză, pe etape, cu genealogia specificației; pagina `/references` se generează din ele.
- `detector/incidents/youtube-2008/config.toml`: sursa datelor redate, prefixul monitorizat și cronologia documentată a incidentului, în ambele limbi.

## Structură

- `lab/`: configurațiile FRR și scripturile care pornesc și opresc deturnarea de test.
- `src/BGPWatch.Core/`: nucleul, fără dependențe: modelul de domeniu, detecția, gruparea alertelor în incidente, istoricul unui prefix, cazurile de utilizare.
- `src/BGPWatch.Web/`: adaptoarele și gazda: stația BMP, legătura cu parserul C, Postgres, Docker, RIPEstat, API-ul, serviciul de traducere (`Localization/`) și paginile (Razor Pages, `wwwroot/static/`).
- `tests/BGPWatch.Tests/`: testele unitare.
- `detector/mrt/`: parserul BGP și MRT în C, cu harness-ul de fuzzing sub sanitizers.
- `detector/incidents/`, `detector/config.toml`, `detector/schema.sql`: datele incidentului din 2008, configurația laboratorului și schema bazei de date.
- `scripts/`: instalatoarele, verificarea paginii, măsurătorile laboratorului, fuzzing-ul parserului, capturile din `media/`.
- `docker-compose.yml` este forma de producție; `docker-compose.override.yml` (încărcat implicit) adaugă portul local și fișierele montate din arborele de lucru; `docker-compose.server.yml` și `deploy/nginx-bgp.conf` sunt stratul pentru server.

## Teste și verificări

- Teste unitare: `sh detector/mrt/build.sh`, apoi `dotnet test --solution BGPWatch.slnx`.
- Verificarea paginii, cu stack-ul pornit: `python scripts/check_page.py` (ambele limbi la 390, 1366 și 1920 px: depășiri, erori de script, mărimea și coliziunile textului de pe hartă, contrastul culorilor, emoji).
- Fuzzing-ul parserului sub ASan și UBSan: `python scripts/fuzz_mrt.py 20000`.

## Licență

MIT, vezi [LICENSE](LICENSE). Fonturile Atkinson Hyperlegible incluse sunt sub SIL Open Font License; fișierele RIPE RIS din 2008 sunt date ale RIPE NCC.
