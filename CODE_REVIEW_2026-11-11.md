# Tidy Mail — code-review na merge (11 november 2026)

**Scope:** `main`, squash-merge van PR [#12](https://github.com/HarounMinhas/Tidy-Mail/pull/12), commit [`8eb1749`](https://github.com/HarounMinhas/Tidy-Mail/commit/8eb17491be3546bf22937bd8bd28f2d5f81ff77e).  
**Reviewtype:** statische code-review op werking, gegevensintegriteit, beveiliging en testdekking, vergelijkbaar met een pullrequestreview.  
**Uitgevoerde verificatie:** laatste PR GitHub Actions: .NET 8-build **geslaagd**; **34 tests geslaagd, 0 mislukt, 0 overgeslagen** ([run](https://github.com/HarounMinhas/Tidy-Mail/actions/runs/42921508661)).  
**Niet uitgevoerd:** een draaiende browser, Google OAuth met een testaccount, echte Gmail API-schrijfacties, twee gelijktijdige gebruikers, productie-deployment en grootschalige belastingtests. De review kan die functionaliteit dus **niet als end-to-end geslaagd certificeren**.  
**Geen Gmail-berichten aangepast** door deze review.

De P1/P2-categorieën hieronder zijn concrete reviewbevindingen of expliciet als risico gemarkeerde verificatiegaten. **P1** = vóór productie oplossen; **P2** = vóór release toetsen / hardenen.

## P1 — belangrijke codebevindingen en releaseblokkers

### RV-01 — Trash-actie blijft in dashboardstatistieken meetellen (codefout)

- **Code:** [`MailboxMetadataStore.cs`, regels 126–130](src/MailboxCleaner.Web/Application/MailboxScanning/MailboxMetadataStore.cs#L126-L130), gebruikt door [`SqliteMailboxMetadataStore.UpdateAfterActionAsync`, regels 210–219](src/MailboxCleaner.Web/Application/MailboxScanning/SqliteMailboxMetadataStore.cs#L210-L219).
- **Scenario:** Een gebruiker verplaatst tien geselecteerde e-mails naar Trash. De cache voegt `TRASH` toe maar verwijdert de tien metadatarecords niet. Het dashboard telt alle berichten uit de cache mee in [`MailboxStatsService.Build`, regels 44–56](src/MailboxCleaner.Web/Application/MailboxStats/MailboxStatsService.cs#L44-L56). De Gmail full-scan haalt daarentegen `IncludeSpamTrash=false` op ([`GmailClient.cs`, regels 50–54](src/MailboxCleaner.Web/Infrastructure/Google/Gmail/GmailClient.cs#L50-L54)).
- **Gevolg:** Opruimaantallen, afzenderstatistieken en suggesties zijn niet consistent na Trash totdat een nieuwe Gmail-sync plaatsvindt.
- **Aanpak:** Hanteer één invariant voor de actieve mailbox: na een bevestigde Trash-actie de berichten uit de actieve cache verwijderen, of consequent overal `TRASH` uitsluiten. Test de dashboardteller onmiddellijk na Trash en na rescan.

### RV-02 — mislukte rescan verbergt een eerdere succesvolle scan (codefout)

- **Code:** [`MailboxScanService.ScanAsync`, regels 28–32 en 109–121](src/MailboxCleaner.Web/Application/MailboxScanning/MailboxScanService.cs#L26-L121); [`Overview.razor`, regels 39–54](src/MailboxCleaner.Web/Pages/Overview.razor#L39-L54).
- **Scenario:** Er is een geslaagde scan met een bruikbare lokale cache. De gebruiker start Rescan en Gmail geeft een fout of de gebruiker annuleert. De nieuwe status wordt `Failed` of `Cancelled`, maar `Overview` geeft alleen toegang tot het dashboard wanneer de status `Completed` is.
- **Gevolg:** De gebruiker zit op het scanscherm terwijl de eerder geladen mailboxdata nog aanwezig is.
- **Aanpak:** Houd `lastSuccessfulScan` en `currentScanAttempt` apart bij; toon een foutbanner met 'Gebruik bestaande scan' en 'Probeer opnieuw'. Test zowel falen als annuleren na eerdere succesvolle scan.

### RV-03 — batchModify-acceptatie is geen individuele Gmail-verificatie (functionele beperking)

- **Code:** [`GmailClient.ExecuteBatchModifyAsync`, regels 297–332](src/MailboxCleaner.Web/Infrastructure/Google/Gmail/GmailClient.cs#L297-L332).
- **Gmail-contract:** [Google users.messages.batchModify](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/batchModify) accepteert maximaal 1.000 IDs en retourneert bij succes een **lege response**.
- **Gevolg:** Bij een succesvolle API-call worden alle ID's als `SucceededMessageIds` gemeld, hoewel geen individuele postcondition is gecontroleerd. De UI zegt wel voorzichtig 'acknowledged', maar de cache gebruikt die IDs al voor definitieve lokale mutaties.
- **Aanpak:** Voor gevoelige/grote acties verifieer de werkelijke labels/toestand van betrokken IDs, en onderscheid `accepted` van `verified`; leg specifieke tests vast voor ongeldige/verwijderde IDs en Gmail 429/5xx. Dit is een **releaseblokker voor betrouwbaarheid**, niet bewijs van een fout bij elke batch.

### RV-04 — geen echte browser-/Gmail-/multiuser-verificatie (releaseblokker)

- **Huidige tests:** 34 geautomatiseerde tests slagen, maar Gmail-acties worden in tests via gesimuleerde `IGmailClient`-implementaties uitgevoerd; er is geen geverifieerde browser-E2E-flow op echte OAuth-accounts.
- **Verplichte testscenario's:** Google login en consent, verlopen/revoked tokens, heraanmelden, lege/grote mailbox, stop/start scan, alle zes bestaande Gmail-acties plus Add label, complexe labels, gedeeltelijke mislukking, een herstart met SQLite en twee gelijktijdige accounts.
- **Extra te controleren:** [`SenderOverviewService.GetUserKey`, regel 165](src/MailboxCleaner.Web/Application/Services/SenderOverviewService.cs#L165) en [`SqliteTokenStore.AccountKey`, regels 55–64](src/MailboxCleaner.Web/Infrastructure/Security/SqliteTokenStore.cs#L55-L64) vertrouwen de scoped `MailboxUserContext`. Bevestig in reconnect/accountwisseltests dat die context nooit veroudert of losstaat van de actuele authenticatieclaims; dit is een **te verifiëren risico**, geen bewezen cross-account-incident.

## P2 — overige bevindingen

### RV-05 — logout via GET wist bewaarde accountgegevens

- **Code:** [`GoogleAuthController.Logout`, regels 102–114](src/MailboxCleaner.Web/Auth/GoogleAuthController.cs#L102-L114).
- Een `GET /auth/logout` wist SQLite-metadata en OAuth-tokens. GET is bedoeld voor veilige leesoperaties; navigatie naar deze URL kan dus onbedoeld blijvende server-side state wissen.
- **Aanpak:** POST + antiforgery-token, en maak het onderscheid tussen 'Afmelden', 'Verbinding verbreken' en 'Verwijder mijn lokale gegevens' expliciet. Dit is ook een privacy- en UX-keuze.

### RV-06 — productie-errorroute bestaat vermoedelijk niet

- **Code:** [`Program.cs`, regels 71–74](src/MailboxCleaner.Web/Program.cs#L71-L74) verwijst naar `/Error`, maar er is geen overeenkomstige Razor-route aangetroffen in de onderzochte bronbestanden.
- **Gevolg:** De globale exceptionhandler kan zelf eindigen met een onbruikbare 404/fallback wanneer een ongehandelde fout optreedt.
- **Aanpak:** Voeg een echte errorroute of `IExceptionHandler` toe en test een opzettelijke fout onder productieconfiguratie.

### RV-07 — verzonden mail met extra gebruikerslabel wordt als labelmap getoond

- **Code:** [`SenderOverviewService.ResolvePrimaryFolder`, regels 175–189](src/MailboxCleaner.Web/Application/Services/SenderOverviewService.cs#L175-L189).
- `INBOX`, `TRASH` en `DRAFT` krijgen prioriteit, maar `SENT` wordt pas na het eerste gebruikerslabel bekeken.
- **Gevolg:** Berichten met `SENT + Label_Project` verschijnen onder `Project` in plaats van `Sent`. Test welke mapsemantiek het product beoogt en pas prioriteit/labelsfilter daarop aan.

### RV-08 — elke history-sync met wijzigingen doorzoekt opnieuw alle bijlagen

- **Code:** [`GmailClient.FetchHistoryDeltaAsync`, regels 190–194](src/MailboxCleaner.Web/Infrastructure/Google/Gmail/GmailClient.cs#L190-L194); [`FetchAttachmentIdsAsync`, regels 466–485](src/MailboxCleaner.Web/Infrastructure/Google/Gmail/GmailClient.cs#L466-L485).
- **Gevolg:** Ook bij één veranderd bericht kan een mailboxbrede paginatie van `has:attachment` nodig zijn; verhoogt latency en quota-risico voor grote mailboxen.
- **Aanpak:** Cache/snapshot de attachment-ID-set, werk de delta bij waar betrouwbaar mogelijk, benchmark de API-kosten en documenteer het privacymodel.

### RV-09 — gedeeltelijke Gmail-fout triggert dure volledige herlezing

- **Code:** [`SenderOverviewService.ReconcileMailItemsAsync`, regels 101–110](src/MailboxCleaner.Web/Application/Services/SenderOverviewService.cs#L101-L110) en [`SenderOverview.razor`, regels 585–595](src/MailboxCleaner.Web/Components/SenderOverview.razor#L585-L595).
- Een gedeeltelijk mislukte actie veroorzaakt een geforceerde volledige metadata-scan, niet alleen controle van betrokken IDs.
- **Aanpak:** Verifieer eerst alleen geselecteerde IDs of gebruik Gmail history; herstel cache en cursor atomair, en houd volledige rescan als fallback.

### RV-10 — gevoelige metadata en Data Protection-keybeheer moeten worden gehard

- **Code:** [`SqliteMailboxMetadataStore`](src/MailboxCleaner.Web/Application/MailboxScanning/SqliteMailboxMetadataStore.cs), [`SqliteTokenStore`](src/MailboxCleaner.Web/Infrastructure/Security/SqliteTokenStore.cs) en [`Program.cs`, regels 17–27](src/MailboxCleaner.Web/Program.cs#L17-L27).
- OAuth-tokens zijn met Data Protection versleuteld, maar de metadata-json in SQLite (waaronder onderwerp/afzender) is niet op applicatieniveau versleuteld. De key ring wordt naar het bestandssysteem weggeschreven.
- **Aanpak:** Productierechten en bestandsvolume controleren; versleuteling/beheer van key ring en database/backups bepalen, minimale retentie en veilige accountverwijdering implementeren, meerdere instanties niet met lokale SQLite deployen zonder aangepaste architectuur.

## Functionele verificatiematrix

| Functie | Beoordeling na merge | Nog niet bewezen |
| --- | --- | --- |
| Google OAuth login/logout en tokenverversing | Code aanwezig, tokenstore-/state-unittests | Echte Google OAuth/cookie-reconnect/twee accounts |
| Metadata-only Gmail-scan en progressie | Implementatie + fake-Gmail-tests | Realtime browserprogressie, quotas, echte kleine/grote mailbox |
| SQLite-cache en historiek-sync | Persistentie-/history-unittests | Herstart op echte host, Gmail label-/trash-delta's, verlopen history-ID |
| Dashboardstatistieken en suggesties | Unitgedrag aanwezig; **RV-01** is inconsistentie | Direct na alle Gmail-acties, incl. Trash |
| Zoeken, groeperen en selecteren | Unitgedrag aanwezig; zichtbare/verborgen selectie beveiligd | Browser-E2E, grote datasets, keyboard/mobiel |
| Suggestie → review → actie | Code verplaatst automatische Archive weg uit review | Gebruikersflow volledig in browser |
| Trash / Archive / read / unread | Fake-Gmail-tests aanwezig | Werkelijke Gmail-postcondities en gedeeltelijke fouten |
| Move / Add label / nieuw label | Fake-Gmail-tests; Add behoudt andere labels | Echte Gmail-labelgevallen, mapsemantiek en retry |
| Beveiliging / accountisolatie | Google ID-tokenvalidatie en tokenstore-tests | Volledige security-/privacyreview en twee accounts |

**Reviewconclusie:** de kern is geïmplementeerd en de code compileert. De app is **nog niet aantoonbaar production-ready**. RV-01 en RV-02 zijn concrete functionele fouten die we vóór publieke inzet moeten herstellen. RV-03 en RV-04 zijn belangrijke verificatie-/betrouwbaarheidsblokkades. De resterende punten moeten vóór of rondom release worden ingepland.

## Aanbevolen werkvolgorde

1. **RV-01 en RV-02 oplossen en unit-/regressietests toevoegen.**
2. **RV-03 verder hardenen:** na mutaties individuele postconditions controleren, gerichte retries en idempotente herstelstrategie.
3. **RV-04:** integratie- en browser-E2E in testomgeving met twee accounts, incl. alle Gmail-acties en login/logout.
4. **RV-05/06/07:** veilige logoutsemantiek, productie-errorroute, map-/labelprioriteit.
5. **RV-08/09/10:** performance en quota, privacy/security, hosting/backups en productiecontrole.
6. Optionele productfeatures (Undo, unsubscribe, opgeslagen filters, synchronisatie op schema) pas ná deze release-gates.

Verwerk nieuwe testresultaten in [TODO.md](TODO.md) en wijzig hoofd-`[ ]`-taken alleen naar `[x]` nadat de vereiste live- en regressietests zijn aangetoond.
