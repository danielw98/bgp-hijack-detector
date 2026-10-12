---
title: "BGP: a reading list for the PTIRC project"
subtitle: "An ordered path from the protocol to route-hijack detection"
date: "Checked on 5 and 11 October 2026"
lang: en
---

# How to use this list

The list has 44 sources in nine stages, in the order in which they are worth reading, plus the lineage of the BGP specification: the six RFCs through which the protocol reached its current version and the twelve that update it. Each stage builds on the one before. Every source has its year, a one-line summary, a full citation, a free and legal link and, for most, its contents, why it is on the list, how long it takes and which sections are worth reading. The sources were opened and read or carefully skimmed on 5 and 11 October 2026, and the links were checked on 11 October 2026, except ACM Queue (source 14), which refuses automated clients and was opened in a browser on 5 October 2026.

Times are estimates for a careful reading with notes.

# Start here

These five sources give the most understanding per hour invested. Together they take about five hours and cover the protocol's mechanism, the policies that govern it, why it is insecure, and a real incident seen in routing data.

1. Peterson and Davie, section 4.1.2 (source 1): the big picture.
2. Caesar and Rexford, “BGP routing policies in ISP networks” (source 3): why BGP looks the way it does.
3. RFC 4271, the sections listed (source 2): the exact mechanics of UPDATEs and of the decision process.
4. Goldberg, “Why is it taking so long to secure internet routing?” (source 14): the trust model and the defenses.
5. RIPE NCC, the YouTube case study (source 17): a sub-prefix hijack traced minute by minute.

# Stage 1. How BGP works

## 1. Computer Networks: A Systems Approach, section 4.1.2

