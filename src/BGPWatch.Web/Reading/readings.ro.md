---
title: "BGP: listă de lecturi pentru proiectul PTIRC"
subtitle: "Parcurs ordonat, de la protocol la detecția deturnărilor de rute"
date: "Verificată la 5 și 11 octombrie 2026"
lang: ro
---

# Cum se folosește lista

Lista are 44 de surse grupate în nouă etape, în ordinea în care merită citite, plus genealogia specificației BGP: cele șase RFC-uri prin care protocolul a ajuns la versiunea de azi și cele douăsprezece care o actualizează. Fiecare etapă se sprijină pe cea dinainte. Fiecare sursă are anul, ideea pe scurt, citarea completă, un link gratuit și legal și, la cele mai multe, conținutul, motivul pentru care e în listă, durata și secțiunile care merită citite. Sursele au fost deschise și citite sau parcurse atent pe 5 și 11 octombrie 2026, iar linkurile au fost verificate pe 11 octombrie 2026, cu excepția ACM Queue (sursa 14), care refuză clienții automați și a fost deschis în browser pe 5 octombrie 2026.

Timpii sunt estimări pentru o citire atentă, cu notițe.

# Pentru început

Aceste cinci surse dau cea mai multă înțelegere pe oră investită. Împreună durează în jur de cinci ore și acoperă mecanismul protocolului, politicile care îl guvernează, motivul pentru care e nesigur și un incident real văzut în datele de rutare.

1. Peterson și Davie, secțiunea 4.1.2 (sursa 1): imaginea de ansamblu.
2. Caesar și Rexford, „BGP routing policies in ISP networks” (sursa 3): de ce BGP arată așa cum arată.
3. RFC 4271, secțiunile indicate (sursa 2): mecanismul exact al UPDATE-urilor și al procesului de decizie.
4. Goldberg, „Why is it taking so long to secure internet routing?” (sursa 14): modelul de încredere și apărările.
5. RIPE NCC, studiul de caz YouTube (sursa 17): o deturnare de sub-prefix urmărită minut cu minut.

# Etapa 1. Cum funcționează BGP

## 1. Computer Networks: A Systems Approach, secțiunea 4.1.2

