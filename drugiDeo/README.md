# FileSearchWebServer — Projekat 2

Konzolni C# web server koji prima HTTP GET zahteve, pretražuje nazive fajlova u lokalnom `root` direktorijumu i vraća HTML stranicu sa rezultatima i linkovima za download. Projekat demonstrira konkurentnu obradu zahteva uz korišćenje `Task`-ova, asinhronih operacija, mehanizama sinhronizacije i thread-safe keširanja.

## Tehnologije

![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)

## Sadržaj

- [Pokretanje](#pokretanje)
- [Arhitektura](#arhitektura)
- [Async operacije i taskovi](#async-operacije-i-taskovi)
- [ContinueWith](#continuewith)
- [Cancellation token i timeout](#cancellation-token-i-timeout)
- [Thread-safe keš (LRU + Cache Stampede)](#thread-safe-keš-lru--cache-stampede)
- [Kritične sekcije i sinhronizacija](#kritične-sekcije-i-sinhronizacija)
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
private const int WorkerCount = 8;    // broj paralelnih worker taskova
private const int CacheSize   = 3;    // maksimalan broj unosa u LRU kešu
```

---

## Arhitektura

Sistem je zasnovan na razdvajanju prijema i obrade zahteva:

```
                  ┌─────────────────────────────────────────┐
HTTP klijent ───► │  WebServer.Run()  (accept petlja, nit)  │
                  │  HttpListener.GetContext() — blokirajuće │
                  └──────────────┬──────────────────────────┘
                                 │ Enqueue
                                 ▼
                  ┌──────────────────────────┐
                  │   BlockingRequestQueue   │  ◄── thread-safe red
                  │  Queue + SemaphoreSlim   │
                  └──────────────┬───────────┘
                                 │ DequeueAsync (await)
                    ┌────────────┼────────────┐
                    ▼            ▼            ▼
               Worker 1     Worker 2  ...  Worker 8      (Task-ovi)
                    │
                    ▼
          ProcessRequestAsync
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
- **Worker taskovi** asinhrono čekaju na red i obrađuju zahteve konkurentno.
- **Broj paralelnih obrada** je kontrolisan fiksnim brojem worker taskova.
- **`Main` nije asinhron** — taskovi se startuju iz `Main`, a accept petlja ostaje blokirajuća. Entry point ne treba da bude `async` jer nema `await` u njemu.

---

## Async operacije i taskovi

| Operacija | Mehanizam | Razlog |
|-----------|-----------|--------|
| Čekanje na zahtev u redu | `SemaphoreSlim.WaitAsync` | Worker ne blokira nit dok nema posla |
| Obrada HTTP zahteva | `async Task` + `await` | Nit se oslobađa tokom I/O čekanja |
| Čitanje fajla za download | `File.ReadAllBytesAsync` | Asinhroni fajl I/O |
| Slanje odgovora klijentu | `OutputStream.WriteAsync` | Asinhroni mrežni I/O |
| Filtriranje i sortiranje naziva | `Task.Run` | CPU-bound operacija — jedino mesto gde `Task.Run` ima smisla |
| Čekanje na rezultat pretrage | `await entry.Completion.Task` | Čekanje na `TaskCompletionSource` bez blokiranja |

> **Napomena o `Task.Run`:** Koristi se isključivo za CPU-bound deo pretrage (filtriranje i sortiranje niza stringova). Na svim ostalim mestima koristi se direktni `await` nad I/O operacijama, jer bi `Task.Run` za I/O samo vezao threadpool nit bez ikakve koristi.

**Konzolni unos** (`Console.ReadKey`) je ostavljen na klasičnoj niti — to je blokirajuća operacija koja nema smisleni async ekvivalent u ovom kontekstu.

---

## ContinueWith

`ContinueWith` je demonstriran na dva mesta gde nastavak predstavlja akciju koja prirodno sledi po završetku taska, bez blokiranja pozivajućeg koda:

**1. `WebServer.StartWorkers` — praćenje worker taskova**

```csharp
workerTask.ContinueWith(t =>
{
    if (t.IsFaulted)
        _logger.Log("Worker Task greska: " + t.Exception?.GetBaseException().Message);
    else
        _logger.Log($"Worker {workerId} zavrsio rad.");
}, TaskScheduler.Default);
```

Loguje ishod svakog worker taska po završetku, bez da accept petlja mora da čeka ili da zna za to.

**2. `SearchCache.GetOrCreateAsync` — upis rezultata u keš**

```csharp
factoryTask.ContinueWith(t =>
{
    if (t.IsCanceled) { RemoveEntry(keyword); entry.Completion.TrySetCanceled(...); }
    else if (t.IsFaulted) { RemoveEntry(keyword); entry.Completion.TrySetException(...); }
    else { entry.Completion.TrySetResult(t.Result); TrimAfterStore(); }
}, TaskScheduler.Default);
```

Po završetku factory taska razrešava `TaskCompletionSource` i time budi sve taskove koji su čekali na isti rezultat (stampede zaštita). Uklanja keš unos u slučaju greške ili otkazivanja.

---

## Cancellation token i timeout

Svaka pretraga ima vremensko ograničenje od **5 sekundi**:

```
Task.WhenAny(searchTask, Task.Delay(5s))
     │
     ├── searchTask završi prvi → rezultat se šalje klijentu
     └── timeoutTask završi prvi → token se otkazuje → HTTP 408
```

- `CancellationTokenSource` se kreira po zahtevu i prosleđuje kroz `SearchCache` do `FileSearchService`.
- `ThrowIfCancellationRequested()` se poziva na više mesta u toku obrade.
- Otkazivanje nije vezano za `Ctrl+C` — to je programski mehanizam za kontrolisano zaustavljanje jedne konkretne operacije.

---

## Thread-safe keš (LRU + Cache Stampede)

### LRU strategija

`SearchCache` čuva najviše `CacheSize` unosa. Kada se keš popuni, uklanja se najduže nekorišćeni unos (`LinkedList` kao LRU lista + `Dictionary` za O(1) pristup).

### Cache Stampede zaštita

Problem: ako 20 taskova istovremeno traži isti keyword koji nije u kešu, svi bi pokrenuli istu skupu pretragu.

Rešenje pomoću `TaskCompletionSource`:

```
Zahtev 1 (MISS) → kreira CacheEntry sa TaskCompletionSource → pokreće pretragu
Zahtev 2 (nađe entry) → čeka na entry.Completion.Task  ┐
Zahtev 3 (nađe entry) → čeka na entry.Completion.Task  ├── svi čekaju isti Task
...                                                      ┘
Pretraga završi → ContinueWith → TrySetResult → svi se bude odjednom
```

Pretraga se izvršava tačno jednom, bez obzira na broj paralelnih zahteva.

### Sinhronizacija keša

Koristi se `ReaderWriterLockSlim` sa upgradeable read lock:

- **Read lock**: provera da li unos postoji (više taskova može čitati istovremeno)
- **Upgradeable read lock**: početna provera pre potencijalnog pisanja
- **Write lock**: dodavanje novog unosa ili ažuriranje LRU liste

---

## Kritične sekcije i sinhronizacija

| Komponenta | Mehanizam | Šta štiti |
|------------|-----------|-----------|
| `BlockingRequestQueue` | `lock` + `SemaphoreSlim` | Red pristiglih zahteva |
| `SearchCache` | `ReaderWriterLockSlim` | Keš rečnik i LRU lista |
| `FileSearchService` | `ReaderWriterLockSlim` | Pristup root direktorijumu |
| `ThreadSafeLogger` | `lock` | Upis u log fajl |
| `WebServer` | Fiksan broj worker taskova | Maks. broj paralelnih obrada |
| `WebServer._running` | `volatile bool` | Signal za zaustavljanje accept petlje |

> **Zašto `ReaderWriterLockSlim` a ne `lock`?** Keš i fajl servis su read-heavy — pretraga u kešu (read) se dešava mnogo češće od upisivanja rezultata (write). `ReaderWriterLockSlim` dozvoljava paralelno čitanje više taskova, dok `lock` bi serializovao sve operacije.

---

## Ponašanje pod opterećenjem

**Veliki broj paralelnih zahteva za različite ključne reči:**
- Accept petlja brzo ubacuje zahteve u red.
- Tačno `WorkerCount` taskova obrađuje istovremeno — nema nekontrolisanog kreiranja taskova.
- Ostali zahtevi čekaju u redu bez blokiranja niti.

**Veliki broj istih zahteva (stampede scenario):**
- Samo jedan task izvršava pretragu.
- Svi ostali asinhrono čekaju na `TaskCompletionSource`.
- Nema redundantnog rada ni na serveru ni na fajl sistemu.

**Timeout scenarijo:**
- Svaka pretraga ima rok od 5 sekundi.
- Po isteku, token se otkazuje i klijent dobija `408 Request Timeout`.
- Keš unos se uklanja kako ne bi ostao "zaglavljeni" nedovršeni entry.

---

## Testiranje

Priložena je Python skripta `testiranje.py` za testiranje API-ja:

```bash
pip install requests
python testiranje.py
```

Skripta pokreće:
1. **Test cache miss → hit** — isti keyword se traži dva puta, drugi put treba biti brži.
2. **Stampede test** — 20 niti istovremeno traži isti keyword; u logu servera treba biti vidljivo da se pretraga izvršila samo jednom.

Moguće je koristiti i **Postman** za manuelno testiranje pojedinačnih zahteva.

Log fajl se kreira u:
```
logs/server.log
```

---

## Struktura projekta

```
FileSearchWebServer-P2/
├── Program.cs                 # Entry point, pokretanje server i shutdown niti
├── WebServer.cs               # Accept petlja, worker taskovi, obrada zahteva
├── BlockingRequestQueue.cs    # Thread-safe red zahteva (Queue + SemaphoreSlim)
├── SearchCache.cs             # LRU keš sa stampede zaštitom (ReaderWriterLockSlim)
├── FileSearchService.cs       # Pretraga i čitanje fajlova
├── ThreadSafeLogger.cs        # Thread-safe logovanje (lock + StreamWriter)
├── ClientRequest.cs           # Wrapper za HttpListenerContext
├── root/                      # Direktorijum sa fajlovima koje server pretražuje
├── logs/                      # Generisani log fajlovi
└── testiranje.py              # Python skripta za testiranje
```
