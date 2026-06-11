# FileSearchWebServer — Projekat 1

Konzolni C# web server koji prima HTTP GET zahteve, pretražuje nazive fajlova u lokalnom `root` direktorijumu i vraća HTML stranicu sa rezultatima i linkovima za download. Projekat demonstrira konkurentnu obradu zahteva korišćenjem **niti**, **ThreadPool-a** i blokirajuće sinhronizacije kroz `lock`, `Monitor.Wait` i `Monitor.Pulse`.

## Tehnologije

![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)

## Sadržaj

- [Pokretanje](#pokretanje)
- [Arhitektura](#arhitektura)
- [Sinhronizacija](#sinhronizacija)
- [Thread-safe keš (LRU + Cache Stampede)](#thread-safe-keš-lru--cache-stampede)
- [Kritične sekcije](#kritične-sekcije)
- [Ponašanje pod opterećenjem](#ponašanje-pod-opterećenjem)
- [Testiranje](#testiranje)
- [Struktura projekta](#struktura-projekta)

---

## Pokretanje

```bash
dotnet run
```

Server sluša na:

```
http://localhost:5050/
```

### Primeri zahteva

| URL | Opis |
|-----|------|
| `http://localhost:5050/` | Početna stranica sa formom za pretragu |
| `http://localhost:5050/dotnet` | Pretraga fajlova čiji naziv sadrži `dotnet` |
| `http://localhost:5050/algorithms.pdf` | Direktan download fajla `algorithms.pdf` |

Za graceful shutdown pritisnuti `ESC` u konzoli.

### Konfiguracija (`Program.cs`)

```csharp
private const int Port        = 5050;
private const int WorkerCount = 8;   // broj worker niti iz ThreadPool-a
private const int CacheSize   = 3;   // maksimalan broj unosa u LRU kešu
```

---

## Arhitektura

Sistem je zasnovan na razdvajanju prijema i obrade zahteva:

```
                  ┌─────────────────────────────────────────┐
HTTP klijent ───► │  WebServer.Run()  (glavna nit)          │
                  │  HttpListener.GetContext() — blokirajuće │
                  └──────────────┬──────────────────────────┘
                                 │ Enqueue + Monitor.Pulse
                                 ▼
                  ┌──────────────────────────┐
                  │   BlockingRequestQueue   │  ◄── thread-safe red
                  │   lock + Monitor.Wait    │
                  └──────────────┬───────────┘
                                 │ Dequeue (blokirajuće)
                    ┌────────────┼────────────┐
                    ▼            ▼            ▼
               Worker 1     Worker 2  ...  Worker 8    (ThreadPool niti)
                    │
                    ▼
            ProcessRequest
            ├── direktan download fajla
            └── pretraga po ključnoj reči
                    │
                    ▼
         SearchCache (LRU, stampede zaštita)
                    │
                    ▼
        FileSearchService (pretraga, čitanje fajlova)
```

- **Accept petlja** (`WebServer.Run`) radi na glavnoj niti i samo prima `HttpListenerContext`, ne obrađuje ga.
- **Worker niti** se uzimaju iz `ThreadPool`-a i blokirajuće čekaju na red zahteva.
- **Broj paralelnih obrada** je kontrolisan fiksnim brojem worker niti (`WorkerCount`).
- Nema `Task`, `async` ni `await` — sinhronizacija je isključivo blokirajuća.

---

## Sinhronizacija

Projekat koristi isključivo mehanizme iz `System.Threading`:

### `lock` + `Monitor.Wait` / `Monitor.Pulse` / `Monitor.PulseAll`

**`BlockingRequestQueue`** — producer-consumer red između accept niti i worker niti:

```csharp
// Accept nit (producer):
lock (_lock)
{
    _queue.Enqueue(request);
    Monitor.Pulse(_lock);       // budi jednu worker nit
}

// Worker nit (consumer):
lock (_lock)
{
    while (_queue.Count == 0 && _running)
        Monitor.Wait(_lock);    // oslobađa lock i blokira nit dok ne stigne signal

    return _queue.Dequeue();
}
```

Worker nit pozivom `Monitor.Wait` atomično oslobađa lock i stavlja nit u stanje čekanja — bez aktivnog čekanja (busy-wait). Accept nit pozivom `Monitor.Pulse` budi tačno jednu nit koja čeka.

**`Stop()`** — graceful shutdown:

```csharp
lock (_lock)
{
    _running = false;
    Monitor.PulseAll(_lock);    // budi SVE worker niti da bi videle _running = false
}
```

---

## Thread-safe keš (LRU + Cache Stampede)

### LRU strategija

`SearchCache` čuva najviše `CacheSize` unosa. Kada se keš popuni, uklanja se najduže nekorišćeni unos (`LinkedList` kao LRU lista + `Dictionary` za O(1) pristup).

### Cache Stampede zaštita

Problem: ako 20 niti istovremeno traži isti keyword koji nije u kešu, sve bi pokrenule istu skupu pretragu.

Rešenje pomoću `Monitor.Wait` / `Monitor.PulseAll` i per-entry lock objekta:

```
Nit 1 (MISS) → kreira CacheEntry → upisuje u rečnik → pokreće pretragu
Nit 2 (HIT)  → nađe entry → lock(entry.Sync) → Monitor.Wait  ┐
Nit 3 (HIT)  → nađe entry → lock(entry.Sync) → Monitor.Wait  ├── čekaju
...                                                            ┘
Pretraga završi → entry.IsReady = true → Monitor.PulseAll(entry.Sync)
                                           → sve niti se bude i čitaju rezultat
```

Ključna odluka: `entry.Sync` je **poseban objekat** od `_cacheLock`. To znači da niti koje čekaju na rezultat pretrage ne drže keš lock — ostale niti mogu slobodno čitati i pisati u keš dok čekanje traje.

```csharp
// Nit koja računa:
lock (entry.Sync)
{
    entry.Result = result;
    entry.IsReady = true;
    Monitor.PulseAll(entry.Sync);   // budi sve niti koje čekaju ovaj keyword
}

// Niti koje čekaju:
lock (entry.Sync)
{
    while (!entry.IsReady)
        Monitor.Wait(entry.Sync);   // čeka bez blokiranja keš locka

    return new List<string>(entry.Result ?? new List<string>());
}
```

---

## Kritične sekcije

| Komponenta | Mehanizam | Šta štiti |
|------------|-----------|-----------|
| `BlockingRequestQueue._queue` | `lock` + `Monitor.Wait/Pulse` | Red pristiglih zahteva |
| `SearchCache._entries` i `_lru` | `lock (_cacheLock)` | Keš rečnik i LRU lista |
| `CacheEntry.IsReady` / `Result` | `lock (entry.Sync)` | Stampede čekanje po entry-ju |
| `FileSearchService` | `lock (_fileAccessLock)` | Pristup root direktorijumu |
| `ThreadSafeLogger` | `lock (_lock)` | Upis u log fajl |
| `WebServer._running` | `volatile bool` | Signal za zaustavljanje accept petlje |

---

## Ponašanje pod opterećenjem

**Veliki broj paralelnih zahteva za različite ključne reči:**
- Accept petlja brzo ubacuje zahteve u red i poziva `Monitor.Pulse` za svaki.
- Tačno `WorkerCount` niti obrađuje istovremeno — nema nekontrolisanog kreiranja niti.
- Ostale worker niti blokiraju na `Monitor.Wait` u redu dok ne dobiju signal.

**Veliki broj istih zahteva (stampede scenario):**
- Samo jedna nit izvršava pretragu.
- Sve ostale blokiraju na `Monitor.Wait(entry.Sync)` — bez CPU trošenja.
- Po završetku pretrage `Monitor.PulseAll` budi sve odjednom.

---

## Testiranje

Za testiranje koristiti Postman ili browser sa više paralelnih tabova:

```
http://localhost:5050/dotnet
http://localhost:5050/algorithms
http://localhost:5050/quantum
```

U `logs/server.log` videti:
- `CACHE MISS` / `CACHE HIT` — keš pogoci i promašaji
- `CACHE STAMPEDE WAIT` — niti koje čekaju isti rezultat
- `CACHE REMOVE LRU` — uklanjanje najstarijeg unosa

---

## Struktura projekta

```
FileSearchWebServer/
├── Program.cs                 # Entry point, pokretanje servera i shutdown niti
├── WebServer.cs               # Accept petlja, ThreadPool workeri, obrada zahteva
├── BlockingRequestQueue.cs    # Thread-safe red (lock + Monitor.Wait/Pulse)
├── SearchCache.cs             # LRU keš sa stampede zaštitom (Monitor)
├── FileSearchService.cs       # Pretraga i čitanje fajlova (lock)
├── ThreadSafeLogger.cs        # Thread-safe logovanje (lock)
├── ClientRequest.cs           # Wrapper za HttpListenerContext
├── root/                      # Direktorijum sa fajlovima koje server pretražuje
└── logs/                      # Generisani log fajlovi
```
