import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns
import os

# --- Konfiguracja ---
CSV_FILE_PATH = 'performance_results.csv'
OUTPUT_DIR = 'charts' # Katalog, gdzie zapiszemy wykresy

# Style wykresów (opcjonalnie, dla lepszego wyglądu)
plt.style.use('seaborn-v0_8-whitegrid') # Lub inny styl, np. 'ggplot', 'seaborn-v0_8-darkgrid'
sns.set_palette("muted") # Ustawienie palety kolorów seaborn

def create_output_directory():
    """Tworzy katalog wyjściowy, jeśli nie istnieje."""
    if not os.path.exists(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)
    print(f"Wykresy zostaną zapisane w katalogu: {os.path.abspath(OUTPUT_DIR)}")

def plot_grouped_bar_chart_avg_times(df):
    """Generuje pogrupowany wykres słupkowy dla średnich czasów."""
    if df.empty:
        print("Brak danych do wygenerowania wykresu średnich czasów.")
        return

    plt.figure(figsize=(14, 8)) # Rozmiar wykresu

    # Wybieramy kolumny ze średnimi czasami
    cols_to_plot = [
        'AvgClientOverallTimeMs',
        'AvgCommunicationOverheadMs',
        'AvgWorkerTotalTimeMs',
        'AvgGpuKernelTimeMs',
        'AvgWorkerCpuOverheadMs'
    ]
    
    # Skracamy nazwy dla legendy
    legend_labels = {
        'AvgClientOverallTimeMs': 'Client Overall',
        'AvgCommunicationOverheadMs': 'Comm. Overhead',
        'AvgWorkerTotalTimeMs': 'Worker Total',
        'AvgGpuKernelTimeMs': 'GPU Kernel',
        'AvgWorkerCpuOverheadMs': 'Worker CPU Ov.'
    }

    df_plot = df[['ScenarioDescription'] + cols_to_plot].copy()
    df_plot.set_index('ScenarioDescription', inplace=True)
    df_plot.rename(columns=legend_labels, inplace=True)

    ax = df_plot.plot(kind='bar', rot=15, width=0.8) # rot - rotacja etykiet osi X, width - szerokość grup słupków
    
    plt.title('Average Processing Times per Scenario', fontsize=16)
    plt.ylabel('Time (ms)', fontsize=12)
    plt.xlabel('Scenario', fontsize=12)
    plt.xticks(fontsize=10)
    plt.yticks(fontsize=10)
    plt.legend(title='Time Component', fontsize=10, title_fontsize=11)
    plt.tight_layout() # Dopasowuje wykres, aby wszystko było czytelne
    
    # Dodanie wartości nad słupkami (opcjonalnie, może być tłoczno)
    # for p in ax.patches:
    #     ax.annotate(f"{p.get_height():.2f}", (p.get_x() + p.get_width() / 2., p.get_height()),
    #                 ha='center', va='center', xytext=(0, 5), textcoords='offset points', fontsize=8)

    plt.savefig(os.path.join(OUTPUT_DIR, 'avg_times_grouped_bar_chart.png'), dpi=300)
    plt.close()
    print(f"Zapisano: avg_times_grouped_bar_chart.png")

def plot_stacked_bar_chart_client_times(df):
    """Generuje skumulowany wykres słupkowy dla składników całkowitego czasu klienta."""
    if df.empty:
        print("Brak danych do wygenerowania wykresu skumulowanego.")
        return

    plt.figure(figsize=(12, 8))

    # Obliczamy poszczególne składniki czasu
    df_stack = df.copy()
    df_stack['GpuKernel'] = df_stack['AvgGpuKernelTimeMs']
    df_stack['WorkerCpuOv'] = df_stack['AvgWorkerCpuOverheadMs']
    df_stack['NetComm'] = (df_stack['AvgCommunicationOverheadMs'] - df_stack['AvgWorkerTotalTimeMs']).clip(lower=0)
    df_stack['ClientCpuOv'] = (df_stack['AvgClientOverallTimeMs'] - df_stack['AvgCommunicationOverheadMs']).clip(lower=0)

    components_to_plot = ['GpuKernel', 'WorkerCpuOv', 'NetComm', 'ClientCpuOv']
    
    # Ustawienie 'ScenarioDescription' jako indeksu do rysowania
    df_stack.set_index('ScenarioDescription')[components_to_plot].plot(
        kind='bar', 
        stacked=True, 
        rot=15,
        colormap='viridis' # Można wybrać inną paletę kolorów
    )

    plt.title('Breakdown of Average Client Overall Time (Stacked)', fontsize=16)
    plt.ylabel('Time (ms)', fontsize=12)
    plt.xlabel('Scenario', fontsize=12)
    plt.xticks(fontsize=10)
    plt.yticks(fontsize=10)
    plt.legend(title='Time Component', fontsize=10, title_fontsize=11, loc='upper left')
    plt.tight_layout()
    
    plt.savefig(os.path.join(OUTPUT_DIR, 'client_time_breakdown_stacked_chart.png'), dpi=300)
    plt.close()
    print(f"Zapisano: client_time_breakdown_stacked_chart.png")

