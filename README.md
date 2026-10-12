# Detector de deturnări de rute BGP

Proiect pentru cursul PTIRC (TCSI, UPB), tema 9. Un detector defensiv: urmărește anunțurile BGP pentru prefixele monitorizate și ridică o alertă când un alt sistem autonom le anunță. Tehnica este cea folosită de ARTEMIS (FORTH-ICS) și BGPalerter (NTT).

Detectează trei situații:

- **origine falsă**: prefixul exact, anunțat de un AS care nu îl deține;
- **sub-prefix**: o parte mai specifică a prefixului, anunțată de un AS străin;
- **RPKI-invalid**: anunț pe care nicio autorizație de origine (ROA) nu îl permite.

Componente: un laborator cu trei rutere FRR, care își raportează rutele prin BMP (BGP Monitoring Protocol) către o stație de monitorizare, aplicația BGPWatch scrisă în C# (stația BMP, detectorul, pagina web), o bază de date Postgres pentru istoricul rutelor și al alertelor și un parser BGP scris în C, care decodează atât mesajele din laborator, cât și datele reale din arhiva RIPE RIS.

**Demo public:** <https://student-dev.ro/bgp/> (aplicația), [`/ghid`](https://student-dev.ro/bgp/ghid) (ghidul de citit), [`/referinte`](https://student-dev.ro/bgp/referinte) (lecturile).

![Simulatorul: trei sisteme autonome, deturnarea sub-prefixului 203.0.113.0/25 și tabelele BGP ale fiecărui ruter](media/app-simulator.png)

## Capturi

| | |
|---|---|
| ![YouTube 2008: un rând pentru fiecare vecin RIS, roșu cât timp trimitea traficul către AS17557](media/app-2008.png) | ![Istoricul prefixului 203.0.113.0/24 pe fiecare ruter](media/app-prefixe.png) |
| **YouTube 2008**, redat din actualizările reale RIPE RIS rrc00 | **Prefixe**: unde trimite fiecare ruter prefixul, de-a lungul timpului |
| ![Verificarea unui prefix real prin RIPEstat, cu starea RPKI](media/app-verifica.png) | ![Lecturile despre BGP, pe etape](media/referinte.png) |
| **Verifică**: un prefix real, cu originea și starea RPKI de la RIPEstat | **Referințe**: 44 de surse în 9 etape |

Capturile se refac cu `python scripts/screenshots.py` (implicit de pe instanța publică; `--url http://localhost:8080/` pentru cea locală).

## Tehnologii

| Strat | Ce folosește |
|---|---|
| Laborator | FRRouting 10.4 în Docker Compose, rețea internă `10.10.0.0/24`, BMP Loc-RIB (RFC 7854, RFC 9069) |
| Parser | C11, bibliotecă partajată (`libmrt.so`, `mrt.dll`): MRT (RFC 6396) și UPDATE BGP, IPv4 și IPv6, AS4_PATH |
| Aplicație | C# pe .NET 10: ASP.NET Core, Razor Pages, API JSON minimal, P/Invoke spre parser, Docker Engine API prin socket |
| Date | PostgreSQL 16 prin Npgsql (COPY binar, fără ORM); RIPEstat Data API, cu cache de 10 minute |
| Pagină | module ES fără framework, SVG, fonturile Atkinson Hyperlegible Next și Mono |
| Teste și verificări | xUnit v3, Playwright pentru Python, ASan și UBSan pentru parser |

## Garanții de izolare

- Rețeaua laboratorului este internă (`internal: true` în `lab/docker-compose.yml`), fără uplink. Niciun anunț nu părăsește calculatorul.
- Ruterele folosesc numere AS private (65000, 65001, 65003) și prefixul de documentație `203.0.113.0/24` (RFC 5737). Stația BMP doar ascultă: nu are sesiuni BGP și nu anunță nimic.
- Deturnarea din laborator este un test: ruterul „atacator” anunță prefixul doar în această rețea închisă.
- Redarea incidentului din 2008 citește un fișier înregistrat. Nu contactează nicio rețea.
- Local, pagina este publicată pe portul 8080 al calculatorului; pe server este servită sub prefixul `/bgp`. În ambele cazuri comandă laboratorul prin socket-ul Docker, deci oricine vede pagina poate declanșa și reseta deturnarea de test, și nimic altceva.
- Niciun pachet real nu traversează laboratorul: Docker aruncă pachetele adresate în afara subrețelei unei rețele interne. „Întreabă nucleele” citește decizia de rutare a fiecărui ruter în loc să trimită pachete.
- Figura 5 întreabă RIPEstat (`stat.ripe.net`) despre un prefix real; este singura cerere către Internet și doar citește.

## Pornire și oprire

Instanța publică rulează la <https://student-dev.ro/bgp/>: aceeași aplicație, cu laboratorul pornit pe server.

