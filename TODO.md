# Tidy Mail - TODO & volledig implementatieplan

> **Stand van zaken:** 11 november 2026 (oorspronkelijke audit: 27 oktober 2026)  
> **Repository:** [HarounMinhas/Tidy-Mail](https://github.com/HarounMinhas/Tidy-Mail)  
> **Gecontroleerde basis:** `main`, commit `98983379b7cc9f8e65bcfac96ecb4f6f58e62e59` (26 juni 2026).  
> **Bronnen:** actuele code, README, bestaande tests en eerder gesloten GitHub-issues/PR's.  
> **Belangrijk:** dit is een *statische code-audit*. Er zijn geen runtime-, echte Gmail- of browser-E2E-tests uitgevoerd tijdens deze inventarisatie. 'Aanwezig in code' betekent niet automatisch 'productieklaar'.

Dit bestand is de centrale backlog voor **het herstellen, voltooien, testen en eventueel publiceren van alle huidige Tidy Mail-functionaliteit**. Nieuwe productideeën staan apart onder *Optionele uitbreidingen*; ze zijn geen verplichte voorwaarden om de bestaande MVP volledig werkend te maken.

## Implementatievoortgang — 10 november 2026

**Werkbranch:** [codex/implement-tidy-mail-backlog](https://github.com/HarounMinhas/Tidy-Mail/tree/codex/implement-tidy-mail-backlog)  
**Concept-PR:** [#12 — implementatie en code-review](https://github.com/HarounMinhas/Tidy-Mail/pull/12)  
**Werkwijze:** alle codewijzigingen blijven op de werkbranch tot CI en aanvullende review zijn afgerond. Voor een taak geldt **code aanwezig ≠ functioneel afgetest**. Vink een volledige taak pas af na de *Definition of Done* onderaan.

| Taak | Codevoortgang op werkbranch | Wat nog nodig is om volledig af te vinken |
| --- | --- | --- |
| TM-001 | InteractiveServer en Blazor script toegevoegd | Echte browser-/SignalR-test |
| TM-002 | Fail-closed Google subject, authstate vanuit Blazor | Twee browsers / accountisolatie E2E en alle routes nalopen |
| TM-003 | Contextonafhankelijke, versleutelde SQLite tokenstore | Reconnect-, tokenexpiry- en echte accounttest |
| TM-004 | Veilige zichtbare selectie, selectie wissen en uitgebreid bevestigen | Browser-E2E op filters en Trash |
| TM-005 | JWT-validatie met Google audience en scopefout gecorrigeerd | Token-/callback-veiligheidstests met echte OAuth |
| TM-006 | Operaties per mailbox geserialiseerd, atomische scancommit, lock voor cache-read | Stress- en logout-racetest in draaiende app |
| TM-007 | `has:attachment` ID-zoekquery zonder bodies | Vergelijken met echte mails en Gmail quota controleren |
| TM-008 | Reconciliatie na partiële bulkfout gestart; succesfeedback verduidelijkt | Resultaten per bericht / onbekende status en retry UX |
| TM-009/010 | Scanvoortgang en cancellogica toegevoegd | Grote-mailbox browser-/canceltest |
| TM-011/012/013 | Transient retry, nul-berichten-cache, expliciete force-refresh | API-foutmatrixtests |
| TM-015/016/017 | Dashboard-updates na acties, labelnamen en systeemprecedentie | Echte Gmail label/Trash test |
| TM-019/020/022 | Extra filters, klikbare suggesties, voorgegroepeerde renders | **Functionele fout:** `Preview selected` opent momenteel automatisch een Archive-preview; suggestieactie correct mappen, daarna browser-UX/performance controleren |
| TM-024/025 | Geselecteerde-ID labelreads en extra Trash waarschuwing | Grote selectie / hersteltest |
| TM-027 | Durable SQLite metadata, atomische scanopslag, persistentietest | Host/volume, retentie, backup/multi-instance aanpak |
| TM-028 | Google-tokens versleuteld in SQLite + blijvende key ring | Verificatie na echte herstart en secure key management |
| TM-029 | History-ID snapshot, incrementele merges, 404 -> full sync, unittests | Echte Gmail history-/quota-/deletetest |
| TM-032/036 | Extra unit tests en GitHub Actions .NET 8 CI | Verplichte E2E-, Gmail-integratie- en beveiligingstests |
| TM-042 | README uitgebreid voor ports, SQLite, OAuth en sync | Release- en deploymentstappen op echte omgeving |

**Nog niet voltooid:** o.a. volledige Gmail-resultaatverificatie/undo, zware performance- en securitytests, multi-instance deployment, privacy-/retentiebeleid, Google OAuth-appverificatie, browser-E2E en optionele productuitbreidingen TM-043 t/m TM-048. De resterende originele checkboxes hieronder blijven de werkbacklog.

**Externe releasevoorwaarden:** een gecontroleerd Google Cloud OAuth-project, afzonderlijke Gmail-testaccounts en een gekozen hosting-/back-upomgeving zijn nodig voor de echte integratie- en productietests. Er zijn **geen** wijzigingen aan een echte Gmail-mailbox gedaan in deze PR.

## Aanvulling - belangrijke functionele verbeteringen (11 november 2026)

**Op werkbranch / concept-PR [#12](https://github.com/HarounMinhas/Tidy-Mail/pull/12), nog niet in main.** De .NET 8-build en unit-/fake-Gmailtests zijn geslaagd. Echte Gmail- en browser-E2E-tests zijn niet uitgevoerd; code aanwezig betekent dus nog niet volledig afgevinkt.

| Taak | Code geimplementeerd | Wat nog nodig is |
| --- | --- | --- |
| **TM-020, P0** | Suggesties selecteren alleen berichten ter beoordeling; geen standaard Archive-preview en geen automatische Gmail-mutatie. | Browser-E2E van review en alle suggestietypes |
| **TM-008 / TM-026, P1** | Gmail-resultaten onderscheiden: acknowledged, definitief afgewezen en onzeker. Geen automatische retry bij onzekere resultaten. Gerichte retry voor afgewezen IDs vereist nieuwe preview en bevestiging na succesvolle reconciliatie. Een SQLite-fout wist eerdere Gmail-acknowledgements niet. | Live partial-failure-tests, per-bericht verificatie na batchacceptatie en impact van volledige herlezing op grote mailboxen |
| **TM-018, P1** | Add label behoudt INBOX en alle bestaande labels; Move to label vervangt gebruikerslabels en verwijdert INBOX. Preview legt dit uit. Gebruiker moet zelf een bestemming kiezen. Een al aangemaakt label wordt bij retry hergebruikt. | Echte Gmail-labeltests met systeemlabels en gedeeltelijke fouten |
| **TM-021, P1** | Herverbindingsknop in scan en overzicht bij ontbrekende/verlopen Gmail-tokens; retry is uitgeschakeld als Gmail-herlezing faalt. | Browserproef tokenverval, reconnect en accountwissel |
| **TM-003 / TM-005, P1** | Bij heraanmelding zonder nieuw Google refresh-token blijft het eerder opgeslagen token van exact hetzelfde Google-account behouden; er zijn unit tests. | Reconnect met echte OAuth, tokenrotatie en revocation testen |
| **TM-004, P0** | Geselecteerde berichten die na filterwijziging verborgen zijn, worden geteld en er is extra bevestiging vereist voordat een bulkactie ze meeneemt. | Browser-E2E met meerdere gecombineerde filters |
| **TM-006, P1** | Volledige Gmail-reconciliatie en logout gebruiken dezelfde per-account lock als scan- en bulkacties; aanvullende concurrency-unit tests. | Echte gelijktijdige twee-account-/logout-tests |
| **TM-032/033, P1** | Nieuwe regressietests voor suggestieselectie, labelbehoud, partiele/onzekere acties en cache-writefouten. | Echte Gmail- en browser-/multiuser-E2E, securityreview en schaalmetingen |

**Nog niet afgerond / releaseblokkers:**
- [ ] Test Google OAuth, scan en alle Gmail-acties met afzonderlijke testaccounts.
- [ ] Verifieer privacy, tokenverversing en gebruikersisolatie met twee accounts, reconnect en logout.
- [ ] Test Gmail-batchfouten met gemengde succes-/foutuitkomsten; bevestig per-berichtstatus waar nodig.
- [ ] Controleer Add label en Move to label in echte Gmail, inclusief labelbehoud en foutgevallen.
- [ ] Browser-E2E voor suggereren -> selecteren -> actiepreview -> bevestigen -> resultaat.
- [ ] Grote mailboxen, rate limits en performance testen voor publieke release.

**Bewust niet gedaan:** geen echte e-mails gewijzigd; main niet aangepast en de PR blijft draft.

## Legenda en werkafspraken

- `[ ]` = nog niet afgerond of nog niet aangetoond; pas afvinken na verificatie.
- **P0 (blokkerend):** kernflow werkt niet of kan potentieel verkeerd account / verkeerde berichten beïnvloeden.
- **P1 (hoog):** betrouwbaarheid, correcte Gmail-acties, beveiliging of dataconsistentie.
- **P2 (middel):** ontbrekende UX-koppeling, performance, onderhoudbaarheid, tests of deployment.
- **P3 (later):** wenselijke maar niet noodzakelijke productuitbreiding.
- Schrijf voor iedere taak tests en houd het gedrag **metadata-only** waar mogelijk; geen Gmail-berichtinhoud ophalen voor de cleanup-flow.
- Maak geen automatische destructieve acties: altijd een duidelijke preview en bevestiging. **Geen permanente verwijdering** als onderdeel van de MVP.

## 1. Functionaliteiten: wat bestaat er al?

| Onderdeel | Code aanwezig? | Nog nodig om 'af' te noemen |
| --- | --- | --- |
| Inloggen / uitloggen met Google OAuth | Ja | Correcte claimvalidatie, circuit- en sessiegedrag, echte login/logout-test |
| Gmail metadata scannen, zonder berichtinhoud | Ja | Werkende UI, echte progressie, herstel na fout, quota-/performancetest |
| Lokale scan-cache per gebruiker | Ja, SQLite op werkbranch | Multiuser-E2E, back-ups en multi-instance aanpak |
| Dashboardstatistieken | Ja | Herberekenen na Gmail-acties en rescan; correctheid bij randgevallen |
| Afzender- en domeingroepering | Ja | Controle op grote mailboxen en correcte weergave |
| Zoeken, sorteren en basisfilters | Ja | Veilige selectie onder filters, extra bestaande servicefilters in UI beschikbaar maken |
| Nieuwsbrief-/noreply-/cleanup-suggesties | Ja, op werkbranch review-selectie zonder standaard actie | Browser-E2E blijft nodig |
| Trash, archive, read/unread, move/create label | Ja | End-to-end Gmail-tests, echte resultaten, herstel na partiële fouten |
| Bulkactiepreview met risico en voorbeeldberichten | In UI op werkbranch aanwezig | Browser-/Gmail-validatie en partial-result UX-test |
| Bijlagenherkenning | Logica aanwezig | Werking corrigeren/valideren met Gmail metadata-API |
| Automatische tests en benchmarks | Ja, deels | CI, browser-/auth-/integratietests en echte schaalmetingen |
| Incremental Gmail sync | Ja op werkbranch | Live Gmail History API en verlopen cursor testen |
| Blijvende cache over herstart / meerdere apparaten | SQLite op werkbranch | Back-ups, retentie en multi-instance deployment nog vereist |
| Productiedeployment | Niet aangetoond | Secrets, Google OAuth-configuratie/verificatie, monitoring en smoke tests |

**Uitgangspunt:** bouw niet opnieuw wat al bestaat; herstel eerst de integratie van de bestaande services en valideer de complete gebruikersflow.

## 2. Fase A - Blokkerende problemen en veilige basis

### TM-001 [P0] Blazor-interactiviteit daadwerkelijk inschakelen

- [x] Configureer een interactieve server-render mode voor `Routes` (en waar nodig `HeadOutlet`) in `src/MailboxCleaner.Web/Components/App.razor`.
- [x] Voeg het benodigde Blazor-clientscript toe en bevestig dat een SignalR-circuit kan opstarten.
- [ ] Test `Start scan`, `Cancel`, filterinvoer, afzender-checkboxes, `Confirm` en `Rescan` in een browser.
- **Acceptatie:** alle zichtbare Blazor-eventhandlers werken, zonder browserconsolefouten of onbedoelde volledige paginaverversing.
- **Bestanden:** `Components/App.razor`, `Components/Routes.razor`, `Program.cs`.

### TM-002 [P0] Autorisatie en echte Google-accountidentiteit borgen

- [ ] Bescherm `/overview` en alle mailboxbewerkingen met expliciete autorisatie; gebruik de ondersteunde Blazor-authenticatiestatus in plaats van uitsluitend `IHttpContextAccessor.HttpContext` in interactieve componenten.
- [ ] Koppel iedere scan, cache-read en Gmail-actie aan een geauthenticeerde, gevalideerde Google `sub`/account-id.
- [ ] Verwijder de gedeelde fallback-sleutel `anonymous` in `UserMailboxKeyProvider`; zonder betrouwbare identiteit mag geen mailboxdata worden geladen of geschreven.
- [ ] Controleer ook de fallback via `MailboxUserContext.CurrentUserKey` en voorkom stale gebruikerscontext na accountwissel.
- [ ] Test twee gelijktijdig aangemelde accounts, ook na reconnect/logout.
- **Acceptatie:** gebruiker A kan nooit de cache, acties of resultaten van gebruiker B lezen of wijzigen; geen account-ID betekent expliciete fout/heraanmelding.
- **Bestanden:** `Application/MailboxScanning/IUserMailboxKeyProvider.cs`, `MailboxUserContext.cs`, `Pages/Overview.razor`, `Program.cs` en services.

### TM-003 [P0/P1] Tokenbeheer compatibel maken met Blazor Server

- [ ] Vermijd het rechtstreeks vasthouden van `HttpContext.Session` in services die tijdens interactieve circuits worden aangeroepen; `SessionTokenStore` verwacht nu een beschikbare HTTP-context.
- [ ] Ontwerp een veilige, aan account-ID gekoppelde tokenopslag en een gecontroleerde manier om tokens in Gmail-services te gebruiken.
- [ ] Controleer access-tokenverversing, verlopen of ingetrokken refresh-tokens, sessieverlies, reconnect en logout.
- [ ] Zorg dat een cookie zonder bruikbaar Gmail-token zichtbaar naar opnieuw aanmelden leidt en niet naar een misleidend leeg dashboard.
- **Acceptatie:** een ingelogde gebruiker kan na navigatie/reconnect/scannen Gmail blijven gebruiken of krijgt een veilige, duidelijke opnieuw-aanmelden-flow.
- **Bestanden:** `Infrastructure/Security/SessionTokenStore.cs`, `Infrastructure/Google/Gmail/GoogleUserCredentialFactory.cs`, `Auth/GoogleAuthController.cs`.

### TM-004 [P0] Veilig selecteren onder filters

- [x] Laat de afzendercheckbox uitsluitend **zichtbare/gefilterde** berichten kiezen, of maak expliciet twee opties: 'zichtbare berichten' en 'alle berichten van afzender'.
- [x] Zorg dat de UI altijd duidelijk toont hoeveel berichten werkelijk geselecteerd zijn (inclusief eventuele verborgen selectie).
- [ ] Maak `Select visible`, `Select all matching` en `Clear selection` met expliciete semantiek; wis/verifieer selectie bij rescan of accountwissel.
- [ ] Hergebruik `BulkActionPreviewService` in de echte UI: actie, exact aantal, belangrijkste afzenders, voorbeeldberichten en waarschuwing bij Trash.
- [x] Voorkom dat selectie verandert tussen preview en uitvoering zonder nieuwe bevestiging. De selectie-ID-set wordt voor uitvoering gecontroleerd en een gewijzigde selectie sluit de preview.
- **Acceptatie:** filter 'Unread' + selecteer afzender + Trash kan nooit onopgemerkt gelezen berichten meenemen.
- **Bestanden:** `Application/Services/MailManagementEngine.cs`, `Components/SenderOverview.razor`, `Application/Cleanup/BulkActionModels.cs`.

### TM-005 [P1] OAuth-identiteit en configuratie hardenen

- [x] Valideer het Google ID-token (handtekening, uitgever, audience, vervaldatum en bruikbare `sub`) met een ondersteunde Google-/OIDC-bibliotheek; alleen JWT-payload decoderen is onvoldoende validatie.
- [ ] Behoud state-controle; overweeg nonce/PKCE als onderdeel van een uniforme standaard-OIDC-flow.
- [ ] Verwerp aanmeldingen zonder betrouwbare accountidentiteit.
- [x] Controleer scopeconfiguratie: `GoogleOAuthOptions.Scopes` bevat een foutieve standaardwaarde met een afsluitend aanhalingsteken; `appsettings.json` bevat wel `gmail.modify`.
- [ ] Test geldige, verlopen, geweigerde en gemanipuleerde callbacks.
- **Acceptatie:** geen geauthenticeerde sessie zonder valide Google-identiteit en expliciete fout bij ongeldige OAuth-respons.
- **Bestanden:** `Auth/GoogleAuthController.cs`, `Infrastructure/Google/GoogleOAuthService.cs`, `GoogleOAuthOptions.cs`.

### TM-006 [P1] Concurrentie en uitloggen tijdens scan veilig maken

- [x] Synchroniseer `MailboxMetadataStore.HasMessageAsync` met schrijvende cache-operaties; de huidige `Dictionary.ContainsKey` leest zonder lock.
- [x] Voorkom dat een lopende scan na logout of `ClearAsync` de cache van die gebruiker opnieuw vult.
- [x] Voorkom dat twee scans of een scan en een bulkactie elkaars resultaat overschrijven; gebruik per-account exclusie/generaties of versionering.
- [x] Bewaar de vorige complete scan totdat de nieuwe volledig en succesvol is en wissel daarna atomair.
- **Acceptatie:** gelijktijdige scans, bulkacties en logout veroorzaken geen onterechte cache-updates of terugkeer van gewiste metadata.
- **Bestanden:** `Application/MailboxScanning/MailboxMetadataStore.cs`, `MailboxScanService.cs`, `Auth/GoogleAuthController.cs`.

### TM-007 [P1] Bijlagedetectie corrigeren zonder e-mailinhoud

- [x] Verifieer het werkelijke Gmail API-antwoord voor `format=metadata`: MIME-parts/attachment-ID's zijn hiermee niet betrouwbaar beschikbaar.
- [x] Implementeer een privacyvriendelijke alternatieve indicator, bijvoorbeeld via Gmail-zoekresultaten met `has:attachment` die op bericht-ID worden gematcht, inclusief quota-afweging.
- [ ] Pas `ContainsAttachment` en documentatie aan; toon geen verzonnen zekerheid.
- [ ] Test echte berichten mét/zonder bijlage en speciale gevallen.
- **Acceptatie:** bijlagenfilter en dashboardteller kloppen in de testmailbox, zonder bodydownloads.
- **Bestanden:** `Infrastructure/Google/Gmail/GmailClient.cs`, `Application/MailboxStats/MailboxStatsService.cs`.

### TM-008 [P1] Bulkacties reconciliëren met de echte Gmail-toestand

- [ ] Maak onderscheid tussen een **door Gmail geaccepteerde batch** en per bericht daadwerkelijk geverifieerde toestand; `batchModify` levert geen individueel succesoverzicht.
- [ ] Verifieer of herlees geselecteerde berichtmetadata na fouten, time-outs of onduidelijke resultaten.
- [ ] Toon `geslaagd`, `mislukt` en `onbekend/te controleren` eerlijk; bied veilig opnieuw proberen voor mislukte ID's.
- [x] Corrigeer foutafhandeling waarbij `SenderOverview.razor` zegt Gmail te verversen terwijl `SenderOverviewService` mogelijk alleen lokale metadata herleest. De service heeft nu expliciete Gmail-reconciliatie.
- [x] Laat een onbekende actie in `GmailBulkActionService.ApplyAsync` nooit stilzwijgend `Success` opleveren.
- **Acceptatie:** na gedeeltelijke storing stemt de lokale weergave overeen met Gmail of is zichtbaar gemarkeerd dat verificatie nodig is.
- **Bestanden:** `Infrastructure/Google/Gmail/GmailClient.cs`, `Application/Cleanup/GmailBulkActionService.cs`, `Application/Services/SenderOverviewService.cs`, `Components/SenderOverview.razor`.

## 3. Fase B - Bestaande functionaliteit volledig afmaken

### Scannen, caching en foutafhandeling

- [ ] **TM-009 [P1] Echte scanvoortgang:** rapporteer pagina, ontdekte/aangevraagde/opgehaalde aantallen en actuele status *tijdens* `FetchMessageMetadataAsync`, niet alleen bij start en einde. Toon realistische percentages of 'onbekend totaal'.
- [ ] **TM-010 [P1] Scans afbreken/herstarten:** `CancellationTokenSource` correct opruimen bij component-disposal, dubbele scans voorkomen en geldige cache behouden na annulering.
- [ ] **TM-011 [P1] Gmail API-fouten:** onderscheid `401/403`, `404`, rate limiting (`429`), serverfouten, time-outs en annulering. Gebruik passende backoff/jitter en eventuele `Retry-After` in plaats van alle fouten blind drie keer te herhalen.
- [ ] **TM-012 [P1] Lege mailbox correct cachen:** een complete scan met nul berichten moet een geldige scan blijven; controleer scanstatus in plaats van `cachedMetadata.Count > 0` om onnodige API-calls te vermijden.
- [ ] **TM-013 [P2] Force rescan echt vers ophalen:** laat de vijfminuten-cache in `GmailClient` een expliciete volledige rescan niet verhinderen.
- [ ] **TM-014 [P2] Grote mailboxes:** voeg paginate-/quotaobservability, bounded concurrency en voorspelbaar geheugengebruik toe; voorkom onverwachte volle her-scans.
- **Acceptatie:** scannen werkt correct voor 0, 1, 500, >500 en veel duizenden berichten, inclusief annuleren, tokenverlies, rate limits en een nieuwe volledige scan.

### Dashboard, afzenders, labels en filters

- [ ] **TM-015 [P1] Dashboard verversen na actie:** vernieuw statistieken, suggesties en afzenderlijst vanuit dezelfde actuele cache-snapshot na archiveren/verplaatsen/prullenbak/lezen.
- [ ] **TM-016 [P1] Labelnamen tonen bij cachegebruik:** laad of bewaar de Gmail label-ID-naar-naam mapping, ook als berichten uit de cache komen; voorkom dat `Label_...` de zichtbare mapnaam is.
- [ ] **TM-017 [P1] Juiste mapstatus:** toon `Trash`/`Spam`/`Sent`/`Draft` correct, ook wanneer een bericht extra gebruikerslabels heeft; herbekijk `ResolvePrimaryFolder`.
- [ ] **TM-018 [P1] 'Move' versus 'add label' definiëren:** de werkbranch heeft nu twee gescheiden acties, met previewuitleg, verplichte expliciete labelkeuze en unit tests voor labelbehoud. Open: echte Gmail-integratie-/browserchecks en complexe labelgevallen.
- [ ] **TM-019 [P2] Alle bestaande filtermogelijkheden in UI:** voeg de al ondersteunde leeftijdsfilters, nieuwsbrief-, notificatie- en expliciete Gmail-label-filters toe; combineerbaar met sender/domain/subject/read/attachment.
- [ ] **TM-020 [P1] Suggesties veilig en correct uitvoerbaar maken:** klik op cleanup-suggestie -> exacte berichtselectie -> preview -> bevestiging -> Gmail-actie -> dashboardupdate. **Open niet standaard Archive voor suggesties met actie `Preview selected`** (zoals oude ongelezen mails): `SenderOverview.razor` roept nu bij `InitialSelectedMessageIds` onvoorwaardelijk `OpenActionPane(MailBulkAction.Archive)` aan. Toon eerst alleen de selectie of map expliciet de voorgestelde actie, met aparte gebruikerskeuze. Geen automatische mutaties of deletions.
- [ ] **TM-021 [P2] UX-afwerking:** duidelijke lege mailbox, verlopen token, foutmelding, retry, accountwissel, laden en gedeeltelijke successen; keyboardtoegankelijkheid, mobiel/responsief gedrag en heldere waarschuwingslabels.
- [ ] **TM-022 [P2] Renderperformance:** voorkom herhaald filteren van `filteredMessages` voor iedere afzender bij elke render; pre-group berichten, overweeg virtualisatie en debounce van zoekinvoer op basis van benchmarks.
- **Acceptatie:** elk filter en elke suggestie is bereikbaar via de UI, gecombineerd selecteren werkt veilig, labelnamen zijn leesbaar en tellerwaarden kloppen na een actie.

### Gmail-bulkacties volledig afmaken

- [ ] **TM-023 [P1] Alle zes workflows end-to-end:** Trash, Archive, Mark read, Mark unread, Move to existing label, Create label + move met een afzonderlijk Gmail-testaccount testen.
- [ ] **TM-024 [P1] Efficiënt 'Move':** `MoveMessagesToLabelAsync` mag niet onnodig de volledige mailbox ophalen voor een selectie van enkele berichten; haal benodigde labels veilig uit de cache of vraag geselecteerde ID's gericht op.
- [ ] **TM-025 [P1] Beschermde berichten:** standaard waarschuwing/expliciete bevestiging voor `STARRED`/`IMPORTANT` en recente/ongeopende berichten; geen 'permanent delete' invoeren.
- [ ] **TM-026 [P2] Bulkactie-UX:** dubbelklikbescherming, Cancel-blokkering tijdens mutatie en gerichte retry na Gmail-herlezing zijn geimplementeerd. Open: werkelijke voortgang bij grote batches, live partial-failure-tests en browser-E2E.
- **Acceptatie:** alle acties blijven ook na verversen in Gmail zichtbaar; nul selecties doen niets; gedeeltelijke fouten zijn duidelijk en veroorzaken geen stille inconsistentie.

## 4. Fase C - Duurzaamheid en synchronisatie

- [ ] **TM-027 [P1 bij multi-device/productie] Duurzame metadata-opslag:** kies SQLite voor één instantie of een serverdatabase voor meerdere instanties. Maak tabellen voor `Account`, `Scan`, `MessageMetadata`, `Label` en eventueel `ActionRun`; gebruik migraties, indexes en unieke sleutels (`accountId + messageId`).
- [ ] **TM-028 [P1 bij productie] Veilige tokenopslag:** tokens versleuteld per account opslaan, sleutels veilig beheren, rotatie/uitloggen ondersteunen en geen tokens of gevoelige headers in logs schrijven.
- [ ] **TM-029 [P1] Gmail History API incremental sync:** bewaar een per-account `historyId`, haal wijzigingen na die ID op, verwerk toevoegingen/verwijderingen/labelwijzigingen idempotent, en val terug op een volledige scan bij ongeldige/verlopen history.
- [ ] **TM-030 [P2] Scan- en sync-jobs beheren:** één actieve job per account, status/cancel/retry, resumebaar waar passend, herstartbestendig bij gekozen opslagmodel.
- [ ] **TM-031 [P2] Retentie en accountverwijdering:** configureer bewaartermijnen, maak 'wis mijn lokale data' en logout/account disconnect controleerbaar, inclusief metadata, scans en tokens.
- **Acceptatie:** na app-herstart blijft de juiste metadata bij de juiste gebruiker; externe Gmail-wijzigingen komen uiteindelijk in de lokale weergave zonder volledige her-scan waar mogelijk.

## 5. Fase D - Tests, CI, security en productie

### Testmatrix

- [ ] **TM-032 [P0/P1] Unit tests voor de gevonden risico's:** geselecteerde berichten onder filters, zero-message-cache, user-key zonder HTTP-context, gelijktijdige cache-updates, labelprecedentie, OAuth-tokenafwijzing, scan-cancel en partial failures.
- [ ] **TM-033 [P1] Gmail adapter-/integratietests:** HTTP-contracttests met gesimuleerde Gmail-antwoorden voor `429`, `5xx`, partial/timeout, meerdere pagina's en labelmutaties; optioneel aparte echte testmailbox (geen persoonlijke mailbox).
- [ ] **TM-034 [P1] Browser-E2E-tests:** de volledige interactieve route Google login (testauth), scan, dashboard, filter, select, preview, bevestig, logout en reconnect.
- [ ] **TM-035 [P1] Multisessietest:** twee Google-accounts, twee browsers, gelijktijdige scan/actie/logout; nul cross-account-lekken.
- [ ] **TM-036 [P2] GitHub Actions CI:** minimaal `dotnet restore`, `dotnet build -c Release` en `dotnet test` bij elke PR; controleer ook formatting en afhankelijkheden. Voor de onderzochte commit waren geen statuschecks of gekoppelde workflow-runs beschikbaar.
- [ ] **TM-037 [P2] Performancebenchmarks:** meet 100/1.000/10.000/50.000+ berichten, geheugen, render/responsiviteit, Gmail API-aantallen en rate limits; leg acceptatiegrenzen vast op basis van metingen.
- **Acceptatie:** CI groen en browser-E2E + multiuser + Gmail-mutaties aantoonbaar geslaagd vóór publieke release.

### Beveiliging, privacy en deployment

- [ ] **TM-038 [P1] Securityreview:** OAuth/cookieconfiguratie, autorisatie op elke mutatie, antiforgery, tokenverversing, logredactie, sessie-isolatie, rate limiting en afhankelijkheidsupdates.
- [ ] **TM-039 [P1] Privacyreview:** documenteer precies welke metadata worden opgeslagen (onder meer afzender, onderwerp, datum en labels), doelbinding, bewaartermijn, verwijderen en toegang; zorg voor een passende privacyverklaring bij extern gebruik.
- [ ] **TM-040 [P1 bij publieke lancering] Google OAuth-consent/verificatie:** registreer correcte redirects, geautoriseerde domeinen, Gmail-scopes, testgebruikers of appverificatie waar vereist.
- [ ] **TM-041 [P2] Productieconfiguratie:** HTTPS, secrets via veilige configuratie, persistente Data Protection keys, databaseback-ups, structured logging zonder PII, health checks en operationele foutmeldingen.
- [ ] **TM-042 [P2] Documentatie corrigeren:** README zegt `https://localhost:5001` terwijl `Properties/launchSettings.json` `https://localhost:51179` instelt; documenteer werkelijke ports, Google redirect URI, installatiestappen, tests en releaseprocedure.
- **Acceptatie:** reproduceerbare installatie/deployment, beschikbare monitoring en geen gevoelige tokens/metadata in logs.

## 6. Volledige acceptatiechecklist voor de huidige productfunctionaliteit

Vink dit pas af nadat de **complete flow in een draaiende app** is aangetoond:

- [ ] Nieuwe gebruiker kan met Google aanmelden, toestemming geven en afmelden.
- [ ] Ongeldige/verlopen Google-tokens leiden tot duidelijke heraanmelding.
- [ ] Geen toegang tot mailboxgegevens of acties zonder geldige accountidentiteit.
- [ ] Nieuwe gebruiker ziet eerst scan-/onboarding-scherm, geen misleidend leeg dashboard.
- [ ] Scan gebruikt metadata-only en haalt geen e-mailbody op.
- [ ] Scannen toont echte voortgang, is annuleerbaar en herhaalbaar.
- [ ] Scanner ondersteunt lege, kleine en grote mailboxen.
- [ ] Dashboard toont correcte totalen, gelezen/ongelezen, leeftijd, bijlagen, afzenders, domeinen, nieuwsbrieven en scangeschiedenis.
- [ ] Afzendergroepen kunnen uit-/ingeklapt worden en tonen correcte berichten.
- [ ] Zoeken, sorteren en alle filters werken afzonderlijk en gecombineerd.
- [ ] Selections (individueel/afzender/zichtbaar/alle overeenkomsten) zijn voorspelbaar en expliciet.
- [ ] Cleanup-suggestie opent juiste berichtselectie en exacte preview.
- [ ] Trash, Archive, Mark read, Mark unread, Move en Create label + move werken echt in Gmail.
- [ ] Preview toont exacte ID-set/aantal, afzenders, voorbeeldberichten en risicowaarschuwing.
- [ ] Geen destructieve Gmail-mutatie zonder expliciete bevestiging.
- [ ] Cache, dashboard en Gmail blijven na elke mutatie consistent, ook bij gedeeltelijke fout.
- [ ] Nieuwe Gmail-wijzigingen worden opgepikt via rescan of incremental sync.
- [ ] Cache/tokens van account A worden nooit gedeeld met account B.
- [ ] Bij een herstart blijven gegevens bewaard volgens het gekozen opslag- en retentiebeleid.
- [ ] Unit-, integratie-, browser-, security- en performancecontroles zijn geslaagd.
- [ ] Installatie- en deploymentdocumentatie is actueel.

## 7. Uitvoervolgorde en afhankelijkheden

1. **Eerst werkend + veilig:** TM-001 -> TM-002/003/005 -> TM-004/006 -> TM-032/034/035.
2. **Daarna betrouwbaar opruimen:** TM-007/008 -> TM-015/016/017/018/023/024/025 -> Gmail-integratietests.
3. **Daarna complete UX:** TM-009 t/m TM-014 en TM-019 t/m TM-022 + TM-026.
4. **Daarna duurzame productiearchitectuur:** TM-027/028 -> TM-029/030/031.
5. **Tot slot release:** TM-036 t/m TM-042 + volledige acceptatiechecklist.

**Release-gate:** niet publiek uitrollen voordat TM-001 t/m TM-008, kernacties en de multiuser-/browser-E2E-tests aantoonbaar geslaagd zijn. Voor multi-device of multi-instance gebruik zijn bovendien duurzame opslag en correct tokenbeheer noodzakelijk.

## 8. Optionele productuitbreidingen (geen MVP-blokkers)

- [ ] **TM-043 [P3] Undo/herstel** van archiveren, labelwijzigingen en verplaatsen naar prullenbak, met duidelijke beperkingen en korte audittrail.
- [ ] **TM-044 [P3] Veilige unsubscribe-workflow** op basis van `List-Unsubscribe`, met expliciete toestemming en controle tegen phishing; geen automatische onbetrouwbare links openen.
- [ ] **TM-045 [P3] Opgeslagen filters en schoonmaakprofielen**, bijvoorbeeld 'oude nieuwsbrieven', met handmatige preview en bevestiging.
- [ ] **TM-046 [P3] Inzicht in bespaarde ruimte en historiek**, alleen uit beschikbare metadata of betrouwbaar gemeten resultaten.
- [ ] **TM-047 [P3] Terugkerende scanherinneringen of sync**, uitsluitend na expliciete gebruikersinstelling; geen automatische verwijderingen.
- [ ] **TM-048 [P3] Meer verfijnde suggesties**, met uitlegbare regels, uitsluitingen voor vertrouwde afzenders en makkelijk aanpasbare selectie.

## 9. Definition of Done per taak

Een checkbox gaat pas naar `[x]` wanneer:

1. De implementatie werkt voor het beschreven scenario en behoudt bestaande functionaliteit.
2. Er zijn relevante automatische tests, ook voor fouten/randgevallen.
3. Autorisatie, privacy en accountisolatie zijn gecontroleerd wanneer relevant.
4. De UI geeft een eerlijke terugkoppeling over wat Gmail werkelijk heeft gedaan.
5. De README/dit document zijn bijgewerkt waar gedrag of configuratie verandert.
6. De wijziging is gereviewd, CI is groen en indien relevant is de browserflow getest.

**Notitie voor volgende audits:** geef bevindingen een task-ID (TM-...) en voeg concrete codeverwijzingen/testresultaten toe. Laat afgeronde taken staan met `[x]` zodat voortgang traceerbaar blijft.
