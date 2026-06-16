# FileSearchWebServer - Projekat 2

Konzolni C# web server koji preko browser-a prima GET zahteve, pretrazuje nazive fajlova u `root` direktorijumu i vraca HTML listu linkova za download.

## Pokretanje

```powershell
dotnet run
```

Server slusa na:

```text
http://localhost:5050/
```

Primeri:

```text
http://localhost:5050/dotnet
http://localhost:5050/algorithms.pdf
```

Za graceful shutdown pritisnuti `ESC` u konzoli.

## Arhitektura

Sistem koristi `HttpListener` za prijem zahteva, a obradu predaje task/thread pool mehanizmu:

- `WebServer.Run()` je accept petlja i prihvata `HttpListenerContext`.
- Svaki pristigli zahtev se odmah zakazuje kroz `Task.Run`.
- `HandleRequestAsync` obradjuje zahtev i poziva asinhrone metode za pretragu ili slanje fajla.
- Server pamti aktivne request taskove samo da bi ih sacekao pri graceful shutdown-u.

`Main` nije asinhron jer nema potrebe da entry point bude `async`. Serverova accept petlja ostaje blokirajuca, dok se pojedinacni zahtevi obradjuju konkurentno.

## Async operacije i taskovi

Korisceno je:

- `Task.Run` za predavanje svakog HTTP zahteva thread pool-u.
- `await` u obradi HTTP zahteva.
- `File.ReadAllBytesAsync` za download fajla.
- `OutputStream.WriteAsync` za slanje fajla klijentu.
- `Task.Run` u CPU-bound delu pretrage, gde se filtriraju i sortiraju nazivi fajlova.

Klasicna nit je zadrzana za `Console.ReadKey`, jer je to blokirajuca konzolna operacija i nema smislen async ekvivalent u ovom programu.

## ContinueWith

`ContinueWith` je demonstriran na dva mesta:

- U `SearchCache.GetOrCreateAsync`, nastavak se izvrsava kada se zavrsi stvarna pretraga. Tada se rezultat upisuje u cache, ili se cache entry uklanja ako je doslo do greske ili otkazivanja.
- U `WebServer.TrackRequestTask`, nastavak uklanja zavrsen request task iz liste aktivnih taskova i loguje eventualnu gresku.

Ova upotreba je smislena jer nastavak predstavlja akciju koja treba da se desi posle zavrsetka taska, bez blokiranja koda koji je task pokrenuo.

## Cancellation token

Pretraga koristi `CancellationTokenSource` sa timeout-om od 5 sekundi:

- Ako se pretraga zavrsi na vreme, rezultat se salje klijentu i smesta u cache.
- Ako timeout istekne, token se otkazuje.
- Metode koje dobiju token pozivaju `ThrowIfCancellationRequested()`.
- Klijent dobija HTTP `408 Request Timeout`.

Ovo nije isto sto i `Ctrl+C`; token je programski mehanizam za kontrolisano otkazivanje operacije.

## Cache

`SearchCache` je thread-safe LRU cache sa maksimalnom velicinom (`CacheSize = 3`).

Svaki cache entry ima expiration od `CacheEntryExpirationSeconds`. Ako entry nije koriscen duze od tog vremena, brise se pri sledecem pristupu cache-u.

Cache stampede zastita:

- Prvi zahtev za keyword kreira `TaskCompletionSource<List<string>>` i pokrece stvarnu pretragu.
- Paralelni zahtevi za isti keyword dobijaju isti cache entry i cekaju isti task.
- Pretraga se za isti keyword izvrsava samo jednom.
- Kada se rezultat dobije, `ContinueWith` popunjava cache i budi sve cekace kroz zavrsavanje taska.

Za sinhronizaciju cache-a koristi se `ReaderWriterLockSlim`, jer vise reader-a moze istovremeno da cita stanje cache-a, dok se izmene recnika i LRU liste rade kroz write lock.

## Kriticne sekcije

| Komponenta | Mehanizam | Sta stiti |
|---|---|---|
| `SearchCache` | `ReaderWriterLockSlim` | cache recnik i LRU lista |
| `FileSearchService` | `ReaderWriterLockSlim` | pristup root direktorijumu |
| `ThreadSafeLogger` | `lock` | upis u log fajl |
| `WebServer` | `lock` | lista aktivnih request taskova |

## Ponasanje pod opterecenjem

Kod velikog broja paralelnih zahteva accept petlja svaki zahtev zakazuje kroz `Task.Run`, a .NET thread pool rasporedjuje izvrsavanje. Kod velikog broja istih pretraga cache stampede mehanizam sprecava da vise taskova istovremeno radi istu pretragu.

Log fajl se nalazi u:

```text
logs/server.log
```
