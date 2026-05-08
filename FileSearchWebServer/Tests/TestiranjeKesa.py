import requests
import concurrent.futures
import time

BASE_URL = "http://localhost:5050"

MISS_KEYWORD = "field"
HIT_KEYWORD = "field"

STAMPEDE_KEYWORD = "dotnet"
NUMBER_OF_REQUESTS = 50
MAX_WORKERS = 20


def single_request(keyword, label):
    print(f"\n{label}")
    print(f"URL: {BASE_URL}/{keyword}")

    start = time.time()

    response = requests.get(f"{BASE_URL}/{keyword}")

    elapsed = time.time() - start

    print(f"Status: {response.status_code}")
    print(f"Vreme odgovora: {elapsed:.4f} sekundi")

    return elapsed


def send_stampede_request(request_id):
    start = time.time()

    try:
        response = requests.get(f"{BASE_URL}/{STAMPEDE_KEYWORD}")

        elapsed = time.time() - start

        return {
            "id": request_id,
            "status": response.status_code,
            "time": elapsed,
            "success": response.status_code == 200
        }

    except Exception as error:
        elapsed = time.time() - start

        return {
            "id": request_id,
            "status": "ERROR",
            "time": elapsed,
            "success": False,
            "error": str(error)
        }


def cache_stampede_test():
    print("\nCACHE STAMPEDE TEST")
    print(f"URL: {BASE_URL}/{STAMPEDE_KEYWORD}")
    print(f"Broj zahteva: {NUMBER_OF_REQUESTS}")
    print(f"Broj paralelnih worker-a: {MAX_WORKERS}")
    print("-----------------------------------")

    total_start = time.time()

    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as executor:
        futures = [
            executor.submit(send_stampede_request, i)
            for i in range(1, NUMBER_OF_REQUESTS + 1)
        ]

        results = [
            future.result()
            for future in concurrent.futures.as_completed(futures)
        ]

    total_time = time.time() - total_start

    successful = [r for r in results if r["success"]]
    failed = [r for r in results if not r["success"]]

    print("REZULTATI")
    print(f"Uspesni zahtevi: {len(successful)}")
    print(f"Neuspesni zahtevi: {len(failed)}")
    print(f"Ukupno vreme: {total_time:.2f} sekundi")

    if successful:
        average_time = sum(r["time"] for r in successful) / len(successful)
        fastest = min(r["time"] for r in successful)
        slowest = max(r["time"] for r in successful)

        print(f"Prosecno vreme odgovora: {average_time:.4f} sekundi")
        print(f"Najbrzi odgovor: {fastest:.4f} sekundi")
        print(f"Najsporiji odgovor: {slowest:.4f} sekundi")

    print("-----------------------------------")

    for result in sorted(results, key=lambda x: x["id"]):
        print(
            f"Zahtev {result['id']:02d} | "
            f"Status: {result['status']} | "
            f"Vreme: {result['time']:.4f}s"
        )


def main():
    print("DEMO TEST ZA CACHE SISTEM")
    print("===================================")

    first_time = single_request(
        MISS_KEYWORD,
        "1. PRVI ZAHTEV - podatak nije u kesu"
    )

    second_time = single_request(
        HIT_KEYWORD,
        "2. DRUGI ZAHTEV - isti podatak jeste u kesu"
    )

    print("\nPOREDJENJE VREMENA")
    print("-----------------------------------")
    print(f"FIRST TIME  (CACHE MISS): {first_time:.4f} sekundi")
    print(f"SECOND TIME (CACHE HIT) : {second_time:.4f} sekundi")
    print("-----------------------------------")

    if second_time < first_time:
        print("Drugi zahtev je brzi jer je rezultat vracen iz kesa.")
    else:
        print("Vremena mogu varirati, ali u server.log treba da se vidi CACHE HIT.")

    print("\n===================================")

    cache_stampede_test()

    print("\n===================================")
    print("Podaci o CACHE MISS i CACHE HIT se nalaze u server.log:")
    print("- za prvi zahtev treba CACHE MISS")
    print("- za drugi zahtev treba CACHE HIT")
    print("- za stampede test treba jedan CACHE MISS, ostalo CACHE HIT")


if __name__ == "__main__":
    main()