def plot_gpu_percentage_chart(df):
    """Generuje wykres słupkowy dla procentowego udziału czasu GPU."""
    if df.empty:
        print("Brak danych do wygenerowania wykresu procentowego udziału GPU.")
        return

    plt.figure(figsize=(10, 7))
    
    cols_to_plot = ['GpuTimeAsPercentageOfWorker', 'GpuTimeAsPercentageOfClient']
    legend_labels = {
        'GpuTimeAsPercentageOfWorker': '% of Worker Total',
        'GpuTimeAsPercentageOfClient': '% of Client Overall'
    }

    df_plot = df[['ScenarioDescription'] + cols_to_plot].copy()
    df_plot.set_index('ScenarioDescription', inplace=True)
    df_plot.rename(columns=legend_labels, inplace=True)

    ax = df_plot.plot(kind='bar', rot=15, width=0.7)

    plt.title('GPU Kernel Time as Percentage of Total Time', fontsize=16)
    plt.ylabel('Percentage (%)', fontsize=12)
    plt.xlabel('Scenario', fontsize=12)
    plt.ylim(0, max(df_plot.max().max() * 1.1, 25)) # Dynamiczna oś Y, ale co najmniej do 25%
    plt.xticks(fontsize=10)
    plt.yticks(fontsize=10)
    plt.legend(title='Reference', fontsize=10, title_fontsize=11)
    plt.tight_layout()

    # Dodanie wartości nad słupkami
    for p in ax.patches:
        ax.annotate(f"{p.get_height():.2f}%", (p.get_x() + p.get_width() / 2., p.get_height()),
                    ha='center', va='center', xytext=(0, 5), textcoords='offset points', fontsize=9)

    plt.savefig(os.path.join(OUTPUT_DIR, 'gpu_percentage_chart.png'), dpi=300)
    plt.close()
    print(f"Zapisano: gpu_percentage_chart.png")


def main():
    create_output_directory()
    try:
        # Wczytanie danych z CSV
        # Używamy kropki jako separatora dziesiętnego, bo tak zapisuje C# z F2
        df = pd.read_csv(CSV_FILE_PATH, decimal='.') 
        print("Dane z CSV wczytane pomyślnie:")
        print(df.head())
        print(f"\nLiczba wierszy: {len(df)}")

        # Konwersja kolumn z czasami na typ numeryczny (float), jeśli jeszcze nie są
        # Sprawdź, czy separator dziesiętny w CSV to kropka czy przecinek
        # Jeśli C# zapisuje z przecinkiem (zależne od kultury), użyj decimal=','
        # Mój kod C# zapisywał z kropką, więc `decimal='.'` powinno być ok.
        
        # Sprawdzenie, czy wszystkie potrzebne kolumny istnieją
        required_cols = [
            'AvgClientOverallTimeMs', 'AvgCommunicationOverheadMs', 'AvgWorkerTotalTimeMs',
            'AvgGpuKernelTimeMs', 'AvgWorkerCpuOverheadMs', 'GpuTimeAsPercentageOfWorker',
            'GpuTimeAsPercentageOfClient', 'ScenarioDescription'
        ]
        missing_cols = [col for col in required_cols if col not in df.columns]
        if missing_cols:
            print(f"\nBŁĄD: Brakuje następujących kolumn w pliku CSV: {', '.join(missing_cols)}")
            print(f"Dostępne kolumny: {', '.join(df.columns)}")
            return

        for col in required_cols:
            if col != 'ScenarioDescription': # ScenarioDescription jest tekstem
                 # Zastąp przecinki kropkami i konwertuj na float
                if df[col].dtype == 'object': # Jeśli pandas odczytał jako tekst
                    df[col] = df[col].str.replace(',', '.').astype(float)
                elif pd.api.types.is_numeric_dtype(df[col]):
                    pass # Już jest numeryczny
                else:
                    print(f"Ostrzeżenie: Kolumna {col} nie jest typu numerycznego ani tekstowego, który można przekonwertować.")


        # Generowanie wykresów
        plot_grouped_bar_chart_avg_times(df)
        plot_stacked_bar_chart_client_times(df)
        plot_gpu_percentage_chart(df)

        print("\nGenerowanie wykresów zakończone.")

    except FileNotFoundError:
        print(f"BŁĄD: Plik CSV '{CSV_FILE_PATH}' nie został znaleziony.")
        print(f"Upewnij się, że plik CSV znajduje się w katalogu: {os.path.abspath('.')}")
    except pd.errors.EmptyDataError:
        print(f"BŁĄD: Plik CSV '{CSV_FILE_PATH}' jest pusty.")
    except Exception as e:
        print(f"Wystąpił nieoczekiwany błąd: {e}")

if __name__ == '__main__':
    main()