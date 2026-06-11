import requests
import threading
import time

BASE_URL = "http://localhost:5050"

KEYWORD = "dotnet"

STAMPEDE_KEYWORD = "stampede"


def send_request(keyword, index=None):
    url = f"{BASE_URL}/{keyword}"
    start = time.time()

    try:
        response = requests.get(url, timeout=10)
        elapsed = time.time() - start

        prefix = f"[Thread {index}]" if index is not None else "[Single]"
        print(f"{prefix} GET {url}")
        print(f"{prefix} Status: {response.status_code}")
        print(f"{prefix} Time: {elapsed:.4f}s")
        print(f"{prefix} Response length: {len(response.text)} chars")
        print("-" * 60)

    except requests.exceptions.RequestException as e:
        print(f"Request error: {e}")


def test_cache_miss_then_hit():
    print("\n=== TEST 1: Prvi poziv - treba da bude CACHE MISS ===")
    send_request(KEYWORD)

    time.sleep(1)

    print("\n=== TEST 2: Drugi isti poziv - treba da bude CACHE HIT ===")
    send_request(KEYWORD)


def test_cache_stampede():
    print("\n=== TEST 3: CACHE STAMPEDE TEST ===")
    print("Pokrece se vise niti koje istovremeno traze isti keyword.")
    print("U logu servera treba da se vidi da samo jedna nit stvarno obradjuje zahtev,")
    print("dok ostale cekaju rezultat iz cache-a.")
    print("-" * 60)

    threads = []
    number_of_threads = 20

    start = time.time()

    for i in range(number_of_threads):
        t = threading.Thread(target=send_request, args=(STAMPEDE_KEYWORD, i + 1))
        threads.append(t)

    for t in threads:
        t.start()

    for t in threads:
        t.join()

    elapsed = time.time() - start
    print(f"\nStampede test zavrsen za {elapsed:.4f}s")


if __name__ == "__main__":

    test_cache_miss_then_hit()
    test_cache_stampede()