- An: 2024
- Pe scurt: Cea mai scurtă introducere în rutarea între domenii: tipuri de AS, path vector, relații comerciale și iBGP.
- Citare: L. Peterson, B. Davie, *Computer Networks: A Systems Approach*, ediția a 6-a, ediție online deschisă (licență CC BY 4.0, versiunea 6.2-dev, © 2024), secțiunea 4.1.2 „Interdomain Routing (BGP)”.
- Link: [book.systemsapproach.org/scaling/global.html](https://book.systemsapproach.org/scaling/global.html)
- Conținut: problemele rutării între domenii, tipurile de AS (stub, multihomed, transit), ideea de path vector și detecția buclelor, relațiile customer, provider și peer cu politicile de export aferente, apoi integrarea cu rutarea internă prin iBGP.
- De ce: e o introducere scurtă și clară, care oferă vocabularul și modelul mental pe care se sprijină restul listei, fără detaliile de format ale RFC-ului.
- Timp: 45 de minute.
- De citit: toată secțiunea 4.1.2. Secțiunea 4.1.1 (Routing Areas, despre OSPF) poate fi sărită.

## 2. RFC 4271, A Border Gateway Protocol 4 (BGP-4)

- An: 2006
- Pe scurt: Specificația în vigoare a BGP-4: mesajele, atributele de cale, mașina de stări și procesul de decizie.
- Citare: Y. Rekhter, T. Li, S. Hares (ed.), „A Border Gateway Protocol 4 (BGP-4)”, RFC 4271, IETF, ianuarie 2006, DOI 10.17487/RFC4271.
- Link: [rfc-editor.org/rfc/rfc4271](https://www.rfc-editor.org/rfc/rfc4271)
- Relația: înlocuiește RFC 1771; douăsprezece RFC-uri îl actualizează, toate în secțiunea „Actualizările RFC 4271”.
- Conținut: specificația protocolului: mesajele OPEN, UPDATE, KEEPALIVE și NOTIFICATION, atributele de cale, mașina de stări a sesiunii și procesul de decizie. Versiunile anterioare sunt în genealogia de mai jos.
- De ce: detectorul citește exact câmpurile descrise aici. Originea unei rute este ultimul AS din AS_PATH, iar un UPDATE poate conține simultan prefixe retrase și prefixe anunțate, ceea ce contează la parsarea MRT.
- Timp: 2 până la 3 ore pentru secțiunile recomandate.
- De citit atent: 1.1 (termeni), 3 și 3.1 până la 3.2 (Summary of Operation, Adj-RIB-In, Loc-RIB, Adj-RIB-Out), 4.3 (formatul UPDATE), 5 și 5.1.1 până la 5.1.5 (ORIGIN, AS_PATH, NEXT_HOP, MULTI_EXIT_DISC, LOCAL_PREF), 9.1 cu 9.1.2.2 (procesul de decizie și regulile de departajare), 9.1.4 (rute suprapuse, adică prefixe mai specifice), 9.2.1.1 (MinRouteAdvertisementIntervalTimer, util pentru sursa 11) și Security Considerations.
- De sărit: 6 (tratarea erorilor), 7, 8 (mașina de stări, peste 35 de pagini), 10 și anexele A până la F. Secțiunea 8 devine utilă doar la depanarea unei sesiuni care nu se stabilește în laborator.

# Genealogia specificației BGP

Patru versiuni ale protocolului în șase RFC-uri, de la BGP-1 (1989) la RFC 4271 (2006). Relațiile sunt cele din RFC Editor („obsoletes”, „obsoleted by”), iar ce s-a schimbat vine din anexele de comparație ale fiecărui document.

## RFC 1105, Border Gateway Protocol (BGP)

- An: 1989
- Versiune: BGP-1
- Relația: înlocuit de RFC 1163
- Ce s-a schimbat: Prima specificație, cu statut experimental, construită pe experiența cu EGP din NSFNET; rulează peste TCP, pe portul 179, și descrie legăturile dintre AS-uri ca relații up, down și horizontal.
- Citare: K. Lougheed, Y. Rekhter, „Border Gateway Protocol (BGP)”, RFC 1105, IETF, iunie 1989, DOI 10.17487/RFC1105.
- Link: [rfc-editor.org/rfc/rfc1105](https://www.rfc-editor.org/rfc/rfc1105)

## RFC 1163, Border Gateway Protocol (BGP)

- An: 1990
- Versiune: BGP-2
- Relația: înlocuiește RFC 1105; înlocuit de RFC 1267
- Ce s-a schimbat: UPDATE primește formatul cu mai multe atribute de cale, Hold Time și versiunea trec în mesajul OPEN, OPEN CONFIRM dispare (confirmarea o face KEEPALIVE), iar relațiile up, down și horizontal sunt scoase.
- Citare: K. Lougheed, Y. Rekhter, „Border Gateway Protocol (BGP)”, RFC 1163, IETF, iunie 1990, DOI 10.17487/RFC1163.
- Link: [rfc-editor.org/rfc/rfc1163](https://www.rfc-editor.org/rfc/rfc1163)

## RFC 1267, Border Gateway Protocol 3 (BGP-3)

- An: 1991
- Versiune: BGP-3
- Relația: înlocuiește RFC 1163
- Ce s-a schimbat: OPEN primește câmpul BGP Identifier, cu care se detectează coliziunea a două conexiuni între aceiași vecini; NEXT_HOP poate indica un ruter din afara AS-ului vorbitorului; schimbul rutelor retrase e simplificat.
- Citare: K. Lougheed, Y. Rekhter, „Border Gateway Protocol 3 (BGP-3)”, RFC 1267, IETF, octombrie 1991, DOI 10.17487/RFC1267.
- Link: [rfc-editor.org/rfc/rfc1267](https://www.rfc-editor.org/rfc/rfc1267)

## RFC 1654, A Border Gateway Protocol 4 (BGP-4)

- An: 1994
- Versiune: BGP-4
- Relația: înlocuit de RFC 1771; RFC Editor nu îl trece ca înlocuitor al RFC 1267
- Ce s-a schimbat: BGP-4 anunță prefixe IP în locul claselor de rețele (CIDR), cu AS_PATH redefinit pentru agregare; apar LOCAL_PREF, ATOMIC_AGGREGATE și AGGREGATOR, INTER_AS_METRIC devine MULTI_EXIT_DISC, iar Hold Time se negociază pe conexiune.
- Citare: Y. Rekhter, T. Li (ed.), „A Border Gateway Protocol 4 (BGP-4)”, RFC 1654, IETF, iulie 1994, DOI 10.17487/RFC1654.
- Link: [rfc-editor.org/rfc/rfc1654](https://www.rfc-editor.org/rfc/rfc1654)

## RFC 1771, A Border Gateway Protocol 4 (BGP-4)

- An: 1995
- Versiune: BGP-4
- Relația: înlocuiește RFC 1654; înlocuit de RFC 4271
- Ce s-a schimbat: Aceeași versiune 4, avansată de la Proposed Standard la Draft Standard; anexele compară textul cu RFC 1267, 1163 și 1105, nu cu RFC 1654.
- Citare: Y. Rekhter, T. Li, „A Border Gateway Protocol 4 (BGP-4)”, RFC 1771, IETF, martie 1995, DOI 10.17487/RFC1771.
- Link: [rfc-editor.org/rfc/rfc1771](https://www.rfc-editor.org/rfc/rfc1771)

## RFC 4271, A Border Gateway Protocol 4 (BGP-4)

- An: 2006
- Versiune: BGP-4, în vigoare
- Relația: înlocuiește RFC 1771; actualizat de cele douăsprezece RFC-uri din secțiunea următoare
- Ce s-a schimbat: Integrează route reflection, confederațiile și route refresh, adaugă limita de prefixe acceptate de la un vecin, clarifică procesul de decizie și tipurile de NEXT_HOP, scoate autentificarea prin câmpul Marker și cere suport pentru TCP MD5.
- Citare: sursa 2.
- Link: [rfc-editor.org/rfc/rfc4271](https://www.rfc-editor.org/rfc/rfc4271)

# Actualizările RFC 4271

Douăsprezece RFC-uri modifică RFC 4271 fără să-l înlocuiască (relația „updated by” din RFC Editor, la 11 octombrie 2026). Securitatea o privesc direct 7606, 7607, 8212 și 9774.

## RFC 4724, Graceful Restart Mechanism for BGP

- An: 2007
- Relația: actualizează RFC 4271; actualizat de RFC 8538
- Ce s-a schimbat: Un ruter care își repornește sesiunea BGP poate păstra tabela de forwarding, iar marcajul End-of-RIB îi spune vecinului că i-a trimis toate rutele inițiale.
- Citare: S. Sangli, E. Chen, R. Fernando, J. Scudder, Y. Rekhter, „Graceful Restart Mechanism for BGP”, RFC 4724, IETF, ianuarie 2007, DOI 10.17487/RFC4724.
- Link: [rfc-editor.org/rfc/rfc4724](https://www.rfc-editor.org/rfc/rfc4724)

## RFC 6286, Autonomous-System-Wide Unique BGP Identifier for BGP-4

- An: 2011
- Relația: actualizează RFC 4271
- Ce s-a schimbat: BGP Identifier devine un întreg de 4 octeți, nenul, care trebuie să fie unic doar în cadrul AS-ului.
- Citare: E. Chen, J. Yuan, „Autonomous-System-Wide Unique BGP Identifier for BGP-4”, RFC 6286, IETF, iunie 2011, DOI 10.17487/RFC6286.
- Link: [rfc-editor.org/rfc/rfc6286](https://www.rfc-editor.org/rfc/rfc6286)

## RFC 6608, Subcodes for BGP Finite State Machine Error

- An: 2012
- Relația: actualizează RFC 4271
- Ce s-a schimbat: Subcoduri pentru eroarea de mașină de stări din NOTIFICATION, care spun în ce stare a sesiunii (OpenSent, OpenConfirm, Established) a sosit mesajul neașteptat.
- Citare: J. Dong, M. Chen, A. Suryanarayana, „Subcodes for BGP Finite State Machine Error”, RFC 6608, IETF, mai 2012, DOI 10.17487/RFC6608.
- Link: [rfc-editor.org/rfc/rfc6608](https://www.rfc-editor.org/rfc/rfc6608)

## RFC 6793, BGP Support for Four-Octet Autonomous System (AS) Number Space

- An: 2012
- Relația: actualizează RFC 4271; înlocuiește RFC 4893
- Ce s-a schimbat: Numere AS pe 4 octeți, negociate ca capabilitate; către vecinii vechi calea merge în atributul AS4_PATH, iar în AS_PATH apare AS_TRANS (23456). Parserul C al proiectului face exact îmbinarea AS_PATH cu AS4_PATH.
- Citare: Q. Vohra, E. Chen, „BGP Support for Four-Octet Autonomous System (AS) Number Space”, RFC 6793, IETF, decembrie 2012, DOI 10.17487/RFC6793.
- Link: [rfc-editor.org/rfc/rfc6793](https://www.rfc-editor.org/rfc/rfc6793)

## RFC 7606, Revised Error Handling for BGP UPDATE Messages

- An: 2015
- Relația: actualizează RFC 1997, 4271, 4360, 4456, 4760, 5543, 5701 și 6368
- Ce s-a schimbat: Un atribut malformat nu mai închide sesiunea: UPDATE-ul e tratat ca retragere (treat-as-withdraw) sau atributul e ignorat, iar celelalte rute ale sesiunii rămân.
- Citare: E. Chen (ed.), J. Scudder (ed.), P. Mohapatra, K. Patel, „Revised Error Handling for BGP UPDATE Messages”, RFC 7606, IETF, august 2015, DOI 10.17487/RFC7606.
- Link: [rfc-editor.org/rfc/rfc7606](https://www.rfc-editor.org/rfc/rfc7606)

## RFC 7607, Codification of AS 0 Processing

- An: 2015
- Relația: actualizează RFC 4271
- Ce s-a schimbat: AS 0 e interzis în OPEN, AS_PATH, AS4_PATH, AGGREGATOR și AS4_AGGREGATOR.
- Citare: W. Kumari, R. Bush, H. Schiller, K. Patel, „Codification of AS 0 Processing”, RFC 7607, IETF, august 2015, DOI 10.17487/RFC7607.
- Link: [rfc-editor.org/rfc/rfc7607](https://www.rfc-editor.org/rfc/rfc7607)

## RFC 7705, Autonomous System Migration Mechanisms and Their Effects on the BGP AS_PATH Attribute

- An: 2015
- Relația: actualizează RFC 4271
- Ce s-a schimbat: Documentează mecanismele de migrare a numărului AS folosite de facto (Local AS, No Prepend Inbound și altele), care schimbă ce apare în AS_PATH.
- Citare: W. George, S. Amante, „Autonomous System Migration Mechanisms and Their Effects on the BGP AS_PATH Attribute”, RFC 7705, IETF, decembrie 2015, DOI 10.17487/RFC7705.
- Link: [rfc-editor.org/rfc/rfc7705](https://www.rfc-editor.org/rfc/rfc7705)

## RFC 8212, Default External BGP (EBGP) Route Propagation Behavior without Policies

- An: 2017
- Relația: actualizează RFC 4271
- Ce s-a schimbat: O sesiune eBGP fără politică explicită de import nu acceptă nicio rută, iar fără politică de export nu anunță nimic. FRR o aplică implicit (`bgp ebgp-requires-policy`); laboratorul o dezactivează în `lab/frr/*/frr.conf`.
- Citare: J. Mauch, J. Snijders, G. Hankins, „Default External BGP (EBGP) Route Propagation Behavior without Policies”, RFC 8212, IETF, iulie 2017, DOI 10.17487/RFC8212.
- Link: [rfc-editor.org/rfc/rfc8212](https://www.rfc-editor.org/rfc/rfc8212)

## RFC 8654, Extended Message Support for BGP

- An: 2019
- Relația: actualizează RFC 4271
- Ce s-a schimbat: Mesajele pot avea până la 65.535 de octeți în loc de 4.096, cu excepția OPEN și KEEPALIVE.
- Citare: R. Bush, K. Patel, D. Ward, „Extended Message Support for BGP”, RFC 8654, IETF, octombrie 2019, DOI 10.17487/RFC8654.
- Link: [rfc-editor.org/rfc/rfc8654](https://www.rfc-editor.org/rfc/rfc8654)

## RFC 9072, Extended Optional Parameters Length for BGP OPEN Message

- An: 2021
- Relația: actualizează RFC 4271
- Ce s-a schimbat: Parametrii opționali din OPEN, unde stau capabilitățile, pot depăși 255 de octeți, compatibil cu implementările vechi.
- Citare: E. Chen, J. Scudder, „Extended Optional Parameters Length for BGP OPEN Message”, RFC 9072, IETF, iulie 2021, DOI 10.17487/RFC9072.
- Link: [rfc-editor.org/rfc/rfc9072](https://www.rfc-editor.org/rfc/rfc9072)

## RFC 9687, Border Gateway Protocol 4 (BGP-4) Send Hold Timer

- An: 2024
- Relația: actualizează RFC 4271
- Ce s-a schimbat: SendHoldTimer: ruterul închide sesiunea când vecinul nu mai citește mesajele, fără să aștepte ca vecinul să o închidă.
- Citare: J. Snijders, B. Cartwright-Cox, Y. Qu, „Border Gateway Protocol 4 (BGP-4) Send Hold Timer”, RFC 9687, IETF, noiembrie 2024, DOI 10.17487/RFC9687.
- Link: [rfc-editor.org/rfc/rfc9687](https://www.rfc-editor.org/rfc/rfc9687)

## RFC 9774, Deprecation of AS_SET and AS_CONFED_SET in BGP

- An: 2025
- Relația: actualizează RFC 4271 și RFC 5065; înlocuiește RFC 6472
- Ce s-a schimbat: AS_SET și AS_CONFED_SET sunt interzise la originarea rutelor, ca originea unei rute să fie lipsită de ambiguitate și mecanismele de securitate să fie mai simple.
- Citare: W. Kumari, K. Sriram, L. Hannachi, J. Haas, „Deprecation of AS_SET and AS_CONFED_SET in BGP”, RFC 9774, IETF, mai 2025, DOI 10.17487/RFC9774.
- Link: [rfc-editor.org/rfc/rfc9774](https://www.rfc-editor.org/rfc/rfc9774)

# Etapa 2. Politici de rutare și economia din spatele lor

## 3. BGP routing policies in ISP networks

- An: 2005
- Pe scurt: De ce un AS preferă ruta prin client și nu re-exportă rutele de la un peer: relațiile comerciale traduse în LocalPref și filtre.
- Citare: M. Caesar, J. Rexford, „BGP routing policies in ISP networks”, *IEEE Network*, vol. 19, nr. 6, 2005, pp. 5-11, DOI 10.1109/MNET.2005.1541715. (În Crossref titlul apare cu greșeala de tipar „BPG”.)
- Link: [cs.princeton.edu/~jrex/papers/policies.pdf](https://www.cs.princeton.edu/~jrex/papers/policies.pdf)
- Conținut: procesul de decizie ca tabel ordonat (LocalPref, lungimea AS_PATH, ORIGIN, MED, eBGP peste iBGP, cost IGP, router ID), cele trei „butoane” de politică (preferință, filtrare, etichetare prin communities), relațiile de afaceri și felul în care se traduc în LocalPref și filtre de export, ingineria traficului, scalabilitatea și politicile de securitate.
- De ce: explică de ce un AS preferă ruta prin client chiar dacă e mai lungă și de ce nu re-exportă rutele primite de la un peer către alt peer. Fără asta, nici route leak-urile, nici felul în care se propagă o deturnare nu au sens.
- Timp: o oră.
- De citit: secțiunile 2 până la 6. Secțiunea 7 (direcții de cercetare din 2005) e opțională.

# Etapa 3. Extensiile care fac BGP-ul de azi

RFC 4271 descrie IPv4 unicast între AS-uri cu numere pe 2 octeți. Protocolul de azi adaugă capabilități negociate, alte familii de adrese, etichete pe rute și scalarea în interiorul AS-ului. Sunt texte de referință: la prima lectură ajung secțiunile indicate, iar la text se revine când un câmp anume apare în date. Trei extensii la fel de importante actualizează direct RFC 4271 și sunt în secțiunea „Actualizările RFC 4271”: 4724 (graceful restart), 6793 (AS pe 4 octeți) și 7606 (tratarea erorilor).

## 4. RFC 5492, Capabilities Advertisement with BGP-4

- An: 2009
- Pe scurt: Cum își spun doi vecini în OPEN ce extensii cunosc; MP-BGP, ADD-PATH, graceful restart și AS-urile pe 4 octeți se negociază așa.
- Citare: J. Scudder, R. Chandra, „Capabilities Advertisement with BGP-4”, RFC 5492, IETF, februarie 2009, DOI 10.17487/RFC5492.
- Link: [rfc-editor.org/rfc/rfc5492](https://www.rfc-editor.org/rfc/rfc5492)
- Relația: înlocuiește RFC 3392; actualizat de RFC 8810.
- Timp: 20 de minute.
- De citit: 3 și 4 (parametrul opțional Capabilities, tipul 2).

## 5. RFC 4760, Multiprotocol Extensions for BGP-4

- An: 2007
- Pe scurt: Atributele MP_REACH_NLRI și MP_UNREACH_NLRI, prin care BGP transportă IPv6 și alte familii de adrese (AFI și SAFI).
- Citare: T. Bates, R. Chandra, D. Katz, Y. Rekhter, „Multiprotocol Extensions for BGP-4”, RFC 4760, IETF, ianuarie 2007, DOI 10.17487/RFC4760.
- Link: [rfc-editor.org/rfc/rfc4760](https://www.rfc-editor.org/rfc/rfc4760)
- Relația: înlocuiește RFC 2858; actualizat de RFC 7606.
- De ce: parserul C citește prefixele IPv6 din exact aceste două atribute.
- Timp: 45 de minute.
- De citit: 3, 4, 5 și 8.

## 6. RFC 1997, BGP Communities Attribute

- An: 1996
- Pe scurt: Etichete de 4 octeți atașate rutelor, prin care operatorii își transmit politici; definește NO_EXPORT și NO_ADVERTISE.
- Citare: R. Chandra, P. Traina, T. Li, „BGP Communities Attribute”, RFC 1997, IETF, august 1996, DOI 10.17487/RFC1997.
- Link: [rfc-editor.org/rfc/rfc1997](https://www.rfc-editor.org/rfc/rfc1997)
- Relația: actualizat de RFC 7606 și RFC 8642.
- Timp: 20 de minute.
- De citit: tot textul (cinci pagini).

## 7. RFC 8092, BGP Large Communities Attribute

- An: 2017
- Pe scurt: Communities de 12 octeți, cu loc pentru un număr AS pe 4 octeți, pe care formatul din RFC 1997 nu îl mai poate cuprinde.
- Citare: J. Heitz (ed.), J. Snijders (ed.), K. Patel, I. Bagdonas, N. Hilliard, „BGP Large Communities Attribute”, RFC 8092, IETF, februarie 2017, DOI 10.17487/RFC8092.
- Link: [rfc-editor.org/rfc/rfc8092](https://www.rfc-editor.org/rfc/rfc8092)
- Timp: 20 de minute.
- De citit: 1, 3 și 5.

## 8. RFC 4456, BGP Route Reflection

- An: 2006
- Pe scurt: Cum scapă un AS mare de sesiunile iBGP între toate perechile de rutere: un reflector retransmite rutele către clienții săi.
- Citare: T. Bates, E. Chen, R. Chandra, „BGP Route Reflection: An Alternative to Full Mesh Internal BGP (IBGP)”, RFC 4456, IETF, aprilie 2006, DOI 10.17487/RFC4456.
- Link: [rfc-editor.org/rfc/rfc4456](https://www.rfc-editor.org/rfc/rfc4456)
- Relația: înlocuiește RFC 2796 și RFC 1966; actualizat de RFC 7606.
- Timp: 45 de minute.
- De citit: 4 până la 9.

## 9. RFC 5065, Autonomous System Confederations for BGP

- An: 2007
- Pe scurt: Cealaltă soluție pentru plasa completă iBGP: AS-ul se împarte în sub-AS-uri care apar în exterior ca unul singur.
- Citare: P. Traina, D. McPherson, J. Scudder, „Autonomous System Confederations for BGP”, RFC 5065, IETF, august 2007, DOI 10.17487/RFC5065.
- Link: [rfc-editor.org/rfc/rfc5065](https://www.rfc-editor.org/rfc/rfc5065)
- Relația: înlocuiește RFC 3065; actualizat de RFC 9774.
- Timp: 30 de minute.
- De citit: 2, 3 și 4.

## 10. RFC 7911, Advertisement of Multiple Paths in BGP

- An: 2016
- Pe scurt: ADD-PATH: un vecin anunță mai multe căi pentru același prefix, fiecare cu un Path Identifier, fără ca o cale nouă să o înlocuiască pe cea veche.
- Citare: D. Walton, A. Retana, E. Chen, J. Scudder, „Advertisement of Multiple Paths in BGP”, RFC 7911, IETF, iulie 2016, DOI 10.17487/RFC7911.
- Link: [rfc-editor.org/rfc/rfc7911](https://www.rfc-editor.org/rfc/rfc7911)
- Timp: 30 de minute.
- De citit: 2 până la 5.

# Etapa 4. Cum se comportă BGP în operare

## 11. Delayed Internet Routing Convergence

- An: 2000
- Pe scurt: După o retragere, BGP explorează căi alternative zeci de minute; rafala de UPDATE-uri tranzitorii nu e o deturnare.
- Citare: C. Labovitz, A. Ahuja, A. Bose, F. Jahanian, „Delayed Internet routing convergence”, *Proceedings of ACM SIGCOMM 2000*, pp. 175-187, DOI 10.1145/347059.347428.
- Link: [cs.princeton.edu, copie pentru curs](https://www.cs.princeton.edu/~jrex/teaching/spring2005/reading/labovitz00.pdf)
- Conținut: un studiu de doi ani în care autorii au injectat sute de mii de defecte de rutare de la puncte de schimb de trafic și au măsurat cât durează până când routerele ajung la o imagine consistentă. Rezultatul e de ordinul zecilor de minute, din cauza explorării căilor alternative în procesul de selecție și a temporizatoarelor. Articolul dă și un model al limitelor de convergență.
- De ce: un detector vede fluxul de UPDATE-uri în timp real. După o retragere sau o schimbare de rută urmează o rafală de anunțuri tranzitorii, iar acestea nu trebuie confundate cu o deturnare.
- Timp: 1,5 ore.
- De citit: rezumatul, 1, 2, 3, 4 și 7. Secțiunea 5 (modelul formal de convergență) e opțională.

## 12. BGP in 2025

- An: 2026
- Pe scurt: Ordinul de mărime al tabelei globale la sfârșitul lui 2025: circa 1.050.000 de prefixe IPv4, peste jumătate mai specifice.
- Citare: G. Huston, „BGP in 2025”, APNIC Blog, 8 ianuarie 2026.
- Link: [blog.apnic.net/2026/01/08/bgp-in-2025](https://blog.apnic.net/2026/01/08/bgp-in-2025/)
- Conținut: raportul anual al lui Huston despre tabela de rutare, pe baza datelor de la AS131072, RouteViews și RIPE RIS. La sfârșitul lui 2025 tabela IPv4 avea în jur de 1.050.000 de intrări (peste jumătate prefixe mai specifice), iar tabela IPv6 în jur de 241.800. Textul analizează creșterea și face prognoze până în 2030.
- De ce: dă ordinul de mărime al datelor pe care le procesează parserul MRT și arată cât de comune sunt prefixele mai specifice legitime, ceea ce explică de ce „a apărut un sub-prefix” nu este, singur, o dovadă de deturnare.
- Timp: o oră.
- De citit: „The BGP measurement environment”, „The IPv4 routing table data” și „Conclusion”. Partea de prognoză e opțională. Articolul pereche, „BGP Updates in 2025” (9 ianuarie 2026, același blog), tratează volumul de UPDATE-uri.

## 13. RFC 7454, BGP Operations and Security

- An: 2015
- Pe scurt: BCP 194: filtrele și protecțiile de sesiune pe care un operator ar trebui să le aibă înainte de RPKI.
- Citare: J. Durand, I. Pepelnjak, G. Doering, „BGP Operations and Security”, RFC 7454 (BCP 194), IETF, februarie 2015, DOI 10.17487/RFC7454.
- Link: [rfc-editor.org/rfc/rfc7454](https://www.rfc-editor.org/rfc/rfc7454)
- Conținut: practica operațională recomandată: protecția sesiunii (TCP-AO, GTSM), filtrarea prefixelor (prefixe speciale, nealocate, prea specifice, filtre generate din IRR), limita maximă de prefixe, filtrarea AS_PATH, route flap dampening și curățarea communities.
- De ce: e lista de filtre pe care un operator ar trebui să le aibă înainte să ajungă la RPKI. Majoritatea incidentelor din etapa 6 ar fi fost oprite de regulile din secțiunea 6.
- Timp: o oră.
- De citit: 4, 5, 6, 8 și 9. Tabelele cu prefixe speciale din 6.1 se consultă doar la nevoie.

# Etapa 5. De ce BGP e nesigur

## 14. Why is it taking so long to secure internet routing?

- An: 2014
- Pe scurt: Cea mai bună explicație scurtă a modelului de încredere și a celor trei apărări: filtrarea prefixelor, RPKI și BGPsec.
- Citare: S. Goldberg, „Why is it taking so long to secure internet routing?”, *Communications of the ACM*, vol. 57, nr. 10, 2014, pp. 56-63, DOI 10.1145/2659899. Același text a apărut în *ACM Queue*, vol. 12, nr. 8, 2014, DOI 10.1145/2668152.2668966.
- Link: [queue.acm.org/detail.cfm?id=2668966](https://queue.acm.org/detail.cfm?id=2668966) (text integral gratuit în browser; serverul refuză clienții automați)
- Conținut: deturnări de prefix și de sub-prefix, route leak-uri, atacuri de scurtare a căii, diferența dintre blackhole și interceptare, apoi trei apărări (filtrarea prefixelor, RPKI, BGPsec) cu beneficiile și problemele de adoptare ale fiecăreia.
- De ce: e cea mai bună explicație scurtă a modelului de încredere. Arată limpede ce prinde RPKI (origine neautorizată) și ce îi scapă (route leak, cale falsificată cu origine corectă), adică exact granița dintre ce verifică detectorul prin RPKI și ce trebuie verificat altfel.
- Timp: 45 de minute.
- De citit: tot articolul. Atenție la două detalii factuale: incidentul YouTube a avut loc în februarie 2008, iar prefixul YouTube era 208.65.152.0/22. Ambele sunt date corect în sursa 17.

## 15. RFC 4272, BGP Security Vulnerabilities Analysis

- An: 2006
- Pe scurt: Analiza IETF a vulnerabilităților, mesaj cu mesaj și câmp cu câmp: ce poate falsifica un vecin sau un intrus și ce rămâne neprotejat.
- Citare: S. Murphy, „BGP Security Vulnerabilities Analysis”, RFC 4272, IETF, ianuarie 2006, DOI 10.17487/RFC4272.
- Link: [rfc-editor.org/rfc/rfc4272](https://www.rfc-editor.org/rfc/rfc4272)
- De ce: Goldberg explică modelul; RFC 4272 arată în ce câmp al UPDATE-ului stă fiecare atac, inclusiv AS_PATH și NLRI, pe care le citește detectorul.
- Timp: o oră.
- De citit: 2, 3.1.5 (UPDATE) și 4.1 (riscul rezidual).

# Etapa 6. Incidente documentate

Pentru fiecare incident de mai jos, primul paragraf din „Conținut” rezumă ce s-a întâmplat la nivel de rutare, iar „De ce” spune ce s-a schimbat sau ce l-ar fi prevenit, după sursa respectivă. Sursele sunt post-mortem-uri ale unor operatori sau registre și articole de măsurare evaluate de colegi.

## 16. RFC 7908, Problem Definition and Classification of BGP Route Leaks

- An: 2016
- Pe scurt: Definiția route leak-ului și clasificarea în șase tipuri, fiecare cu incidente reale.
- Citare: K. Sriram, D. Montgomery, D. McPherson, E. Osterweil, B. Dickson, „Problem Definition and Classification of BGP Route Leaks”, RFC 7908, IETF, iunie 2016, DOI 10.17487/RFC7908.
- Link: [rfc-editor.org/rfc/rfc7908](https://www.rfc-editor.org/rfc/rfc7908)
- Conținut: definiția de lucru a route leak-ului (propagarea unui anunț dincolo de scopul intenționat, în contradicție cu politicile celor implicați) și o clasificare în șase tipuri, fiecare cu incidente reale citate.
- De ce: oferă vocabularul exact pentru referat, care deosebește o deturnare (origine falsă) de un leak (rută reală, propagată unde nu trebuia).
- Timp: 30 de minute.
- De citit: secțiunile 2 și 3.

## 17. YouTube Hijacking: A RIPE NCC RIS case study

- An: 2008
- Pe scurt: Cazul-școală de deturnare de sub-prefix, urmărit minut cu minut în datele RIS; e incidentul din vederea „YouTube 2008”.
- Citare: RIPE NCC, „YouTube Hijacking: A RIPE NCC RIS case study”, 17 martie 2008.
- Link: [ripe.net, studiul de caz](https://www.ripe.net/about-us/news/youtube-hijacking-a-ripe-ncc-ris-case-study/)
- Conținut: pe 24 februarie 2008, la 18:47 UTC, Pakistan Telecom (AS17557) a început să anunțe 208.65.153.0/24, un sub-prefix al lui 208.65.152.0/22 anunțat de YouTube (AS36561). Anunțul a fost propagat global de PCCW Global (AS3491). YouTube a anunțat același /24 la 20:07 și două /25 la 20:18, iar PCCW a retras rutele originate de Pakistan Telecom la 21:01. Analiza folosește RIS, RISwhois și BGPlay.
- De ce: e cazul-școală de deturnare de sub-prefix, exact ce semnalează detectorul ca `subprefix_hijack` (`HijackDetector`, în `src/BGPWatch.Core/Domain/`). Goldberg (sursa 14, secțiunea despre filtrarea prefixelor) arată că filtrele de prefix ale PCCW pe sesiunea cu clientul ar fi oprit incidentul.
- Timp: 20 de minute.
- De citit: tot textul.

## 18. Characterizing Large-scale Routing Anomalies: A Case Study of the China Telecom Incident

- An: 2013
- Pe scurt: Circa 50.000 de prefixe străine anunțate de China Telecom în 2010: ce se poate și ce nu se poate dovedi din datele publice.
- Citare: R. Hiran, N. Carlsson, P. Gill, „Characterizing Large-Scale Routing Anomalies: A Case Study of the China Telecom Incident”, *Passive and Active Measurement (PAM 2013)*, Lecture Notes in Computer Science, Springer, 2013, pp. 229-238, DOI 10.1007/978-3-642-36516-4_23.
- Link: [ida.liu.se/~nikca89/papers/pam13.pdf](https://www.ida.liu.se/~nikca89/papers/pam13.pdf)
- Conținut: pe 8 aprilie 2010, AS23724 (China Telecom) a anunțat aproximativ 50.000 de prefixe ale altor AS-uri. Peste 99% dintre ele coincideau cu prefixe existente în tabela globală, ceea ce susține ipoteza unei scurgeri a propriei tabele de rutare. O parte din trafic a fost interceptat și a ajuns totuși la destinație, pentru că unii furnizori preferau ruta prin client în locul celei prin China Telecom.
- De ce: arată ce se poate și ce nu se poate afla din datele publice (intenția nu poate fi dovedită tehnic) și de ce interceptarea e mai greu de observat decât un blackhole. Goldberg (sursa 14) explică de ce filtrarea prefixelor nu ar fi ajutat aici: anunțul a venit pe o legătură de peering, iar filtrele de prefix se aplică de obicei doar sesiunilor cu clienții.
- Timp: o oră.
- De citit: 1, 4, 5 și 6. Metodologia din 3 e opțională.

## 19. BGP leaks and cryptocurrencies

- An: 2018
- Pe scurt: O deturnare de sub-prefix a serverelor DNS Amazon Route 53, care a dat răspunsuri false pentru un portofel de criptomonede.
- Citare: L. Poinsignon, „BGP leaks and cryptocurrencies”, The Cloudflare Blog, 24 aprilie 2018.
- Link: [blog.cloudflare.com/bgp-leaks-and-crypto-currencies](https://blog.cloudflare.com/bgp-leaks-and-crypto-currencies/)
- Conținut: între aproximativ 11:05 și 12:55 UTC, AS10297 (eNet) a anunțat cinci prefixe /24 mai specifice din blocurile /23 ale serverelor DNS Amazon Route 53 (AS16509), propagate prin Hurricane Electric (AS6939). Serverele de pe adresele deturnate răspundeau doar pentru myetherwallet.com. Rezolvatoarele DNS care au urmat ruta falsă au primit răspunsuri false, inclusiv 1.1.1.1 în unele locații.
- De ce: e incidentul în care traficul unui serviciu anume a fost redirecționat, și arată că o deturnare de sub-prefix poate ținti un nivel superior (DNS). Postarea recomandă ROA-uri și validare RPKI, DNSSEC și filtrare la furnizorii de tranzit.
- Timp: 20 de minute.
- De citit: tot textul.

## 20. How Verizon and a BGP Optimizer Knocked Large Parts of the Internet Offline Today

- An: 2019
- Pe scurt: Un route leak amplificat de un optimizator BGP: prefixe mai specifice cu originea corectă, propagate de Verizon în tot Internetul.
- Citare: T. Strickx, „How Verizon and a BGP Optimizer Knocked Large Parts of the Internet Offline Today”, The Cloudflare Blog, 24 iunie 2019.
- Link: [blog.cloudflare.com, post-mortem iunie 2019](https://blog.cloudflare.com/how-verizon-and-a-bgp-optimizer-knocked-large-parts-of-the-internet-offline-today/)
- Conținut: de la 10:30 UTC, DQE Communications (AS33154) folosea un optimizator BGP care spărgea prefixe în bucăți mai specifice (de exemplu 104.20.0.0/20 al Cloudflare în două /21). DQE le-a trimis clientului Allegheny Technologies (AS396531), care le-a trimis mai departe celuilalt furnizor, Verizon (AS701), iar Verizon le-a propagat în tot Internetul.
- De ce: e un route leak de tip 1 în clasificarea din RFC 7908, agravat de prefixe mai specifice cu origine corectă. Regulile `origin_hijack` și `subprefix_hijack` ale detectorului nu îl semnalează, pentru că originea e cea așteptată. Îl prinde regula `rpki_invalid`, pentru că ROA-urile Cloudflare au maxLength /20 și un /21 devine invalid. Postarea enumeră ce l-ar fi oprit: filtre de prefix și limite de prefixe pe sesiunea cu clientul, filtre din IRR, validare RPKI și MANRS.
- Timp: 20 de minute.
- De citit: tot textul.

## 21. Profiling BGP Serial Hijackers

- An: 2019
- Pe scurt: Deturnările se repetă la aceleași AS-uri, luni sau ani la rând; trăsăturile acestor AS-uri ajută la prioritizarea alertelor.
- Citare: C. Testart, P. Richter, A. King, A. Dainotti, D. Clark, „Profiling BGP Serial Hijackers: Capturing Persistent Misbehavior in the Global Routing Table”, *Proceedings of the Internet Measurement Conference (IMC 2019)*, ACM, 2019, pp. 420-434, DOI 10.1145/3355369.3355581.
- Link: [people.csail.mit.edu, PDF-ul autoarei](https://people.csail.mit.edu/ctestart/publications/BGPserialHijackers.pdf)
- Conținut: pornind de la cinci ani de discuții pe listele operatorilor, autorii construiesc un set de AS-uri care deturnează în mod repetat și le compară cu AS-uri legitime: prezență intermitentă, prefixe de scurtă durată, spațiu de adrese fragmentat între registre, multe conflicte MOAS. Un clasificator antrenat pe aceste trăsături găsește în jur de 900 de AS-uri cu comportament similar în tabela IPv4.
- De ce: arată că deturnările se repetă adesea la aceleași AS-uri, timp de luni sau ani. Trăsăturile din secțiunea 5 sunt idei bune pentru a prioritiza alertele detectorului.
- Timp: 1,5 ore.
- De citit: 1, 2.1, 5 și 7. Partea de învățare automată din 6 e opțională.

# Etapa 7. Mitigări

## 22. NIST SP 800-189 Rev. 1, Border Gateway Protocol Security and Resilience

- An: 2025
- Pe scurt: Catalogul la zi al mitigărilor, cu recomandări numerotate, de la IRR și RPKI la ASPA, roluri BGP și GTSM.
- Citare: K. Sriram, D. Montgomery, *Border Gateway Protocol Security and Resilience*, NIST Special Publication 800-189 Rev. 1, ciornă publică inițială, 3 ianuarie 2025. Versiunea finală în vigoare este NIST SP 800-189, „Resilient Interdomain Traffic Exchange: BGP Security and DDoS Mitigation”, decembrie 2019, DOI 10.6028/NIST.SP.800-189.
- Link: [csrc.nist.gov/pubs/sp/800/189/r1/ipd](https://csrc.nist.gov/pubs/sp/800/189/r1/ipd)
- Conținut: catalogul actual al mitigărilor, cu recomandări numerotate: înregistrarea în IRR, certificarea resurselor în RPKI, validarea originii pe bază de ROA, categoriile de filtre de prefix pe tipuri de vecini, verificarea AS_PATH (BGPsec, ASPA, roluri BGP și OTC) și GTSM.
- De ce: e cea mai completă imagine la zi a mitigărilor, într-un singur document. Secțiunea 2 oferă și taxonomia amenințărilor (deturnări de prefix, deturnări de cale, route leak-uri), utilă în referat.
- Timp: 3 ore pentru secțiunile recomandate.
- De citit: 2, 4.1 până la 4.3 (inclusiv 4.3.1 despre deturnările cu origine falsificată), 4.4 până la 4.7. Secțiunea 5 (validarea adresei sursă și DDoS) iese din tema proiectului.

## 23. RFC 6480, An Infrastructure to Support Secure Internet Routing

- An: 2012
- Pe scurt: Arhitectura RPKI: certificate care urmează alocarea adreselor, ROA-uri, depozite și cache-ul local al validatorului.
- Citare: M. Lepinski, S. Kent, „An Infrastructure to Support Secure Internet Routing”, RFC 6480, IETF, februarie 2012, DOI 10.17487/RFC6480.
- Link: [rfc-editor.org/rfc/rfc6480](https://www.rfc-editor.org/rfc/rfc6480)
- Conținut: arhitectura RPKI: certificatele de resurse care urmează ierarhia de alocare, ROA-urile, depozitele și cache-ul local din care routerele își iau datele.
- De ce: e fundația pe care stau sursele 24 până la 27.
- Timp: 45 de minute.
- De citit: secțiunile 1 până la 3. Secțiunile 4 până la 7 (depozite, manifeste, rotirea cheilor) contează doar pentru rularea unui validator.

## 24. RFC 9582, A Profile for Route Origin Authorizations (ROAs)

- An: 2024
- Pe scurt: Ce conține un ROA: AS-ul autorizat, prefixele și maxLength, semnate de deținătorul blocului de adrese.
- Citare: J. Snijders, B. Maddison, M. Lepinski, D. Kong, S. Kent, „A Profile for Route Origin Authorizations (ROAs)”, RFC 9582, IETF, mai 2024, DOI 10.17487/RFC9582.
- Link: [rfc-editor.org/rfc/rfc9582](https://www.rfc-editor.org/rfc/rfc9582)
- Relația: înlocuiește RFC 6482.
- Timp: 30 de minute.
- De citit: 1, 1.2, 4 (în special 4.3.2.2, maxLength) și 5.

## 25. RFC 6811, BGP Prefix Origin Validation

- An: 2013
- Pe scurt: Algoritmul care dă unei rute starea Valid, Invalid sau NotFound pe baza VRP-urilor; regula `rpki_invalid` a detectorului îl implementează.
- Citare: P. Mohapatra, J. Scudder, D. Ward, R. Bush, R. Austein, „BGP Prefix Origin Validation”, RFC 6811, IETF, ianuarie 2013, DOI 10.17487/RFC6811.
- Link: [rfc-editor.org/rfc/rfc6811](https://www.rfc-editor.org/rfc/rfc6811)
- Relația: actualizat de RFC 8481 și RFC 8893.
- Conținut: algoritmul care dă unei rute starea Valid, Invalid sau NotFound pe baza VRP-urilor.
- De ce: funcția `RpkiValidity` din `HijackDetector` implementează secțiunea 2 și merită comparată rând cu rând cu pseudo-codul din 2.1.
- Timp: 45 de minute.
- De citit: integral (10 pagini).

## 26. RFC 9319, The Use of maxLength in the Resource Public Key Infrastructure (RPKI)

- An: 2022
- Pe scurt: De ce un maxLength prea larg lasă loc deturnărilor de sub-prefix cu origine falsificată; recomandă ROA-uri minimale.
- Citare: Y. Gilad, S. Goldberg, K. Sriram, J. Snijders, B. Maddison, „The Use of maxLength in the Resource Public Key Infrastructure (RPKI)”, RFC 9319 (BCP), IETF, octombrie 2022, DOI 10.17487/RFC9319.
- Link: [rfc-editor.org/rfc/rfc9319](https://www.rfc-editor.org/rfc/rfc9319)
- De ce: arată atacul pe care validarea originii nu îl prinde: un atacator care pune AS-ul victimei la capătul căii și anunță un sub-prefix acoperit de un maxLength prea larg primește starea Valid. ROA-ul din laborator are maxLength 24, deci orice `/25` e invalid.
- Timp: 30 de minute.
- De citit: 1, 3 și 5.

## 27. RFC 8210, The Resource Public Key Infrastructure (RPKI) to Router Protocol, Version 1

- An: 2017
- Pe scurt: Protocolul RTR, prin care ruterul primește de la un cache de încredere VRP-urile și cheile de ruter; versiunea 1 adaugă PDU-ul Router Key.
- Citare: R. Bush, R. Austein, „The Resource Public Key Infrastructure (RPKI) to Router Protocol, Version 1”, RFC 8210, IETF, septembrie 2017, DOI 10.17487/RFC8210.
- Link: [rfc-editor.org/rfc/rfc8210](https://www.rfc-editor.org/rfc/rfc8210)
- Relația: actualizează RFC 6810 (versiunea 0).
- De ce: e legătura dintre validator și ruter, cea configurată în FRR (sursa 42).
- Timp: 45 de minute.
- De citit: 1.2, 3, 4, 5.6 până la 5.8 și 8.

## 28. RPKI is Coming of Age

- An: 2019
- Pe scurt: O rută RPKI-invalidă nu e automat o deturnare: multe invalide sunt greșeli ale propriului deținător.
- Citare: T. Chung, E. Aben, T. Bruijnzeels, B. Chandrasekaran, D. Choffnes, D. Levin, B. M. Maggs, A. Mislove, R. van Rijswijk-Deij, J. Rula, N. Sullivan, „RPKI is Coming of Age: A Longitudinal Study of RPKI Deployment and Invalid Route Origins”, *Proceedings of the Internet Measurement Conference (IMC 2019)*, ACM, 2019, pp. 406-419, DOI 10.1145/3355369.3355596.
- Link: [cs.umd.edu/~dml/papers/rpki_imc19.pdf](https://www.cs.umd.edu/~dml/papers/rpki_imc19.pdf)
- Conținut: studiu longitudinal al tuturor ROA-urilor de la începutul RPKI, combinat cu anunțuri BGP de la peste 3.300 de colectoare. Autorii clasifică anunțurile invalide în tipuri de configurare greșită (de exemplu anunțuri prea specifice față de maxLength) și separă un rest de anunțuri potențial malițioase.
- De ce: concluzia lor e direct relevantă pentru detector. RPKI e bun pentru a filtra, dar o rută invalidă nu e automat o deturnare, pentru că multe invalide provin din greșeli ale propriului deținător. O alertă `rpki_invalid` are nevoie de context.
- Timp: 1,5 ore.
- De citit: 1, 2.3, 2.4, 5 și 8.

## 29. RFC 9234, Route Leak Prevention and Detection Using Roles in UPDATE and OPEN Messages

- An: 2022
- Pe scurt: Rolurile BGP din OPEN și atributul OTC opresc route leak-urile, clasa de incidente pe care validarea originii nu o acoperă.
- Citare: A. Azimov, E. Bogomazov, R. Bush, K. Patel, K. Sriram, „Route Leak Prevention and Detection Using Roles in UPDATE and OPEN Messages”, RFC 9234, IETF, mai 2022, DOI 10.17487/RFC9234.
- Link: [rfc-editor.org/rfc/rfc9234](https://www.rfc-editor.org/rfc/rfc9234)
- Conținut: capabilitatea BGP Role din mesajul OPEN (provider, customer, peer, route server) obligă cele două capete să fie de acord asupra relației, iar atributul OTC (Only to Customer) marchează rutele care nu mai au voie să urce spre un provider sau un peer.
- De ce: e răspunsul la clasa de incidente din sursa 20. FRRouting implementează rolurile (sursa 42), deci pot fi încercate în laborator.
- Timp: 45 de minute.
- De citit: secțiunile 1, 3.1, 4 și 5.

## 30. ASPA, BGP AS_PATH Verification Based on Autonomous System Provider Authorization Objects

- An: 2026
- Pe scurt: ASPA publică în RPKI furnizorii unui AS și verifică pe baza lor dacă un AS_PATH respectă relațiile declarate; încă draft, nu RFC.
- Citare: A. Azimov, E. Bogomazov, R. Bush, K. Patel, J. Snijders, K. Sriram, „BGP AS_PATH Verification Based on Autonomous System Provider Authorization (ASPA) Objects”, Internet-Draft draft-ietf-sidrops-aspa-verification-28, IETF, 24 august 2026 (lucrare în curs, nu este RFC la 11 octombrie 2026).
- Link: [datatracker.ietf.org, draftul ASPA](https://datatracker.ietf.org/doc/draft-ietf-sidrops-aspa-verification/)
- Conținut: fiecare AS publică în RPKI lista furnizorilor săi, iar ruterul care primește un anunț verifică dacă fiecare pas din AS_PATH respectă relațiile declarate.
- De ce: detectează route leak-urile și manipulările de AS_PATH, unde RFC 6811 nu ajută.
- Timp: 45 de minute.
- De citit: 1 și 5.1 până la 5.2, plus 8 (relația cu ROA, BGPsec și OTC).

## 31. RFC 8205, BGPsec Protocol Specification

- An: 2017
- Pe scurt: Semnături pe fiecare AS din cale, în atributul BGPsec_PATH care înlocuiește AS_PATH; protejează calea, nu doar originea.
- Citare: M. Lepinski (ed.), K. Sriram (ed.), „BGPsec Protocol Specification”, RFC 8205, IETF, septembrie 2017, DOI 10.17487/RFC8205.
- Link: [rfc-editor.org/rfc/rfc8205](https://www.rfc-editor.org/rfc/rfc8205)
- Relația: actualizat de RFC 8206.
- De ce: e apărarea standardizată împotriva căilor falsificate cu origine corectă. Goldberg (sursa 14) explică de ce adoptarea ei e grea.
- Timp: o oră.
- De citit: 1, 2, 3 și 5.1.

## 32. MANRS Implementation Guide for Network Operators

- An: online
- Pe scurt: Cele patru acțiuni MANRS pentru operatori, cu pașii concreți: obiecte în IRR, ROA-uri, filtre generate, validatoare.
- Citare: MANRS (Mutually Agreed Norms for Routing Security), „MANRS Implementation Guide for Network Operators”, document MANRS-007.01, versiunea online consultată la 5 octombrie 2026. Programul MANRS este administrat acum de Global Cyber Alliance.
- Link: [manrs.org/netops/guide](https://manrs.org/netops/guide/)
- Conținut: cele patru acțiuni MANRS pentru operatori (filtrare, anti-spoofing, coordonare, publicarea informațiilor de rutare), cu ghid de implementare: obiecte route în IRR, ROA-uri, filtre generate cu unelte ca bgpq4 și validatoare RPKI ca Routinator sau rpki-client, plus exemple de configurare pentru mai mulți furnizori de echipamente.
- De ce: arată cum se traduc recomandările din sursele 22, 23 și 25 în practica unui operator și cum se măsoară conformitatea. E și un argument bun pentru secțiunea de mitigări din referat.
- Timp: o oră.
- De citit: 3.2, 4.2 și 4.4. Dintre exemplele de configurare, doar cele pentru furnizorul de echipamente relevant.

# Etapa 8. Detecție și monitorizare

## 33. ARTEMIS: Neutralizing BGP Hijacking within a Minute

- An: 2018
- Pe scurt: Sistemul după care e modelat proiectul: taxonomia deturnărilor și detecția în timp real, rulată chiar de AS-ul victimă.
- Citare: P. Sermpezis, V. Kotronis, P. Gigis, X. Dimitropoulos, D. Cicalese, A. King, A. Dainotti, „ARTEMIS: Neutralizing BGP Hijacking Within a Minute”, *IEEE/ACM Transactions on Networking*, vol. 26, nr. 6, 2018, pp. 2471-2486, DOI 10.1109/TNET.2018.2869798.
- Link: [arxiv.org/abs/1801.01085](https://arxiv.org/abs/1801.01085) (versiunea v4, iunie 2018) și [codul sursă, FORTH-ICS-INSPIRE/artemis](https://github.com/FORTH-ICS-INSPIRE/artemis)
- Conținut: o taxonomie a deturnărilor după AS_PATH-ul anunțat (Type-0 pentru origine falsă, Type-N pentru căi falsificate, Type-U) și după prefixul afectat (exact, sub-prefix, squatting), o analiză a vizibilității lor în colectoarele publice și un sistem rulat de AS-ul victimă, care compară fluxurile în timp real cu o configurație locală a prefixelor și originilor proprii.
- De ce: e sistemul după care e modelat proiectul. Cu taxonomia din secțiunea 2 se poate spune exact ce tipuri acoperă detectorul (Type-0 exact și sub-prefix) și ce tipuri rămân în afara lui (Type-N, de exemplu).
- Timp: 2 ore.
- De citit: 1, 2, 4.2 și 5. Secțiunile 6 și 7 (mitigarea prin anunțuri mai specifice și experimentele reale) sunt opționale.

## 34. BGPalerter, documentația

- An: 2025
- Pe scurt: Monitorul open source al NTT peste RIS Live; regula lui `monitorHijack` e aproape identică cu detectorul proiectului.
- Citare: NTT, *BGPalerter*, software open source (licență BSD-3-Clause), versiunea 2.0.1, august 2025, documentația din directorul `docs` al depozitului.
- Link: [github.com/nttgin/BGPalerter](https://github.com/nttgin/BGPalerter) și [docs/configuration.md](https://github.com/nttgin/BGPalerter/blob/main/docs/configuration.md)
- Conținut: un monitor care se conectează la RIS Live și verifică prefixele declarate în `prefixes.yml`. Arhitectura are trei componente (conectori, monitoare, rapoarte). Monitoarele acoperă originea greșită și sub-prefixele cu altă origine (`monitorHijack`), pierderea vizibilității, prefixe noi mai specifice cu aceeași origine (`monitorNewPrefix`), anunțuri RPKI invalide, modificări de ROA și vecini neașteptați pe cale.
- De ce: e al doilea sistem de referință al proiectului. Regulile din `monitorHijack` sunt aproape identice cu regulile `origin_hijack` și `subprefix_hijack` ale detectorului, iar `monitorNewPrefix` acoperă exact cazul din sursa 20, pe care detectorul îl prinde doar prin RPKI. Parametrul `thresholdMinPeers` arată cum se evită alertele dintr-un singur punct de vedere.
- Timp: o oră.
- De citit: README, apoi din `configuration.md` secțiunile Connectors (connectorRIS) și Monitors, apoi `prefixes.md`.

## 35. RFC 6396, Multi-Threaded Routing Toolkit (MRT) Routing Information Export Format

- An: 2011
- Pe scurt: Formatul binar al arhivelor RIS și RouteViews, pe care îl citește parserul C al proiectului.
- Citare: L. Blunk, M. Karir, C. Labovitz, „Multi-Threaded Routing Toolkit (MRT) Routing Information Export Format”, RFC 6396, IETF, noiembrie 2011, DOI 10.17487/RFC6396.
- Link: [rfc-editor.org/rfc/rfc6396](https://www.rfc-editor.org/rfc/rfc6396)
- Conținut: formatul binar al fișierelor de rutare: TABLE_DUMP_V2 pentru instantanee, BGP4MP pentru UPDATE-uri.
- De ce: parserul C al proiectului citește exact acest format.
- Timp: o oră.
- De citit: secțiunile 2, 3, 4.3 (TABLE_DUMP_V2) și 4.4 (BGP4MP).

## 36. Route Collection Raw Data: MRT Files

- An: 2024
- Pe scurt: Schema de nume și frecvența fișierelor RIS, plus uneltele de parsare cu care se poate compara ieșirea parserului.
- Citare: RIPE NCC, „Route Collection Raw Data: MRT Files”, RIPE RIS Docs, actualizată în martie 2024.
- Link: [ris.ripe.net/docs/mrt](https://ris.ripe.net/docs/mrt/)
- Conținut: schema de nume a fișierelor, frecvența lor (instantanee la 8 ore, update-uri la 5 minute), colectoarele multi-hop și uneltele de parsare existente.
- De ce: lista de unelte (bgpdump, bgpscanner, BGPKit) oferă implementări de referință cu care se compară ieșirea parserului. Pentru RouteViews, arhiva e la [archive.routeviews.org](https://archive.routeviews.org/), cu date publicate sub licența CC BY 4.0.
- Timp: 30 de minute.
- De citit: pagina integral.

## 37. RIPE RIS: colectoarele și RIS Live

- An: online
- Pe scurt: De unde vin datele publice: colectoarele RRC cu sesiuni BGP la IXP-uri și multi-hop, și RIS Live, fluxul lor în timp aproape real.
- Citare: RIPE NCC, „Route collectors”, RIPE RIS Docs, actualizată la 28 septembrie 2026; RIPE NCC, „RIS Live Manual”, consultat la 11 octombrie 2026.
- Link: [ris.ripe.net/docs/route-collectors](https://ris.ripe.net/docs/route-collectors/) și [manualul RIS Live](https://ris-live.ripe.net/manual/)
- Conținut: RIS primește datele prin sesiuni BGP, ca AS12654. Cele mai multe colectoare stau în rețelele de peering ale unor IXP-uri, iar cele multi-hop, ca RRC00 din Amsterdam, acceptă sesiuni de oriunde. RIS Live dă mesajele BGP ca JSON, printr-un WebSocket filtrabil (`wss://ris-live.ripe.net/v1/ws/`) sau ca flux complet („firehose”), fără autentificare.
- De ce: datele incidentului din 2008 pe care le redă aplicația vin de la colectorul rrc00. RIS Live e sursa în timp real a BGPalerter (sursa 34).
- Timp: 30 de minute.
- De citit: pagina colectoarelor, apoi din manual „WebSocket interface” și „ris_subscribe”.

## 38. RFC 7854, BGP Monitoring Protocol (BMP)

- An: 2016
- Pe scurt: Protocolul prin care un ruter își raportează sesiunile și rutele unui monitor; ruterele din laborator raportează așa aplicației.
- Citare: J. Scudder (ed.), R. Fernando, S. Stuart, „BGP Monitoring Protocol (BMP)”, RFC 7854, IETF, iunie 2016, DOI 10.17487/RFC7854.
- Link: [rfc-editor.org/rfc/rfc7854](https://www.rfc-editor.org/rfc/rfc7854)
- Relația: actualizat de RFC 8671 (Adj-RIB-Out), RFC 9069 (Loc-RIB), RFC 9515 și RFC 9736.
- De ce: stația BMP a aplicației (`src/BGPWatch.Web/Bmp/`) citește exact mesajele din secțiunea 4.
- Timp: o oră.
- De citit: 3, 4.1 până la 4.6, 4.9, 4.10 și 5.

## 39. RFC 9069, Support for Local RIB in the BGP Monitoring Protocol (BMP)

- An: 2022
- Pe scurt: Extensia BMP pentru Loc-RIB, rutele alese de procesul de decizie; exact ce trimite fiecare ruter al laboratorului.
- Citare: T. Evens, S. Bayraktar, M. Bhardwaj, P. Lucente, „Support for Local RIB in the BGP Monitoring Protocol (BMP)”, RFC 9069, IETF, februarie 2022, DOI 10.17487/RFC9069.
- Link: [rfc-editor.org/rfc/rfc9069](https://www.rfc-editor.org/rfc/rfc9069)
- Relația: actualizează RFC 7854; actualizat de RFC 9736.
- De ce: ruterele din laborator raportează cu `bmp monitor ipv4 unicast loc-rib`; stația aplicației păstrează doar rutele cu tipul de peer Loc-RIB Instance (3).
- Timp: 30 de minute.
- De citit: 1, 3, 4.1 și 5.

## 40. BGPStream: A Software Framework for Live and Historical BGP Data Analysis

- An: 2016
- Pe scurt: Cadrul care unifică RIS și RouteViews într-un singur flux ordonat în timp, cu bibliotecă C și legături Python.
- Citare: C. Orsini, A. King, D. Giordano, V. Giotsas, A. Dainotti, „BGPStream: A Software Framework for Live and Historical BGP Data Analysis”, *Proceedings of the Internet Measurement Conference (IMC 2016)*, ACM, 2016, pp. 429-444, DOI 10.1145/2987443.2987482.
- Link: [caida.org, PDF-ul articolului](https://www-old.caida.org/publications/papers/2016/bgpstream/bgpstream.pdf) și [bgpstream.caida.org](https://bgpstream.caida.org/)
- Conținut: arhitectura unui cadru care unifică RIS și RouteViews într-un singur flux ordonat în timp, cu unealtă în linie de comandă, bibliotecă C și legături Python, plus aplicații de monitorizare construite pe el.
- De ce: e alternativa matură la combinația de parser C și aplicație C# a proiectului. Secțiunea 3 explică problemele pe care le întâlnește orice astfel de sistem (sortarea înregistrărilor din colectoare diferite, date întârziate).
- Timp: 1,5 ore.
- De citit: 1, 3 și 4.2 (legăturile Python), apoi tutorialul PyBGPStream de pe site.

# Etapa 9. Practică

## 41. BGP Labs

- An: online
- Pe scurt: Exerciții de configurare pe FRRouting, rulabile în containere, de la sesiuni de bază la LocalPref și communities.
- Citare: I. Pepelnjak, *BGP Labs*, ipSpace.net, proiect online de exerciții, consultat la 5 octombrie 2026.
- Link: [bgplabs.net](https://bgplabs.net/)
- Conținut: exerciții de configurare orchestrate cu netlab, rulabile în containere sau în GitHub Codespaces, cu FRRouting printre platformele suportate. Sunt grupate în sesiuni de bază, politici (LocalPref, MED, prepending, communities, filtrarea rutelor de tranzit) și scenarii mai complexe.
- De ce: laboratorul proiectului rulează deja FRRouting în Docker, deci exercițiile se potrivesc direct. Politicile din etapa 2 devin configurații care pot fi văzute funcționând.
- Timp: 30 până la 60 de minute pe exercițiu.
- De făcut: „Configuring BGP on FRRouting”, apoi exercițiile de sesiune de bază, „Filter Transit Routes”, „Filter Advertised Prefixes”, „Select Preferred Uplink with BGP Local Preference”, „Use AS-Path Prepending to Influence Incoming Traffic Flow” și cele două despre communities.

## 42. FRRouting User Guide, capitolul BGP

- An: online
- Pe scurt: Cum se pornesc validarea originii prin RTR și rolurile BGP pe un ruter FRR, ca în laboratorul proiectului.
- Citare: FRRouting Project, *FRRouting User Guide*, capitolul „BGP”, secțiunile „Prefix Origin Validation Using RPKI” și „BGP Roles and Only to Customers”, documentația versiunii curente, consultată la 5 octombrie 2026.
- Link: [docs.frrouting.org, RPKI în bgpd](https://docs.frrouting.org/en/latest/bgp.html#prefix-origin-validation-using-rpki) și [rolurile BGP](https://docs.frrouting.org/en/latest/bgp.html#bgp-roles-and-only-to-customers)
- Conținut: conectarea bgpd la un cache RPKI prin protocolul RTR, potrivirea stării de validare în route-map-uri (`match rpki valid|invalid|notfound`), comenzile `show rpki` și configurarea `local-role` din RFC 9234, cu modul strict și atributul OTC.
- De ce: permite adăugarea în laboratorul izolat a unui router care aplică ROV sau roluri BGP și verificarea faptului că detectorul vede ce vede și routerul. Cache-ul RTR are nevoie de o sursă de VRP-uri. În rețeaua izolată, aceasta trebuie să fie locală, de exemplu un server RTR ca StayRTR, care acceptă și aserțiuni locale prin SLURM (RFC 8416).
- Timp: 2 ore, cu configurarea.
- De citit: cele două secțiuni numite, inclusiv exemplul de configurare RPKI de la finalul primei.

## 43. RIPEstat Data API

- An: online
- Pe scurt: API-ul JSON peste datele RIS și RPKI: verificarea pe date reale a celor citite, fără niciun anunț BGP.
- Citare: RIPE NCC, *RIPEstat Data API*, documentație online, consultată la 5 octombrie 2026.
- Link: [stat.ripe.net/docs/02.data-api](https://stat.ripe.net/docs/02.data-api/)
- Conținut: interfața JSON peste datele RIS și RPKI: `routing-status`, `rpki-validation`, `bgp-updates`, `bgplay`, `looking-glass`, `routing-history`. Pentru utilizare regulată de peste 1.000 de cereri pe zi se cere parametrul `sourceapp`, iar limita e de 8 cereri simultane de pe aceeași adresă IP.
- De ce: e cel mai rapid mod de a verifica pe date reale cele citite, fără a anunța nimic. Exercițiile de mai jos au fost rulate pe 5 octombrie 2026.
- Timp: o oră.
- De făcut: o cerere `rpki-validation` pentru AS13335 și 104.20.0.0/20 (rezultat valid, maxLength 20), apoi una pentru 104.20.0.0/21 (rezultat `invalid_length`), care arată de ce leak-ul din sursa 20 e invalid RPKI. O cerere `routing-status` pentru 208.65.152.0/22 arată cine originează azi prefixul YouTube din sursa 17 și câți peeri RIS îl văd. Pentru incidentul însuși, `bgplay` nu întoarce date din 2008, deci sursa e arhiva RIS: fișierul `rrc00/2008.02/updates.20080224.1845.gz` de pe [data.ris.ripe.net](https://data.ris.ripe.net/rrc00/2008.02/) conține anunțurile pentru 208.65.153.0/24 cu AS17557 în cale. Ieșirea parserului MRT al proiectului, rulat pe acest fișier, se compară apoi cu cronologia din sursa 17.

## 44. bgp.tools

- An: online
- Pe scurt: Explorator gratuit al rutării globale: pagina unui prefix sau a unui AS arată originea, furnizorii, vecinii și validarea RPKI.
- Citare: Port 179 Ltd, *bgp.tools*, serviciu online, consultat la 11 octombrie 2026.
- Link: [bgp.tools](https://bgp.tools/)
- Conținut: căutare după AS, prefix sau nume DNS. Pagina unui prefix dă originea și numele AS-ului, registrul și data înregistrării, furnizorii și vecinii din graful de conectivitate, whois, DNS și validarea; site-ul are și un looking glass. Datele sunt aproape în timp real și gratuite; monitorizarea și API-ul sunt pentru utilizatorii plătitori.
- De ce: e modelul vederii „Prefixe” a aplicației: prefixul ca obiect, cu originea ca prim fapt. Pe 11 octombrie 2026, pagina lui 208.65.152.0/22 arăta originea AS43515 (Google Ireland) și nota că blocul YouTube din 2008 nu e vizibil acum în tabela globală.
- Timp: 20 de minute.
- De făcut: pagina lui AS13335 și cea a prefixului 208.65.152.0/22, comparate cu `routing-status` din RIPEstat (sursa 43).
