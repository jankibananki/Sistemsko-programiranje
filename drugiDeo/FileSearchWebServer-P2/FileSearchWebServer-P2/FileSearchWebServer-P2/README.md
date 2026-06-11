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

Sistem je podeljen na prijem i obradu zahteva:

- `WebServer.Run()` je accept petlja i samo prihvata `HttpListenerContext`.
- Svaki pristigli zahtev se pakuje u `ClientRequest` i ubacuje u `BlockingRequestQueue`.
- `Program.Main()` startuje fiksan broj worker taskova preko `server.StartWorkers()`.
- Worker taskovi asinhrono cekaju na `BlockingRequestQueue.DequeueAsync()` i obradjuju zahteve.
- Broj paralelnih obrada je kontrolisan brojem worker taskova (`WorkerCount = 8`).

`Main` nije asinhron jer nema potrebe da entry point bude `async`. Taskovi se startuju iz `Main`, a serverova accept petlja ostaje blokirajuca.

## Async operacije i taskovi

Korisceno je:

- `Task` worker-i za konkurentnu obradu zahteva.
- `await` u obradi HTTP zahteva.
- `File.ReadAllBytesAsync` za download fajla.
- `OutputStream.WriteAsync` za slanje fajla klijentu.
- `SemaphoreSlim.WaitAsync` u redu zahteva, da worker task ne zauzima nit dok nema posla.
- `Task.Run` samo u CPU-bound delu pretrage, gde se filtriraju i sortiraju nazivi fajlova.

Klasicna nit je zadrzana za `Console.ReadKey`, jer je to blokirajuca konzolna operacija i nema smislen async ekvivalent u ovom programu.

## ContinueWith

`ContinueWith` je demonstriran na dva mesta:

- U `SearchCache.GetOrCreateAsync`, nastavak se izvrsava kada se zavrsi stvarna pretraga. Tada se rezultat upisuje u cache, ili se cache entry uklanja ako je doslo do greske ili otkazivanja.
- U `WebServer.StartWorkers`, nastavak loguje zavrsetak ili gresku worker taska.

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

Cache stampede zastita:

- Prvi zahtev za keyword kreira `TaskCompletionSource<List<string>>` i pokrece stvarnu pretragu.
- Paralelni zahtevi za isti keyword dobijaju isti cache entry i cekaju isti task.
- Pretraga se za isti keyword izvrsava samo jednom.
- Kada se rezultat dobije, `ContinueWith` popunjava cache i budi sve cekace kroz zavrsavanje taska.

Za sinhronizaciju cache-a koristi se `ReaderWriterLockSlim`, jer vise reader-a moze istovremeno da cita stanje cache-a, dok se izmene recnika i LRU liste rade kroz write lock.

## Kriticne sekcije

| Komponenta | Mehanizam | Sta stiti |
|---|---|---|
| `BlockingRequestQueue` | `lock` + `SemaphoreSlim` | red pristiglih zahteva |
| `SearchCache` | `ReaderWriterLockSlim` | cache recnik i LRU lista |
| `FileSearchService` | `ReaderWriterLockSlim` | pristup root direktorijumu |
| `ThreadSafeLogger` | `lock` | upis u log fajl |
| `WebServer` | fiksan broj worker taskova | maksimalan broj paralelnih obrada |

## Ponasanje pod opterecenjem

Kod velikog broja paralelnih zahteva accept petlja brzo ubacuje zahteve u red, dok ih najvise `WorkerCount` taskova obradjuje istovremeno. Time se izbegava nekontrolisano kreiranje obrada. Kod velikog broja istih pretraga cache stampede mehanizam sprecava da vise taskova istovremeno radi istu pretragu.

Log fajl se nalazi u:

```text
logs/server.log
```
