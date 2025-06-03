# System Rozproszonego Przetwarzania Obrazów z CUDA i RabbitMQ

## 📋 Opis Projektu

Projekt ten demonstruje implementację systemu rozproszonego przeznaczonego do przetwarzania obrazów z wykorzystaniem akceleracji GPU (NVIDIA CUDA) oraz brokera wiadomości RabbitMQ do komunikacji między komponentami. System opiera się na architekturze klient-serwer (producent-konsument), gdzie klient wysyła żądania przetworzenia obrazu, a dedykowany worker wykonuje obliczenia na GPU i odsyła wyniki.

Głównym celem projektu było zbadanie wydajności takiego rozwiązania, ze szczególnym uwzględnieniem narzutów komunikacyjnych w stosunku do czasu samego przetwarzania na GPU. Implementacja obejmuje filtrację Laplace'a jako przykładową operację przetwarzania obrazu.

**Projekt został zrealizowany w ramach przedmiotu "Zaawansowane techniki programowania" prowadzonego przez prof. dr hab. inż. Mirosława Kordosa.**

## 🏗️ Architektura

System składa się z następujących komponentów:

### 1. ImageClientCs (Klient C#)

- Odpowiedzialny za ładowanie obrazów
- Serializuje obrazy (do formatu Base64) i metadane (do JSON)
- Implementuje wzorzec RPC (Remote Procedure Call) nad RabbitMQ
- Wysyła żądania przetworzenia do kolejki zadań
- Oczekuje na odpowiedzi na dedykowanej, tymczasowej kolejce
- Deserializuje wyniki i zapisuje przetworzone obrazy
- Mierzy czasy operacji (całkowity czas klienta, narzut komunikacyjny)

### 2. ImageWorkerCs (Worker C#)

- Konsumuje wiadomości z głównej kolejki zadań RabbitMQ
- Deserializuje żądania
- Wywołuje funkcje z biblioteki CUDA (`laplacian_processor.dll`) za pomocą P/Invoke
- Przekazuje dane obrazu do biblioteki C++/CUDA
- Odbiera przetworzone dane i czas wykonania jądra CUDA
- Serializuje odpowiedź i odsyła ją do klienta poprzez RabbitMQ
- Mierzy czas swojej pracy (całkowity czas workera, czas wywołania CUDA)

### 3. laplacian_processor.dll (Biblioteka C++/CUDA)

