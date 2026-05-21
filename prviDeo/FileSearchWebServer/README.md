# FileSearchWebServer

Konzolni C# web server za pretragu naziva fajlova po kljucnoj reci.

## Pokretanje

```bash
dotnet run
```

Server se pokrece na:

```text
http://localhost:5050/
```

Primer pretrage:

```text
http://localhost:5050/dotnet
```

Ako postoji fajl ciji naziv sadrzi zadatu kljucnu rec, server vraca HTML listu linkova. Klik na link salje GET zahtev oblika:

```text
http://localhost:5050/naziv_fajla.ekstenzija
```

i fajl se download-uje iz `root` direktorijuma.

## Arhitektura

- `WebServer` sadrzi beskonačnu petlju za osluskivanje zahteva preko `HttpListener.GetContext()`.
- Prijem zahteva i obrada zahteva su razdvojeni.
- Pristigli zahtevi se smestaju u `BlockingRequestQueue`.
- Obradu vrsi fiksan broj worker niti, cime se kontrolise broj paralelnih obrada.
- Nema `Task`, `async` ni `await`.
- Sinhronizacija je blokirajuca i koristi `lock`, `Monitor.Wait` i `Monitor.Pulse`.
- `SearchCache` je thread-safe LRU cache ogranicene velicine.
- Cache stampede je resen tako sto se za istu kljucnu rec pretraga izvrsava samo jednom, dok ostale niti cekaju rezultat preko `Monitor.Wait`.
- `ThreadSafeLogger` zakljucava upis u konzolu i log fajl.
- `FileSearchService` zakljucava pristup root direktorijumu i dozvoljava samo fajlove iz tog direktorijuma.

## Kriticne sekcije

1. Red zahteva u `BlockingRequestQueue`.
2. Cache strukture u `SearchCache`.
3. Cekanje na rezultat cache entry-ja kod cache stampede situacije.
4. Upis u log fajl u `ThreadSafeLogger`.
5. Pristup fajlovima u `FileSearchService`.

## Testiranje

U browser-u otvoriti vise tabova ili koristiti Postman/JMeter/skriptu i poslati vise paralelnih zahteva, npr:

```text
http://localhost:5050/dotnet
http://localhost:5050/sysprog
http://localhost:5050/lab
```

U `logs/server.log` se vidi kada je zahtev dodat u red, kada je cache hit/miss i kada vise niti cekaju isti rezultat.