- Year: 2024
- Summary: The shortest introduction to interdomain routing: AS types, path vector, business relationships and iBGP.
- Citation: L. Peterson, B. Davie, *Computer Networks: A Systems Approach*, 6th edition, open online edition (CC BY 4.0 license, version 6.2-dev, © 2024), section 4.1.2 “Interdomain Routing (BGP)”.
- Link: [book.systemsapproach.org/scaling/global.html](https://book.systemsapproach.org/scaling/global.html)
- Contents: the problems of interdomain routing, the AS types (stub, multihomed, transit), the path-vector idea and loop detection, the customer, provider and peer relationships with their export policies, then the integration with internal routing through iBGP.
- Why: a short, clear introduction that provides the vocabulary and the mental model the rest of the list builds on, without the RFC's format details.
- Time: 45 minutes.
- Read: all of section 4.1.2. Section 4.1.1 (Routing Areas, on OSPF) can be skipped.

## 2. RFC 4271, A Border Gateway Protocol 4 (BGP-4)

- Year: 2006
- Summary: The current BGP-4 specification: the messages, the path attributes, the state machine and the decision process.
- Citation: Y. Rekhter, T. Li, S. Hares (eds.), “A Border Gateway Protocol 4 (BGP-4)”, RFC 4271, IETF, January 2006, DOI 10.17487/RFC4271.
- Link: [rfc-editor.org/rfc/rfc4271](https://www.rfc-editor.org/rfc/rfc4271)
- Relation: obsoletes RFC 1771; twelve RFCs update it, all listed under “Updates to RFC 4271”.
- Contents: the protocol specification: the OPEN, UPDATE, KEEPALIVE and NOTIFICATION messages, the path attributes, the session state machine and the decision process. The earlier versions are in the lineage below.
- Why: the detector reads exactly the fields described here. A route's origin is the last AS in AS_PATH, and a single UPDATE can carry withdrawn and announced prefixes at the same time, which matters when parsing MRT.
- Time: 2 to 3 hours for the recommended sections.
- Read closely: 1.1 (terms), 3 and 3.1 to 3.2 (Summary of Operation, Adj-RIB-In, Loc-RIB, Adj-RIB-Out), 4.3 (the UPDATE format), 5 and 5.1.1 to 5.1.5 (ORIGIN, AS_PATH, NEXT_HOP, MULTI_EXIT_DISC, LOCAL_PREF), 9.1 with 9.1.2.2 (the decision process and its tie-breaking rules), 9.1.4 (overlapping routes, that is, more-specific prefixes), 9.2.1.1 (MinRouteAdvertisementIntervalTimer, useful for source 11) and Security Considerations.
- Skip: 6 (error handling), 7, 8 (the state machine, over 35 pages), 10 and appendices A to F. Section 8 becomes useful only when debugging a session that does not come up in the lab.

# Lineage of the BGP specification

Four protocol versions in six RFCs, from BGP-1 (1989) to RFC 4271 (2006). The relations are those of the RFC Editor (“obsoletes”, “obsoleted by”), and what changed comes from the comparison appendices of each document.

## RFC 1105, Border Gateway Protocol (BGP)

- Year: 1989
- Version: BGP-1
- Relation: obsoleted by RFC 1163
- What changed: The first specification, with experimental status, built on the experience with EGP in NSFNET; it runs over TCP, on port 179, and describes the links between ASes as up, down and horizontal relationships.
- Citation: K. Lougheed, Y. Rekhter, “Border Gateway Protocol (BGP)”, RFC 1105, IETF, June 1989, DOI 10.17487/RFC1105.
- Link: [rfc-editor.org/rfc/rfc1105](https://www.rfc-editor.org/rfc/rfc1105)

## RFC 1163, Border Gateway Protocol (BGP)

- Year: 1990
- Version: BGP-2
- Relation: obsoletes RFC 1105; obsoleted by RFC 1267
- What changed: UPDATE gets the format with multiple path attributes, Hold Time and the version move into the OPEN message, OPEN CONFIRM disappears (KEEPALIVE does the confirming), and the up, down and horizontal relationships are removed.
- Citation: K. Lougheed, Y. Rekhter, “Border Gateway Protocol (BGP)”, RFC 1163, IETF, June 1990, DOI 10.17487/RFC1163.
- Link: [rfc-editor.org/rfc/rfc1163](https://www.rfc-editor.org/rfc/rfc1163)

## RFC 1267, Border Gateway Protocol 3 (BGP-3)

- Year: 1991
- Version: BGP-3
- Relation: obsoletes RFC 1163
- What changed: OPEN gets the BGP Identifier field, which detects a collision between two connections of the same neighbors; NEXT_HOP can point to a router outside the speaker's AS; the exchange of withdrawn routes is simplified.
- Citation: K. Lougheed, Y. Rekhter, “Border Gateway Protocol 3 (BGP-3)”, RFC 1267, IETF, October 1991, DOI 10.17487/RFC1267.
- Link: [rfc-editor.org/rfc/rfc1267](https://www.rfc-editor.org/rfc/rfc1267)

## RFC 1654, A Border Gateway Protocol 4 (BGP-4)

- Year: 1994
- Version: BGP-4
- Relation: obsoleted by RFC 1771; the RFC Editor does not list it as obsoleting RFC 1267
- What changed: BGP-4 announces IP prefixes instead of network classes (CIDR), with AS_PATH redefined for aggregation; LOCAL_PREF, ATOMIC_AGGREGATE and AGGREGATOR appear, INTER_AS_METRIC becomes MULTI_EXIT_DISC, and Hold Time is negotiated per connection.
- Citation: Y. Rekhter, T. Li (eds.), “A Border Gateway Protocol 4 (BGP-4)”, RFC 1654, IETF, July 1994, DOI 10.17487/RFC1654.
- Link: [rfc-editor.org/rfc/rfc1654](https://www.rfc-editor.org/rfc/rfc1654)

## RFC 1771, A Border Gateway Protocol 4 (BGP-4)

- Year: 1995
- Version: BGP-4
- Relation: obsoletes RFC 1654; obsoleted by RFC 4271
- What changed: The same version 4, advanced from Proposed Standard to Draft Standard; the appendices compare the text with RFC 1267, 1163 and 1105, not with RFC 1654.
- Citation: Y. Rekhter, T. Li, “A Border Gateway Protocol 4 (BGP-4)”, RFC 1771, IETF, March 1995, DOI 10.17487/RFC1771.
- Link: [rfc-editor.org/rfc/rfc1771](https://www.rfc-editor.org/rfc/rfc1771)

## RFC 4271, A Border Gateway Protocol 4 (BGP-4)

- Year: 2006
- Version: BGP-4, current
- Relation: obsoletes RFC 1771; updated by the twelve RFCs in the next section
- What changed: Folds in route reflection, confederations and route refresh, adds the limit on the prefixes accepted from a neighbor, clarifies the decision process and the NEXT_HOP types, removes authentication through the Marker field and requires support for TCP MD5.
- Citation: source 2.
- Link: [rfc-editor.org/rfc/rfc4271](https://www.rfc-editor.org/rfc/rfc4271)

# Updates to RFC 4271

Twelve RFCs modify RFC 4271 without obsoleting it (the RFC Editor's “updated by” relation, as of 11 October 2026). Four of them bear directly on security: 7606, 7607, 8212 and 9774.

## RFC 4724, Graceful Restart Mechanism for BGP

- Year: 2007
- Relation: updates RFC 4271; updated by RFC 8538
- What changed: A router that restarts its BGP session can keep its forwarding table, and the End-of-RIB marker tells the neighbor that all the initial routes have been sent.
- Citation: S. Sangli, E. Chen, R. Fernando, J. Scudder, Y. Rekhter, “Graceful Restart Mechanism for BGP”, RFC 4724, IETF, January 2007, DOI 10.17487/RFC4724.
- Link: [rfc-editor.org/rfc/rfc4724](https://www.rfc-editor.org/rfc/rfc4724)

## RFC 6286, Autonomous-System-Wide Unique BGP Identifier for BGP-4

- Year: 2011
- Relation: updates RFC 4271
- What changed: The BGP Identifier becomes a nonzero 4-octet integer that only has to be unique within the AS.
- Citation: E. Chen, J. Yuan, “Autonomous-System-Wide Unique BGP Identifier for BGP-4”, RFC 6286, IETF, June 2011, DOI 10.17487/RFC6286.
- Link: [rfc-editor.org/rfc/rfc6286](https://www.rfc-editor.org/rfc/rfc6286)

## RFC 6608, Subcodes for BGP Finite State Machine Error

- Year: 2012
- Relation: updates RFC 4271
- What changed: Subcodes for the state machine error in NOTIFICATION, which tell in which session state (OpenSent, OpenConfirm, Established) the unexpected message arrived.
- Citation: J. Dong, M. Chen, A. Suryanarayana, “Subcodes for BGP Finite State Machine Error”, RFC 6608, IETF, May 2012, DOI 10.17487/RFC6608.
- Link: [rfc-editor.org/rfc/rfc6608](https://www.rfc-editor.org/rfc/rfc6608)

## RFC 6793, BGP Support for Four-Octet Autonomous System (AS) Number Space

- Year: 2012
- Relation: updates RFC 4271; obsoletes RFC 4893
- What changed: 4-octet AS numbers, negotiated as a capability; toward old neighbors the path travels in the AS4_PATH attribute, and AS_TRANS (23456) appears in AS_PATH. The project's C parser performs exactly this merge of AS_PATH with AS4_PATH.
- Citation: Q. Vohra, E. Chen, “BGP Support for Four-Octet Autonomous System (AS) Number Space”, RFC 6793, IETF, December 2012, DOI 10.17487/RFC6793.
- Link: [rfc-editor.org/rfc/rfc6793](https://www.rfc-editor.org/rfc/rfc6793)

## RFC 7606, Revised Error Handling for BGP UPDATE Messages

- Year: 2015
- Relation: updates RFC 1997, 4271, 4360, 4456, 4760, 5543, 5701 and 6368
- What changed: A malformed attribute no longer resets the session: the UPDATE is treated as a withdrawal (treat-as-withdraw) or the attribute is discarded, and the session's other routes stay.
- Citation: E. Chen (ed.), J. Scudder (ed.), P. Mohapatra, K. Patel, “Revised Error Handling for BGP UPDATE Messages”, RFC 7606, IETF, August 2015, DOI 10.17487/RFC7606.
- Link: [rfc-editor.org/rfc/rfc7606](https://www.rfc-editor.org/rfc/rfc7606)

## RFC 7607, Codification of AS 0 Processing

- Year: 2015
- Relation: updates RFC 4271
- What changed: AS 0 is forbidden in OPEN, AS_PATH, AS4_PATH, AGGREGATOR and AS4_AGGREGATOR.
- Citation: W. Kumari, R. Bush, H. Schiller, K. Patel, “Codification of AS 0 Processing”, RFC 7607, IETF, August 2015, DOI 10.17487/RFC7607.
- Link: [rfc-editor.org/rfc/rfc7607](https://www.rfc-editor.org/rfc/rfc7607)

## RFC 7705, Autonomous System Migration Mechanisms and Their Effects on the BGP AS_PATH Attribute

- Year: 2015
- Relation: updates RFC 4271
- What changed: Documents the AS number migration mechanisms in de facto use (Local AS, No Prepend Inbound and others), which change what appears in AS_PATH.
- Citation: W. George, S. Amante, “Autonomous System Migration Mechanisms and Their Effects on the BGP AS_PATH Attribute”, RFC 7705, IETF, December 2015, DOI 10.17487/RFC7705.
- Link: [rfc-editor.org/rfc/rfc7705](https://www.rfc-editor.org/rfc/rfc7705)

## RFC 8212, Default External BGP (EBGP) Route Propagation Behavior without Policies

- Year: 2017
- Relation: updates RFC 4271
- What changed: An eBGP session with no explicit import policy accepts no routes, and with no export policy it announces nothing. FRR applies this by default (`bgp ebgp-requires-policy`); the lab turns it off in `lab/frr/*/frr.conf`.
- Citation: J. Mauch, J. Snijders, G. Hankins, “Default External BGP (EBGP) Route Propagation Behavior without Policies”, RFC 8212, IETF, July 2017, DOI 10.17487/RFC8212.
- Link: [rfc-editor.org/rfc/rfc8212](https://www.rfc-editor.org/rfc/rfc8212)

## RFC 8654, Extended Message Support for BGP

- Year: 2019
- Relation: updates RFC 4271
- What changed: Messages can be up to 65,535 octets instead of 4,096, except OPEN and KEEPALIVE.
- Citation: R. Bush, K. Patel, D. Ward, “Extended Message Support for BGP”, RFC 8654, IETF, October 2019, DOI 10.17487/RFC8654.
- Link: [rfc-editor.org/rfc/rfc8654](https://www.rfc-editor.org/rfc/rfc8654)

## RFC 9072, Extended Optional Parameters Length for BGP OPEN Message

- Year: 2021
- Relation: updates RFC 4271
- What changed: The optional parameters of OPEN, where the capabilities sit, can exceed 255 octets, compatibly with old implementations.
- Citation: E. Chen, J. Scudder, “Extended Optional Parameters Length for BGP OPEN Message”, RFC 9072, IETF, July 2021, DOI 10.17487/RFC9072.
- Link: [rfc-editor.org/rfc/rfc9072](https://www.rfc-editor.org/rfc/rfc9072)

## RFC 9687, Border Gateway Protocol 4 (BGP-4) Send Hold Timer

- Year: 2024
- Relation: updates RFC 4271
- What changed: SendHoldTimer: the router closes the session when the neighbor stops reading messages, without waiting for the neighbor to close it.
- Citation: J. Snijders, B. Cartwright-Cox, Y. Qu, “Border Gateway Protocol 4 (BGP-4) Send Hold Timer”, RFC 9687, IETF, November 2024, DOI 10.17487/RFC9687.
- Link: [rfc-editor.org/rfc/rfc9687](https://www.rfc-editor.org/rfc/rfc9687)

## RFC 9774, Deprecation of AS_SET and AS_CONFED_SET in BGP

- Year: 2025
- Relation: updates RFC 4271 and RFC 5065; obsoletes RFC 6472
- What changed: AS_SET and AS_CONFED_SET are forbidden when originating routes, so that a route's origin is unambiguous and the security mechanisms are simpler.
- Citation: W. Kumari, K. Sriram, L. Hannachi, J. Haas, “Deprecation of AS_SET and AS_CONFED_SET in BGP”, RFC 9774, IETF, May 2025, DOI 10.17487/RFC9774.
- Link: [rfc-editor.org/rfc/rfc9774](https://www.rfc-editor.org/rfc/rfc9774)

# Stage 2. Routing policy and the economics behind it

## 3. BGP routing policies in ISP networks

- Year: 2005
- Summary: Why an AS prefers the route through a customer and does not re-export routes from a peer: business relationships translated into LocalPref and filters.
- Citation: M. Caesar, J. Rexford, “BGP routing policies in ISP networks”, *IEEE Network*, vol. 19, no. 6, 2005, pp. 5-11, DOI 10.1109/MNET.2005.1541715. (Crossref lists the title with the typo “BPG”.)
- Link: [cs.princeton.edu/~jrex/papers/policies.pdf](https://www.cs.princeton.edu/~jrex/papers/policies.pdf)
- Contents: the decision process as an ordered table (LocalPref, AS_PATH length, ORIGIN, MED, eBGP over iBGP, IGP cost, router ID), the three policy “knobs” (preference, filtering, tagging with communities), the business relationships and how they translate into LocalPref and export filters, traffic engineering, scalability and security policies.
- Why: it explains why an AS prefers the route through a customer even when it is longer, and why it does not re-export routes learned from one peer to another peer. Without this, neither route leaks nor the way a hijack propagates make sense.
- Time: one hour.
- Read: sections 2 to 6. Section 7 (research directions as of 2005) is optional.

# Stage 3. The extensions that make today's BGP

RFC 4271 describes IPv4 unicast between ASes with 2-octet numbers. Today's protocol adds negotiated capabilities, other address families, route tags and scaling inside the AS. These are reference texts: the sections listed are enough for a first reading, and the rest is worth returning to when a particular field turns up in the data. Three equally important extensions update RFC 4271 directly and are listed under “Updates to RFC 4271”: 4724 (graceful restart), 6793 (4-octet AS numbers) and 7606 (error handling).

## 4. RFC 5492, Capabilities Advertisement with BGP-4

- Year: 2009
- Summary: How two neighbors tell each other in OPEN which extensions they support; MP-BGP, ADD-PATH, graceful restart and 4-octet AS numbers are negotiated this way.
- Citation: J. Scudder, R. Chandra, “Capabilities Advertisement with BGP-4”, RFC 5492, IETF, February 2009, DOI 10.17487/RFC5492.
- Link: [rfc-editor.org/rfc/rfc5492](https://www.rfc-editor.org/rfc/rfc5492)
- Relation: obsoletes RFC 3392; updated by RFC 8810.
- Time: 20 minutes.
- Read: 3 and 4 (the Capabilities optional parameter, type 2).

## 5. RFC 4760, Multiprotocol Extensions for BGP-4

- Year: 2007
- Summary: The MP_REACH_NLRI and MP_UNREACH_NLRI attributes, through which BGP carries IPv6 and other address families (AFI and SAFI).
- Citation: T. Bates, R. Chandra, D. Katz, Y. Rekhter, “Multiprotocol Extensions for BGP-4”, RFC 4760, IETF, January 2007, DOI 10.17487/RFC4760.
- Link: [rfc-editor.org/rfc/rfc4760](https://www.rfc-editor.org/rfc/rfc4760)
- Relation: obsoletes RFC 2858; updated by RFC 7606.
- Why: the C parser reads IPv6 prefixes from exactly these two attributes.
- Time: 45 minutes.
- Read: 3, 4, 5 and 8.

## 6. RFC 1997, BGP Communities Attribute

- Year: 1996
- Summary: 4-octet tags attached to routes, through which operators signal policy to each other; defines NO_EXPORT and NO_ADVERTISE.
- Citation: R. Chandra, P. Traina, T. Li, “BGP Communities Attribute”, RFC 1997, IETF, August 1996, DOI 10.17487/RFC1997.
- Link: [rfc-editor.org/rfc/rfc1997](https://www.rfc-editor.org/rfc/rfc1997)
- Relation: updated by RFC 7606 and RFC 8642.
- Time: 20 minutes.
- Read: the whole text (five pages).

## 7. RFC 8092, BGP Large Communities Attribute

- Year: 2017
- Summary: 12-octet communities, with room for a 4-octet AS number, which the RFC 1997 format can no longer hold.
- Citation: J. Heitz (ed.), J. Snijders (ed.), K. Patel, I. Bagdonas, N. Hilliard, “BGP Large Communities Attribute”, RFC 8092, IETF, February 2017, DOI 10.17487/RFC8092.
- Link: [rfc-editor.org/rfc/rfc8092](https://www.rfc-editor.org/rfc/rfc8092)
- Time: 20 minutes.
- Read: 1, 3 and 5.

## 8. RFC 4456, BGP Route Reflection

- Year: 2006
- Summary: How a large AS avoids iBGP sessions between every pair of routers: a reflector re-advertises routes to its clients.
- Citation: T. Bates, E. Chen, R. Chandra, “BGP Route Reflection: An Alternative to Full Mesh Internal BGP (IBGP)”, RFC 4456, IETF, April 2006, DOI 10.17487/RFC4456.
- Link: [rfc-editor.org/rfc/rfc4456](https://www.rfc-editor.org/rfc/rfc4456)
- Relation: obsoletes RFC 2796 and RFC 1966; updated by RFC 7606.
- Time: 45 minutes.
- Read: 4 to 9.

## 9. RFC 5065, Autonomous System Confederations for BGP

- Year: 2007
- Summary: The other answer to the iBGP full mesh: the AS is split into sub-ASes that appear from outside as a single one.
- Citation: P. Traina, D. McPherson, J. Scudder, “Autonomous System Confederations for BGP”, RFC 5065, IETF, August 2007, DOI 10.17487/RFC5065.
- Link: [rfc-editor.org/rfc/rfc5065](https://www.rfc-editor.org/rfc/rfc5065)
- Relation: obsoletes RFC 3065; updated by RFC 9774.
- Time: 30 minutes.
- Read: 2, 3 and 4.

## 10. RFC 7911, Advertisement of Multiple Paths in BGP

- Year: 2016
- Summary: ADD-PATH: a neighbor announces several paths for the same prefix, each with a Path Identifier, without a new path replacing the old one.
- Citation: D. Walton, A. Retana, E. Chen, J. Scudder, “Advertisement of Multiple Paths in BGP”, RFC 7911, IETF, July 2016, DOI 10.17487/RFC7911.
- Link: [rfc-editor.org/rfc/rfc7911](https://www.rfc-editor.org/rfc/rfc7911)
- Time: 30 minutes.
- Read: 2 to 5.

# Stage 4. How BGP behaves in operation

## 11. Delayed Internet Routing Convergence

- Year: 2000
- Summary: After a withdrawal, BGP explores alternative paths for tens of minutes; the burst of transient UPDATEs is not a hijack.
- Citation: C. Labovitz, A. Ahuja, A. Bose, F. Jahanian, “Delayed Internet routing convergence”, *Proceedings of ACM SIGCOMM 2000*, pp. 175-187, DOI 10.1145/347059.347428.
- Link: [cs.princeton.edu, course copy](https://www.cs.princeton.edu/~jrex/teaching/spring2005/reading/labovitz00.pdf)
- Contents: a two-year study in which the authors injected hundreds of thousands of routing faults from Internet exchange points and measured how long routers take to reach a consistent view. The answer is on the order of tens of minutes, because of path exploration in the selection process and because of timers. The paper also gives a model of the bounds on convergence.
- Why: a detector sees the UPDATE stream in real time. A withdrawal or a route change is followed by a burst of transient announcements, and these must not be mistaken for a hijack.
- Time: 1.5 hours.
- Read: the abstract, 1, 2, 3, 4 and 7. Section 5 (the formal convergence model) is optional.

## 12. BGP in 2025

- Year: 2026
- Summary: The order of magnitude of the global table at the end of 2025: about 1,050,000 IPv4 prefixes, over half of them more-specifics.
- Citation: G. Huston, “BGP in 2025”, APNIC Blog, 8 January 2026.
- Link: [blog.apnic.net/2026/01/08/bgp-in-2025](https://blog.apnic.net/2026/01/08/bgp-in-2025/)
- Contents: Huston's annual report on the routing table, based on data from AS131072, RouteViews and RIPE RIS. At the end of 2025 the IPv4 table had about 1,050,000 entries (over half of them more-specific prefixes) and the IPv6 table about 241,800. The text analyzes the growth and makes projections up to 2030.
- Why: it gives the order of magnitude of the data the MRT parser processes and shows how common legitimate more-specific prefixes are, which explains why “a sub-prefix appeared” is not, on its own, evidence of a hijack.
- Time: one hour.
- Read: “The BGP measurement environment”, “The IPv4 routing table data” and “Conclusion”. The projections are optional. The companion article, “BGP Updates in 2025” (9 January 2026, same blog), covers the volume of UPDATEs.

## 13. RFC 7454, BGP Operations and Security

- Year: 2015
- Summary: BCP 194: the filters and session protections an operator should have in place before RPKI.
- Citation: J. Durand, I. Pepelnjak, G. Doering, “BGP Operations and Security”, RFC 7454 (BCP 194), IETF, February 2015, DOI 10.17487/RFC7454.
- Link: [rfc-editor.org/rfc/rfc7454](https://www.rfc-editor.org/rfc/rfc7454)
- Contents: recommended operational practice: session protection (TCP-AO, GTSM), prefix filtering (special-purpose, unallocated and overly specific prefixes, filters generated from the IRR), the maximum-prefix limit, AS_PATH filtering, route flap dampening and community scrubbing.
- Why: it is the list of filters an operator should have before getting to RPKI. Most of the incidents in stage 6 would have been stopped by the rules in section 6.
- Time: one hour.
- Read: 4, 5, 6, 8 and 9. The special-purpose prefix tables in 6.1 are for lookup only, when needed.

# Stage 5. Why BGP is insecure

## 14. Why is it taking so long to secure internet routing?

- Year: 2014
- Summary: The best short explanation of the trust model and of the three defenses: prefix filtering, RPKI and BGPsec.
- Citation: S. Goldberg, “Why is it taking so long to secure internet routing?”, *Communications of the ACM*, vol. 57, no. 10, 2014, pp. 56-63, DOI 10.1145/2659899. The same text appeared in *ACM Queue*, vol. 12, no. 8, 2014, DOI 10.1145/2668152.2668966.
- Link: [queue.acm.org/detail.cfm?id=2668966](https://queue.acm.org/detail.cfm?id=2668966) (full text free in a browser; the server refuses automated clients)
- Contents: prefix and sub-prefix hijacks, route leaks, path-shortening attacks, the difference between a blackhole and interception, then three defenses (prefix filtering, RPKI, BGPsec) with the benefits and the adoption problems of each.
- Why: it is the best short explanation of the trust model. It shows plainly what RPKI catches (an unauthorized origin) and what it misses (a route leak, a forged path with the correct origin), which is exactly the line between what the detector checks through RPKI and what has to be checked some other way.
- Time: 45 minutes.
- Read: the whole article. Two factual details need care: the YouTube incident took place in February 2008, and the YouTube prefix was 208.65.152.0/22. Source 17 gives both correctly.

## 15. RFC 4272, BGP Security Vulnerabilities Analysis

- Year: 2006
- Summary: The IETF's vulnerability analysis, message by message and field by field: what a neighbor or an intruder can forge and what stays unprotected.
- Citation: S. Murphy, “BGP Security Vulnerabilities Analysis”, RFC 4272, IETF, January 2006, DOI 10.17487/RFC4272.
- Link: [rfc-editor.org/rfc/rfc4272](https://www.rfc-editor.org/rfc/rfc4272)
- Why: Goldberg explains the model; RFC 4272 shows which UPDATE field each attack sits in, including AS_PATH and NLRI, which the detector reads.
- Time: one hour.
- Read: 2, 3.1.5 (UPDATE) and 4.1 (residual risk).

# Stage 6. Documented incidents

For each incident below, the first paragraph of “Contents” sums up what happened at the routing level, and “Why” says what changed or what would have prevented it, according to that source. The sources are post-mortems by operators or registries and peer-reviewed measurement papers.

## 16. RFC 7908, Problem Definition and Classification of BGP Route Leaks

- Year: 2016
- Summary: The definition of a route leak and its classification into six types, each with real incidents.
- Citation: K. Sriram, D. Montgomery, D. McPherson, E. Osterweil, B. Dickson, “Problem Definition and Classification of BGP Route Leaks”, RFC 7908, IETF, June 2016, DOI 10.17487/RFC7908.
- Link: [rfc-editor.org/rfc/rfc7908](https://www.rfc-editor.org/rfc/rfc7908)
- Contents: the working definition of a route leak (the propagation of an announcement beyond its intended scope, in violation of the policies of the parties involved) and a classification into six types, each with real incidents cited.
- Why: it provides the exact vocabulary for the report, which tells a hijack (a false origin) apart from a leak (a real route, propagated where it should not go).
- Time: 30 minutes.
- Read: sections 2 and 3.

## 17. YouTube Hijacking: A RIPE NCC RIS case study

- Year: 2008
- Summary: The textbook sub-prefix hijack, traced minute by minute in RIS data; it is the incident behind the “YouTube 2008” view.
- Citation: RIPE NCC, “YouTube Hijacking: A RIPE NCC RIS case study”, 17 March 2008.
- Link: [ripe.net, the case study](https://www.ripe.net/about-us/news/youtube-hijacking-a-ripe-ncc-ris-case-study/)
- Contents: on 24 February 2008, at 18:47 UTC, Pakistan Telecom (AS17557) started announcing 208.65.153.0/24, a sub-prefix of 208.65.152.0/22 announced by YouTube (AS36561). PCCW Global (AS3491) propagated the announcement globally. YouTube announced the same /24 at 20:07 and two /25s at 20:18, and PCCW withdrew the routes originated by Pakistan Telecom at 21:01. The analysis uses RIS, RISwhois and BGPlay.
- Why: it is the textbook sub-prefix hijack, exactly what the detector flags as `subprefix_hijack` (`HijackDetector`, in `src/BGPWatch.Core/Domain/`). Goldberg (source 14, the section on prefix filtering) shows that PCCW's prefix filters on the customer session would have stopped the incident.
- Time: 20 minutes.
- Read: the whole text.

## 18. Characterizing Large-scale Routing Anomalies: A Case Study of the China Telecom Incident

- Year: 2013
- Summary: About 50,000 foreign prefixes announced by China Telecom in 2010: what can and cannot be proven from public data.
- Citation: R. Hiran, N. Carlsson, P. Gill, “Characterizing Large-Scale Routing Anomalies: A Case Study of the China Telecom Incident”, *Passive and Active Measurement (PAM 2013)*, Lecture Notes in Computer Science, Springer, 2013, pp. 229-238, DOI 10.1007/978-3-642-36516-4_23.
- Link: [ida.liu.se/~nikca89/papers/pam13.pdf](https://www.ida.liu.se/~nikca89/papers/pam13.pdf)
- Contents: on 8 April 2010, AS23724 (China Telecom) announced about 50,000 prefixes belonging to other ASes. Over 99% of them matched prefixes already present in the global table, which supports the hypothesis of a leak of its own routing table. Part of the traffic was intercepted and still reached its destination, because some providers preferred the route through a customer over the one through China Telecom.
- Why: it shows what can and cannot be learned from public data (intent cannot be proven technically) and why interception is harder to notice than a blackhole. Goldberg (source 14) explains why prefix filtering would not have helped here: the announcement came over a peering link, and prefix filters are usually applied only to customer sessions.
- Time: one hour.
- Read: 1, 4, 5 and 6. The methodology in 3 is optional.

## 19. BGP leaks and cryptocurrencies

- Year: 2018
- Summary: A sub-prefix hijack of the Amazon Route 53 DNS servers, which gave false answers for a cryptocurrency wallet.
- Citation: L. Poinsignon, “BGP leaks and cryptocurrencies”, The Cloudflare Blog, 24 April 2018.
- Link: [blog.cloudflare.com/bgp-leaks-and-crypto-currencies](https://blog.cloudflare.com/bgp-leaks-and-crypto-currencies/)
- Contents: between about 11:05 and 12:55 UTC, AS10297 (eNet) announced five more-specific /24 prefixes out of the /23 blocks of the Amazon Route 53 DNS servers (AS16509), propagated through Hurricane Electric (AS6939). The servers on the hijacked addresses answered only for myetherwallet.com. The DNS resolvers that followed the false route received false answers, including 1.1.1.1 in some locations.
- Why: it is the incident in which the traffic of one specific service was redirected, and it shows that a sub-prefix hijack can target a higher layer (DNS). The post recommends ROAs and RPKI validation, DNSSEC and filtering at transit providers.
- Time: 20 minutes.
- Read: the whole text.

## 20. How Verizon and a BGP Optimizer Knocked Large Parts of the Internet Offline Today

- Year: 2019
- Summary: A route leak amplified by a BGP optimizer: more-specific prefixes with the correct origin, propagated by Verizon across the whole Internet.
- Citation: T. Strickx, “How Verizon and a BGP Optimizer Knocked Large Parts of the Internet Offline Today”, The Cloudflare Blog, 24 June 2019.
- Link: [blog.cloudflare.com, June 2019 post-mortem](https://blog.cloudflare.com/how-verizon-and-a-bgp-optimizer-knocked-large-parts-of-the-internet-offline-today/)
- Contents: from 10:30 UTC, DQE Communications (AS33154) was using a BGP optimizer that split prefixes into more-specific pieces (for example Cloudflare's 104.20.0.0/20 into two /21s). DQE sent them to its customer Allegheny Technologies (AS396531), which passed them on to its other provider, Verizon (AS701), and Verizon propagated them across the whole Internet.
- Why: it is a type 1 route leak in the RFC 7908 classification, made worse by more-specific prefixes with the correct origin. The detector's `origin_hijack` and `subprefix_hijack` rules do not flag it, because the origin is the expected one. The `rpki_invalid` rule catches it, because Cloudflare's ROAs have maxLength /20 and a /21 becomes invalid. The post lists what would have stopped it: prefix filters and prefix limits on the customer session, IRR-based filters, RPKI validation and MANRS.
- Time: 20 minutes.
- Read: the whole text.

## 21. Profiling BGP Serial Hijackers

- Year: 2019
- Summary: Hijacks recur at the same ASes, for months or years; the traits of these ASes help prioritize alerts.
- Citation: C. Testart, P. Richter, A. King, A. Dainotti, D. Clark, “Profiling BGP Serial Hijackers: Capturing Persistent Misbehavior in the Global Routing Table”, *Proceedings of the Internet Measurement Conference (IMC 2019)*, ACM, 2019, pp. 420-434, DOI 10.1145/3355369.3355581.
- Link: [people.csail.mit.edu, the author's PDF](https://people.csail.mit.edu/ctestart/publications/BGPserialHijackers.pdf)
- Contents: starting from five years of discussions on operator mailing lists, the authors build a set of ASes that hijack repeatedly and compare them with legitimate ASes: intermittent presence, short-lived prefixes, address space fragmented across registries, many MOAS conflicts. A classifier trained on these traits finds about 900 ASes with similar behavior in the IPv4 table.
- Why: it shows that hijacks often recur at the same ASes, over months or years. The traits in section 5 are good ideas for prioritizing the detector's alerts.
- Time: 1.5 hours.
- Read: 1, 2.1, 5 and 7. The machine-learning part in 6 is optional.

# Stage 7. Mitigations

## 22. NIST SP 800-189 Rev. 1, Border Gateway Protocol Security and Resilience

- Year: 2025
- Summary: The up-to-date catalog of mitigations, with numbered recommendations, from IRR and RPKI to ASPA, BGP roles and GTSM.
- Citation: K. Sriram, D. Montgomery, *Border Gateway Protocol Security and Resilience*, NIST Special Publication 800-189 Rev. 1, initial public draft, 3 January 2025. The final version in force is NIST SP 800-189, “Resilient Interdomain Traffic Exchange: BGP Security and DDoS Mitigation”, December 2019, DOI 10.6028/NIST.SP.800-189.
- Link: [csrc.nist.gov/pubs/sp/800/189/r1/ipd](https://csrc.nist.gov/pubs/sp/800/189/r1/ipd)
- Contents: the current catalog of mitigations, with numbered recommendations: registration in the IRR, resource certification in RPKI, ROA-based origin validation, the categories of prefix filters by neighbor type, AS_PATH verification (BGPsec, ASPA, BGP roles and OTC) and GTSM.
- Why: it is the most complete up-to-date picture of the mitigations, in a single document. Section 2 also provides the threat taxonomy (prefix hijacks, path hijacks, route leaks), useful for the report.
- Time: 3 hours for the recommended sections.
- Read: 2, 4.1 to 4.3 (including 4.3.1 on forged-origin hijacks), 4.4 to 4.7. Section 5 (source address validation and DDoS) is outside the project's topic.

## 23. RFC 6480, An Infrastructure to Support Secure Internet Routing

- Year: 2012
- Summary: The RPKI architecture: certificates that follow address allocation, ROAs, repositories and the validator's local cache.
- Citation: M. Lepinski, S. Kent, “An Infrastructure to Support Secure Internet Routing”, RFC 6480, IETF, February 2012, DOI 10.17487/RFC6480.
- Link: [rfc-editor.org/rfc/rfc6480](https://www.rfc-editor.org/rfc/rfc6480)
- Contents: the RPKI architecture: the resource certificates that follow the allocation hierarchy, the ROAs, the repositories and the local cache from which routers get their data.
- Why: it is the foundation sources 24 to 27 stand on.
- Time: 45 minutes.
- Read: sections 1 to 3. Sections 4 to 7 (repositories, manifests, key rollover) matter only for running a validator.

## 24. RFC 9582, A Profile for Route Origin Authorizations (ROAs)

- Year: 2024
- Summary: What a ROA holds: the authorized AS, the prefixes and maxLength, signed by the holder of the address block.
- Citation: J. Snijders, B. Maddison, M. Lepinski, D. Kong, S. Kent, “A Profile for Route Origin Authorizations (ROAs)”, RFC 9582, IETF, May 2024, DOI 10.17487/RFC9582.
- Link: [rfc-editor.org/rfc/rfc9582](https://www.rfc-editor.org/rfc/rfc9582)
- Relation: obsoletes RFC 6482.
- Time: 30 minutes.
- Read: 1, 1.2, 4 (in particular 4.3.2.2, maxLength) and 5.

## 25. RFC 6811, BGP Prefix Origin Validation

- Year: 2013
- Summary: The algorithm that gives a route the state Valid, Invalid or NotFound from the VRPs; the detector's `rpki_invalid` rule implements it.
- Citation: P. Mohapatra, J. Scudder, D. Ward, R. Bush, R. Austein, “BGP Prefix Origin Validation”, RFC 6811, IETF, January 2013, DOI 10.17487/RFC6811.
- Link: [rfc-editor.org/rfc/rfc6811](https://www.rfc-editor.org/rfc/rfc6811)
- Relation: updated by RFC 8481 and RFC 8893.
- Contents: the algorithm that gives a route the state Valid, Invalid or NotFound from the VRPs.
- Why: the `RpkiValidity` function in `HijackDetector` implements section 2 and is worth comparing line by line with the pseudo-code in 2.1.
- Time: 45 minutes.
- Read: all of it (10 pages).

## 26. RFC 9319, The Use of maxLength in the Resource Public Key Infrastructure (RPKI)

- Year: 2022
- Summary: Why an overly broad maxLength leaves room for forged-origin sub-prefix hijacks; recommends minimal ROAs.
- Citation: Y. Gilad, S. Goldberg, K. Sriram, J. Snijders, B. Maddison, “The Use of maxLength in the Resource Public Key Infrastructure (RPKI)”, RFC 9319 (BCP), IETF, October 2022, DOI 10.17487/RFC9319.
- Link: [rfc-editor.org/rfc/rfc9319](https://www.rfc-editor.org/rfc/rfc9319)
- Why: it shows the attack that origin validation does not catch: an attacker who puts the victim's AS at the end of the path and announces a sub-prefix covered by an overly broad maxLength gets the state Valid. The lab's ROA has maxLength 24, so any `/25` is invalid.
- Time: 30 minutes.
- Read: 1, 3 and 5.

## 27. RFC 8210, The Resource Public Key Infrastructure (RPKI) to Router Protocol, Version 1

- Year: 2017
- Summary: The RTR protocol, through which a router receives the VRPs and the router keys from a trusted cache; version 1 adds the Router Key PDU.
- Citation: R. Bush, R. Austein, “The Resource Public Key Infrastructure (RPKI) to Router Protocol, Version 1”, RFC 8210, IETF, September 2017, DOI 10.17487/RFC8210.
- Link: [rfc-editor.org/rfc/rfc8210](https://www.rfc-editor.org/rfc/rfc8210)
- Relation: updates RFC 6810 (version 0).
- Why: it is the link between validator and router, the one configured in FRR (source 42).
- Time: 45 minutes.
- Read: 1.2, 3, 4, 5.6 to 5.8 and 8.

## 28. RPKI is Coming of Age

- Year: 2019
- Summary: An RPKI-invalid route is not automatically a hijack: many invalids are mistakes by the holder itself.
- Citation: T. Chung, E. Aben, T. Bruijnzeels, B. Chandrasekaran, D. Choffnes, D. Levin, B. M. Maggs, A. Mislove, R. van Rijswijk-Deij, J. Rula, N. Sullivan, “RPKI is Coming of Age: A Longitudinal Study of RPKI Deployment and Invalid Route Origins”, *Proceedings of the Internet Measurement Conference (IMC 2019)*, ACM, 2019, pp. 406-419, DOI 10.1145/3355369.3355596.
- Link: [cs.umd.edu/~dml/papers/rpki_imc19.pdf](https://www.cs.umd.edu/~dml/papers/rpki_imc19.pdf)
- Contents: a longitudinal study of all ROAs since the start of RPKI, combined with BGP announcements from over 3,300 collectors. The authors classify the invalid announcements into types of misconfiguration (for example announcements more specific than maxLength allows) and separate out a remainder of potentially malicious announcements.
- Why: their conclusion bears directly on the detector. RPKI is good for filtering, but an invalid route is not automatically a hijack, because many invalids come from mistakes by the holder itself. An `rpki_invalid` alert needs context.
- Time: 1.5 hours.
- Read: 1, 2.3, 2.4, 5 and 8.

## 29. RFC 9234, Route Leak Prevention and Detection Using Roles in UPDATE and OPEN Messages

- Year: 2022
- Summary: BGP roles in OPEN and the OTC attribute stop route leaks, the class of incidents that origin validation does not cover.
- Citation: A. Azimov, E. Bogomazov, R. Bush, K. Patel, K. Sriram, “Route Leak Prevention and Detection Using Roles in UPDATE and OPEN Messages”, RFC 9234, IETF, May 2022, DOI 10.17487/RFC9234.
- Link: [rfc-editor.org/rfc/rfc9234](https://www.rfc-editor.org/rfc/rfc9234)
- Contents: the BGP Role capability in the OPEN message (provider, customer, peer, route server) makes the two ends agree on the relationship, and the OTC (Only to Customer) attribute marks the routes that may no longer go up to a provider or a peer.
- Why: it is the answer to the class of incidents in source 20. FRRouting implements the roles (source 42), so they can be tried in the lab.
- Time: 45 minutes.
- Read: sections 1, 3.1, 4 and 5.

## 30. ASPA, BGP AS_PATH Verification Based on Autonomous System Provider Authorization Objects

- Year: 2026
- Summary: ASPA publishes an AS's providers in RPKI and uses them to check whether an AS_PATH respects the declared relationships; still a draft, not an RFC.
- Citation: A. Azimov, E. Bogomazov, R. Bush, K. Patel, J. Snijders, K. Sriram, “BGP AS_PATH Verification Based on Autonomous System Provider Authorization (ASPA) Objects”, Internet-Draft draft-ietf-sidrops-aspa-verification-28, IETF, 24 August 2026 (work in progress, not an RFC as of 11 October 2026).
- Link: [datatracker.ietf.org, the ASPA draft](https://datatracker.ietf.org/doc/draft-ietf-sidrops-aspa-verification/)
- Contents: each AS publishes the list of its providers in RPKI, and the router that receives an announcement checks whether each hop in the AS_PATH respects the declared relationships.
- Why: it detects route leaks and AS_PATH manipulation, where RFC 6811 does not help.
- Time: 45 minutes.
- Read: 1 and 5.1 to 5.2, plus 8 (the relation to ROA, BGPsec and OTC).

## 31. RFC 8205, BGPsec Protocol Specification

- Year: 2017
- Summary: Signatures on every AS in the path, in the BGPsec_PATH attribute that replaces AS_PATH; it protects the path, not only the origin.
- Citation: M. Lepinski (ed.), K. Sriram (ed.), “BGPsec Protocol Specification”, RFC 8205, IETF, September 2017, DOI 10.17487/RFC8205.
- Link: [rfc-editor.org/rfc/rfc8205](https://www.rfc-editor.org/rfc/rfc8205)
- Relation: updated by RFC 8206.
- Why: it is the standardized defense against forged paths with the correct origin. Goldberg (source 14) explains why its adoption is hard.
- Time: one hour.
- Read: 1, 2, 3 and 5.1.

## 32. MANRS Implementation Guide for Network Operators

- Year: online
- Summary: The four MANRS actions for operators, with concrete steps: IRR objects, ROAs, generated filters, validators.
- Citation: MANRS (Mutually Agreed Norms for Routing Security), “MANRS Implementation Guide for Network Operators”, document MANRS-007.01, online version accessed on 5 October 2026. The MANRS program is now run by the Global Cyber Alliance.
- Link: [manrs.org/netops/guide](https://manrs.org/netops/guide/)
- Contents: the four MANRS actions for operators (filtering, anti-spoofing, coordination, publishing routing information), with an implementation guide: route objects in the IRR, ROAs, filters generated with tools such as bgpq4 and RPKI validators such as Routinator or rpki-client, plus configuration examples for several equipment vendors.
- Why: it shows how the recommendations of sources 22, 23 and 25 translate into an operator's practice and how compliance is measured. It is also a good argument for the mitigations section of the report.
- Time: one hour.
- Read: 3.2, 4.2 and 4.4. Of the configuration examples, only those for the relevant equipment vendor.

# Stage 8. Detection and monitoring

## 33. ARTEMIS: Neutralizing BGP Hijacking within a Minute

- Year: 2018
- Summary: The system the project is modeled on: the hijack taxonomy and real-time detection, run by the victim AS itself.
- Citation: P. Sermpezis, V. Kotronis, P. Gigis, X. Dimitropoulos, D. Cicalese, A. King, A. Dainotti, “ARTEMIS: Neutralizing BGP Hijacking Within a Minute”, *IEEE/ACM Transactions on Networking*, vol. 26, no. 6, 2018, pp. 2471-2486, DOI 10.1109/TNET.2018.2869798.
- Link: [arxiv.org/abs/1801.01085](https://arxiv.org/abs/1801.01085) (version v4, June 2018) and [the source code, FORTH-ICS-INSPIRE/artemis](https://github.com/FORTH-ICS-INSPIRE/artemis)
- Contents: a taxonomy of hijacks by the announced AS_PATH (Type-0 for a false origin, Type-N for forged paths, Type-U) and by the affected prefix (exact, sub-prefix, squatting), an analysis of their visibility in public collectors, and a system run by the victim AS that compares the real-time feeds with a local configuration of its own prefixes and origins.
- Why: it is the system the project is modeled on. With the taxonomy in section 2 it is possible to state exactly which types the detector covers (Type-0 exact and sub-prefix) and which types stay outside it (Type-N, for example).
- Time: 2 hours.
- Read: 1, 2, 4.2 and 5. Sections 6 and 7 (mitigation through more-specific announcements and the real-world experiments) are optional.

## 34. BGPalerter documentation

- Year: 2025
- Summary: NTT's open-source monitor on top of RIS Live; its `monitorHijack` rule is almost identical to the project's detector.
- Citation: NTT, *BGPalerter*, open-source software (BSD-3-Clause license), version 2.0.1, August 2025, the documentation in the repository's `docs` directory.
- Link: [github.com/nttgin/BGPalerter](https://github.com/nttgin/BGPalerter) and [docs/configuration.md](https://github.com/nttgin/BGPalerter/blob/main/docs/configuration.md)
- Contents: a monitor that connects to RIS Live and checks the prefixes declared in `prefixes.yml`. The architecture has three components (connectors, monitors, reports). The monitors cover a wrong origin and sub-prefixes with a different origin (`monitorHijack`), loss of visibility, new more-specific prefixes with the same origin (`monitorNewPrefix`), RPKI-invalid announcements, ROA changes and unexpected neighbors on the path.
- Why: it is the project's second reference system. The rules in `monitorHijack` are almost identical to the detector's `origin_hijack` and `subprefix_hijack` rules, and `monitorNewPrefix` covers exactly the case in source 20, which the detector catches only through RPKI. The `thresholdMinPeers` parameter shows how to avoid alerts from a single vantage point.
- Time: one hour.
- Read: the README, then in `configuration.md` the Connectors (connectorRIS) and Monitors sections, then `prefixes.md`.

## 35. RFC 6396, Multi-Threaded Routing Toolkit (MRT) Routing Information Export Format

- Year: 2011
- Summary: The binary format of the RIS and RouteViews archives, which the project's C parser reads.
- Citation: L. Blunk, M. Karir, C. Labovitz, “Multi-Threaded Routing Toolkit (MRT) Routing Information Export Format”, RFC 6396, IETF, November 2011, DOI 10.17487/RFC6396.
- Link: [rfc-editor.org/rfc/rfc6396](https://www.rfc-editor.org/rfc/rfc6396)
- Contents: the binary format of routing files: TABLE_DUMP_V2 for snapshots, BGP4MP for UPDATEs.
- Why: the project's C parser reads exactly this format.
- Time: one hour.
- Read: sections 2, 3, 4.3 (TABLE_DUMP_V2) and 4.4 (BGP4MP).

## 36. Route Collection Raw Data: MRT Files

- Year: 2024
- Summary: The naming scheme and frequency of the RIS files, plus the parsing tools the parser's output can be compared against.
- Citation: RIPE NCC, “Route Collection Raw Data: MRT Files”, RIPE RIS Docs, updated March 2024.
- Link: [ris.ripe.net/docs/mrt](https://ris.ripe.net/docs/mrt/)
- Contents: the file naming scheme, the file frequency (snapshots every 8 hours, updates every 5 minutes), the multi-hop collectors and the existing parsing tools.
- Why: the list of tools (bgpdump, bgpscanner, BGPKit) provides reference implementations to compare the parser's output against. For RouteViews, the archive is at [archive.routeviews.org](https://archive.routeviews.org/), with data published under the CC BY 4.0 license.
- Time: 30 minutes.
- Read: the whole page.

## 37. RIPE RIS: the route collectors and RIS Live

- Year: online
- Summary: Where the public data comes from: the RRC collectors, with BGP sessions at IXPs and multi-hop, and RIS Live, their near-real-time feed.
- Citation: RIPE NCC, “Route collectors”, RIPE RIS Docs, updated 28 September 2026; RIPE NCC, “RIS Live Manual”, accessed on 11 October 2026.
- Link: [ris.ripe.net/docs/route-collectors](https://ris.ripe.net/docs/route-collectors/) and [the RIS Live manual](https://ris-live.ripe.net/manual/)
- Contents: RIS receives its data through BGP sessions, as AS12654. Most collectors sit in the peering networks of IXPs, and the multi-hop ones, such as RRC00 in Amsterdam, accept sessions from anywhere. RIS Live delivers the BGP messages as JSON, through a filterable WebSocket (`wss://ris-live.ripe.net/v1/ws/`) or as the full stream (the “firehose”), with no authentication.
- Why: the 2008 incident data that the application replays comes from the rrc00 collector. RIS Live is BGPalerter's real-time source (source 34).
- Time: 30 minutes.
- Read: the collectors page, then in the manual “WebSocket interface” and “ris_subscribe”.

## 38. RFC 7854, BGP Monitoring Protocol (BMP)

- Year: 2016
- Summary: The protocol through which a router reports its sessions and routes to a monitor; the lab routers report to the application this way.
- Citation: J. Scudder (ed.), R. Fernando, S. Stuart, “BGP Monitoring Protocol (BMP)”, RFC 7854, IETF, June 2016, DOI 10.17487/RFC7854.
- Link: [rfc-editor.org/rfc/rfc7854](https://www.rfc-editor.org/rfc/rfc7854)
- Relation: updated by RFC 8671 (Adj-RIB-Out), RFC 9069 (Loc-RIB), RFC 9515 and RFC 9736.
- Why: the application's BMP station (`src/BGPWatch.Web/Bmp/`) reads exactly the messages in section 4.
- Time: one hour.
- Read: 3, 4.1 to 4.6, 4.9, 4.10 and 5.

## 39. RFC 9069, Support for Local RIB in the BGP Monitoring Protocol (BMP)

- Year: 2022
- Summary: The BMP extension for the Loc-RIB, the routes chosen by the decision process; exactly what each lab router sends.
- Citation: T. Evens, S. Bayraktar, M. Bhardwaj, P. Lucente, “Support for Local RIB in the BGP Monitoring Protocol (BMP)”, RFC 9069, IETF, February 2022, DOI 10.17487/RFC9069.
- Link: [rfc-editor.org/rfc/rfc9069](https://www.rfc-editor.org/rfc/rfc9069)
- Relation: updates RFC 7854; updated by RFC 9736.
- Why: the lab routers report with `bmp monitor ipv4 unicast loc-rib`; the application's station keeps only the routes with the Loc-RIB Instance peer type (3).
- Time: 30 minutes.
- Read: 1, 3, 4.1 and 5.

## 40. BGPStream: A Software Framework for Live and Historical BGP Data Analysis

- Year: 2016
- Summary: The framework that merges RIS and RouteViews into a single time-ordered stream, with a C library and Python bindings.
- Citation: C. Orsini, A. King, D. Giordano, V. Giotsas, A. Dainotti, “BGPStream: A Software Framework for Live and Historical BGP Data Analysis”, *Proceedings of the Internet Measurement Conference (IMC 2016)*, ACM, 2016, pp. 429-444, DOI 10.1145/2987443.2987482.
- Link: [caida.org, the paper's PDF](https://www-old.caida.org/publications/papers/2016/bgpstream/bgpstream.pdf) and [bgpstream.caida.org](https://bgpstream.caida.org/)
- Contents: the architecture of a framework that merges RIS and RouteViews into a single time-ordered stream, with a command-line tool, a C library and Python bindings, plus monitoring applications built on it.
- Why: it is the mature alternative to the project's combination of a C parser and a C# application. Section 3 explains the problems any such system runs into (sorting records from different collectors, late data).
- Time: 1.5 hours.
- Read: 1, 3 and 4.2 (the Python bindings), then the PyBGPStream tutorial on the site.

# Stage 9. Practice

## 41. BGP Labs

- Year: online
- Summary: Configuration exercises on FRRouting, runnable in containers, from basic sessions to LocalPref and communities.
- Citation: I. Pepelnjak, *BGP Labs*, ipSpace.net, online exercise project, accessed on 5 October 2026.
- Link: [bgplabs.net](https://bgplabs.net/)
- Contents: configuration exercises orchestrated with netlab, runnable in containers or in GitHub Codespaces, with FRRouting among the supported platforms. They are grouped into basic sessions, policies (LocalPref, MED, prepending, communities, filtering transit routes) and more complex scenarios.
- Why: the project's lab already runs FRRouting in Docker, so the exercises fit directly. The policies of stage 2 become configurations that can be seen working.
- Time: 30 to 60 minutes per exercise.
- Exercises: “Configuring BGP on FRRouting”, then the basic session exercises, “Filter Transit Routes”, “Filter Advertised Prefixes”, “Select Preferred Uplink with BGP Local Preference”, “Use AS-Path Prepending to Influence Incoming Traffic Flow” and the two on communities.

## 42. FRRouting User Guide, BGP chapter

- Year: online
- Summary: How to turn on origin validation through RTR and BGP roles on an FRR router, as in the project's lab.
- Citation: FRRouting Project, *FRRouting User Guide*, chapter “BGP”, sections “Prefix Origin Validation Using RPKI” and “BGP Roles and Only to Customers”, documentation of the current version, accessed on 5 October 2026.
- Link: [docs.frrouting.org, RPKI in bgpd](https://docs.frrouting.org/en/latest/bgp.html#prefix-origin-validation-using-rpki) and [BGP roles](https://docs.frrouting.org/en/latest/bgp.html#bgp-roles-and-only-to-customers)
- Contents: connecting bgpd to an RPKI cache through the RTR protocol, matching the validation state in route-maps (`match rpki valid|invalid|notfound`), the `show rpki` commands and the `local-role` configuration from RFC 9234, with strict mode and the OTC attribute.
- Why: it makes it possible to add to the isolated lab a router that applies ROV or BGP roles, and to check that the detector sees what the router sees. The RTR cache needs a source of VRPs. In the isolated network that source has to be local, for example an RTR server such as StayRTR, which also accepts local assertions through SLURM (RFC 8416).
- Time: 2 hours, including the configuration.
- Read: the two sections named, including the RPKI configuration example at the end of the first.

## 43. RIPEstat Data API

- Year: online
- Summary: The JSON API over RIS and RPKI data: checking what has been read against real data, without announcing anything.
- Citation: RIPE NCC, *RIPEstat Data API*, online documentation, accessed on 5 October 2026.
- Link: [stat.ripe.net/docs/02.data-api](https://stat.ripe.net/docs/02.data-api/)
- Contents: the JSON interface over RIS and RPKI data: `routing-status`, `rpki-validation`, `bgp-updates`, `bgplay`, `looking-glass`, `routing-history`. Regular use above 1,000 requests a day requires the `sourceapp` parameter, and the limit is 8 concurrent requests from the same IP address.
- Why: it is the fastest way to check what has been read against real data, without announcing anything. The exercises below were run on 5 October 2026.
- Time: one hour.
- Exercises: an `rpki-validation` query for AS13335 and 104.20.0.0/20 (result valid, maxLength 20), then one for 104.20.0.0/21 (result `invalid_length`), which shows why the leak in source 20 is RPKI-invalid. A `routing-status` query for 208.65.152.0/22 shows who originates the YouTube prefix of source 17 today and how many RIS peers see it. For the incident itself, `bgplay` returns no data from 2008, so the source is the RIS archive: the file `rrc00/2008.02/updates.20080224.1845.gz` on [data.ris.ripe.net](https://data.ris.ripe.net/rrc00/2008.02/) holds the announcements for 208.65.153.0/24 with AS17557 in the path. The output of the project's MRT parser, run on this file, can then be compared with the timeline in source 17.

## 44. bgp.tools

- Year: online
- Summary: A free explorer of global routing: the page of a prefix or an AS shows the origin, the providers, the neighbors and RPKI validation.
- Citation: Port 179 Ltd, *bgp.tools*, online service, accessed on 11 October 2026.
- Link: [bgp.tools](https://bgp.tools/)
- Contents: search by AS, prefix or DNS name. A prefix page gives the origin and the AS name, the registry and the registration date, the providers and neighbors in the connectivity graph, whois, DNS and validation; the site also has a looking glass. The data is near real time and free; monitoring and the API are for paying users.
- Why: it is the model for the application's “Prefixes” view: the prefix as an object, with the origin as its first fact. On 11 October 2026, the page for 208.65.152.0/22 showed the origin AS43515 (Google Ireland) and noted that the 2008 YouTube block is not currently visible in the global table.
- Time: 20 minutes.
- Exercises: the page for AS13335 and the one for the prefix 208.65.152.0/22, compared with RIPEstat's `routing-status` (source 43).
