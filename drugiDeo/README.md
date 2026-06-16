# FileSearchWebServer - Projekat 2

Konzolni C# web server koji prima HTTP GET zahteve, pretrazuje nazive fajlova u lokalnom `root` direktorijumu i vraca HTML stranicu sa rezultatima i linkovima za download. Drugi deo koristi `Task`, `async/await`, timeout i thread-safe cache.

## Pokretanje

```bash
dotnet run
```

Server slusa na:

```text
http://localhost:5050/
```

Primeri:

```text
http://localhost:5050/
http://localhost:5050/dotnet
http://localhost:5050/algorithms.pdf
```

Za graceful shutdown pritisnuti `ESC` u konzoli.

## Arhitektura

Drugi deo koristi task/thread pool model:

- `WebServer.Run()` radi accept petlju i blokirajuce ceka `HttpListenerContext`.
- Kada zahtev stigne, odmah se zakazuje preko `Task.Run`.
- `HandleRequestAsync` i `ProcessRequestAsync` obradjuju zahtev asinhrono.
- Download koristi `File.ReadAllBytesAsync` i `OutputStream.WriteAsync`.
- Server pamti aktivne request taskove samo da bi ih sacekao pri graceful shutdown-u.

Ne koristi se vise `BlockingRequestQueue` ni `ClientRequest`, jer nema rucnog reda izmedju prijema i obrade zahteva.

## Async Operacije

| Operacija | Mehanizam |
|---|---|
| Zakazivanje HTTP zahteva | `Task.Run` |
| Obrada zahteva | `async Task` + `await` |
| Citanje fajla | `File.ReadAllBytesAsync` |
| Slanje fajla | `OutputStream.WriteAsync` |
| Timeout pretrage | `CancellationTokenSource` + `Task.WhenAny` |
| Stampede zastita | `TaskCompletionSource<List<string>>` |

## Sinhronizacija

| Komponenta | Mehanizam | Sta stiti |
|---|---|---|
| `WebServer` | `lock` | lista aktivnih request taskova |
| `SearchCache` | `ReaderWriterLockSlim` | cache recnik i LRU lista |
| `FileSearchService` | `ReaderWriterLockSlim` | pristup root direktorijumu |
| `ThreadSafeLogger` | `lock` | upis u log fajl |
| `WebServer._running` | `volatile bool` | signal za zaustavljanje accept petlje |

## Cache I Timeout

`SearchCache` je thread-safe LRU cache sa maksimalnom velicinom `CacheSize`.

Svaki cache entry ima expiration od `CacheEntryExpirationSeconds`. Kada entry nije koriscen duze od tog vremena, brise se pri sledecem pristupu cache-u.

Cache stampede zastita radi preko `TaskCompletionSource`: prvi zahtev za keyword pokrece pretragu, a paralelni zahtevi za isti keyword cekaju isti task.

Svaka pretraga ima timeout od 5 sekundi. Ako timeout istekne, token se otkazuje i klijent dobija HTTP `408 Request Timeout`.

## Struktura projekta

```text
FileSearchWebServer-P2/
├── Program.cs
├── WebServer.cs
├── SearchCache.cs
├── FileSearchService.cs
├── ThreadSafeLogger.cs
├── root/
├── logs/
└── testiranje.py
```