- Zawiera logikę przetwarzania obrazu (filtr Laplace'a) zaimplementowaną w CUDA C++
- Jest kompilowana jako biblioteka dynamiczna (.dll) i wywoływana przez Workera

### 4. RabbitMQ Server

- Pełni rolę brokera wiadomości
- Używana jest główna, trwała kolejka zadań (`task_queue_laplacian`)
- Dla każdego żądania RPC tworzona jest tymczasowa, automatycznie usuwana kolejka odpowiedzi

## 🛠️ Technologie

- **Języki programowania:** C#, C++/CUDA
- **Platforma:** .NET (dla C#)
- **Akceleracja GPU:** NVIDIA CUDA Toolkit
- **Broker wiadomości:** RabbitMQ
- **Format danych:** JSON, Base64 dla obrazów
- **Komunikacja:** Wzorzec RPC nad RabbitMQ
- **Konteneryzacja (dla RabbitMQ):** Docker

## 📁 Struktura Projektu

```text
.
├── ImageClientCs/                    # Projekt klienta C#
│   ├── ImageClientCs.csproj
│   ├── Program.cs
│   ├── RpcClient.cs
│   ├── ImageUtils.cs
│   ├── test_image_small_cs.png
│   ├── ... (inne obrazy testowe)
│   └── performance_results.csv       # Wyniki wydajności (opcjonalnie)
├── ImageWorkerCs/                    # Projekt workera C#
│   ├── ImageWorkerCs.csproj
│   ├── Program.cs
│   ├── laplacian_processor.dll       # Biblioteka CUDA
│   └── ...
├── project_cuda_rabbitmq/            # Rozwiązanie Visual Studio
│   └── project_cuda_rabbitmq.sln
├── laplacian_processor_cuda/         # Projekt C++/CUDA (opcjonalnie)
│   ├── laplacian_kernel.cu
│   ├── ... (pliki źródłowe i nagłówkowe CUDA)
│   └── ... (plik projektu .vcxproj)
├── SharedModels/                     # Współdzielone modele DTO (opcjonalnie)
│   └── SharedModels.cs
├── charts/                           # Wykresy z wynikami (opcjonalnie)
│   ├── avg_times_grouped_bar_chart.png
│   ├── client_time_breakdown_stacked_chart.png
│   └── gpu_percentage_chart.png
├── .gitignore
└── README.md
```

## ⚙️ Wymagania Wstępne

- **.NET SDK** (wersja 6.0 lub nowsza)
- **NVIDIA CUDA Toolkit** (wersja kompatybilna z kartą GPU)
- **Karta graficzna NVIDIA** z obsługą CUDA
- **Docker Desktop** (do uruchomienia RabbitMQ) lub zainstalowany serwer RabbitMQ

## 🚀 Konfiguracja i Uruchomienie

### 1. Uruchomienie RabbitMQ w Dockerze

Uruchom RabbitMQ używając oficjalnego obrazu Docker:

```bash
docker run -d --hostname my-rabbit --name some-rabbit \
  -p 15672:15672 -p 5672:5672 \
  rabbitmq:3-management
```

**Opis parametrów:**

- `-d`: uruchomienie w tle (detached)
- `--hostname my-rabbit`: ustawienie nazwy hosta kontenera
- `--name some-rabbit`: nazwa kontenera
- `-p 15672:15672`: port dla interfejsu zarządzania RabbitMQ (http://localhost:15672, login/hasło: guest/guest)
- `-p 5672:5672`: port dla komunikacji AMQP (używanego przez klientów C#)

### 2. Kompilacja Projektu

#### a) Biblioteka C++/CUDA (laplacian_processor.dll)

Jeśli masz kod źródłowy, skompiluj go używając NVCC (np. poprzez projekt Visual Studio C++ z integracją CUDA). Upewnij się, że wynikowa biblioteka `.dll` jest dostępna dla projektu `ImageWorkerCs`.

Przykład ścieżki w `ImageWorkerCs/Program.cs`:

```csharp
private const string CUDA_DLL_PATH = "laplacian_processor.dll";
```

#### b) Projekty C# (ImageClientCs, ImageWorkerCs)

Otwórz rozwiązanie `project_cuda_rabbitmq.sln` w Visual Studio lub użyj poleceń .NET CLI:

```bash
cd path/to/your/solution_folder
dotnet build --configuration Release
```

### 3. Uruchomienie Aplikacji

#### a) Uruchom Worker

Przejdź do katalogu wyjściowego `ImageWorkerCs` i uruchom:

```bash
cd ImageWorkerCs/bin/Release/netX.X/
dotnet ImageWorkerCs.dll
# lub bezpośrednio ImageWorkerCs.exe jeśli istnieje
```

Worker powinien połączyć się z RabbitMQ i oczekiwać na wiadomości.

#### b) Uruchom Klienta

Przejdź do katalogu wyjściowego `ImageClientCs` i uruchom:

```bash
cd ImageClientCs/bin/Release/netX.X/
dotnet ImageClientCs.dll
# lub bezpośrednio ImageClientCs.exe jeśli istnieje
```

Klient rozpocznie wysyłanie obrazów testowych do przetworzenia. Obserwuj konsolę klienta i workera w celu śledzenia postępów.

## 📊 Wyniki i Analiza

Przykładowe zagregowane wyniki dla testów na różnych rozmiarach obrazów:

| Scenariusz       | Klient Całkowity (ms) | Narzut Komun. (ms) | Worker Całkowity (ms) | GPU Kernel (ms) | GPU % Workera | GPU % Klienta |
| ---------------- | --------------------- | ------------------ | --------------------- | --------------- | ------------- | ------------- |
| Mały (320×240)   | 2160.84               | 2138.09            | 77.76                 | 4.34            | 5.59%         | 0.20%         |
| Średni (640×480) | 99.50                 | 87.91              | 4.42                  | 0.18            | 4.10%         | 0.18%         |
| Duży (1920×1080) | 242.89                | 177.84             | 31.46                 | 0.33            | 1.05%         | 0.14%         |

**Szczegółowe wyniki** znajdują się w pliku `ImageClientCs/performance_results.csv`.

**Wykresy wizualizujące** te dane znajdują się w folderze `charts/`.

## 🔧 Możliwe Ulepszenia i Dalsze Prace

- Optymalizacja formatu przesyłania danych (np. Protocol Buffers zamiast JSON/Base64)
- Badanie wpływu bardziej złożonych algorytmów GPU
- Implementacja przetwarzania wsadowego
- Bardziej szczegółowe profilowanie workera
- Dodanie obsługi błędów i mechanizmów odporności na awarie
- Implementacja load balancingu dla wielu workerów

## 👨‍💻 Autor

**Maciej** _(uzupełnij nazwisko i opcjonalnie kontakt)_

## 📄 Licencja

Ten projekt jest udostępniany na licencji MIT - zobacz plik `LICENSE.md` po szczegóły.

---

_Projekt został zrealizowany w ramach przedmiotu "Zaawansowane techniki programowania"_
