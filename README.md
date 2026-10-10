# Detector de deturnări de rute BGP

Proiect pentru cursul PTIRC (TCSI, UPB), tema 9. Un detector defensiv: urmărește anunțurile BGP pentru prefixele monitorizate și ridică o alertă când un alt sistem autonom le anunță. Tehnica este cea folosită de ARTEMIS (FORTH-ICS) și BGPalerter (NTT).

Detectează trei situații:

- **origine falsă**: prefixul exact, anunțat de un AS care nu îl deține;
- **sub-prefix**: o parte mai specifică a prefixului, anunțată de un AS străin;
- **RPKI-invalid**: anunț pe care nicio autorizație de origine (ROA) nu îl permite.

Componente: un laborator cu trei rutere FRR, care își raportează rutele prin BMP (BGP Monitoring Protocol) către o stație de monitorizare, aplicația BGPWatch scrisă în C# (stația BMP, detectorul, pagina web), o bază de date Postgres pentru istoricul rutelor și al alertelor și un parser BGP scris în C, care decodează atât mesajele din laborator, cât și datele reale din arhiva RIPE RIS.

## Garanții de izolare

- Rețeaua laboratorului este internă (`internal: true` în `lab/docker-compose.yml`), fără uplink. Niciun anunț nu părăsește calculatorul.
- Ruterele folosesc numere AS private (65000, 65001, 65003) și prefixul de documentație `203.0.113.0/24` (RFC 5737). Stația BMP doar ascultă: nu are sesiuni BGP și nu anunță nimic.
- Deturnarea din laborator este un test: ruterul „atacator” anunță prefixul doar în această rețea închisă.
- Redarea incidentului din 2008 citește un fișier înregistrat. Nu contactează nicio rețea.
- Pagina este publicată pe portul 8080 al calculatorului și comandă laboratorul prin socket-ul Docker. Folosește-o local.
- Niciun pachet real nu traversează laboratorul: Docker aruncă pachetele adresate în afara subrețelei unei rețele interne. „Întreabă nucleele” citește decizia de rutare a fiecărui ruter în loc să trimită pachete.
- Figura 5 întreabă RIPEstat (`stat.ripe.net`) despre un prefix real; este singura cerere către Internet și doar citește.

## Pornire și oprire