Ca să rulezi aplicația local îți ajunge Docker. Restul dependențelor (SDK-ul .NET 10, gcc, Python cu Playwright) servesc la teste și la scripturile din `scripts/`.

### Instalare

- **Windows 10/11:** dublu-clic pe `install.bat`. Instalează prin winget Docker Desktop, SDK-ul .NET 10, MSYS2 cu gcc și Python 3.12, apoi Playwright cu Chromium și pachetele NuGet; sare peste ce există deja. După prima instalare a Docker Desktop, repornește calculatorul o dată (cere WSL 2).
- **Debian sau Ubuntu:** `sh scripts/install-linux.sh` din rădăcina proiectului. Instalează Docker Engine cu pluginul compose, `build-essential`, SDK-ul .NET 10 în `~/.dotnet` și un `.venv` cu Playwright și Chromium.

### Pornire și oprire

- **Windows:** dublu-clic pe `start.bat`. Pornește Docker Desktop dacă este oprit, apoi laboratorul și detectorul, și deschide pagina `http://localhost:8080`. Prima pornire construiește imaginile și durează câteva minute. Oprire: dublu-clic pe `stop.bat`. Istoricul rutelor și alertele rămân în volumul Docker.
- **Linux sau macOS**, din rădăcina proiectului:

```sh
# pornire: laboratorul, apoi detectorul (stația BMP, Postgres, pagina pe :8080)
docker compose -f lab/docker-compose.yml up -d
docker compose up -d --build --remove-orphans

# oprire
docker compose down
docker compose -f lab/docker-compose.yml down
```

### Comenzi Docker utile

```sh
docker compose ps                                   # starea aplicației și a bazei de date
docker compose logs -f app                          # jurnalul aplicației
docker compose -f lab/docker-compose.yml ps         # cele trei rutere
docker exec lab-transit vtysh -c "show ip bgp"      # tabela BGP a ruterului de tranzit
docker exec lab-transit vtysh -c "show bmp"         # sesiunea BMP către stație
docker compose -f lab/docker-compose.yml up -d --force-recreate   # după o schimbare în lab/frr/
docker compose down -v                              # oprește și ȘTERGE istoricul din Postgres
```

Parola Postgres din `docker-compose.yml` (`bgp`) este valoarea implicită a demo-ului local; baza nu are port publicat.

## Ce să apeși

Pagina de pornire este aplicația: simulatorul pe tot ecranul, cu patru vederi în bara de sus (Simulator, YouTube 2008, Prefixe, Verifică) și butoanele „Ghid” și „Referințe”. Ghidul (`/ghid`) este pagina de citit de sus în jos, cu aceleași figuri între paragrafe și explicațiile; fiecare vedere a aplicației trimite la capitolul ei din ghid. Fiecare figură are datele brute la îndemână, într-un sertar „Date brute” închis, iar în ghid termenii subliniați punctat deschid o definiție. Referințele (`/referinte`) sunt lecturile pentru a învăța BGP, pe etape, cu genealogia specificației de la RFC 1105 la RFC 4271.

