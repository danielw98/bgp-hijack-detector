# Detector de deturnări de rute BGP

Proiect pentru cursul PTIRC (TCSI, UPB), tema 9. Un detector defensiv: urmărește anunțurile BGP pentru prefixele monitorizate și ridică o alertă când un alt sistem autonom le anunță. Tehnica este cea folosită de ARTEMIS (FORTH-ICS) și BGPalerter (NTT).

Detectează trei situații:

- **origine falsă**: prefixul exact, anunțat de un AS care nu îl deține;
- **sub-prefix**: o parte mai specifică a prefixului, anunțată de un AS străin;
- **RPKI-invalid**: anunț pe care nicio autorizație de origine (ROA) nu îl permite.

Componente: un laborator cu trei rutere FRR, un colector (exabgp) care ascultă toate ruterele, detectorul scris în Python, o bază de date Postgres pentru istoricul rutelor și al alertelor, o pagină web și un parser MRT scris în C, care redă date reale din arhiva RIPE RIS.

## Garanții de izolare

- Rețeaua laboratorului este internă (`internal: true` în `lab/docker-compose.yml`), fără uplink. Niciun anunț nu părăsește calculatorul.
- Ruterele folosesc numere AS private (65000, 65001, 65003, colectorul 65100) și prefixul de documentație `203.0.113.0/24` (RFC 5737).
- Deturnarea din laborator este un test: ruterul „atacator” anunță prefixul doar în această rețea închisă.
- Redarea incidentului din 2008 citește un fișier înregistrat. Nu contactează nicio rețea.
- Pagina este publicată pe portul 8080 al calculatorului și comandă laboratorul prin socket-ul Docker. Folosește-o local.

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

1. **Starea de bază.** În „Laboratorul în direct” vezi cele trei rutere, sesiunile BGP dintre ele și colectorul. Victima (AS65001) anunță `203.0.113.0/24`, iar fiecare tabelă BGP are o singură rută.
2. **„Simulează deturnarea”.** Atacatorul (AS65003) anunță prefixul exact și sub-prefixul `203.0.113.0/25`. Sub-prefixul apare ca rută aleasă pe toate cele trei rutere. Anunțul fals pentru `/24` rămâne rută de rezervă pe tranzit, care păstrează ruta mai veche a victimei. În tabelul „Alerte” apar cele trei tipuri de alertă.
3. **O alertă.** Apasă pe ea și primești explicația: cine a anunțat, ce era așteptat și pe unde a ajuns anunțul la colector.
4. **„Resetează”.** Atacatorul își retrage anunțurile și tabelele revin la starea de bază. Alertele rămân în istoric.
5. **Incidentul din 2008.** La pornire, aplicația încarcă o singură dată actualizările BGP reale din 24 februarie 2008 (RIPE RIS, colectorul rrc00, 18:45-21:05 UTC, 136.344 de actualizări), când AS17557 (Pakistan Telecom) a anunțat `208.65.153.0/24` din spațiul YouTube. Detectorul ridică 31 de alerte de sub-prefix, prima la 18:47:57 UTC. „Redă incidentul” le reîncarcă de la zero, fără dubluri.
6. **„Golește istoricul”.** După confirmare, șterge toate alertele și tot istoricul de rute. Ruterele își retrimit apoi tabelele către colector, așa că rutele curente reapar imediat.

## Unde citești mai mult

- `referat/referat.md`: referatul cursului, cu noțiunile BGP, patru incidente documentate, arhitectura detectorului, rezultate și limite.
- `docs/lecturi-bgp.md`: listă de lecturi despre BGP, 25 de surse în 8 etape.
- `detector/incidents/youtube-2008/config.toml`: sursa datelor redate, prefixul monitorizat și cronologia documentată a incidentului.

## Structură

- `lab/`: configurațiile FRR și scripturile care pornesc și opresc deturnarea de test.
- `detector/bgpwatch/`: modelele, detecția, parserele și scrierea în Postgres.
- `detector/mrt/`: parserul MRT în C.
- `dashboard/`: pagina web și API-ul ei.
- `scripts/`: măsurătorile din referat și verificarea lui.

Teste: `sh detector/mrt/build.sh`, apoi `python -m pytest detector/tests dashboard/tests`.
