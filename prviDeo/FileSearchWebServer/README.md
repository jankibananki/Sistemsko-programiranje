# FileSearchWebServer - Projekat 1

Konzolni C# web server koji prima HTTP GET zahteve, pretrazuje nazive fajlova u lokalnom `root` direktorijumu i vraca HTML stranicu sa rezultatima i linkovima za download.

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

## Konfiguracija

```csharp
private const int Port = 5050;
private const int CacheSize = 3;
private const int CacheEntryExpirationSeconds = 60;
```

## Arhitektura

Prvi deo koristi klasicni `ThreadPool` model:

- `WebServer.Run()` radi accept petlju i blokirajuce ceka `HttpListenerContext`.
- Kada zahtev stigne, odmah se predaje thread pool-u preko `ThreadPool.QueueUserWorkItem`.
- `ProcessRequest` obradjuje zahtev sinhrono: pocetna stranica, direktan download fajla ili pretraga po kljucnoj reci.
- Server broji aktivne zahteve i pri graceful shutdown-u ceka da se trenutno aktivne obrade zavrse.

Ne koristi se vise `BlockingRequestQueue` ni `ClientRequest`, jer nema rucnog reda izmedju prijema i obrade zahteva.

## Sinhronizacija

| Komponenta | Mehanizam | Sta stiti |
|---|---|---|
| `WebServer` | `lock` + `Monitor.Wait/PulseAll` | broj aktivnih zahteva pri gasenju |
| `SearchCache` | `lock` + `Monitor.Wait/PulseAll` | cache recnik, LRU lista i stampede cekanje |
| `FileSearchService` | `lock` | pristup root direktorijumu |
| `ThreadSafeLogger` | `lock` | upis u log fajl |
| `WebServer._running` | `volatile bool` | signal za zaustavljanje accept petlje |

## Cache

`SearchCache` je thread-safe LRU cache sa maksimalnom velicinom `CacheSize`.

Svaki cache entry ima expiration od `CacheEntryExpirationSeconds`. Kada entry nije koriscen duze od tog vremena, brise se pri sledecem pristupu cache-u.

Ako vise niti istovremeno trazi isti keyword koji nije u cache-u, samo jedna nit izvrsava pretragu. Ostale cekaju na isti cache entry pomocu `Monitor.Wait`, a po zavrsetku pretrage bude se pomocu `Monitor.PulseAll`.

## Struktura projekta

```text
FileSearchWebServer/
├── Program.cs
├── WebServer.cs
├── SearchCache.cs
├── FileSearchService.cs
├── ThreadSafeLogger.cs
├── root/
└── logs/
```