1. **Figura 1, simulatorul.** Pe pânză, harta celor trei sisteme autonome cu ruterele lor, sesiunile BGP și colectorul; în dreapta, inspectorul cu tabela BGP a fiecărui ruter, lista de evenimente și incidentele, citite la fiecare două secunde. Bara de sus ține cei patru pași, numerotați, și „Opțiuni”, unde alegi scenariul (prefixul exact, sub-prefixul sau ambele) și pachetul. Apasă „1 Deturnează”: fiecare salt al mesajelor UPDATE, de la atacator din ruter în ruter și spre colector, este un eveniment: un punct parcurge legătura, iar eticheta mesajului așteaptă deja la ruterul țintă; rândurile atacatorului apar cu roșu, sub-prefixul `203.0.113.0/25` este ales pe toate ruterele, iar prefixul exact rămâne rută de rezervă pe tranzit, care păstrează ruta mai veche a victimei. Bara de redare, ca în Packet Tracer: „Pauză” și „Redă”, „Pas” (un eveniment înainte), „Înapoi”, viteza de la 0,25x la 4x și evenimentul curent; în tabul „Evenimente”, un clic pe un eveniment sare la el, iar „Șterge evenimentele” golește lista. „Trimite pachetul” urmărește un pachet către `203.0.113.1` (în sub-prefix) sau `203.0.113.129` (în afara lui), după tabelele BGP, cu rândul consultat evidențiat în inspector la fiecare salt și verdictul lângă ruterul unde se oprește. „Întreabă nucleele” cere fiecărui ruter decizia sa reală de rutare pentru aceeași adresă și spune dacă nucleele confirmă simularea; niciun pachet nu este trimis. „Retrage anunțurile” readuce ruterele la starea de bază. Un clic pe eticheta evenimentului curent sau pe rândul lui din „Evenimente” oprește redarea și deschide fișa lui: pentru un UPDATE, calea AS din acel salt, originea (așteptată sau nu) și ce face ruterul care îl primește; pentru un raport BMP, alertele ridicate de detector; pentru un pachet, rândurile care se potrivesc și următorul salt; pentru nucleu, răspunsul lui `ip route get`. Un clic pe un ruter îi arată sesiunile, raportarea BMP și tabela.
2. **Figura 2, incidentele.** Alertele aceleiași deturnări, văzute din mai multe puncte de observație, formează un incident. Apasă pe el și primești explicația: cine a anunțat, ce era așteptat, de unde a venit anunțul.
3. **Figura 3, YouTube 2008.** Tot incidentul pe un singur ecran: un rând pentru fiecare dintre cei 11 vecini ai colectorului rrc00, cu banda care arată cui îi trimitea traficul pentru blocul deturnat (roșu către AS17557, verde către YouTube) și, la dreapta, calea AS din momentul ales, cu sistemul autonom care predă ruta originii încadrat. Axa benzilor e împărțită în faze la cele cinci evenimente documentate: fiecare fază are lățimea după rădăcina pătrată a duratei, cu durata reală scrisă deasupra, iar faza comprimată mult (cele 80 de minute ale deturnării) poartă semnul de rupere //. Deasupra stau „Redă” (cursorul merge cu viteză constantă pe bandă, iar eticheta arată ritmul fazei curente), ora și câți vecini trimit spre fiecare origine, apoi evenimentul documentat de RIPE NCC din momentul ales, cu butoane spre cel anterior și cel următor. Trage pe benzi sau pe bara de sub ele ca să derulezi. Evenimentele, prima observare la fiecare vecin și datele brute stau închise sub tabel. Actualizările BGP reale (18:45-21:05 UTC, 136.344 de actualizări) sunt încărcate o singură dată la pornire; detectorul ridică 31 de alerte de sub-prefix, prima la 18:47:57 UTC. „Reîncarcă incidentul din fișiere”, în datele brute, le rescrie fără dubluri.
4. **Figura 4, istoricul unui prefix.** Alege prefixul urmărit și fereastra (10 min, 1 oră, 24 h, tot). Verdictul spune unde trimite fiecare ruter prefixul acum. Dedesubt, fiecare ruter are câte un rând pentru fiecare prefix văzut (prefixul urmărit și sub-prefixele lui), cu banda în fereastră (verde spre originea așteptată, roșu spre alt AS, linie subțire fără rută) și calea de acum. Schimbările, alertele și datele brute stau închise dedesubt.
5. **Figura 5, un prefix real.** Caută un prefix sau o adresă, alege un exemplu ori apasă „Adresa mea”. Răspunsul începe cu verdictul RPKI, apoi prefixul care o acoperă, originea, deținătorul, autorizațiile (ROA) și ce înseamnă starea RPKI, de la RIPEstat. Este singura cerere a paginii către Internet și doar citește.
6. **„Golește istoricul simulatorului”**, sub inspector. După confirmare, șterge alertele și istoricul de rute ale simulatorului; incidentul din 2008 rămâne. Stația închide apoi conexiunile BMP, ruterele se reconectează și își retrimit tabelele, așa că rutele curente reapar imediat.

## Unde citești mai mult

- `src/BGPWatch.Web/Reading/lecturi-bgp.md`: lista de lecturi despre BGP, pe etape, cu genealogia specificației; pagina `/referinte` se generează din ea.
- `detector/incidents/youtube-2008/config.toml`: sursa datelor redate, prefixul monitorizat și cronologia documentată a incidentului.

## Structură

- `lab/`: configurațiile FRR și scripturile care pornesc și opresc deturnarea de test.
- `src/BGPWatch.Core/`: nucleul, fără dependențe: modelul de domeniu, detecția, gruparea alertelor în incidente, istoricul unui prefix, cazurile de utilizare.
- `src/BGPWatch.Web/`: adaptoarele și gazda: stația BMP, legătura cu parserul C, Postgres, Docker, RIPEstat, API-ul și paginile (Razor Pages, `wwwroot/static/`).
- `tests/BGPWatch.Tests/`: testele unitare.
- `detector/mrt/`: parserul BGP și MRT în C.
- `detector/incidents/`, `detector/config.toml`, `detector/schema.sql`: datele incidentului din 2008, configurația laboratorului și schema bazei de date.
- `scripts/`: instalatoarele, verificarea paginii, măsurătorile laboratorului, fuzzing-ul parserului, capturile din `media/`.
- `docker-compose.yml` este forma de producție; `docker-compose.override.yml` (încărcat implicit) adaugă portul local și fișierele montate din arborele de lucru; `docker-compose.server.yml` și `deploy/nginx-bgp.conf` sunt stratul pentru server.

Teste: `sh detector/mrt/build.sh`, apoi `dotnet test --solution BGPWatch.slnx`.
