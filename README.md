# File Search Web Server

Web server razvijen u programskom jeziku C# za predmet Sistemsko programiranje.

Server omogućava pretraživanje naziva fajlova po ključnoj reči preko browser-a, prikaz rezultata u HTML formatu i download pronađenih fajlova.

---

# Pokretanje projekta

## 1. Pokretanje servera

U root folderu projekta pokrenuti:

```bash
dotnet run
```

Server će biti dostupan na:

```txt
http://localhost:5050
```

---

# Korišćenje aplikacije

## Početna stranica

Otvoriti u browser-u:

```txt
http://localhost:5050
```

Na početnoj stranici nalazi se forma za unos ključne reči.

---

## Pretraga fajlova

Primer GET zahteva:

```txt
http://localhost:5050/dotnet
```

Server vraća HTML stranicu sa spiskom fajlova koji zadovoljavaju kriterijum pretrage.

---

## Download fajlova

Klikom na dugme Download fajl se preuzima sa servera.

Primer:

```txt
http://localhost:5050/test.txt
```

---

# Root direktorijum servera

Pretraga se vrši samo nad fajlovima koji se nalaze u folderu:

```txt
Files/
```

Primer:

```txt
Files/
├── dotnet_notes.txt
├── field.pdf
└── test.docx
```

---

# Implementirane funkcionalnosti

- konkurentna obrada zahteva
- thread-safe pristup deljenim resursima
- cache memorija
- cache stampede zaštita
- ograničenje veličine cache memorije
- thread-safe logovanje
- HTML prikaz rezultata
- download fajlova
- obrada grešaka
- Python skripta za testiranje opterećenja

---

# Cache sistem

Implementiran je thread-safe cache sistem sa strategijom:

```txt
Ograničenje veličine cache memorije
```

Maksimalan broj elemenata u cache-u je ograničen konstantom:

```csharp
MAX_CACHE_SIZE
```

U slučaju istovremenih zahteva za istim resursom koristi se lock mehanizam kako bi se izbegao cache stampede problem.

---

# Korišćeni mehanizmi sinhronizacije

Za sinhronizaciju niti korišćeni su:

- lock
- Monitor
- Thread-safe kolekcije

Kritične sekcije:
- pristup cache memoriji
- logovanje
- obrada paralelnih zahteva

---

# Testiranje opterećenja

Za testiranje sistema koristi se Python skripta:

```txt
Tests/demo_test.py
```

Skripta demonstrira:

1. CACHE MISS
2. CACHE HIT
3. CACHE STAMPEDE scenario

---

# Pokretanje Python testa

Instalirati biblioteku:

```bash
pip install requests
```

Pokretanje skripte:

```bash
cd Tests
python demo_test.py
```

---

# Primer logovanja

```txt
CACHE MISS: dotnet
CACHE HIT: dotnet
CACHE HIT: dotnet
```

---

# Tehnologije

- C#
- ASP.NET Core
- Python
- requests biblioteka

---

# Autor

Projekat izrađen za predmet Sistemsko programiranje.