Ai nevoie de [Docker Desktop](https://www.docker.com/products/docker-desktop/).

- **Pornire:** dublu-clic pe `start.bat`. Pornește Docker Desktop dacă este oprit, apoi laboratorul și detectorul, și deschide pagina `http://localhost:8080`. Prima pornire construiește imaginile și durează câteva minute.
- **Oprire:** dublu-clic pe `stop.bat`. Istoricul rutelor și alertele rămân în volumul Docker.

Pe Linux sau macOS:

```sh
docker compose -f lab/docker-compose.yml up -d && docker compose up -d --build
docker compose down && docker compose -f lab/docker-compose.yml down
```

## Ce să apeși

Pagina de pornire este aplicația: laboratorul în direct, pe tot ecranul, cu patru vederi în bara de sus (Laborator, YouTube 2008, Prefixe, Verifică) și butonul „Ghid”. Ghidul (`/ghid`) este pagina de citit de sus în jos, cu aceleași figuri între paragrafe și explicațiile; fiecare vedere a aplicației trimite la capitolul ei din ghid. Fiecare figură are un sertar „Date brute” cu răspunsul exact al API-ului, iar în ghid termenii subliniați punctat deschid o definiție.

1. **Figura 1, laboratorul.** Harta celor trei sisteme autonome cu ruterele lor, sesiunile BGP și colectorul, plus tabela BGP a fiecărui ruter, citite la fiecare două secunde. Alege scenariul (prefixul exact, sub-prefixul sau ambele) și apasă „Simulează deturnarea”: mesajele UPDATE pornesc pe hartă de la atacator, din ruter în ruter și spre colector; rândurile atacatorului apar cu roșu, sub-prefixul `203.0.113.0/25` este ales pe toate ruterele, iar prefixul exact rămâne rută de rezervă pe tranzit, care păstrează ruta mai veche a victimei. „Trimite pachetul” urmărește pe hartă un pachet către `203.0.113.1` (în sub-prefix) sau `203.0.113.129` (în afara lui), după tabelele BGP, cu rândul consultat evidențiat la fiecare salt; „Pas cu pas” face un salt la fiecare apăsare. „Întreabă nucleele” cere fiecărui ruter decizia sa reală de rutare pentru aceeași adresă și spune dacă nucleele confirmă simularea; niciun pachet nu este trimis. „Resetează” retrage anunțurile.
2. **Figura 2, incidentele.** Alertele aceleiași deturnări, văzute din mai multe puncte de observație, formează un incident. Apasă pe el și primești explicația: cine a anunțat, ce era așteptat, de unde a venit anunțul.
3. **Figura 3, YouTube 2008.** O cronologie de derulat: harta sistemelor autonome din căile înregistrate (vecinii colectorului la stânga, originile la dreapta) se colorează cu căile folosite în momentul ales, iar o bandă pentru fiecare dintre cei 11 vecini ai colectorului rrc00 arată cui îi trimitea traficul pentru blocul deturnat, roșu către AS17557, verde către YouTube. Trage cursorul sau apasă „Redă” (180x); cele cinci evenimente documentate de RIPE NCC sunt marcate, iar sub hartă stă prima observare a sub-prefixului la fiecare vecin. Actualizările BGP reale (18:45-21:05 UTC, 136.344 de actualizări) sunt încărcate o singură dată la pornire; detectorul ridică 31 de alerte de sub-prefix, prima la 18:47:57 UTC. „Reîncarcă incidentul din fișiere”, în sertar, le rescrie fără dubluri.
4. **Figura 4, istoricul unui prefix.** Pentru prefixul ales: ruta curentă la fiecare punct de observație și fiecare schimbare, cu starea dinainte și cea de după.
5. **Figura 5, un prefix real.** Caută un prefix sau o adresă, ori apasă „Adresa mea”: prefixul care o acoperă, originea, deținătorul și verdictul RPKI, de la RIPEstat. Este singura cerere a paginii către Internet și doar citește.
6. **„Golește istoricul laboratorului”**, la finalul paginii. După confirmare, șterge alertele și istoricul de rute ale laboratorului; incidentul din 2008 rămâne. Stația închide apoi conexiunile BMP, ruterele se reconectează și își retrimit tabelele, așa că rutele curente reapar imediat.

## Unde citești mai mult

- `referat/referat.md`: referatul cursului, cu noțiunile BGP, patru incidente documentate, arhitectura detectorului, rezultate și limite.
- `docs/lecturi-bgp.md`: listă de lecturi despre BGP, 25 de surse în 8 etape.
- `detector/incidents/youtube-2008/config.toml`: sursa datelor redate, prefixul monitorizat și cronologia documentată a incidentului.

## Structură

- `lab/`: configurațiile FRR și scripturile care pornesc și opresc deturnarea de test.
- `src/BGPWatch.Core/`: nucleul, fără dependențe: modelul de domeniu, detecția, gruparea alertelor în incidente, istoricul unui prefix, cazurile de utilizare.
- `src/BGPWatch.Web/`: adaptoarele și gazda: stația BMP, legătura cu parserul C, Postgres, Docker, RIPEstat, API-ul și paginile (Razor Pages, `wwwroot/static/`).
- `tests/BGPWatch.Tests/`: testele unitare.
- `detector/mrt/`: parserul BGP și MRT în C.
- `detector/incidents/`, `detector/config.toml`, `detector/schema.sql`: datele incidentului din 2008, configurația laboratorului și schema bazei de date.
- `scripts/`: măsurătorile din referat și verificările lui și ale paginii.

Teste: `sh detector/mrt/build.sh`, apoi `dotnet test --solution BGPWatch.slnx`.
